using Asp.Versioning;
using Ess.Api.Services;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Domain.Common;
using Ess.Domain.Organization;
using Ess.Domain.Workflow;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ess.Api.Controllers;

/// <summary>Personal dashboard (spec §4.1). Every figure comes from NAV or real portal records; empty lists mean "nothing yet".</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dashboard")]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly EmployeeContext _me;
    private readonly ICurrentUser _user;
    private readonly IMemoryCache _cache;

    public DashboardController(EssDbContext db, EmployeeContext me, ICurrentUser user, IMemoryCache cache)
    {
        _db = db;
        _me = me;
        _user = user;
        _cache = cache;
    }

    public sealed record UpcomingLeave(Guid Id, string Reference, string LeaveType, DateOnly StartDate, DateOnly EndDate, decimal Days, string Status);
    public sealed record PayslipPeriod(Guid PeriodId, string Name, DateOnly? PayDate);
    public sealed record AnnouncementView(Guid Id, string Title, string BodyHtml, DateTime PublishAtUtc, bool IsPinned);
    public sealed record Celebration(string Name, string Kind, DateOnly Date, int? Years, string? Department);
    public sealed record PendingCounts(int MyOpenRequests, int MyPendingLeave, int ApprovalsWaitingForMe, int UnreadNotifications);

    public sealed record DashboardResponse(
        EmployeesController.EmployeeSummary? Employee,
        LeaveBalancesResult? Leave,
        IReadOnlyList<UpcomingLeave> UpcomingLeave,
        IReadOnlyList<PayslipPeriod> RecentPayslips,
        PendingCounts Pending,
        IReadOnlyList<AnnouncementView> Announcements,
        IReadOnlyList<Celebration> Celebrations);

    [HttpGet("me")]
    public async Task<DashboardResponse> Me([FromServices] LeaveBalanceService balances, CancellationToken ct)
    {
        var e = await _me.GetAsync(ct);
        var companyId = _user.CompanyId ?? Guid.Empty;
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)); // East Africa Time for "today"

        EmployeesController.EmployeeSummary? summary = null;
        LeaveBalancesResult? leave = null;
        var upcoming = new List<UpcomingLeave>();
        var payslips = new List<PayslipPeriod>();
        int openRequests = 0, pendingLeave = 0;

        if (e is not null)
        {
            summary = await (from x in _db.Employees.AsNoTracking().Where(x => x.Id == e.Id)
                             join d in _db.Departments on x.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                             join l in _db.Locations on x.LocationId equals l.Id into lj from l in lj.DefaultIfEmpty()
                             join p in _db.Positions on x.PositionId equals p.Id into pj from p in pj.DefaultIfEmpty()
                             join m in _db.Employees on x.ManagerEmployeeId equals m.Id into mj from m in mj.DefaultIfEmpty()
                             select new EmployeesController.EmployeeSummary(x.Id, x.NavKey, x.FirstName + " " + x.LastName, p != null ? p.Title : null,
                                 d != null ? d.Name : null, l != null ? l.Name : null, x.ManagerEmployeeId, m != null ? m.FirstName + " " + m.LastName : null,
                                 x.Status.ToString(), x.EmploymentDate, x.ContractEndDate, x.WorkEmail, x.LastSyncedAtUtc)).FirstAsync(ct);

            if (_user.HasPermission(Permissions.LeaveViewOwn)) leave = await balances.GetAsync(e, ct);

            upcoming = await (from a in _db.LeaveApplications.AsNoTracking()
                              join t in _db.LeaveTypes on a.LeaveTypeId equals t.Id
                              where a.EmployeeId == e.Id && a.EndDate >= today
                                    && (a.Status == RequestStatus.Approved || a.Status == RequestStatus.InApproval || a.Status == RequestStatus.Submitted)
                              orderby a.StartDate
                              select new UpcomingLeave(a.Id, a.Reference, t.Name, a.StartDate, a.EndDate, a.DaysRequested, a.Status.ToString()))
                .Take(3).ToListAsync(ct);

            if (_user.HasPermission(Permissions.PayrollViewOwn))
                payslips = await _db.PayrollPeriodsCache.AsNoTracking()
                    .Where(p => p.CompanyId == e.CompanyId && p.IsPublished && p.IsClosed)
                    .OrderByDescending(p => p.StartDate).Take(3)
                    .Select(p => new PayslipPeriod(p.Id, p.Name, p.PayDate)).ToListAsync(ct);

            var open = new[] { RequestStatus.Submitted, RequestStatus.InApproval, RequestStatus.ReturnedForCorrection };
            openRequests = await _db.HrRequests.CountAsync(r => r.EmployeeId == e.Id && open.Contains(r.Status), ct)
                           + await _db.ProfileChangeRequests.CountAsync(r => r.EmployeeId == e.Id && open.Contains(r.Status), ct)
                           + await _db.ExpenseClaims.CountAsync(r => r.EmployeeId == e.Id && open.Contains(r.Status), ct);
            pendingLeave = await _db.LeaveApplications.CountAsync(r => r.EmployeeId == e.Id && open.Contains(r.Status), ct);
        }

        var approvals = _user.HasPermission(Permissions.ApprovalsAct)
            ? await _db.ApprovalTasks.CountAsync(t => t.AssigneeUserId == _user.UserId && t.Status == ApprovalTaskStatus.Pending, ct)
            : 0;
        var unread = await _db.Notifications.CountAsync(n => n.RecipientUserId == _user.UserId && n.Channel == Domain.Notifications.NotificationChannel.InApp && n.ReadAtUtc == null, ct);

        var now = DateTime.UtcNow;
        var announcements = await _db.Announcements.AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.PublishAtUtc <= now && (a.ExpiresAtUtc == null || a.ExpiresAtUtc > now)
                        && (a.TargetDepartmentId == null || (e != null && a.TargetDepartmentId == e.DepartmentId))
                        && (a.TargetLocationId == null || (e != null && a.TargetLocationId == e.LocationId)))
            .OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.PublishAtUtc).Take(5)
            .Select(a => new AnnouncementView(a.Id, a.Title, a.BodyHtml, a.PublishAtUtc, a.IsPinned)).ToListAsync(ct);

        return new DashboardResponse(summary, leave, upcoming, payslips, new PendingCounts(openRequests, pendingLeave, approvals, unread), announcements,
            await CelebrationsAsync(companyId, today, ct));
    }

    /// <summary>Birthdays (day and month only) and work anniversaries in the next 14 days, if the company's privacy settings allow.</summary>
    // The list is the same for everyone in a company and changes once a day, so it is computed once per company every
    // ten minutes instead of on every dashboard load (with 10,000 staff it was the dashboard's most expensive query).
    private Task<IReadOnlyList<Celebration>> CelebrationsAsync(Guid companyId, DateOnly today, CancellationToken ct) =>
        _cache.GetOrCreateAsync(("dashboard.celebrations", companyId, today), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return LoadCelebrationsAsync(companyId, today, ct);
        })!;

    private async Task<IReadOnlyList<Celebration>> LoadCelebrationsAsync(Guid companyId, DateOnly today, CancellationToken ct)
    {
        var settings = await _db.SystemSettings.AsNoTracking()
            .Where(s => (s.CompanyId == companyId || s.CompanyId == null) && (s.Key == "privacy.showBirthdays" || s.Key == "privacy.showAnniversaries"))
            .OrderBy(s => s.CompanyId == null) // company-specific first
            .ToListAsync(ct);
        bool Flag(string key, bool def) => settings.FirstOrDefault(s => s.Key == key) is { } s ? bool.TryParse(s.Value, out var b) && b : def;
        var showBirthdays = Flag("privacy.showBirthdays", false);
        var showAnniversaries = Flag("privacy.showAnniversaries", true);
        if (!showBirthdays && !showAnniversaries) return Array.Empty<Celebration>();

        var month = today.Month;
        var nextMonth = today.Month % 12 + 1;
        var staff = await (from x in _db.Employees.AsNoTracking()
                           join d in _db.Departments on x.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                           where x.CompanyId == companyId && x.Status == EmploymentStatus.Active
                                 // only dates in this month or next can fall in the 14-day window
                                 && ((x.BirthDate != null && (x.BirthDate.Value.Month == month || x.BirthDate.Value.Month == nextMonth))
                                     || (x.EmploymentDate != null && (x.EmploymentDate.Value.Month == month || x.EmploymentDate.Value.Month == nextMonth)))
                           select new { Name = x.FirstName + " " + x.LastName, x.BirthDate, x.EmploymentDate, Dept = d != null ? d.Name : null })
            .ToListAsync(ct);
        var list = new List<Celebration>();
        DateOnly Next(DateOnly d)
        {
            var day = Math.Min(d.Day, DateTime.DaysInMonth(today.Year, d.Month));
            var n = new DateOnly(today.Year, d.Month, day);
            return n < today ? new DateOnly(today.Year + 1, d.Month, Math.Min(d.Day, DateTime.DaysInMonth(today.Year + 1, d.Month))) : n;
        }
        foreach (var s in staff)
        {
            if (showBirthdays && s.BirthDate is { } b && Next(b).DayNumber - today.DayNumber <= 14)
                list.Add(new Celebration(s.Name, "Birthday", Next(b), null, s.Dept));
            if (showAnniversaries && s.EmploymentDate is { } h && h < today && Next(h).DayNumber - today.DayNumber <= 14)
            {
                var years = Next(h).Year - h.Year;
                if (years > 0) list.Add(new Celebration(s.Name, "Work anniversary", Next(h), years, s.Dept));
            }
        }
        return list.OrderBy(c => c.Date).Take(10).ToList();
    }
}
