using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Api.Services;
using Ess.Application.Abstractions;
using Ess.Application.Integration;
using Ess.Application.Security;
using Ess.Domain.Common;
using Ess.Domain.Organization;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers.Hr;

/// <summary>
/// HR employee records (spec §6): search and open any employee within the HR user's department scope. NAV stays the master:
/// the portal shows the synced record and the live NAV profile, and changes go through approved change requests and NAV operations.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/hr/employees")]
[Route("api/hr/employees")]
[HasPermission(Permissions.EmployeesViewAll)]
public sealed class HrEmployeesController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly ICurrentUser _user;
    private readonly EmployeeContext _me;
    private readonly IAuditWriter _audit;

    public HrEmployeesController(EssDbContext db, ICurrentUser user, EmployeeContext me, IAuditWriter audit)
    {
        _db = db;
        _user = user;
        _me = me;
        _audit = audit;
    }

    private Guid CompanyId => _user.CompanyId ?? throw new UnauthorizedAccessException();

    public sealed record EmployeeRow(Guid Id, string EmployeeNo, string Name, string? Position, string? Department, string? Location, string Status,
        string? Manager, DateOnly? EmploymentDate, DateOnly? ContractEndDate, bool HasAccount, DateTime LastSyncedAtUtc);

    private async Task<IQueryable<Employee>> ScopedAsync(CancellationToken ct)
    {
        var scope = await _me.DepartmentScopeAsync(Permissions.EmployeesViewAll, ct);
        var q = _db.Employees.AsNoTracking().Where(e => e.CompanyId == CompanyId);
        return scope is null ? q : q.Where(e => e.DepartmentId != null && scope.Contains(e.DepartmentId.Value));
    }

    [HttpGet]
    public async Task<PagedResult<EmployeeRow>> List([FromQuery] string? search, [FromQuery] Guid? departmentId, [FromQuery] Guid? locationId, [FromQuery] Guid? positionId,
        [FromQuery] EmploymentStatus? status, [FromQuery] bool contractEndingSoon = false, [FromQuery] bool withoutAccount = false,
        [FromQuery] string sort = "name", [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var q = await ScopedAsync(ct);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(e => (e.FirstName + " " + e.LastName).ToLower().Contains(s) || e.NavKey.ToLower().Contains(s) || (e.WorkEmail != null && e.WorkEmail.ToLower().Contains(s)));
        }
        if (departmentId is not null) q = q.Where(e => e.DepartmentId == departmentId);
        if (locationId is not null) q = q.Where(e => e.LocationId == locationId);
        if (positionId is not null) q = q.Where(e => e.PositionId == positionId);
        if (status is not null) q = q.Where(e => e.Status == status);
        if (contractEndingSoon)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var limit = today.AddDays(90);
            q = q.Where(e => e.ContractEndDate != null && e.ContractEndDate >= today && e.ContractEndDate <= limit);
        }
        if (withoutAccount) q = q.Where(e => !_db.EmployeeMappings.Any(m => m.EmployeeId == e.Id));
        q = sort switch
        {
            "no" => q.OrderBy(e => e.NavKey),
            "employed" => q.OrderByDescending(e => e.EmploymentDate),
            "contract" => q.OrderBy(e => e.ContractEndDate == null).ThenBy(e => e.ContractEndDate),
            _ => q.OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
        };
        var projected = from e in q
                        join d in _db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                        join l in _db.Locations on e.LocationId equals l.Id into lj from l in lj.DefaultIfEmpty()
                        join p in _db.Positions on e.PositionId equals p.Id into pj from p in pj.DefaultIfEmpty()
                        join m in _db.Employees on e.ManagerEmployeeId equals m.Id into mj from m in mj.DefaultIfEmpty()
                        select new EmployeeRow(e.Id, e.NavKey, e.FirstName + " " + e.LastName, p != null ? p.Title : null, d != null ? d.Name : null, l != null ? l.Name : null,
                            e.Status.ToString(), m != null ? m.FirstName + " " + m.LastName : null, e.EmploymentDate, e.ContractEndDate,
                            _db.EmployeeMappings.Any(x => x.EmployeeId == e.Id), e.LastSyncedAtUtc);
        return await PagedResult<EmployeeRow>.CreateAsync(projected, page, pageSize, ct);
    }

    public sealed record AccountInfo(Guid UserId, string UserName, string Email, bool IsActive, bool MfaEnabled, DateTime? LastLoginAtUtc, IReadOnlyList<string> Roles);
    public sealed record BalanceRow(string LeaveType, string Period, decimal Entitlement, decimal Taken, decimal Pending, decimal Available, DateTime RetrievedAtUtc);
    public sealed record ActivityRow(Guid Id, string Kind, string Reference, string Summary, string Status, DateTime? AtUtc, string? Link);
    public sealed record DirectReport(Guid Id, string Name, string? Position);
    public sealed record EmployeeDetail(EmployeeRow Employee, string? Gender, DateOnly? TerminationDate, string? WorkEmail, string? MobilePhone, Guid? ManagerId,
        AccountInfo? Account, IReadOnlyList<DirectReport> DirectReports, IReadOnlyList<BalanceRow> Balances, IReadOnlyList<ActivityRow> Activity,
        int Documents, int ExpiringDocuments, int PendingProfileChanges);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmployeeDetail>> Get(Guid id, CancellationToken ct)
    {
        var q = (await ScopedAsync(ct)).Where(e => e.Id == id);
        var e = await q.FirstOrDefaultAsync(ct);
        if (e is null) return NotFound();
        var row = await RowAsync(q, ct);

        var account = await (from m in _db.EmployeeMappings.AsNoTracking()
                             join u in _db.Users on m.UserId equals u.Id
                             where m.EmployeeId == id
                             select new { u.Id, u.UserName, u.Email, u.IsActive, u.MfaEnabled, u.LastLoginAtUtc }).FirstOrDefaultAsync(ct);
        AccountInfo? acc = null;
        if (account is not null)
        {
            var roles = await _db.UserRoles.AsNoTracking().Where(r => r.UserId == account.Id).Select(r => r.Role!.Name).Distinct().ToListAsync(ct);
            acc = new AccountInfo(account.Id, account.UserName, account.Email, account.IsActive, account.MfaEnabled, account.LastLoginAtUtc, roles);
        }

        var reports = await (from r in _db.Employees.AsNoTracking()
                             join p in _db.Positions on r.PositionId equals p.Id into pj from p in pj.DefaultIfEmpty()
                             where r.ManagerEmployeeId == id && r.Status != EmploymentStatus.Terminated
                             orderby r.LastName
                             select new DirectReport(r.Id, r.FirstName + " " + r.LastName, p != null ? p.Title : null)).ToListAsync(ct);

        var balances = await (from b in _db.LeaveBalancesCache.AsNoTracking()
                              join t in _db.LeaveTypes on b.LeaveTypeId equals t.Id
                              where b.EmployeeId == id
                              orderby t.Name
                              select new BalanceRow(t.Name, b.LeavePeriodCode, b.Entitlement + b.BroughtForward, b.Taken, b.PendingApproval, b.Available, b.RetrievedAtUtc)).ToListAsync(ct);

        var activity = new List<ActivityRow>();
        activity.AddRange(await (from a in _db.LeaveApplications.AsNoTracking()
                                 join t in _db.LeaveTypes on a.LeaveTypeId equals t.Id
                                 where a.EmployeeId == id && a.Status != RequestStatus.Draft
                                 orderby a.CreatedAtUtc descending
                                 select new ActivityRow(a.Id, "Leave", a.Reference, t.Name + ": " + a.DaysRequested + " day(s) from " + a.StartDate, a.Status.ToString(), a.SubmittedAtUtc, null))
            .Take(10).ToListAsync(ct));
        activity.AddRange(await _db.ExpenseClaims.AsNoTracking().Where(x => x.EmployeeId == id && x.Status != RequestStatus.Draft).OrderByDescending(x => x.CreatedAtUtc).Take(10)
            .Select(x => new ActivityRow(x.Id, "Expense claim", x.Reference, x.Purpose, x.Status.ToString(), x.SubmittedAtUtc, null)).ToListAsync(ct));
        activity.AddRange(await (from r in _db.HrRequests.AsNoTracking()
                                 join t in _db.HrRequestTypes on r.RequestTypeId equals t.Id
                                 where r.EmployeeId == id && r.Status != RequestStatus.Withdrawn
                                 orderby r.CreatedAtUtc descending
                                 select new ActivityRow(r.Id, "HR request", r.Reference, t.IsSensitive ? t.Name : t.Name + ": " + r.Subject, r.Status.ToString(), r.SubmittedAtUtc, null))
            .Take(10).ToListAsync(ct));
        activity.AddRange(await _db.ProfileChangeRequests.AsNoTracking().Where(x => x.EmployeeId == id).OrderByDescending(x => x.CreatedAtUtc).Take(10)
            .Select(x => new ActivityRow(x.Id, "Profile change", x.Reference, "Change to " + x.Section, x.Status.ToString(), x.CreatedAtUtc, null)).ToListAsync(ct));
        activity.AddRange(await (from t in _db.EmployeeTraining.AsNoTracking()
                                 join p in _db.TrainingPrograms on t.TrainingProgramId equals p.Id
                                 where t.EmployeeId == id
                                 orderby t.CreatedAtUtc descending
                                 select new ActivityRow(t.Id, "Training", t.Reference, p.Title, t.Status.ToString(), t.CreatedAtUtc, null)).Take(10).ToListAsync(ct));

        var soon = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60));
        var docs = await _db.EmployeeDocuments.AsNoTracking().Where(d => d.EmployeeId == id && d.OwnerEntityType == null).Select(d => d.ExpiryDate).ToListAsync(ct);
        var pendingChanges = await _db.ProfileChangeRequests.CountAsync(x => x.EmployeeId == id && (x.Status == RequestStatus.Submitted || x.Status == RequestStatus.InApproval), ct);

        await _audit.WriteAsync("employee.record.viewed", nameof(Employee), id.ToString(), ct: ct);
        return new EmployeeDetail(row, e.Gender, e.TerminationDate, e.WorkEmail, e.MobilePhone, e.ManagerEmployeeId, acc, reports, balances,
            activity.OrderByDescending(a => a.AtUtc).Take(25).ToList(), docs.Count, docs.Count(d => d != null && d <= soon), pendingChanges);
    }

    private async Task<EmployeeRow> RowAsync(IQueryable<Employee> q, CancellationToken ct) =>
        await (from e in q
               join d in _db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
               join l in _db.Locations on e.LocationId equals l.Id into lj from l in lj.DefaultIfEmpty()
               join p in _db.Positions on e.PositionId equals p.Id into pj from p in pj.DefaultIfEmpty()
               join m in _db.Employees on e.ManagerEmployeeId equals m.Id into mj from m in mj.DefaultIfEmpty()
               select new EmployeeRow(e.Id, e.NavKey, e.FirstName + " " + e.LastName, p != null ? p.Title : null, d != null ? d.Name : null, l != null ? l.Name : null,
                   e.Status.ToString(), m != null ? m.FirstName + " " + m.LastName : null, e.EmploymentDate, e.ContractEndDate,
                   _db.EmployeeMappings.Any(x => x.EmployeeId == e.Id), e.LastSyncedAtUtc)).FirstAsync(ct);

    public sealed record NavProfileResponse(NavEmployeeProfile? Profile, bool Available, string? Message);

    /// <summary>The employee's full profile read live from NAV. Bank details arrive masked. Every view is audited.</summary>
    [HttpGet("{id:guid}/profile")]
    public async Task<ActionResult<NavProfileResponse>> Profile(Guid id, [FromServices] INavEmployeeService nav, CancellationToken ct)
    {
        var e = await (await ScopedAsync(ct)).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        var r = await nav.GetProfileAsync(e.CompanyId, e.NavKey, ct);
        await _audit.WriteAsync("employee.profile.viewed", nameof(Employee), id.ToString(), new { e.NavKey }, success: r.Success, ct: ct);
        Response.Headers.CacheControl = "no-store";
        if (r.Success && !string.Equals(r.Value!.Core.No, e.NavKey, StringComparison.OrdinalIgnoreCase))
            return new NavProfileResponse(null, false, "NAV returned a different employee record; contact the administrator.");
        return r.Success ? new NavProfileResponse(r.Value, true, null)
            : new NavProfileResponse(null, false, r.Error!.Category == NavErrorCategory.Configuration
                ? "The detailed NAV profile is not set up yet (see the NAV customisation guide)."
                : "NAV could not be reached. The synced record above is still shown.");
    }

    /// <summary>Re-reads one employee from NAV now, instead of waiting for the next scheduled sync.</summary>
    [HttpPost("{id:guid}/refresh")]
    public async Task<IActionResult> Refresh(Guid id, [FromServices] INavEmployeeService nav, CancellationToken ct)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId, ct);
        if (e is null || !await (await ScopedAsync(ct)).AnyAsync(x => x.Id == id, ct)) return NotFound();
        var r = await nav.GetEmployeeAsync(e.CompanyId, e.NavKey, ct);
        if (!r.Success) throw new NavUnavailableException("NAV could not be reached. Try again later.");
        if (r.Value is null) return Problem(statusCode: 409, title: "Not in NAV", detail: $"Employee {e.NavKey} was not found in NAV.");
        var n = r.Value;
        e.FirstName = n.FirstName;
        e.MiddleName = n.MiddleName;
        e.LastName = n.LastName;
        e.WorkEmail = n.CompanyEmail;
        e.MobilePhone = n.MobilePhone;
        e.EmploymentDate = n.EmploymentDate;
        e.ContractEndDate = n.ContractEndDate;
        e.TerminationDate = n.TerminationDate;
        e.Gender = n.Gender;
        e.LastSyncedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("employee.refreshed", nameof(Employee), id.ToString(), ct: ct);
        return NoContent();
    }
}
