using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Api.Services;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Domain.Documents;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/documents")]
[Route("api/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly DocumentService _docs;
    private readonly EmployeeContext _me;
    private readonly ICurrentUser _user;
    private readonly IAuditWriter _audit;

    public DocumentsController(EssDbContext db, DocumentService docs, EmployeeContext me, ICurrentUser user, IAuditWriter audit)
    {
        _db = db;
        _docs = docs;
        _me = me;
        _user = user;
        _audit = audit;
    }

    private Guid CompanyId => _user.CompanyId ?? throw new UnauthorizedAccessException("Your account has no company.");

    public sealed record CategoryView(Guid Id, string Code, string Name, bool EmployeeCanUpload, int? RetentionMonths);

    [HttpGet("categories")]
    [HasPermission(Permissions.DocumentsViewOwn)]
    public async Task<IReadOnlyList<CategoryView>> Categories(CancellationToken ct)
    {
        await _docs.EnsureCategoriesAsync(CompanyId, ct);
        return await _db.DocumentCategories.AsNoTracking().Where(c => c.CompanyId == CompanyId).OrderBy(c => c.Name)
            .Select(c => new CategoryView(c.Id, c.Code, c.Name, c.EmployeeCanUpload, c.RetentionMonths)).ToListAsync(ct);
    }

    /// <summary>The employee's own documents plus company-wide and department documents (policies, notices).</summary>
    [HttpGet("mine")]
    [HasPermission(Permissions.DocumentsViewOwn)]
    public async Task<IReadOnlyList<DocumentView>> Mine(CancellationToken ct)
    {
        var me = await _me.GetAsync(ct);
        var q = _db.EmployeeDocuments.AsNoTracking().Where(d => d.CompanyId == CompanyId && d.OwnerEntityType == null
            && ((me != null && d.EmployeeId == me.Id) || d.Visibility == DocumentVisibility.Company
                || (d.Visibility == DocumentVisibility.Department && me != null && d.DepartmentId == me.DepartmentId)));
        return await _docs.Views(q, me?.Id, false).OrderBy(v => v.Category).ThenBy(v => v.Title).ToListAsync(ct);
    }

    /// <summary>HR: documents of one employee, or company/department documents when no employee is given.</summary>
    [HttpGet]
    [HasPermission(Permissions.DocumentsManage)]
    public async Task<PagedResult<DocumentView>> List([FromQuery] Guid? employeeId, [FromQuery] Guid? categoryId, [FromQuery] bool expiringOnly = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var scope = await _me.DepartmentScopeAsync(Permissions.DocumentsManage, ct);
        var q = _db.EmployeeDocuments.AsNoTracking().Where(d => d.CompanyId == CompanyId && d.OwnerEntityType == null);
        if (employeeId is not null) q = q.Where(d => d.EmployeeId == employeeId);
        if (categoryId is not null) q = q.Where(d => d.CategoryId == categoryId);
        if (expiringOnly)
        {
            var limit = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60));
            q = q.Where(d => d.ExpiryDate != null && d.ExpiryDate <= limit);
        }
        if (scope is not null)
            q = q.Where(d => d.EmployeeId == null
                ? d.DepartmentId != null && scope.Contains(d.DepartmentId.Value)
                : _db.Employees.Any(e => e.Id == d.EmployeeId && e.DepartmentId != null && scope.Contains(e.DepartmentId.Value)));
        return await PagedResult<DocumentView>.CreateAsync(_docs.Views(q, null, true).OrderBy(v => v.ExpiryDate ?? DateOnly.MaxValue).ThenBy(v => v.Title), page, pageSize, ct);
    }

    public sealed class UploadForm
    {
        public IFormFile File { get; set; } = default!;
        public Guid CategoryId { get; set; }
        public string? Title { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        /// <summary>HR only: upload for this employee.</summary>
        public Guid? EmployeeId { get; set; }
        /// <summary>HR only: Company or Department visibility for policies and notices.</summary>
        public DocumentVisibility? Visibility { get; set; }
        public Guid? DepartmentId { get; set; }
    }

    [HttpPost]
    [HasPermission(Permissions.DocumentsViewOwn)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] UploadForm form, CancellationToken ct)
    {
        await _docs.EnsureCategoriesAsync(CompanyId, ct);
        var category = await _db.DocumentCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == form.CategoryId && c.CompanyId == CompanyId, ct);
        if (category is null || category.Code == DocumentService.ReceiptCategory) return Field("categoryId", "Choose a document category.");
        var title = string.IsNullOrWhiteSpace(form.Title) ? category.Name : form.Title.Trim();
        if (title.Length > 200) return Field("title", "Use at most 200 characters.");

        var doc = new EmployeeDocument { CompanyId = CompanyId, CategoryId = category.Id, Title = title, ExpiryDate = form.ExpiryDate };
        var hrUpload = form.EmployeeId is not null || form.Visibility is DocumentVisibility.Company or DocumentVisibility.Department;
        if (hrUpload)
        {
            if (!_user.HasPermission(Permissions.DocumentsManage)) return Forbid();
            if (form.EmployeeId is not null)
            {
                if (!await _db.Employees.AnyAsync(e => e.Id == form.EmployeeId && e.CompanyId == CompanyId, ct)) return Field("employeeId", "Unknown employee.");
                doc.EmployeeId = form.EmployeeId;
                doc.Visibility = form.Visibility is DocumentVisibility.HrOnly ? DocumentVisibility.HrOnly : DocumentVisibility.EmployeeOnly;
            }
            else
            {
                doc.Visibility = form.Visibility!.Value;
                doc.DepartmentId = form.Visibility == DocumentVisibility.Department ? form.DepartmentId : null;
                if (doc.Visibility == DocumentVisibility.Department && doc.DepartmentId is null) return Field("departmentId", "Choose a department.");
            }
            if (!await _docs.CanManageAsync(doc, ct)) return Forbid();
        }
        else
        {
            if (!category.EmployeeCanUpload) return Field("categoryId", "HR uploads this kind of document. Ask HR for it.");
            doc.EmployeeId = (await _me.RequireAsync(ct)).Id;
            doc.Visibility = DocumentVisibility.EmployeeOnly;
        }

        await _docs.StoreAsync(doc, form.File, ct);
        await _audit.WriteAsync("document.uploaded", nameof(EmployeeDocument), doc.Id.ToString(), new { doc.Title, category = category.Code, doc.EmployeeId }, ct: ct);
        var me = await _me.GetAsync(ct);
        return Created($"/api/documents/{doc.Id}", await _docs.Views(_db.EmployeeDocuments.Where(d => d.Id == doc.Id), me?.Id, hrUpload).FirstAsync(ct));
    }

    /// <summary>Uploads a receipt for an expense claim line. It is attached to the claim when the claim is saved.</summary>
    [HttpPost("receipts")]
    [HasPermission(Permissions.ExpenseSubmit)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> UploadReceipt(IFormFile file, CancellationToken ct)
    {
        await _docs.EnsureCategoriesAsync(CompanyId, ct);
        var me = await _me.RequireAsync(ct);
        var category = await _db.DocumentCategories.FirstAsync(c => c.CompanyId == CompanyId && c.Code == DocumentService.ReceiptCategory, ct);
        var doc = new EmployeeDocument { CompanyId = CompanyId, CategoryId = category.Id, EmployeeId = me.Id, Title = "Receipt", Visibility = DocumentVisibility.EmployeeOnly };
        await _docs.StoreAsync(doc, file, ct);
        return Created($"/api/documents/{doc.Id}", new { doc.Id });
    }

    [HttpPost("{id:guid}/versions")]
    [HasPermission(Permissions.DocumentsViewOwn)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> NewVersion(Guid id, IFormFile file, [FromForm] DateOnly? expiryDate, CancellationToken ct)
    {
        var doc = await _db.EmployeeDocuments.FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == CompanyId, ct);
        if (doc is null || !await CanChangeAsync(doc, ct)) return NotFound();
        if (expiryDate is not null) doc.ExpiryDate = expiryDate;
        await _docs.StoreAsync(doc, file, ct);
        await _audit.WriteAsync("document.version_added", nameof(EmployeeDocument), doc.Id.ToString(), new { doc.CurrentVersion }, ct: ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, [FromQuery] int? version, [FromQuery] bool inline = false, CancellationToken ct = default)
    {
        var doc = await _db.EmployeeDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null || !await _docs.CanReadAsync(doc, ct)) return NotFound();
        var (bytes, v) = await _docs.ReadAsync(doc, version, inline ? "view" : "download", ct);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Only PDFs and images are shown inline; other types always download.
        var showInline = inline && (v.ContentType == "application/pdf" || v.ContentType.StartsWith("image/"));
        return showInline ? File(bytes, v.ContentType) : File(bytes, v.ContentType, v.FileName);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.DocumentsViewOwn)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var doc = await _db.EmployeeDocuments.FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == CompanyId, ct);
        if (doc is null || !await CanChangeAsync(doc, ct)) return NotFound();
        if (doc.OwnerEntityId is not null) return Problem(statusCode: 409, title: "In use", detail: "This receipt belongs to an expense claim.");
        _db.EmployeeDocuments.Remove(doc); // soft delete; files are purged by the retention job
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("document.deleted", nameof(EmployeeDocument), doc.Id.ToString(), new { doc.Title }, ct: ct);
        return NoContent();
    }

    private async Task<bool> CanChangeAsync(EmployeeDocument doc, CancellationToken ct)
    {
        if (await _docs.CanManageAsync(doc, ct)) return true;
        var me = await _me.GetAsync(ct);
        if (me is null || doc.EmployeeId != me.Id) return false;
        return await _db.DocumentCategories.AnyAsync(c => c.Id == doc.CategoryId && c.EmployeeCanUpload, ct);
    }

    private IActionResult Field(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = new[] { message } }));
}
