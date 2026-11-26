using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Api.Services;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Application.Workflow;
using Ess.Domain.Attendance;
using Ess.Domain.Common;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers;

/// <summary>
/// Attendance (spec §6): web clock-in/out, own history, team view for managers, and corrections or overtime through approval.
/// Location is stored only when Attendance:CaptureLocation is enabled and the employee's browser shares it (Data Protection Act: minimisation).
/// Biometric imports and NAV time sheets are integrated in a later phase.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/attendance")]
[Route("api/attendance")]
public sealed class AttendanceController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly EmployeeContext _me;
    private readonly ICurrentUser _user;
    private readonly IConfiguration _config;

    public AttendanceController(EssDbContext db, EmployeeContext me, ICurrentUser user, IConfiguration config)
    {
        _db = db;
        _me = me;
        _user = user;
        _config = config;
    }

    private int Offset => _config.GetValue("App:UtcOffsetHours", 3);
    private DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(utc.AddHours(Offset));

    public sealed record RecordView(Guid Id, DateOnly WorkDate, DateTime? ClockInUtc, DateTime? ClockOutUtc, int LateMinutes, decimal WorkedHours, AttendanceSource Source,
        string? EmployeeName = null);
    public sealed record ClockInput(string Action, decimal? Latitude, decimal? Longitude);
    public sealed record TodayView(RecordView? Today, string ShiftName, TimeOnly ShiftStart, TimeOnly ShiftEnd);

    private async Task<Shift> ShiftAsync(Guid companyId, CancellationToken ct) =>
        await _db.Shifts.AsNoTracking().Where(s => s.CompanyId == companyId).OrderBy(s => s.Code).FirstOrDefaultAsync(ct)
        ?? new Shift
        {
            CompanyId = companyId, Code = "DEFAULT", Name = "Standard day",
            StartTime = TimeOnly.Parse(_config["Attendance:ShiftStart"] ?? "08:00"), EndTime = TimeOnly.Parse(_config["Attendance:ShiftEnd"] ?? "17:00"),
            GraceMinutes = _config.GetValue("Attendance:GraceMinutes", 15)
        };

    [HttpGet("today")]
    [HasPermission(Permissions.AttendanceClock)]
    public async Task<TodayView> Today(CancellationToken ct)
    {
        var me = await _me.RequireAsync(ct);
        var shift = await ShiftAsync(me.CompanyId, ct);
        var today = LocalDate(DateTime.UtcNow);
        var rec = await _db.AttendanceRecords.AsNoTracking().FirstOrDefaultAsync(a => a.EmployeeId == me.Id && a.WorkDate == today, ct);
        return new TodayView(rec is null ? null : View(rec), shift.Name, shift.StartTime, shift.EndTime);
    }

    [HttpPost("clock")]
    [HasPermission(Permissions.AttendanceClock)]
    public async Task<ActionResult<RecordView>> Clock(ClockInput input, CancellationToken ct)
    {
        var me = await _me.RequireAsync(ct);
        var now = DateTime.UtcNow;
        var today = LocalDate(now);
        var rec = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.EmployeeId == me.Id && a.WorkDate == today, ct);
        var capture = _config.GetValue("Attendance:CaptureLocation", false);

        if (input.Action == "in")
        {
            if (rec?.ClockInUtc is not null) return Problem(statusCode: 409, title: "Already clocked in", detail: $"You clocked in at {rec.ClockInUtc.Value.AddHours(Offset):HH:mm}.");
            var shift = await ShiftAsync(me.CompanyId, ct);
            var start = today.ToDateTime(shift.StartTime).AddHours(-Offset);
            rec ??= _db.AttendanceRecords.Add(new AttendanceRecord { CompanyId = me.CompanyId, EmployeeId = me.Id, WorkDate = today, Source = AttendanceSource.Portal }).Entity;
            rec.ClockInUtc = now;
            rec.ShiftCode = shift.Code;
            rec.LateMinutes = Math.Max(0, (int)(now - start).TotalMinutes - shift.GraceMinutes);
            if (capture) { rec.Latitude = input.Latitude; rec.Longitude = input.Longitude; }
        }
        else if (input.Action == "out")
        {
            if (rec?.ClockInUtc is null) return Problem(statusCode: 409, title: "Not clocked in", detail: "Clock in first, or request a correction for a missed clock-in.");
            if (rec.ClockOutUtc is not null) return Problem(statusCode: 409, title: "Already clocked out", detail: $"You clocked out at {rec.ClockOutUtc.Value.AddHours(Offset):HH:mm}.");
            rec.ClockOutUtc = now;
            rec.WorkedHours = Math.Round((decimal)(now - rec.ClockInUtc.Value).TotalHours, 2);
        }
        else return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["action"] = new[] { "Use \"in\" or \"out\"." } }));

        rec.SourceDeviceId = Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua[..Math.Min(ua.Length, 250)] : null;
        await _db.SaveChangesAsync(ct);
        return View(rec);
    }

    [HttpGet("mine")]
    [HasPermission(Permissions.AttendanceClock)]
    public async Task<IReadOnlyList<RecordView>> Mine([FromQuery(Name = "from")] DateOnly start, [FromQuery(Name = "to")] DateOnly end, CancellationToken ct)
    {
        var me = await _me.RequireAsync(ct);
        if (end < start || end.DayNumber - start.DayNumber > 92) end = start.AddDays(92);
        var rows = await _db.AttendanceRecords.AsNoTracking().Where(a => a.EmployeeId == me.Id && a.WorkDate >= start && a.WorkDate <= end).OrderByDescending(a => a.WorkDate).ToListAsync(ct);
        return rows.Select(r => View(r)).ToList();
    }

    /// <summary>Managers: their direct and indirect reports' attendance for a day (attendance.view.team); HR sees everyone (attendance.manage).</summary>
    [HttpGet("team")]
    [HasPermission(Permissions.AttendanceViewTeam)]
    public async Task<IReadOnlyList<RecordView>> Team([FromQuery] DateOnly? date, CancellationToken ct)
    {
        var me = await _me.RequireAsync(ct);
        var day = date ?? LocalDate(DateTime.UtcNow);
        var ids = new List<Guid>();
        var level = new List<Guid> { me.Id };
        for (var i = 0; i < 6 && level.Count > 0; i++)
        {
            level = await _db.Employees.Where(e => e.ManagerEmployeeId != null && level.Contains(e.ManagerEmployeeId.Value)).Select(e => e.Id).ToListAsync(ct);
            ids.AddRange(level);
        }
        var rows = await (from a in _db.AttendanceRecords.AsNoTracking()
                          join e in _db.Employees on a.EmployeeId equals e.Id
                          where ids.Contains(a.EmployeeId) && a.WorkDate == day
                          orderby e.LastName
                          select new { a, Name = e.FirstName + " " + e.LastName }).ToListAsync(ct);
        return rows.Select(r => View(r.a, r.Name)).ToList();
    }

    public sealed record RequestInput(AttendanceRequestType Type, DateOnly WorkDate, TimeOnly? ClockIn, TimeOnly? ClockOut, decimal? OvertimeHours, string Reason);
    public sealed record RequestView(Guid Id, string Reference, AttendanceRequestType Type, DateOnly WorkDate, DateTime? RequestedClockInUtc, DateTime? RequestedClockOutUtc,
        decimal? OvertimeHours, string Reason, RequestStatus Status, Guid? WorkflowInstanceId, DateTime CreatedAtUtc);

    [HttpPost("requests")]
    [HasPermission(Permissions.AttendanceClock)]
    public async Task<IActionResult> CreateRequest(RequestInput input, [FromServices] IReferenceGenerator refs, [FromServices] IWorkflowEngine workflow, CancellationToken ct)
    {
        var me = await _me.RequireAsync(ct);
        var today = LocalDate(DateTime.UtcNow);
        var errors = new List<string>();
        if (input.WorkDate > today) errors.Add("The date cannot be in the future.");
        if (input.WorkDate < today.AddDays(-_config.GetValue("Attendance:MaxCorrectionDays", 30))) errors.Add("That date is too far back. Contact HR.");
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 500) errors.Add("Give a reason (up to 500 characters).");
        if (input.Type == AttendanceRequestType.Overtime && input.OvertimeHours is not (> 0 and <= 16)) errors.Add("Enter overtime between 0 and 16 hours.");
        if (input.Type != AttendanceRequestType.Overtime && input.ClockIn is null && input.ClockOut is null) errors.Add("Enter the correct clock-in or clock-out time.");
        if (input.ClockIn is not null && input.ClockOut is not null && input.ClockOut <= input.ClockIn) errors.Add("Clock-out must be after clock-in.");
        if (await _db.AttendanceRequests.AnyAsync(r => r.EmployeeId == me.Id && r.WorkDate == input.WorkDate && r.Type == input.Type
                && (r.Status == RequestStatus.Submitted || r.Status == RequestStatus.InApproval), ct))
            errors.Add("You already have a pending request of this kind for that day.");
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["request"] = errors.ToArray() }) { Detail = string.Join(" ", errors) });

        DateTime? Utc(TimeOnly? t) => t is null ? null : DateTime.SpecifyKind(input.WorkDate.ToDateTime(t.Value).AddHours(-Offset), DateTimeKind.Utc);
        var r = new AttendanceRequest
        {
            CompanyId = me.CompanyId, Reference = await refs.NextAsync(me.CompanyId, "ATT", ct), EmployeeId = me.Id, Type = input.Type, WorkDate = input.WorkDate,
            RequestedClockInUtc = Utc(input.ClockIn), RequestedClockOutUtc = Utc(input.ClockOut), OvertimeHours = input.OvertimeHours, Reason = input.Reason.Trim(),
            Status = RequestStatus.Submitted, NavStatus = NavSyncStatus.NotRequired
        };
        _db.AttendanceRequests.Add(r);
        var label = input.Type switch { AttendanceRequestType.Overtime => "overtime", AttendanceRequestType.MissingPunch => "missed clock", _ => "attendance correction" };
        var result = await workflow.StartAsync(new WorkflowStartRequest(me.CompanyId, DocumentTypes.Attendance, r.Id, r.Reference, _user.UserId!.Value, me.Id,
            null, $"{me.FullName}: {label} on {input.WorkDate:d MMM}", "/attendance"), ct);
        r.WorkflowInstanceId = result.InstanceId;
        if (result.Outcome == WorkflowOutcome.Pending) r.Status = RequestStatus.InApproval;
        await _db.SaveChangesAsync(ct);
        return Created($"/api/attendance/requests/{r.Id}", ToView(r));
    }

    [HttpGet("requests/mine")]
    [HasPermission(Permissions.AttendanceClock)]
    public async Task<IReadOnlyList<RequestView>> MyRequests(CancellationToken ct)
    {
        var me = await _me.RequireAsync(ct);
        var rows = await _db.AttendanceRequests.AsNoTracking().Where(r => r.EmployeeId == me.Id).OrderByDescending(r => r.CreatedAtUtc).Take(100).ToListAsync(ct);
        return rows.Select(ToView).ToList();
    }

    private static RequestView ToView(AttendanceRequest r) =>
        new(r.Id, r.Reference, r.Type, r.WorkDate, r.RequestedClockInUtc, r.RequestedClockOutUtc, r.OvertimeHours, r.Reason, r.Status, r.WorkflowInstanceId, r.CreatedAtUtc);

    private static RecordView View(AttendanceRecord r, string? name = null) =>
        new(r.Id, r.WorkDate, r.ClockInUtc, r.ClockOutUtc, r.LateMinutes, r.WorkedHours, r.Source, name);
}
