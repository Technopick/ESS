using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Api.Services;
using Ess.Application.Abstractions;
using Ess.Application.Notifications;
using Ess.Application.Security;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers;

/// <summary>The employee's own payslips and statutory summary. Responses are never cached by browsers or proxies.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payroll")]
[Route("api/payroll")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PayrollController : ControllerBase
{
    private readonly PayrollService _payroll;
    private readonly EmployeeContext _me;
    private readonly ICurrentUser _user;

    public PayrollController(PayrollService payroll, EmployeeContext me, ICurrentUser user)
    {
        _payroll = payroll;
        _me = me;
        _user = user;
    }

    [HttpGet("periods")]
    [HasPermission(Permissions.PayrollViewOwn)]
    public async Task<IReadOnlyList<PayrollPeriodView>> Periods(CancellationToken ct) =>
        await _payroll.PublishedPeriodsAsync((await _me.RequireAsync(ct)).CompanyId, ct);

    [HttpGet("payslips/{periodCode}")]
    [HasPermission(Permissions.PayrollViewOwn)]
    public async Task<ActionResult<PayslipView>> Payslip(string periodCode, CancellationToken ct)
    {
        var slip = await _payroll.GetAsync(await _me.RequireAsync(ct), periodCode, requirePublished: true, "own payslip", ct);
        return slip is null ? NotFound(new ProblemDetails { Title = "No payslip", Detail = "NAV has no payslip for you in this period." }) : slip;
    }

    [HttpGet("payslips/{periodCode}/pdf")]
    [HasPermission(Permissions.PayrollViewOwn)]
    public async Task<IActionResult> Pdf(string periodCode, CancellationToken ct)
    {
        var me = await _me.RequireAsync(ct);
        var pdf = await _payroll.GetPdfAsync(me, periodCode, requirePublished: true, ct);
        return pdf is null
            ? NotFound(new ProblemDetails { Title = "No PDF", Detail = "NAV does not provide payslip PDFs. Use Print to save it as PDF." })
            : File(pdf, "application/pdf", $"payslip-{me.NavKey}-{periodCode}.pdf");
    }

    [HttpGet("statutory")]
    [HasPermission(Permissions.PayrollViewOwn)]
    public async Task<StatutorySummary> Statutory([FromQuery] int? year, CancellationToken ct) =>
        await _payroll.StatutoryAsync(await _me.RequireAsync(ct), year ?? DateTime.UtcNow.Year, ct);

    /// <summary>Payroll staff viewing another employee's payslip (payroll.view.all, within department scope). Always audited with the reason.</summary>
    [HttpGet("employees/{employeeId:guid}/payslips/{periodCode}")]
    [HasPermission(Permissions.PayrollViewAll)]
    public async Task<ActionResult<PayslipView>> EmployeePayslip(Guid employeeId, string periodCode, [FromQuery] string? reason,
        [FromServices] EssDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["reason"] = new[] { "Give a reason for viewing this payslip; it is recorded." } }));
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == _user.CompanyId, ct);
        if (employee is null) return NotFound();
        var scope = await _me.DepartmentScopeAsync(Permissions.PayrollViewAll, ct);
        if (scope is not null && (employee.DepartmentId is null || !scope.Contains(employee.DepartmentId.Value))) return NotFound();
        var slip = await _payroll.GetAsync(employee, periodCode, requirePublished: false, reason.Trim(), ct);
        return slip is null ? NotFound() : slip;
    }
}
