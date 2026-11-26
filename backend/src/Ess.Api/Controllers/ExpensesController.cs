using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Api.Services;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Domain.Common;
using Ess.Domain.Expenses;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/expenses")]
[Route("api/expenses")]
public sealed class ExpensesController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly ExpenseService _expenses;
    private readonly EmployeeContext _me;
    private readonly ICurrentUser _user;

    public ExpensesController(EssDbContext db, ExpenseService expenses, EmployeeContext me, ICurrentUser user)
    {
        _db = db;
        _expenses = expenses;
        _me = me;
        _user = user;
    }

    public sealed record CategoryView(Guid Id, string Code, string Name, bool RequiresReceipt, decimal? MaxAmountPerLine);
    public sealed record LineView(Guid Id, int LineNo, Guid CategoryId, string Category, DateOnly ExpenseDate, string Description, decimal Amount, string Currency,
        decimal ExchangeRate, Guid? ReceiptDocumentId);
    public sealed record ClaimView(Guid Id, string Reference, string Purpose, string Currency, decimal TotalAmount, string? ProjectCode, string? CostCentreCode,
        RequestStatus Status, NavSyncStatus NavStatus, string? NavDocumentNo, string? PaymentReference, DateTime? PaidAtUtc, DateTime? SubmittedAtUtc,
        DateTime CreatedAtUtc, Guid? WorkflowInstanceId, string EmployeeName, IReadOnlyList<LineView> Lines, IReadOnlyList<string> Warnings);
    public sealed record ClaimSummary(Guid Id, string Reference, string Purpose, string Currency, decimal TotalAmount, RequestStatus Status, NavSyncStatus NavStatus,
        DateTime CreatedAtUtc, DateTime? SubmittedAtUtc, int LineCount);

    private Guid CompanyId => _user.CompanyId ?? throw new UnauthorizedAccessException("Your account has no company.");

    [HttpGet("categories")]
    [HasPermission(Permissions.ExpenseSubmit)]
    public async Task<IReadOnlyList<CategoryView>> Categories(CancellationToken ct)
    {
        await _expenses.EnsureCategoriesAsync(CompanyId, ct);
        return await _db.ExpenseCategories.AsNoTracking().Where(c => c.CompanyId == CompanyId && c.IsActive).OrderBy(c => c.Name)
            .Select(c => new CategoryView(c.Id, c.Code, c.Name, c.RequiresReceipt, c.MaxAmountPerLine)).ToListAsync(ct);
    }

    [HttpGet("mine")]
    [HasPermission(Permissions.ExpenseSubmit)]
    public async Task<PagedResult<ClaimSummary>> Mine([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var me = await _me.RequireAsync(ct);
        var q = _db.ExpenseClaims.AsNoTracking().Where(c => c.EmployeeId == me.Id && c.Status != RequestStatus.Withdrawn).OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new ClaimSummary(c.Id, c.Reference, c.Purpose, c.Currency, c.TotalAmount, c.Status, c.NavStatus, c.CreatedAtUtc, c.SubmittedAtUtc, c.Lines.Count));
        return await PagedResult<ClaimSummary>.CreateAsync(q, page, pageSize, ct);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClaimView>> Get(Guid id, CancellationToken ct)
    {
        var claim = await _db.ExpenseClaims.AsNoTracking().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == CompanyId, ct);
        if (claim is null || !await CanSeeAsync(claim, ct)) return NotFound();
        return await ViewAsync(claim, Array.Empty<string>(), ct);
    }

    /// <summary>Creates a draft. Errors that only matter at submission (missing receipts) are not checked yet.</summary>
    [HttpPost]
    [HasPermission(Permissions.ExpenseSubmit)]
    public async Task<IActionResult> Create(ExpenseClaimInput input, CancellationToken ct)
    {
        await _expenses.EnsureCategoriesAsync(CompanyId, ct);
        var me = await _me.RequireAsync(ct);
        var (errors, warnings) = await _expenses.CheckAsync(me, input, null, forSubmit: false, ct);
        if (errors.Count > 0) return Errors(errors);
        var claim = await _expenses.SaveDraftAsync(me, null, input, ct);
        return Created($"/api/expenses/{claim.Id}", await ViewAsync(claim, warnings, ct));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.ExpenseSubmit)]
    public async Task<IActionResult> Update(Guid id, ExpenseClaimInput input, CancellationToken ct)
    {
        var me = await _me.RequireAsync(ct);
        var (errors, warnings) = await _expenses.CheckAsync(me, input, id, forSubmit: false, ct);
        if (errors.Count > 0) return Errors(errors);
        var claim = await _expenses.SaveDraftAsync(me, id, input, ct);
        return Ok(await ViewAsync(claim, warnings, ct));
    }

    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.ExpenseSubmit)]
    public async Task<ClaimView> Submit(Guid id, CancellationToken ct)
    {
        await _expenses.SubmitAsync(await _me.RequireAsync(ct), _user.UserId!.Value, id, ct);
        var claim = await _db.ExpenseClaims.AsNoTracking().Include(c => c.Lines).FirstAsync(c => c.Id == id, ct);
        return await ViewAsync(claim, Array.Empty<string>(), ct);
    }

    [HttpPost("{id:guid}/withdraw")]
    [HasPermission(Permissions.ExpenseSubmit)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct)
    {
        await _expenses.CancelAsync(await _me.RequireAsync(ct), _user.UserId!.Value, id, ct);
        return NoContent();
    }

    /// <summary>Finance: all claims (expense.view.all), newest first.</summary>
    [HttpGet]
    [HasPermission(Permissions.ExpenseViewAll)]
    public async Task<PagedResult<ClaimSummary>> All([FromQuery] RequestStatus? status, [FromQuery] NavSyncStatus? navStatus, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var q = _db.ExpenseClaims.AsNoTracking().Where(c => c.CompanyId == CompanyId && c.Status != RequestStatus.Draft && c.Status != RequestStatus.Withdrawn);
        if (status is not null) q = q.Where(c => c.Status == status);
        if (navStatus is not null) q = q.Where(c => c.NavStatus == navStatus);
        return await PagedResult<ClaimSummary>.CreateAsync(q.OrderByDescending(c => c.SubmittedAtUtc)
            .Select(c => new ClaimSummary(c.Id, c.Reference, c.Purpose, c.Currency, c.TotalAmount, c.Status, c.NavStatus, c.CreatedAtUtc, c.SubmittedAtUtc, c.Lines.Count)), page, pageSize, ct);
    }

    private async Task<bool> CanSeeAsync(ExpenseClaim c, CancellationToken ct)
    {
        var me = await _me.GetAsync(ct);
        if (me?.Id == c.EmployeeId) return true;
        if (c.Status == RequestStatus.Draft) return false;
        if (_user.HasPermission(Permissions.ExpenseViewAll)) return true;
        return c.WorkflowInstanceId is not null && await _db.ApprovalTasks.AnyAsync(t => t.WorkflowInstanceId == c.WorkflowInstanceId && t.AssigneeUserId == _user.UserId, ct);
    }

    private async Task<ClaimView> ViewAsync(ExpenseClaim c, IReadOnlyList<string> warnings, CancellationToken ct)
    {
        var cats = await _db.ExpenseCategories.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var name = await _db.Employees.Where(e => e.Id == c.EmployeeId).Select(e => e.FirstName + " " + e.LastName).FirstAsync(ct);
        return new ClaimView(c.Id, c.Reference, c.Purpose, c.Currency, c.TotalAmount, c.ProjectCode, c.CostCentreCode, c.Status, c.NavStatus, c.NavDocumentNo,
            c.PaymentReference, c.PaidAtUtc, c.SubmittedAtUtc, c.CreatedAtUtc, c.WorkflowInstanceId, name,
            c.Lines.OrderBy(l => l.LineNo).Select(l => new LineView(l.Id, l.LineNo, l.CategoryId, cats.GetValueOrDefault(l.CategoryId, ""), l.ExpenseDate, l.Description,
                l.Amount, l.Currency, l.ExchangeRate, l.ReceiptDocumentId)).ToList(), warnings);
    }

    private IActionResult Errors(List<string> errors) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["claim"] = errors.ToArray() }) { Detail = string.Join(" ", errors) });
}
