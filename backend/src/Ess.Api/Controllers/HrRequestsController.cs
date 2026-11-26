using System.Text.Json;
using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Api.Services;
using Ess.Application.Abstractions;
using Ess.Application.Notifications;
using Ess.Application.Security;
using Ess.Application.Workflow;
using Ess.Domain.Common;
using Ess.Domain.Requests;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers;

/// <summary>
/// HR service requests (letters, certificates, salary advance, general queries). Each type has a simple form definition;
/// requests go through the "HrRequest:{code}" approval route, then HR resolves them (spec §6).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/requests")]
[Route("api/requests")]
public sealed class HrRequestsController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly EmployeeContext _me;
    private readonly ICurrentUser _user;
    private readonly ISecretProtector _secrets;

    public HrRequestsController(EssDbContext db, EmployeeContext me, ICurrentUser user, ISecretProtector secrets)
    {
        _db = db;
        _me = me;
        _user = user;
        _secrets = secrets;
    }

    public sealed record FormField(string Name, string Label, string Type, bool Required, IReadOnlyList<string>? Options = null, int? MaxLength = null);
    public sealed record TypeView(Guid Id, string Code, string Name, string? Description, IReadOnlyList<FormField> Fields, bool IsSensitive, int? SlaHours);
    public sealed record RequestInput(Guid TypeId, string Subject, Dictionary<string, string?> Fields);
    public sealed record RequestView(Guid Id, string Reference, Guid TypeId, string Type, string Subject, IReadOnlyDictionary<string, string?> Fields,
        RequestStatus Status, DateTime? SubmittedAtUtc, DateTime? DueAtUtc, DateTime? ResolvedAtUtc, string? Resolution, Guid? WorkflowInstanceId,
        string EmployeeName, Guid? ResolutionDocumentId);

    private Guid CompanyId => _user.CompanyId ?? throw new UnauthorizedAccessException("Your account has no company.");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task EnsureTypesAsync(CancellationToken ct)
    {
        if (await _db.HrRequestTypes.AnyAsync(t => t.CompanyId == CompanyId, ct)) return;
        void Add(string code, string name, string desc, bool sensitive, int sla, params FormField[] fields) =>
            _db.HrRequestTypes.Add(new HrRequestType
            {
                CompanyId = CompanyId, Code = code, Name = name, Description = desc, IsSensitive = sensitive, SlaHours = sla,
                FormSchemaJson = JsonSerializer.Serialize(fields, Json)
            });
        Add("EMPLOYMENT_LETTER", "Letter of employment", "Confirms your employment, for banks, embassies or landlords.", false, 48,
            new FormField("addressedTo", "Addressed to", "text", true, MaxLength: 150), new FormField("purpose", "Purpose", "text", true, MaxLength: 250),
            new FormField("includeSalary", "Include salary", "select", true, new[] { "No", "Yes" }));
        Add("BANK_LETTER", "Bank introduction letter", "For opening a salary account.", false, 48,
            new FormField("bankName", "Bank", "text", true, MaxLength: 100), new FormField("branch", "Branch", "text", false, MaxLength: 100));
        Add("SALARY_ADVANCE", "Salary advance", "An advance recovered from future salary.", true, 72,
            new FormField("amount", "Amount (KES)", "number", true), new FormField("months", "Repay over (months)", "select", true, new[] { "1", "2", "3" }),
            new FormField("reason", "Reason", "textarea", true, MaxLength: 500));
        Add("CERT_OF_SERVICE", "Certificate of service", "Issued on leaving, as required by the Employment Act.", false, 120,
            new FormField("lastWorkingDay", "Last working day", "date", true));
        Add("GENERAL", "Other HR request", "Anything else for HR.", false, 72, new FormField("details", "Details", "textarea", true, MaxLength: 2000));
        await _db.SaveChangesAsync(ct);
    }

    [HttpGet("types")]
    [HasPermission(Permissions.RequestsSubmit)]
    public async Task<IReadOnlyList<TypeView>> Types(CancellationToken ct)
    {
        await EnsureTypesAsync(ct);
        var types = await _db.HrRequestTypes.AsNoTracking().Where(t => t.CompanyId == CompanyId && t.IsActive).OrderBy(t => t.Name).ToListAsync(ct);
        return types.Select(t => new TypeView(t.Id, t.Code, t.Name, t.Description, FieldsOf(t), t.IsSensitive, t.SlaHours)).ToList();
    }

    [HttpPost]
    [HasPermission(Permissions.RequestsSubmit)]
    public async Task<IActionResult> Submit(RequestInput input, [FromServices] IReferenceGenerator refs, [FromServices] IWorkflowEngine workflow, CancellationToken ct)
    {
        await EnsureTypesAsync(ct);
        var me = await _me.RequireAsync(ct);
        var type = await _db.HrRequestTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == input.TypeId && t.CompanyId == CompanyId && t.IsActive, ct);
        if (type is null) return Field("typeId", "Choose a request type.");
        if (string.IsNullOrWhiteSpace(input.Subject) || input.Subject.Length > 200) return Field("subject", "Enter a subject of up to 200 characters.");
        var errors = Validate(FieldsOf(type), input.Fields);
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors.ToDictionary(e => e.Key, e => new[] { e.Value })));

        var allowed = FieldsOf(type).Select(f => f.Name).ToHashSet();
        var payload = JsonSerializer.Serialize(input.Fields.Where(kv => allowed.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value?.Trim()), Json);
        var req = new HrRequest
        {
            CompanyId = CompanyId, Reference = await refs.NextAsync(CompanyId, "HRQ", ct), EmployeeId = me.Id, RequestTypeId = type.Id, Subject = input.Subject.Trim(),
            PayloadJson = type.IsSensitive ? _secrets.Protect(payload) : payload, PayloadEncrypted = type.IsSensitive,
            Status = RequestStatus.Submitted, NavStatus = NavSyncStatus.NotRequired, SubmittedAtUtc = DateTime.UtcNow
        };
        _db.HrRequests.Add(req);
        var result = await workflow.StartAsync(new WorkflowStartRequest(CompanyId, $"{DocumentTypes.HrRequest}:{type.Code}", req.Id, req.Reference, _user.UserId!.Value, me.Id,
            type.Code == "SALARY_ADVANCE" && decimal.TryParse(input.Fields.GetValueOrDefault("amount"), out var amt) ? amt : null,
            $"{me.FullName}: {type.Name}", $"/requests/{req.Id}"), ct);
        req.WorkflowInstanceId = result.InstanceId;
        if (result.Outcome == WorkflowOutcome.Pending) req.Status = RequestStatus.InApproval;
        await _db.SaveChangesAsync(ct);
        return Created($"/api/requests/{req.Id}", await ViewAsync(req.Id, ct));
    }

    [HttpGet("mine")]
    [HasPermission(Permissions.RequestsSubmit)]
    public async Task<IReadOnlyList<RequestView>> Mine(CancellationToken ct)
    {
        var me = await _me.RequireAsync(ct);
        var ids = await _db.HrRequests.Where(r => r.EmployeeId == me.Id).OrderByDescending(r => r.CreatedAtUtc).Select(r => r.Id).Take(100).ToListAsync(ct);
        var list = new List<RequestView>();
        foreach (var id in ids) list.Add((await ViewAsync(id, ct))!);
        return list;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RequestView>> Get(Guid id, CancellationToken ct)
    {
        var r = await _db.HrRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId, ct);
        if (r is null || !await CanSeeAsync(r, ct)) return NotFound();
        return (await ViewAsync(id, ct))!;
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.RequestsSubmit)]
    public async Task<IActionResult> Cancel(Guid id, [FromServices] IWorkflowEngine workflow, CancellationToken ct)
    {
        var me = await _me.RequireAsync(ct);
        var r = await _db.HrRequests.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == me.Id, ct);
        if (r is null) return NotFound();
        if (r.Status is not (RequestStatus.Submitted or RequestStatus.InApproval or RequestStatus.ReturnedForCorrection))
            return Problem(statusCode: 409, title: "Cannot cancel", detail: "This request has already been decided.");
        if (r.WorkflowInstanceId is not null) await workflow.CancelAsync(r.WorkflowInstanceId.Value, _user.UserId!.Value, "Withdrawn by the employee", ct);
        r.Status = RequestStatus.Withdrawn;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>HR queue: approved requests waiting to be fulfilled, and all others for reference.</summary>
    [HttpGet]
    [HasPermission(Permissions.RequestsManage)]
    public async Task<PagedResult<RequestView>> Queue([FromQuery] RequestStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var scope = await _me.DepartmentScopeAsync(Permissions.RequestsManage, ct);
        var q = _db.HrRequests.AsNoTracking().Where(r => r.CompanyId == CompanyId && r.Status != RequestStatus.Withdrawn && (status == null || r.Status == status));
        if (scope is not null) q = q.Where(r => _db.Employees.Any(e => e.Id == r.EmployeeId && e.DepartmentId != null && scope.Contains(e.DepartmentId.Value)));
        var total = await q.CountAsync(ct);
        var ids = await q.OrderBy(r => r.Status == RequestStatus.Approved ? 0 : 1).ThenBy(r => r.SubmittedAtUtc).Skip((Math.Max(page, 1) - 1) * pageSize).Take(Math.Clamp(pageSize, 1, 100))
            .Select(r => r.Id).ToListAsync(ct);
        var items = new List<RequestView>();
        foreach (var id in ids) items.Add((await ViewAsync(id, ct))!);
        return new PagedResult<RequestView>(items, Math.Max(page, 1), Math.Clamp(pageSize, 1, 100), total);
    }

    public sealed record ResolveInput(string Resolution, Guid? DocumentId);

    [HttpPost("{id:guid}/resolve")]
    [HasPermission(Permissions.RequestsManage)]
    public async Task<IActionResult> Resolve(Guid id, ResolveInput input, [FromServices] INotificationService notify, [FromServices] IAuditWriter audit, CancellationToken ct)
    {
        var r = await _db.HrRequests.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId, ct);
        if (r is null || !await CanSeeAsync(r, ct)) return NotFound();
        if (r.Status != RequestStatus.Approved) return Problem(statusCode: 409, title: "Not ready", detail: "Only approved requests can be resolved.");
        if (string.IsNullOrWhiteSpace(input.Resolution)) return Field("resolution", "Describe what was done.");
        if (input.DocumentId is not null)
        {
            var doc = await _db.EmployeeDocuments.FirstOrDefaultAsync(d => d.Id == input.DocumentId && d.EmployeeId == r.EmployeeId, ct);
            if (doc is null) return Field("documentId", "Upload the document to the employee's file first.");
            doc.OwnerEntityType = "HrRequest";
            doc.OwnerEntityId = r.Id;
        }
        r.Status = RequestStatus.Completed;
        r.Resolution = input.Resolution.Trim();
        r.ResolvedAtUtc = DateTime.UtcNow;
        r.AssignedToUserId = _user.UserId;
        var user = await _db.EmployeeMappings.Where(m => m.EmployeeId == r.EmployeeId).Select(m => (Guid?)m.UserId).FirstOrDefaultAsync(ct);
        if (user is not null)
            await notify.QueueAsync(new NotificationRequest(r.CompanyId, user.Value, NotificationEvents.RequestApproved, $"{r.Reference} is complete",
                $"HR has completed your request {r.Reference}: {r.Resolution}", $"/requests/{r.Id}"), ct);
        await _db.SaveChangesAsync(ct);
        await audit.WriteAsync("hr_request.resolved", nameof(HrRequest), r.Id.ToString(), new { r.Reference }, ct: ct);
        return NoContent();
    }

    // ---------------------------------------------------------------- helpers

    private static IReadOnlyList<FormField> FieldsOf(HrRequestType t) =>
        string.IsNullOrWhiteSpace(t.FormSchemaJson) ? Array.Empty<FormField>() : JsonSerializer.Deserialize<List<FormField>>(t.FormSchemaJson, Json) ?? new();

    private static Dictionary<string, string> Validate(IReadOnlyList<FormField> fields, Dictionary<string, string?> values)
    {
        var errors = new Dictionary<string, string>();
        foreach (var f in fields)
        {
            var v = values.GetValueOrDefault(f.Name)?.Trim();
            if (string.IsNullOrEmpty(v)) { if (f.Required) errors[f.Name] = $"{f.Label} is required."; continue; }
            if (v.Length > (f.MaxLength ?? 2000)) errors[f.Name] = $"{f.Label} is too long.";
            else if (f.Type == "number" && (!decimal.TryParse(v, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var n) || n <= 0)) errors[f.Name] = $"{f.Label} must be a positive number.";
            else if (f.Type == "date" && !DateOnly.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out _)) errors[f.Name] = $"{f.Label} must be a date.";
            else if (f.Type == "select" && f.Options is not null && !f.Options.Contains(v)) errors[f.Name] = $"Choose a value for {f.Label}.";
        }
        return errors;
    }

    private async Task<bool> CanSeeAsync(HrRequest r, CancellationToken ct)
    {
        var me = await _me.GetAsync(ct);
        if (me?.Id == r.EmployeeId) return true;
        if (r.WorkflowInstanceId is not null && await _db.ApprovalTasks.AnyAsync(t => t.WorkflowInstanceId == r.WorkflowInstanceId && t.AssigneeUserId == _user.UserId, ct)) return true;
        if (!_user.HasPermission(Permissions.RequestsManage)) return false;
        var scope = await _me.DepartmentScopeAsync(Permissions.RequestsManage, ct);
        if (scope is null) return true;
        var dept = await _db.Employees.Where(e => e.Id == r.EmployeeId).Select(e => e.DepartmentId).FirstOrDefaultAsync(ct);
        return dept is not null && scope.Contains(dept.Value);
    }

    private async Task<RequestView?> ViewAsync(Guid id, CancellationToken ct)
    {
        var x = await (from r in _db.HrRequests.AsNoTracking()
                       join t in _db.HrRequestTypes on r.RequestTypeId equals t.Id
                       join e in _db.Employees on r.EmployeeId equals e.Id
                       where r.Id == id
                       select new { r, t.Name, t.SlaHours, Employee = e.FirstName + " " + e.LastName }).FirstOrDefaultAsync(ct);
        if (x is null) return null;
        var json = x.r.PayloadJson is null ? "{}" : x.r.PayloadEncrypted ? _secrets.Unprotect(x.r.PayloadJson) : x.r.PayloadJson;
        var doc = await _db.EmployeeDocuments.Where(d => d.OwnerEntityType == "HrRequest" && d.OwnerEntityId == id).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);
        return new RequestView(x.r.Id, x.r.Reference, x.r.RequestTypeId, x.Name, x.r.Subject, JsonSerializer.Deserialize<Dictionary<string, string?>>(json, Json) ?? new(),
            x.r.Status, x.r.SubmittedAtUtc, x.SlaHours is null ? null : x.r.SubmittedAtUtc?.AddHours(x.SlaHours.Value), x.r.ResolvedAtUtc, x.r.Resolution,
            x.r.WorkflowInstanceId, x.Employee, doc);
    }

    private IActionResult Field(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = new[] { message } }));
}
