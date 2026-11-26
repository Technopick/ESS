using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Api.Services;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Domain.Common;
using Ess.Domain.Organization;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/leave")]
[Route("api/leave")]
public sealed class LeaveController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly EmployeeContext _me;
    private readonly ICurrentUser _user;
    private readonly LeaveService _leave;

    public LeaveController(EssDbContext db, EmployeeContext me, ICurrentUser user, LeaveService leave)
    {
        _db = db;
        _me = me;
        _user = user;
        _leave = leave;
    }

    public sealed record LeaveTypeView(Guid Id, string Code, string Name, bool AllowHalfDay, bool RequiresAttachment, int MinNoticeDays, int? MaxDaysPerApplication, bool CountsWorkingDaysOnly);

    public sealed record LeaveView(Guid Id, string Reference, Guid LeaveTypeId, string LeaveType, DateOnly StartDate, DateOnly EndDate, bool StartHalfDay, bool EndHalfDay,
        decimal Days, string? Reason, Guid? RelieverEmployeeId, string? Reliever, RequestStatus Status, NavSyncStatus NavStatus, string? NavDocumentNo,
        Guid? WorkflowInstanceId, DateTime? SubmittedAtUtc, DateTime CreatedAtUtc, string? EmployeeName = null);

    [HttpGet("types")]
    [HasPermission(Permissions.LeaveViewOwn)]
    public async Task<IReadOnlyList<LeaveTypeView>> Types(CancellationToken ct)
    {
        var companyId = _user.CompanyId ?? Guid.Empty;
        return await _db.LeaveTypes.AsNoTracking().Where(t => t.CompanyId == companyId && t.IsActive).OrderBy(t => t.Name)
            .Select(t => new LeaveTypeView(t.Id, t.NavKey, t.Name, t.AllowHalfDay, t.RequiresAttachment, t.MinNoticeDays, t.MaxDaysPerApplication, t.CountsWorkingDaysOnly))
            .ToListAsync(ct);
    }

    [HttpGet("balances")]
    [HasPermission(Permissions.LeaveViewOwn)]
    public async Task<LeaveBalancesResult> Balances([FromServices] LeaveBalanceService balances, CancellationToken ct) =>
        await balances.GetAsync(await _me.RequireAsync(ct), ct);

    /// <summary>Checks an application without saving it: days, balance after, errors and warnings.</summary>
    [HttpPost("check")]
    [HasPermission(Permissions.LeaveApply)]
    public async Task<LeaveCheck> Check(LeaveInput input, [FromQuery] Guid? applicationId, CancellationToken ct) =>
        await _leave.CheckAsync(await _me.RequireAsync(ct), input, applicationId, ct);

    [HttpPost]
    [HasPermission(Permissions.LeaveApply)]
    public async Task<IActionResult> Submit(LeaveInput input, CancellationToken ct)
    {
        var (app, check) = await _leave.SubmitAsync(await _me.RequireAsync(ct), _user.UserId!.Value, input, ct);
        if (app is null) return Problem(check);
        return Created($"/api/leave/{app.Id}", await GetView(app.Id, ct));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.LeaveApply)]
    public async Task<IActionResult> Resubmit(Guid id, LeaveInput input, CancellationToken ct)
    {
        var (app, check) = await _leave.ResubmitAsync(await _me.RequireAsync(ct), _user.UserId!.Value, id, input, ct);
        if (app is null) return Problem(check);
        return Ok(await GetView(app.Id, ct));
    }

    public sealed record CancelInput(string? Reason);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.LeaveApply)]
    public async Task<LeaveView> Cancel(Guid id, CancelInput input, CancellationToken ct)
    {
        await _leave.CancelAsync(await _me.RequireAsync(ct), _user.UserId!.Value, id, input.Reason, ct);
        return (await GetView(id, ct))!;
    }

    [HttpGet("mine")]
    [HasPermission(Permissions.LeaveViewOwn)]
    public async Task<PagedResult<LeaveView>> Mine([FromQuery] int? year, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var e = await _me.RequireAsync(ct);
        var q = Views(_db.LeaveApplications.AsNoTracking().Where(a => a.EmployeeId == e.Id && (year == null || a.StartDate.Year == year)));
        return await PagedResult<LeaveView>.CreateAsync(q.OrderByDescending(v => v.StartDate), page, pageSize, ct);
    }

    /// <summary>One application: the employee's own, or one the caller may see as approver, manager or HR.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LeaveView>> Get(Guid id, CancellationToken ct)
    {
        var app = await _db.LeaveApplications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.CompanyId == _user.CompanyId, ct);
        if (app is null || !await CanSeeAsync(app.EmployeeId, app.WorkflowInstanceId, ct)) return NotFound();
        return (await GetView(id, ct))!;
    }

    public sealed record CalendarEntry(Guid EmployeeId, string EmployeeName, string? Department, DateOnly StartDate, DateOnly EndDate,
        bool StartHalfDay, bool EndHalfDay, string? LeaveType, RequestStatus Status);

    /// <summary>
    /// Team calendar. Managers and HR (leave.view.team / leave.view.all) see their team's approved and pending leave with the type.
    /// Others see approved leave in their own department without the type, since some types (e.g. sick leave) are health data under the Data Protection Act.
    /// </summary>
    [HttpGet("calendar")]
    [HasPermission(Permissions.LeaveViewOwn)]
    public async Task<IReadOnlyList<CalendarEntry>> Calendar([FromQuery(Name = "from")] DateOnly start, [FromQuery(Name = "to")] DateOnly end, [FromQuery] Guid? departmentId, CancellationToken ct)
    {
        if (end < start || end.DayNumber - start.DayNumber > 92) end = start.AddDays(92);
        var me = await _me.RequireAsync(ct);
        IQueryable<Employee> people;
        var detailed = true;
        if (_user.HasPermission(Permissions.LeaveViewAll))
        {
            var scope = await _me.DepartmentScopeAsync(Permissions.LeaveViewAll, ct);
            people = _db.Employees.Where(e => e.CompanyId == me.CompanyId && (scope == null || (e.DepartmentId != null && scope.Contains(e.DepartmentId.Value))));
        }
        else if (_user.HasPermission(Permissions.LeaveViewTeam))
        {
            var reports = await ReportsOfAsync(me.Id, ct);
            people = _db.Employees.Where(e => reports.Contains(e.Id) || e.Id == me.Id);
        }
        else
        {
            detailed = false;
            people = _db.Employees.Where(e => e.CompanyId == me.CompanyId && e.DepartmentId == me.DepartmentId && me.DepartmentId != null);
        }
        if (departmentId is not null) people = people.Where(e => e.DepartmentId == departmentId);

        var statuses = detailed ? new[] { RequestStatus.Approved, RequestStatus.InApproval } : new[] { RequestStatus.Approved };
        return await (from a in _db.LeaveApplications.AsNoTracking()
                      join e in people on a.EmployeeId equals e.Id
                      join t in _db.LeaveTypes on a.LeaveTypeId equals t.Id
                      join d in _db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                      where statuses.Contains(a.Status) && a.StartDate <= end && a.EndDate >= start
                      orderby a.StartDate
                      select new CalendarEntry(e.Id, e.FirstName + " " + e.LastName, d != null ? d.Name : null, a.StartDate, a.EndDate, a.StartHalfDay, a.EndHalfDay,
                          detailed || e.Id == me.Id ? t.Name : null, a.Status)).Take(1000).ToListAsync(ct);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Direct and indirect reports (up to 6 levels), from the NAV manager field.</summary>
    private async Task<List<Guid>> ReportsOfAsync(Guid managerId, CancellationToken ct)
    {
        var all = new List<Guid>();
        var level = new List<Guid> { managerId };
        for (var i = 0; i < 6 && level.Count > 0; i++)
        {
            level = await _db.Employees.Where(e => e.ManagerEmployeeId != null && level.Contains(e.ManagerEmployeeId.Value) && !all.Contains(e.Id))
                .Select(e => e.Id).ToListAsync(ct);
            all.AddRange(level);
        }
        return all;
    }

    private async Task<bool> CanSeeAsync(Guid employeeId, Guid? instanceId, CancellationToken ct)
    {
        var me = await _me.GetAsync(ct);
        if (me?.Id == employeeId) return true;
        if (instanceId is not null && await _db.ApprovalTasks.AnyAsync(t => t.WorkflowInstanceId == instanceId && t.AssigneeUserId == _user.UserId, ct)) return true;
        if (_user.HasPermission(Permissions.LeaveViewAll))
        {
            var scope = await _me.DepartmentScopeAsync(Permissions.LeaveViewAll, ct);
            if (scope is null) return true;
            var dept = await _db.Employees.Where(e => e.Id == employeeId).Select(e => e.DepartmentId).FirstOrDefaultAsync(ct);
            if (dept is not null && scope.Contains(dept.Value)) return true;
        }
        if (me is not null && _user.HasPermission(Permissions.LeaveViewTeam) && (await ReportsOfAsync(me.Id, ct)).Contains(employeeId)) return true;
        return false;
    }

    private IQueryable<LeaveView> Views(IQueryable<Domain.Leave.LeaveApplication> q) =>
        from a in q
        join t in _db.LeaveTypes on a.LeaveTypeId equals t.Id
        join e in _db.Employees on a.EmployeeId equals e.Id
        join r in _db.Employees on a.RelieverEmployeeId equals r.Id into rj from r in rj.DefaultIfEmpty()
        select new LeaveView(a.Id, a.Reference, a.LeaveTypeId, t.Name, a.StartDate, a.EndDate, a.StartHalfDay, a.EndHalfDay, a.DaysRequested, a.Reason,
            a.RelieverEmployeeId, r != null ? r.FirstName + " " + r.LastName : null, a.Status, a.NavStatus, a.NavDocumentNo, a.WorkflowInstanceId,
            a.SubmittedAtUtc, a.CreatedAtUtc, e.FirstName + " " + e.LastName);

    private Task<LeaveView?> GetView(Guid id, CancellationToken ct) =>
        Views(_db.LeaveApplications.AsNoTracking().Where(a => a.Id == id)).FirstOrDefaultAsync(ct);

    private IActionResult Problem(LeaveCheck check) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["leave"] = check.Errors.ToArray() }) { Detail = string.Join(" ", check.Errors) });
}
