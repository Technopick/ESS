using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Api.Services;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Domain.Common;
using Ess.Domain.Leave;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers.Hr;

/// <summary>
/// Leave administration (spec §6): monitor every application in scope, set the portal-side rules for each NAV leave type,
/// and keep the public holiday list used to count working days. NAV still validates every application when it is posted.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/hr/leave")]
[Route("api/hr/leave")]
public sealed class HrLeaveController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly ICurrentUser _user;
    private readonly EmployeeContext _me;
    private readonly IAuditWriter _audit;

    public HrLeaveController(EssDbContext db, ICurrentUser user, EmployeeContext me, IAuditWriter audit)
    {
        _db = db;
        _user = user;
        _me = me;
        _audit = audit;
    }

    private Guid CompanyId => _user.CompanyId ?? throw new UnauthorizedAccessException();

    public sealed record ApplicationRow(Guid Id, string Reference, Guid EmployeeId, string Employee, string? Department, string LeaveType, DateOnly StartDate, DateOnly EndDate,
        decimal Days, RequestStatus Status, NavSyncStatus NavStatus, string? NavDocumentNo, DateTime? SubmittedAtUtc, Guid? WorkflowInstanceId, string? PendingWith);

    [HttpGet("applications")]
    [HasPermission(Permissions.LeaveViewAll)]
    public async Task<PagedResult<ApplicationRow>> Applications([FromQuery] RequestStatus? status, [FromQuery] Guid? leaveTypeId, [FromQuery] Guid? departmentId,
        [FromQuery(Name = "from")] DateOnly? start, [FromQuery(Name = "to")] DateOnly? end, [FromQuery] string? search, [FromQuery] NavSyncStatus? navStatus,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var scope = await _me.DepartmentScopeAsync(Permissions.LeaveViewAll, ct);
        var q = from a in _db.LeaveApplications.AsNoTracking()
                join e in _db.Employees on a.EmployeeId equals e.Id
                join t in _db.LeaveTypes on a.LeaveTypeId equals t.Id
                join d in _db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                where a.CompanyId == CompanyId && a.Status != RequestStatus.Draft
                select new { a, e, t, Dept = d };
        if (scope is not null) q = q.Where(x => x.e.DepartmentId != null && scope.Contains(x.e.DepartmentId.Value));
        if (status is not null) q = q.Where(x => x.a.Status == status);
        if (navStatus is not null) q = q.Where(x => x.a.NavStatus == navStatus);
        if (leaveTypeId is not null) q = q.Where(x => x.a.LeaveTypeId == leaveTypeId);
        if (departmentId is not null) q = q.Where(x => x.e.DepartmentId == departmentId);
        if (start is not null) q = q.Where(x => x.a.EndDate >= start);
        if (end is not null) q = q.Where(x => x.a.StartDate <= end);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(x => (x.e.FirstName + " " + x.e.LastName).ToLower().Contains(s) || x.a.Reference.ToLower().Contains(s) || x.e.NavKey.ToLower().Contains(s));
        }
        var projected = q.OrderByDescending(x => x.a.SubmittedAtUtc).Select(x => new ApplicationRow(x.a.Id, x.a.Reference, x.e.Id, x.e.FirstName + " " + x.e.LastName,
            x.Dept != null ? x.Dept.Name : null, x.t.Name, x.a.StartDate, x.a.EndDate, x.a.DaysRequested, x.a.Status, x.a.NavStatus, x.a.NavDocumentNo, x.a.SubmittedAtUtc,
            x.a.WorkflowInstanceId,
            (from tk in _db.ApprovalTasks
             join u in _db.Users on tk.AssigneeUserId equals u.Id
             join m in _db.EmployeeMappings on u.Id equals m.UserId into mj from m in mj.DefaultIfEmpty()
             join ae in _db.Employees on m.EmployeeId equals ae.Id into aej from ae in aej.DefaultIfEmpty()
             where tk.WorkflowInstanceId == x.a.WorkflowInstanceId && tk.Status == Domain.Workflow.ApprovalTaskStatus.Pending
             select ae != null ? ae.FirstName + " " + ae.LastName : u.UserName).FirstOrDefault()));
        return await PagedResult<ApplicationRow>.CreateAsync(projected, page, pageSize, ct);
    }

    public sealed record LeaveTypeRow(Guid Id, string Code, string Name, bool CountsWorkingDaysOnly, bool RequiresAttachment, int? MaxDaysPerApplication, int MinNoticeDays,
        bool AllowNegativeBalance, bool AllowHalfDay, string? EligibleGender, bool IsActive, DateTime LastSyncedAtUtc);

    [HttpGet("types")]
    [HasPermission(Permissions.LeaveConfigure)]
    public async Task<IReadOnlyList<LeaveTypeRow>> Types(CancellationToken ct) =>
        await _db.LeaveTypes.AsNoTracking().Where(t => t.CompanyId == CompanyId).OrderBy(t => t.Name)
            .Select(t => new LeaveTypeRow(t.Id, t.NavKey, t.Name, t.CountsWorkingDaysOnly, t.RequiresAttachment, t.MaxDaysPerApplication, t.MinNoticeDays, t.AllowNegativeBalance,
                t.AllowHalfDay, t.EligibleGender, t.IsActive, t.LastSyncedAtUtc)).ToListAsync(ct);

    public sealed record LeaveTypeRules(bool CountsWorkingDaysOnly, bool RequiresAttachment, int? MaxDaysPerApplication, int MinNoticeDays, bool AllowNegativeBalance,
        bool AllowHalfDay, string? EligibleGender, bool IsActive);

    /// <summary>Portal rules for a NAV leave type. Code, name and entitlement stay in NAV; NAV re-checks the balance when the leave is posted.</summary>
    [HttpPut("types/{id:guid}")]
    [HasPermission(Permissions.LeaveConfigure)]
    public async Task<IActionResult> UpdateType(Guid id, LeaveTypeRules input, CancellationToken ct)
    {
        var t = await _db.LeaveTypes.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId, ct);
        if (t is null) return NotFound();
        var errors = new Dictionary<string, string[]>();
        if (input.MaxDaysPerApplication is < 1 or > 365) errors["maxDaysPerApplication"] = new[] { "Use 1 to 365 days, or leave it empty for no limit." };
        if (input.MinNoticeDays is < 0 or > 365) errors["minNoticeDays"] = new[] { "Use 0 to 365 days." };
        if (input.EligibleGender is { Length: > 20 }) errors["eligibleGender"] = new[] { "Use the value NAV holds, for example Female or Male." };
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));
        var before = new { t.CountsWorkingDaysOnly, t.RequiresAttachment, t.MaxDaysPerApplication, t.MinNoticeDays, t.AllowNegativeBalance, t.AllowHalfDay, t.EligibleGender, t.IsActive };
        t.CountsWorkingDaysOnly = input.CountsWorkingDaysOnly;
        t.RequiresAttachment = input.RequiresAttachment;
        t.MaxDaysPerApplication = input.MaxDaysPerApplication;
        t.MinNoticeDays = input.MinNoticeDays;
        t.AllowNegativeBalance = input.AllowNegativeBalance;
        t.AllowHalfDay = input.AllowHalfDay;
        t.EligibleGender = string.IsNullOrWhiteSpace(input.EligibleGender) ? null : input.EligibleGender.Trim();
        t.IsActive = input.IsActive;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("leave.type.rules.changed", nameof(LeaveType), id.ToString(), new { before, after = input }, ct: ct);
        return NoContent();
    }

    public sealed record HolidayRow(Guid Id, DateOnly Date, string Name, bool FromNav);

    [HttpGet("holidays")]
    [HasPermission(Permissions.LeaveViewAll)]
    public async Task<IReadOnlyList<HolidayRow>> Holidays([FromQuery] int? year, CancellationToken ct)
    {
        var y = year ?? DateTime.UtcNow.Year;
        return await _db.PublicHolidays.AsNoTracking().Where(h => h.CompanyId == CompanyId && h.Date.Year == y).OrderBy(h => h.Date)
            .Select(h => new HolidayRow(h.Id, h.Date, h.Name, h.NavCalendarCode != null)).ToListAsync(ct);
    }

    public sealed record HolidayInput(DateOnly Date, string Name);

    [HttpPost("holidays")]
    [HasPermission(Permissions.LeaveConfigure)]
    public async Task<IActionResult> AddHoliday(HolidayInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["name"] = new[] { "Name the holiday (up to 100 characters)." } }));
        if (await _db.PublicHolidays.AnyAsync(h => h.CompanyId == CompanyId && h.Date == input.Date, ct))
            return Problem(statusCode: 409, title: "Already a holiday", detail: $"{input.Date:d MMM yyyy} is already in the list.");
        var h = new PublicHoliday { CompanyId = CompanyId, Date = input.Date, Name = input.Name.Trim() };
        _db.PublicHolidays.Add(h);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("leave.holiday.added", nameof(PublicHoliday), h.Id.ToString(), new { h.Date, h.Name }, ct: ct);
        return Created($"/api/hr/leave/holidays/{h.Id}", new HolidayRow(h.Id, h.Date, h.Name, false));
    }

    [HttpDelete("holidays/{id:guid}")]
    [HasPermission(Permissions.LeaveConfigure)]
    public async Task<IActionResult> RemoveHoliday(Guid id, CancellationToken ct)
    {
        var h = await _db.PublicHolidays.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId, ct);
        if (h is null) return NotFound();
        if (h.NavCalendarCode is not null) return Problem(statusCode: 409, title: "Kept in NAV", detail: "This holiday comes from the NAV base calendar. Remove it there.");
        h.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("leave.holiday.removed", nameof(PublicHoliday), id.ToString(), new { h.Date, h.Name }, ct: ct);
        return NoContent();
    }
}
