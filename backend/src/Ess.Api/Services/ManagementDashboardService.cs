using Ess.Api.Reporting;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Domain.Common;
using Ess.Domain.Organization;
using Ess.Domain.Training;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Services;

public sealed record DashboardFilter(Guid? DepartmentId, Guid? LocationId, EmploymentStatus? Status, Guid? EmployeeId, DateOnly? From, DateOnly? To);

public sealed record CountItem(string Label, int Count);
public sealed record MonthPoint(string Month, decimal Value);
public sealed record PersonItem(Guid EmployeeId, string Name, string? Department, DateOnly Date, string? Detail);
public sealed record ManagementDashboard(
    DateOnly From, DateOnly To, DateOnly Today, string ScopeLabel,
    int TotalEmployees, int ActiveEmployees, int OnLeaveToday, int PendingLeave, int PendingExpenses, decimal PendingExpenseAmount, int OpenHrRequests,
    int Joiners, int Leavers, decimal? TurnoverPercent,
    IReadOnlyList<CountItem> ByDepartment, IReadOnlyList<CountItem> ByLocation, IReadOnlyList<CountItem> ByStatus, IReadOnlyList<CountItem> ByGender,
    IReadOnlyList<PersonItem> OnLeave, IReadOnlyList<PersonItem> ContractExpiries, IReadOnlyList<PersonItem> Birthdays, IReadOnlyList<PersonItem> Anniversaries,
    IReadOnlyList<MonthPoint> LeaveDaysByMonth, IReadOnlyList<MonthPoint> HeadcountByMonth,
    TrainingStats Training, RequestStats Requests, bool BirthdaysShown);
public sealed record TrainingStats(int Programmes, int Participants, int Completed, int NoShows, decimal? AverageRating, IReadOnlyList<CountItem> ByStatus);
public sealed record RequestStats(int Submitted, int Resolved, int Open, decimal? WithinSlaPercent, decimal? AverageHoursToResolve, IReadOnlyList<CountItem> ByType);

/// <summary>
/// Management dashboard (spec §7). Figures come from the portal's synced copy of NAV data and portal transactions, limited to the
/// departments the viewer is responsible for. Only counts and names are shown; no pay or personal contact data.
/// </summary>
public sealed class ManagementDashboardService
{
    private static readonly EmploymentStatus[] Current = { EmploymentStatus.Active, EmploymentStatus.OnProbation, EmploymentStatus.Suspended };
    private readonly EssDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ReportRunner _runner;

    public ManagementDashboardService(EssDbContext db, ICurrentUser user, ReportRunner runner)
    {
        _db = db;
        _user = user;
        _runner = runner;
    }

    public async Task<ManagementDashboard> BuildAsync(DashboardFilter f, CancellationToken ct)
    {
        var rf = new ReportFilter(f.From, f.To, f.DepartmentId, f.LocationId, f.EmployeeId);
        var c = await _runner.ContextAsync(Permissions.DashboardManagement, rf, ct);
        var today = c.Today;
        var to = f.To ?? today;
        var from = f.From ?? to.AddMonths(-12).AddDays(1);
        if (to < from) (from, to) = (to, from);

        var people = c.Employees(rf);
        var staff = await (from e in people
                           join d in _db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                           join l in _db.Locations on e.LocationId equals l.Id into lj from l in lj.DefaultIfEmpty()
                           select new { e.Id, Name = e.FirstName + " " + e.LastName, e.Status, e.Gender, e.EmploymentDate, e.TerminationDate, e.ContractEndDate, e.BirthDate,
                               Dept = d != null ? d.Name : "No department", Loc = l != null ? l.Name : "No location" }).ToListAsync(ct);
        var filtered = f.Status is null ? staff : staff.Where(s => s.Status == f.Status).ToList();
        var current = filtered.Where(s => Current.Contains(s.Status)).ToList();
        var ids = staff.Select(s => s.Id).ToList();

        // Headcount at a date, rebuilt from NAV employment and termination dates.
        int HeadcountOn(DateOnly d) => staff.Count(s => s.EmploymentDate is { } h && h <= d && (s.TerminationDate is null || s.TerminationDate > d)
                                                        && (s.Status != EmploymentStatus.Terminated || s.TerminationDate is not null));
        var joiners = staff.Count(s => s.EmploymentDate >= from && s.EmploymentDate <= to);
        var leavers = staff.Count(s => s.TerminationDate >= from && s.TerminationDate <= to);
        var avg = (HeadcountOn(from) + HeadcountOn(to)) / 2m;
        decimal? turnover = avg > 0 && staff.Any(s => s.TerminationDate is not null || s.Status == EmploymentStatus.Terminated) ? Math.Round(leavers * 100m / avg, 1) : avg > 0 ? 0 : null;

        var onLeave = await (from a in _db.LeaveApplications.AsNoTracking()
                             join e in people on a.EmployeeId equals e.Id
                             join d in _db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                             where a.Status == RequestStatus.Approved && a.StartDate <= today && a.EndDate >= today
                             orderby a.EndDate
                             select new PersonItem(e.Id, e.FirstName + " " + e.LastName, d != null ? d.Name : null, a.EndDate, "Back after " + a.EndDate)).ToListAsync(ct);

        var pendingLeave = await _db.LeaveApplications.CountAsync(a => ids.Contains(a.EmployeeId) && (a.Status == RequestStatus.InApproval || a.Status == RequestStatus.Submitted), ct);
        var pendingExp = await _db.ExpenseClaims.Where(x => ids.Contains(x.EmployeeId) && (x.Status == RequestStatus.InApproval || x.Status == RequestStatus.Submitted))
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Amount = g.Sum(x => x.TotalAmount) }).FirstOrDefaultAsync(ct);
        var openRequests = await _db.HrRequests.CountAsync(r => ids.Contains(r.EmployeeId) && (r.Status == RequestStatus.Submitted || r.Status == RequestStatus.InApproval || r.Status == RequestStatus.Approved), ct);

        var contracts = current.Where(s => s.ContractEndDate >= today && s.ContractEndDate <= today.AddDays(90)).OrderBy(s => s.ContractEndDate)
            .Select(s => new PersonItem(s.Id, s.Name, s.Dept, s.ContractEndDate!.Value, $"{s.ContractEndDate!.Value.DayNumber - today.DayNumber} days left")).ToList();

        var (showBirthdays, showAnniversaries) = await PrivacyAsync(c.CompanyId, ct);
        DateOnly Next(DateOnly d)
        {
            var n = new DateOnly(today.Year, d.Month, Math.Min(d.Day, DateTime.DaysInMonth(today.Year, d.Month)));
            return n < today ? new DateOnly(today.Year + 1, d.Month, Math.Min(d.Day, DateTime.DaysInMonth(today.Year + 1, d.Month))) : n;
        }
        var birthdays = !showBirthdays ? new List<PersonItem>() : current.Where(s => s.BirthDate is { } b && Next(b).DayNumber - today.DayNumber <= 30)
            .Select(s => new PersonItem(s.Id, s.Name, s.Dept, Next(s.BirthDate!.Value), null)).OrderBy(p => p.Date).ToList();
        var anniversaries = !showAnniversaries ? new List<PersonItem>() : current.Where(s => s.EmploymentDate is { } h && h < today && Next(h).DayNumber - today.DayNumber <= 30 && Next(h).Year > h.Year)
            .Select(s => new PersonItem(s.Id, s.Name, s.Dept, Next(s.EmploymentDate!.Value), $"{Next(s.EmploymentDate!.Value).Year - s.EmploymentDate!.Value.Year} years")).OrderBy(p => p.Date).ToList();

        // Monthly series over the last 12 months up to the end of the range.
        var months = Enumerable.Range(0, 12).Select(i => new DateOnly(to.Year, to.Month, 1).AddMonths(i - 11)).ToList();
        var leaveRows = await _db.LeaveApplications.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId) && a.Status == RequestStatus.Approved && a.EndDate >= months[0] && a.StartDate <= to)
            .Select(a => new { a.StartDate, a.EndDate, a.DaysRequested }).ToListAsync(ct);
        var leaveByMonth = months.Select(m =>
        {
            var end = m.AddMonths(1).AddDays(-1);
            // Days are apportioned to months by calendar overlap, which is close enough for a trend line.
            var v = leaveRows.Sum(a =>
            {
                var span = a.EndDate.DayNumber - a.StartDate.DayNumber + 1;
                var overlap = Math.Min(a.EndDate.DayNumber, end.DayNumber) - Math.Max(a.StartDate.DayNumber, m.DayNumber) + 1;
                return overlap <= 0 || span <= 0 ? 0 : a.DaysRequested * overlap / span;
            });
            return new MonthPoint(m.ToString("yyyy-MM"), Math.Round(v, 1));
        }).ToList();
        var headcountByMonth = months.Select(m => new MonthPoint(m.ToString("yyyy-MM"), HeadcountOn(m.AddMonths(1).AddDays(-1) > today ? today : m.AddMonths(1).AddDays(-1)))).ToList();

        var (startUtc, endUtc) = (c.ToUtc(from), c.ToUtc(to.AddDays(1)));
        var training = await (from t in _db.EmployeeTraining.AsNoTracking()
                              join p in _db.TrainingPrograms on t.TrainingProgramId equals p.Id
                              where ids.Contains(t.EmployeeId) && p.StartsAtUtc >= startUtc && p.StartsAtUtc < endUtc
                              select new { t.Status, t.FeedbackRating, p.Id }).ToListAsync(ct);
        var trainingStats = new TrainingStats(training.Select(t => t.Id).Distinct().Count(), training.Count,
            training.Count(t => t.Status == TrainingParticipationStatus.Completed || t.Status == TrainingParticipationStatus.Attended),
            training.Count(t => t.Status == TrainingParticipationStatus.NoShow),
            training.Any(t => t.FeedbackRating != null) ? Math.Round((decimal)training.Where(t => t.FeedbackRating != null).Average(t => t.FeedbackRating!.Value), 1) : null,
            training.GroupBy(t => t.Status).Select(g => new CountItem(g.Key.ToString(), g.Count())).OrderByDescending(x => x.Count).ToList());

        var requests = await (from r in _db.HrRequests.AsNoTracking()
                              join t in _db.HrRequestTypes on r.RequestTypeId equals t.Id
                              where ids.Contains(r.EmployeeId) && r.SubmittedAtUtc >= startUtc && r.SubmittedAtUtc < endUtc && r.Status != RequestStatus.Withdrawn
                              select new { t.Name, t.SlaHours, r.SubmittedAtUtc, r.ResolvedAtUtc, r.Status }).ToListAsync(ct);
        var resolved = requests.Where(r => r.ResolvedAtUtc != null).ToList();
        var withSla = resolved.Where(r => r.SlaHours != null).ToList();
        var requestStats = new RequestStats(requests.Count, resolved.Count, requests.Count(r => r.ResolvedAtUtc == null && r.Status is not (RequestStatus.Rejected or RequestStatus.Cancelled)),
            withSla.Count == 0 ? null : Math.Round(withSla.Count(r => (r.ResolvedAtUtc!.Value - r.SubmittedAtUtc!.Value).TotalHours <= r.SlaHours!.Value) * 100m / withSla.Count, 1),
            resolved.Count == 0 ? null : Math.Round((decimal)resolved.Average(r => (r.ResolvedAtUtc!.Value - r.SubmittedAtUtc!.Value).TotalHours), 1),
            requests.GroupBy(r => r.Name).Select(g => new CountItem(g.Key, g.Count())).OrderByDescending(x => x.Count).ToList());

        static List<CountItem> Group<T>(IEnumerable<T> rows, Func<T, string> key) =>
            rows.GroupBy(key).Select(g => new CountItem(g.Key, g.Count())).OrderByDescending(x => x.Count).ThenBy(x => x.Label).ToList();

        return new ManagementDashboard(from, to, today, c.Scope is null ? "Whole company" : "Departments you are responsible for",
            filtered.Count(s => s.Status != EmploymentStatus.Terminated && s.Status != EmploymentStatus.Inactive), current.Count(s => s.Status == EmploymentStatus.Active),
            onLeave.Count, pendingLeave, pendingExp?.Count ?? 0, pendingExp?.Amount ?? 0, openRequests, joiners, leavers, turnover,
            Group(current, s => s.Dept), Group(current, s => s.Loc), Group(staff, s => s.Status == EmploymentStatus.OnProbation ? "On probation" : s.Status.ToString()),
            Group(current, s => string.IsNullOrWhiteSpace(s.Gender) ? "Not recorded" : s.Gender!),
            onLeave.Take(50).ToList(), contracts, birthdays, anniversaries, leaveByMonth, headcountByMonth, trainingStats, requestStats, showBirthdays);
    }

    private async Task<(bool Birthdays, bool Anniversaries)> PrivacyAsync(Guid companyId, CancellationToken ct)
    {
        var settings = await _db.SystemSettings.AsNoTracking()
            .Where(s => (s.CompanyId == companyId || s.CompanyId == null) && (s.Key == "privacy.showBirthdays" || s.Key == "privacy.showAnniversaries"))
            .OrderBy(s => s.CompanyId == null).ToListAsync(ct);
        bool Flag(string key, bool def) => settings.FirstOrDefault(s => s.Key == key) is { } s ? bool.TryParse(s.Value, out var b) && b : def;
        return (Flag("privacy.showBirthdays", false), Flag("privacy.showAnniversaries", true));
    }

    /// <summary>The dashboard as tables, for Excel and PDF export.</summary>
    public static IReadOnlyList<ReportTable> ToTables(ManagementDashboard d, IReadOnlyList<KeyValuePair<string, string>> filters)
    {
        var none = Array.Empty<KeyValuePair<string, string>>();
        static ReportColumn[] Cols(string a, string b, ReportColumnType t = ReportColumnType.Number) => new[] { new ReportColumn("label", a), new ReportColumn("value", b, t) };
        var summary = new List<object?[]>
        {
            new object?[] { "Total employees", (decimal)d.TotalEmployees }, new object?[] { "Active employees", (decimal)d.ActiveEmployees },
            new object?[] { "On leave today", (decimal)d.OnLeaveToday }, new object?[] { "Leave applications waiting for approval", (decimal)d.PendingLeave },
            new object?[] { "Expense claims waiting for approval", (decimal)d.PendingExpenses }, new object?[] { "Value of expense claims waiting (KES)", d.PendingExpenseAmount },
            new object?[] { "Open HR requests", (decimal)d.OpenHrRequests }, new object?[] { "Joiners in period", (decimal)d.Joiners }, new object?[] { "Leavers in period", (decimal)d.Leavers },
            new object?[] { "Staff turnover in period (%)", d.TurnoverPercent }, new object?[] { "Training participants", (decimal)d.Training.Participants },
            new object?[] { "Training completed", (decimal)d.Training.Completed }, new object?[] { "HR requests submitted", (decimal)d.Requests.Submitted },
            new object?[] { "HR requests resolved within SLA (%)", d.Requests.WithinSlaPercent },
        };
        var f = filters.Prepend(new("Period", $"{d.From:d MMM yyyy} to {d.To:d MMM yyyy}")).Append(new("Scope", d.ScopeLabel)).ToList();
        return new[]
        {
            new ReportTable("Management summary", Cols("Measure", "Value", ReportColumnType.Decimal), summary, f),
            new ReportTable("Staff by department", Cols("Department", "Staff"), d.ByDepartment.Select(x => new object?[] { x.Label, x.Count }).ToList(), none),
            new ReportTable("Staff by location", Cols("Location", "Staff"), d.ByLocation.Select(x => new object?[] { x.Label, x.Count }).ToList(), none),
            new ReportTable("Staff by status", Cols("Status", "Staff"), d.ByStatus.Select(x => new object?[] { x.Label, x.Count }).ToList(), none),
            new ReportTable("Headcount by month", Cols("Month", "Headcount", ReportColumnType.Decimal), d.HeadcountByMonth.Select(x => new object?[] { x.Month, x.Value }).ToList(), none),
            new ReportTable("Approved leave days by month", Cols("Month", "Days", ReportColumnType.Decimal), d.LeaveDaysByMonth.Select(x => new object?[] { x.Month, x.Value }).ToList(), none),
            new ReportTable("Contracts ending in 90 days", new[] { new ReportColumn("name", "Employee"), new ReportColumn("dept", "Department"), new ReportColumn("date", "Contract ends", ReportColumnType.Date) },
                d.ContractExpiries.Select(x => new object?[] { x.Name, x.Department, x.Date }).ToList(), none),
            new ReportTable("HR requests by type", Cols("Type", "Requests"), d.Requests.ByType.Select(x => new object?[] { x.Label, x.Count }).ToList(), none),
        };
    }
}
