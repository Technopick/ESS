using Ess.Application.Workflow;
using System.Text.Json;
using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Api.Services;
using Ess.Application.Abstractions;
using Ess.Application.Integration;
using Ess.Application.Security;
using Ess.Domain.Common;
using Ess.Domain.Organization;
using Ess.Domain.Requests;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers;

/// <summary>Employee self-service profile, change requests and the employee directory (spec §4.2, §14).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/employees")]
[Route("api/employees")]
public sealed class EmployeesController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly EmployeeContext _me;
    private readonly ICurrentUser _user;

    public EmployeesController(EssDbContext db, EmployeeContext me, ICurrentUser user)
    {
        _db = db;
        _me = me;
        _user = user;
    }

    public sealed record EmployeeSummary(Guid Id, string EmployeeNo, string FullName, string? JobTitle, string? Department, string? Location,
        Guid? ManagerId, string? ManagerName, string Status, DateOnly? EmploymentDate, DateOnly? ContractEndDate, string? WorkEmail, DateTime LastSyncedAtUtc);

    [HttpGet("me")]
    [HasPermission(Permissions.ProfileViewOwn)]
    public async Task<EmployeeSummary> Me(CancellationToken ct)
    {
        var e = await _me.RequireAsync(ct);
        return (await SummariesAsync(_db.Employees.Where(x => x.Id == e.Id), ct)).Single();
    }

    public sealed record ProfileResponse(EmployeeSummary Summary, NavEmployeeProfile? Nav, bool NavAvailable, string? NavMessage);

    /// <summary>Full profile read live from NAV. If NAV is unavailable the cached summary is still returned.</summary>
    [HttpGet("me/profile")]
    [HasPermission(Permissions.ProfileViewOwn)]
    public async Task<ProfileResponse> Profile([FromServices] INavEmployeeService nav, CancellationToken ct)
    {
        var e = await _me.RequireAsync(ct);
        var summary = (await SummariesAsync(_db.Employees.Where(x => x.Id == e.Id), ct)).Single();
        var r = await nav.GetProfileAsync(e.CompanyId, e.NavKey, ct);
        if (r.Success && !string.Equals(r.Value!.Core.No, e.NavKey, StringComparison.OrdinalIgnoreCase))
            return new ProfileResponse(summary, null, false, "NAV returned a different employee record; contact the administrator.");
        return r.Success
            ? new ProfileResponse(summary, r.Value, true, null)
            : new ProfileResponse(summary, null, false, r.Error!.Category == NavErrorCategory.Configuration
                ? "Detailed profile information is not available from NAV yet."
                : "NAV could not be reached. Showing the information held by the portal.");
    }

    /// <summary>Sections an employee may ask to change, with the fields allowed in each. Sensitive sections are encrypted at rest.</summary>
    public static readonly IReadOnlyDictionary<string, (string[] Fields, bool Sensitive)> ChangeSections = new Dictionary<string, (string[], bool)>(StringComparer.OrdinalIgnoreCase)
    {
        ["Contact"] = (new[] { "mobilePhone", "personalEmail", "homePhone" }, false),
        ["Address"] = (new[] { "address", "address2", "city", "county", "postCode", "country" }, false),
        ["EmergencyContacts"] = (new[] { "name", "relationship", "phone", "action" }, false),
        ["NextOfKin"] = (new[] { "name", "relationship", "phone", "idNumber", "percentage", "action" }, true),
        ["Dependants"] = (new[] { "name", "relationship", "birthDate", "action" }, true),
        ["Personal"] = (new[] { "maritalStatus", "nationalId", "kraPin", "nssfNo", "shifNo", "passportNo" }, true),
        ["BankDetails"] = (new[] { "bankName", "bankCode", "branch", "branchCode", "accountName", "accountNo" }, true),
        ["Qualifications"] = (new[] { "qualification", "institution", "from", "to", "expires", "action" }, false),
    };

    public sealed record ChangeRequestInput(string Section, Dictionary<string, string?> Changes, string? Reason);
    public sealed record ChangeRequestView(Guid Id, string Reference, string Section, Dictionary<string, string?>? Changes, RequestStatus Status,
        NavSyncStatus NavStatus, DateTime CreatedAtUtc);

    [HttpPost("me/change-requests")]
    [HasPermission(Permissions.ProfileRequestChange)]
    public async Task<IActionResult> RequestChange(ChangeRequestInput req, [FromServices] ISecretProtector secrets, [FromServices] IReferenceGenerator refs,
        [FromServices] IAuditWriter audit, [FromServices] IWorkflowEngine workflow, CancellationToken ct)
    {
        var e = await _me.RequireAsync(ct);
        if (!ChangeSections.TryGetValue(req.Section ?? "", out var def))
            return Field("section", "Choose one of: " + string.Join(", ", ChangeSections.Keys));
        var unknown = req.Changes.Keys.Where(k => !def.Fields.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0) return Field("changes", "These fields cannot be changed here: " + string.Join(", ", unknown));
        if (req.Changes.Count == 0 || req.Changes.All(kv => string.IsNullOrWhiteSpace(kv.Value))) return Field("changes", "Enter at least one change.");
        if (req.Changes.Values.Any(v => v?.Length > 250)) return Field("changes", "Each value can have at most 250 characters.");
        if (await _db.ProfileChangeRequests.AnyAsync(p => p.EmployeeId == e.Id && p.Section == req.Section
                && (p.Status == RequestStatus.Submitted || p.Status == RequestStatus.InApproval), ct))
            return Field("section", "You already have a pending change for this section. Wait for HR to process it.");

        var payload = JsonSerializer.Serialize(new { changes = req.Changes, reason = req.Reason });
        var pcr = new ProfileChangeRequest
        {
            CompanyId = e.CompanyId, Reference = await refs.NextAsync(e.CompanyId, "PCR", ct), EmployeeId = e.Id, Section = CanonicalSection(req.Section!),
            ChangesJson = def.Sensitive ? secrets.Protect(payload) : payload, ChangesEncrypted = def.Sensitive,
            Status = RequestStatus.Submitted, NavStatus = NavSyncStatus.NotRequired
        };
        _db.ProfileChangeRequests.Add(pcr);
        // HR reviews the change through the approval workflow; on approval it is queued for NAV.
        var result = await workflow.StartAsync(new WorkflowStartRequest(e.CompanyId, DocumentTypes.ProfileChange, pcr.Id, pcr.Reference, _user.UserId!.Value, e.Id,
            null, $"{e.FullName}: change of {SectionLabel(pcr.Section)}", "/profile"), ct);
        if (result.Outcome == WorkflowOutcome.Pending) pcr.Status = RequestStatus.InApproval;
        pcr.WorkflowInstanceId = result.InstanceId;
        await _db.SaveChangesAsync(ct);
        await audit.WriteAsync("profile.change_requested", nameof(ProfileChangeRequest), pcr.Id.ToString(), new { pcr.Reference, pcr.Section, fields = req.Changes.Keys }, ct: ct);
        return Created($"/api/employees/me/change-requests/{pcr.Id}", new { pcr.Id, pcr.Reference });
    }

    [HttpGet("me/change-requests")]
    [HasPermission(Permissions.ProfileViewOwn)]
    public async Task<IReadOnlyList<ChangeRequestView>> MyChangeRequests([FromServices] ISecretProtector secrets, CancellationToken ct)
    {
        var e = await _me.RequireAsync(ct);
        var rows = await _db.ProfileChangeRequests.AsNoTracking().Where(p => p.EmployeeId == e.Id).OrderByDescending(p => p.CreatedAtUtc).Take(50).ToListAsync(ct);
        return rows.Select(p =>
        {
            var json = p.ChangesEncrypted ? secrets.Unprotect(p.ChangesJson) : p.ChangesJson;
            var changes = JsonDocument.Parse(json).RootElement.GetProperty("changes").Deserialize<Dictionary<string, string?>>();
            if (changes is not null && p.Section == "BankDetails" && changes.TryGetValue("accountNo", out var acc) && acc is { Length: > 4 })
                changes["accountNo"] = new string('•', acc.Length - 4) + acc[^4..];
            return new ChangeRequestView(p.Id, p.Reference, p.Section, changes, p.Status, p.NavStatus, p.CreatedAtUtc);
        }).ToList();
    }

    public sealed record DirectoryEntry(Guid Id, string FullName, string? JobTitle, string? Department, string? Location, string? WorkEmail,
        string? EmployeeNo, string? Status, string? ManagerName, DateOnly? EmploymentDate);

    /// <summary>
    /// Employee directory. Everyone with directory access sees name, title, department, location and work email of active staff.
    /// HR with employees.view.all also sees number, status, manager and dates, limited to their department scope.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.EmployeesViewDirectory)]
    public async Task<PagedResult<DirectoryEntry>> Directory([FromQuery] string? search, [FromQuery] Guid? departmentId, [FromQuery] Guid? locationId,
        [FromQuery] EmploymentStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var companyId = _user.CompanyId ?? Guid.Empty;
        var full = _user.HasPermission(Permissions.EmployeesViewAll);
        var q = _db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId);
        if (full)
        {
            var scope = await _me.DepartmentScopeAsync(Permissions.EmployeesViewAll, ct);
            if (scope is not null) q = q.Where(e => e.DepartmentId != null && scope.Contains(e.DepartmentId.Value));
            if (status is not null) q = q.Where(e => e.Status == status);
        }
        else q = q.Where(e => e.Status == EmploymentStatus.Active || e.Status == EmploymentStatus.OnProbation);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(e => (e.FirstName + " " + e.LastName).ToLower().Contains(s) || (e.WorkEmail != null && e.WorkEmail.ToLower().Contains(s))
                             || (full && e.NavKey.ToLower().Contains(s)));
        }
        if (departmentId is not null) q = q.Where(e => e.DepartmentId == departmentId);
        if (locationId is not null) q = q.Where(e => e.LocationId == locationId);

        var projected = from e in q
                        join d in _db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                        join l in _db.Locations on e.LocationId equals l.Id into lj from l in lj.DefaultIfEmpty()
                        join p in _db.Positions on e.PositionId equals p.Id into pj from p in pj.DefaultIfEmpty()
                        join m in _db.Employees on e.ManagerEmployeeId equals m.Id into mj from m in mj.DefaultIfEmpty()
                        orderby e.LastName, e.FirstName
                        select new DirectoryEntry(e.Id, e.FirstName + " " + e.LastName, p != null ? p.Title : null, d != null ? d.Name : null,
                            l != null ? l.Name : null, e.WorkEmail,
                            full ? e.NavKey : null, full ? e.Status.ToString() : null,
                            full && m != null ? m.FirstName + " " + m.LastName : null, full ? e.EmploymentDate : null);
        return await PagedResult<DirectoryEntry>.CreateAsync(projected, page, pageSize, ct);
    }

    private static string SectionLabel(string section) => section switch
    {
        "BankDetails" => "bank details",
        "NextOfKin" => "next of kin",
        "EmergencyContacts" => "emergency contacts",
        _ => System.Text.RegularExpressions.Regex.Replace(section, "(?<!^)([A-Z])", " $1").ToLowerInvariant()
    };

    private static string CanonicalSection(string s) => ChangeSections.Keys.First(k => k.Equals(s, StringComparison.OrdinalIgnoreCase));

    private async Task<List<EmployeeSummary>> SummariesAsync(IQueryable<Employee> q, CancellationToken ct) =>
        await (from e in q.AsNoTracking()
               join d in _db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
               join l in _db.Locations on e.LocationId equals l.Id into lj from l in lj.DefaultIfEmpty()
               join p in _db.Positions on e.PositionId equals p.Id into pj from p in pj.DefaultIfEmpty()
               join m in _db.Employees on e.ManagerEmployeeId equals m.Id into mj from m in mj.DefaultIfEmpty()
               select new EmployeeSummary(e.Id, e.NavKey, e.FirstName + " " + e.LastName, p != null ? p.Title : null, d != null ? d.Name : null,
                   l != null ? l.Name : null, e.ManagerEmployeeId, m != null ? m.FirstName + " " + m.LastName : null, e.Status.ToString(),
                   e.EmploymentDate, e.ContractEndDate, e.WorkEmail, e.LastSyncedAtUtc)).ToListAsync(ct);

    private IActionResult Field(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = new[] { message } }));
}
