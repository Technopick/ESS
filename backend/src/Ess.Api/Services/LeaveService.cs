using Ess.Application.Abstractions;
using Ess.Application.Integration;
using Ess.Application.Leave;
using Ess.Application.Notifications;
using Ess.Application.Workflow;
using Ess.Domain.Common;
using Ess.Domain.Leave;
using Ess.Domain.Organization;
using Ess.Infrastructure.Persistence;
using Ess.Integration.Nav.Queue;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Services;

public sealed record LeaveInput(Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, bool StartHalfDay, bool EndHalfDay, string? Reason, Guid? RelieverEmployeeId);

public sealed record LeaveCheck(decimal Days, decimal? Available, decimal? AvailableAfter, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, bool BalanceFromCache)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Leave applications (spec §6). The portal validates for a fast answer, then NAV validates again on submission and
/// its answer wins. Balances come from NAV, reduced by applications NAV has not seen yet.
/// </summary>
public sealed class LeaveService
{
    private static readonly RequestStatus[] Active = { RequestStatus.Submitted, RequestStatus.InApproval, RequestStatus.Approved, RequestStatus.ReturnedForCorrection };

    private readonly EssDbContext _db;
    private readonly INavLeaveService _navLeave;
    private readonly INavOrganizationService _navOrg;
    private readonly LeaveBalanceService _balances;
    private readonly IWorkflowEngine _workflow;
    private readonly IIntegrationQueue _queue;
    private readonly IReferenceGenerator _refs;
    private readonly INotificationService _notify;
    private readonly IClock _clock;
    private readonly IConfiguration _config;

    public LeaveService(EssDbContext db, INavLeaveService navLeave, INavOrganizationService navOrg, LeaveBalanceService balances, IWorkflowEngine workflow,
        IIntegrationQueue queue, IReferenceGenerator refs, INotificationService notify, IClock clock, IConfiguration config)
    {
        _db = db;
        _navLeave = navLeave;
        _navOrg = navOrg;
        _balances = balances;
        _workflow = workflow;
        _queue = queue;
        _refs = refs;
        _notify = notify;
        _clock = clock;
        _config = config;
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow.AddHours(_config.GetValue("App:UtcOffsetHours", 3)));

    // ---------------------------------------------------------------- validation

    public async Task<decimal> CountDaysAsync(Guid companyId, LeaveType type, DateOnly start, DateOnly end, bool startHalf, bool endHalf, CancellationToken ct)
    {
        var holidays = new HashSet<DateOnly>(await _db.PublicHolidays.Where(h => h.CompanyId == companyId && h.Date >= start && h.Date <= end).Select(h => h.Date).ToListAsync(ct));
        var nav = await _navOrg.GetNonWorkingDatesAsync(companyId, start, end, ct);
        if (nav.Success) holidays.UnionWith(nav.Value!);
        var weekend = (_config["Leave:NonWorkingDays"] ?? "Saturday,Sunday").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Enum.Parse<DayOfWeek>);
        return new WorkingDayCalculator(weekend).Calculate(start, end, holidays, type.CountsWorkingDaysOnly, startHalf, endHalf);
    }

    public async Task<LeaveCheck> CheckAsync(Employee employee, LeaveInput input, Guid? excludeApplicationId, CancellationToken ct)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var type = await _db.LeaveTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == input.LeaveTypeId && t.CompanyId == employee.CompanyId && t.IsActive, ct);
        if (type is null) return new LeaveCheck(0, null, null, new[] { "Choose a leave type." }, warnings, false);
        if (input.EndDate < input.StartDate) return new LeaveCheck(0, null, null, new[] { "The end date must be on or after the start date." }, warnings, false);
        if (input.EndDate.DayNumber - input.StartDate.DayNumber > 366) return new LeaveCheck(0, null, null, new[] { "A leave application can cover at most one year." }, warnings, false);

        if ((input.StartHalfDay || input.EndHalfDay) && !type.AllowHalfDay) errors.Add($"{type.Name} cannot be taken in half days.");
        if (type.MinNoticeDays > 0 && input.StartDate < Today.AddDays(type.MinNoticeDays))
            errors.Add($"{type.Name} needs at least {type.MinNoticeDays} days' notice.");
        else if (type.MinNoticeDays == 0 && input.StartDate < Today.AddDays(-_config.GetValue("Leave:MaxBackdatedDays", 30)))
            errors.Add("This leave starts too far in the past. Contact HR.");
        else if (type.MinNoticeDays > 0 && input.StartDate < Today)
            errors.Add($"{type.Name} cannot start in the past.");

        var days = await CountDaysAsync(employee.CompanyId, type, input.StartDate, input.EndDate, input.StartHalfDay, input.EndHalfDay, ct);
        if (days <= 0) errors.Add("These dates contain no working days.");
        if (type.MaxDaysPerApplication is { } max && days > max) errors.Add($"{type.Name} allows at most {max} days per application.");
        if (!string.IsNullOrWhiteSpace(type.EligibleGender) && !string.IsNullOrWhiteSpace(employee.Gender)
            && !string.Equals(type.EligibleGender.Trim(), employee.Gender.Trim(), StringComparison.OrdinalIgnoreCase))
            errors.Add($"{type.Name} is not available to you according to your NAV record. Contact HR if this is wrong.");

        var overlapping = await _db.LeaveApplications.AsNoTracking()
            .Where(a => a.EmployeeId == employee.Id && a.Id != excludeApplicationId && Active.Contains(a.Status)
                        && a.StartDate <= input.EndDate && input.StartDate <= a.EndDate)
            .Select(a => a.Reference).ToListAsync(ct);
        if (overlapping.Count > 0) errors.Add($"These dates overlap your application {string.Join(", ", overlapping)}.");

        if (input.RelieverEmployeeId is { } rid)
        {
            if (rid == employee.Id) errors.Add("You cannot be your own reliever.");
            else
            {
                var reliever = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == rid && e.CompanyId == employee.CompanyId, ct);
                if (reliever is null || reliever.Status is not (EmploymentStatus.Active or EmploymentStatus.OnProbation)) errors.Add("Choose an active colleague as reliever.");
                else if (await _db.LeaveApplications.AnyAsync(a => a.EmployeeId == rid && (a.Status == RequestStatus.Approved || a.Status == RequestStatus.InApproval)
                             && a.StartDate <= input.EndDate && input.StartDate <= a.EndDate, ct))
                    warnings.Add($"{reliever.FullName} is also on leave during some of these days.");
            }
        }
        if (type.RequiresAttachment) warnings.Add($"{type.Name} needs a supporting document. Upload it under Documents and give HR the reference.");

        // Balance: NAV's available figure already reflects what NAV knows; subtract portal applications NAV has not received yet.
        var balances = await _balances.GetAsync(employee, ct);
        var bal = balances.Balances.FirstOrDefault(b => b.LeaveTypeId == type.Id);
        decimal? available = null, after = null;
        if (bal is not null)
        {
            var notInNav = await _db.LeaveApplications.AsNoTracking()
                .Where(a => a.EmployeeId == employee.Id && a.LeaveTypeId == type.Id && a.Id != excludeApplicationId && a.NavDocumentNo == null
                            && (a.Status == RequestStatus.Submitted || a.Status == RequestStatus.InApproval || a.Status == RequestStatus.Approved))
                .SumAsync(a => (decimal?)a.DaysRequested, ct) ?? 0;
            available = bal.Available - notInNav;
            after = available - days;
            if (after < 0 && !type.AllowNegativeBalance) errors.Add($"You have {available:0.#} days of {type.Name} available.");
            if (balances.FromCache) warnings.Add("NAV could not be reached, so the balance is from the last update. NAV will check it again when the leave is posted.");
        }
        else warnings.Add("No balance for this leave type was found in NAV. NAV will check it when the leave is posted.");

        // NAV's own dry-run, where the customer exposes one; NAV's day count is authoritative.
        if (errors.Count == 0)
        {
            var nav = await _navLeave.ValidateAsync(employee.CompanyId, Submission(employee, type, input, days, "PREVIEW", "PREVIEW", null, false, null), ct);
            if (nav.Success) days = nav.Value;
            else if (nav.Error!.Category == NavErrorCategory.Validation) errors.Add(nav.Error.Message);
        }
        return new LeaveCheck(days, available, available is null ? null : available - days, errors, warnings, balances.FromCache);
    }

    // ---------------------------------------------------------------- submit, resubmit, cancel

    public async Task<(LeaveApplication? App, LeaveCheck Check)> SubmitAsync(Employee employee, Guid userId, LeaveInput input, CancellationToken ct)
    {
        var check = await CheckAsync(employee, input, null, ct);
        if (!check.IsValid) return (null, check);

        var app = new LeaveApplication
        {
            CompanyId = employee.CompanyId,
            Reference = await _refs.NextAsync(employee.CompanyId, "LV", ct),
            EmployeeId = employee.Id,
            LeaveTypeId = input.LeaveTypeId,
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            StartHalfDay = input.StartHalfDay,
            EndHalfDay = input.EndHalfDay,
            DaysRequested = check.Days,
            Reason = input.Reason?.Trim(),
            RelieverEmployeeId = input.RelieverEmployeeId,
            Status = RequestStatus.Submitted,
            NavStatus = NavSyncStatus.NotRequired,
            SubmittedAtUtc = _clock.UtcNow
        };
        _db.LeaveApplications.Add(app);
        await RouteAsync(employee, userId, app, ct);
        return (app, check);
    }

    public async Task<(LeaveApplication? App, LeaveCheck Check)> ResubmitAsync(Employee employee, Guid userId, Guid id, LeaveInput input, CancellationToken ct)
    {
        var app = await _db.LeaveApplications.FirstOrDefaultAsync(a => a.Id == id && a.EmployeeId == employee.Id, ct) ?? throw new KeyNotFoundException("Leave application not found.");
        if (app.Status != RequestStatus.ReturnedForCorrection) throw new WorkflowException("Only applications returned for correction can be edited.");
        var check = await CheckAsync(employee, input, app.Id, ct);
        if (!check.IsValid) return (null, check);

        app.LeaveTypeId = input.LeaveTypeId;
        app.StartDate = input.StartDate;
        app.EndDate = input.EndDate;
        app.StartHalfDay = input.StartHalfDay;
        app.EndHalfDay = input.EndHalfDay;
        app.DaysRequested = check.Days;
        app.Reason = input.Reason?.Trim();
        app.RelieverEmployeeId = input.RelieverEmployeeId;
        app.Status = RequestStatus.Submitted;
        app.SubmittedAtUtc = _clock.UtcNow;
        await RouteAsync(employee, userId, app, ct);
        return (app, check);
    }

    private async Task RouteAsync(Employee employee, Guid userId, LeaveApplication app, CancellationToken ct)
    {
        var type = await _db.LeaveTypes.AsNoTracking().FirstAsync(t => t.Id == app.LeaveTypeId, ct);
        var summary = $"{employee.FullName}: {type.Name}, {app.DaysRequested:0.#} day{(app.DaysRequested == 1 ? "" : "s")}, {app.StartDate:d MMM} to {app.EndDate:d MMM yyyy}";
        var result = await _workflow.StartAsync(new WorkflowStartRequest(app.CompanyId, DocumentTypes.Leave, app.Id, app.Reference, userId, employee.Id,
            null, summary, $"/leave/{app.Id}"), ct);

        switch (result.Outcome)
        {
            case WorkflowOutcome.Pending:
                app.Status = RequestStatus.InApproval;
                app.WorkflowInstanceId = result.InstanceId;
                break;
            case WorkflowOutcome.Approved:
                app.WorkflowInstanceId = result.InstanceId; // the handler already set Approved and queued it for NAV
                break;
            case WorkflowOutcome.NavOwned:
                // NAV's approval workflow runs; submit now (not pre-approved) and mirror NAV's status.
                app.Status = RequestStatus.InApproval;
                await EnqueueSubmitAsync(employee, type, app, preApproved: false, approvedBy: null, ct);
                break;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task EnqueueSubmitAsync(Employee employee, LeaveType type, LeaveApplication app, bool preApproved, string? approvedBy, CancellationToken ct)
    {
        string? relieverNo = app.RelieverEmployeeId is null ? null
            : await _db.Employees.Where(e => e.Id == app.RelieverEmployeeId).Select(e => e.NavKey).FirstOrDefaultAsync(ct);
        // A resubmission after correction is a new NAV document, so the revision is part of the key.
        var revision = await _db.IntegrationQueue.CountAsync(q => q.EntityId == app.Id && q.Operation == NavOperations.LeaveSubmit, ct);
        var key = IntegrationQueue.KeyFor(NavOperations.LeaveSubmit, app.Id, revision);
        app.NavStatus = NavSyncStatus.PendingIntegration;
        await _queue.EnqueueAsync(app.CompanyId, NavOperations.LeaveSubmit, nameof(LeaveApplication), app.Id, key,
            Submission(employee, type, new LeaveInput(app.LeaveTypeId, app.StartDate, app.EndDate, app.StartHalfDay, app.EndHalfDay, app.Reason, null),
                app.DaysRequested, key, app.Reference, relieverNo, preApproved, approvedBy), sensitive: false, ct: ct);
    }

    public async Task CancelAsync(Employee employee, Guid userId, Guid id, string? reason, CancellationToken ct)
    {
        var app = await _db.LeaveApplications.FirstOrDefaultAsync(a => a.Id == id && a.EmployeeId == employee.Id, ct) ?? throw new KeyNotFoundException("Leave application not found.");
        reason = string.IsNullOrWhiteSpace(reason) ? "Cancelled by the employee" : reason.Trim();

        switch (app.Status)
        {
            case RequestStatus.Submitted or RequestStatus.InApproval or RequestStatus.ReturnedForCorrection when app.NavDocumentNo is null && app.NavStatus is NavSyncStatus.NotRequired or NavSyncStatus.Failed:
                if (app.WorkflowInstanceId is not null) await _workflow.CancelAsync(app.WorkflowInstanceId.Value, userId, reason, ct);
                app.Status = RequestStatus.Cancelled;
                break;
            case RequestStatus.Approved when app.NavStatus is NavSyncStatus.Failed:
                app.Status = RequestStatus.Cancelled; // NAV never accepted it
                break;
            case RequestStatus.Approved or RequestStatus.InApproval when app.NavStatus == NavSyncStatus.Posted && app.NavDocumentNo is not null:
                if (app.StartDate <= Today) throw new WorkflowException("Leave that has started can only be cancelled by HR.");
                var revision = await _db.IntegrationQueue.CountAsync(q => q.EntityId == app.Id && q.Operation == NavOperations.LeaveCancel, ct);
                await _queue.EnqueueAsync(app.CompanyId, NavOperations.LeaveCancel, nameof(LeaveApplication), app.Id,
                    IntegrationQueue.KeyFor(NavOperations.LeaveCancel, app.Id, revision), new { NavDocumentNo = app.NavDocumentNo, Reason = reason }, false, ct: ct);
                app.Status = RequestStatus.Cancelled;
                app.NavStatus = NavSyncStatus.PendingIntegration;
                break;
            case RequestStatus.Approved or RequestStatus.InApproval:
                throw new WorkflowException("This leave is being recorded in NAV. Try again in a few minutes.");
            default:
                throw new WorkflowException("This application can no longer be cancelled.");
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>For document types whose approval NAV owns: copy NAV's decision back to the portal (scheduled).</summary>
    public async Task<int> MirrorNavStatusesAsync(CancellationToken ct)
    {
        var apps = await _db.LeaveApplications
            .Where(a => a.Status == RequestStatus.InApproval && a.WorkflowInstanceId == null && a.NavDocumentNo != null && a.NavStatus == NavSyncStatus.Posted)
            .OrderBy(a => a.SubmittedAtUtc).Take(200).ToListAsync(ct);
        var changed = 0;
        foreach (var a in apps)
        {
            var s = await _navLeave.GetStatusAsync(a.CompanyId, a.NavDocumentNo!, ct);
            if (!s.Success || s.Value is null) continue;
            var status = s.Value.Status.ToLowerInvariant() switch
            {
                "approved" or "released" or "posted" => RequestStatus.Approved,
                "rejected" => RequestStatus.Rejected,
                "cancelled" or "canceled" => RequestStatus.Cancelled,
                _ => (RequestStatus?)null
            };
            if (status is null || status == a.Status) continue;
            a.Status = status.Value;
            changed++;
            var user = await _db.EmployeeMappings.Where(m => m.EmployeeId == a.EmployeeId).Select(m => (Guid?)m.UserId).FirstOrDefaultAsync(ct);
            if (user is not null)
                await _notify.QueueAsync(new NotificationRequest(a.CompanyId, user.Value,
                    status == RequestStatus.Approved ? NotificationEvents.RequestApproved : NotificationEvents.RequestRejected,
                    $"{a.Reference} was {status.Value.ToString().ToLowerInvariant()} in NAV", $"Your leave application {a.Reference} was {status.Value.ToString().ToLowerInvariant()}.", $"/leave/{a.Id}"), ct);
        }
        await _db.SaveChangesAsync(ct);
        return changed;
    }

    private static NavLeaveSubmission Submission(Employee e, LeaveType t, LeaveInput i, decimal days, string key, string reference, string? relieverNo, bool preApproved, string? approvedBy) =>
        new(key, reference, e.NavKey, t.NavKey, i.StartDate, i.EndDate, days, i.Reason, relieverNo, preApproved, approvedBy);
}
