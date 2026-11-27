using Ess.Application.Abstractions;
using Ess.Application.Integration;
using Ess.Application.Security;
using Ess.Domain.Common;
using Ess.Domain.Organization;
using Ess.Domain.Training;
using Ess.Domain.Workflow;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using T = Ess.Api.Reporting.ReportColumnType;

namespace Ess.Api.Reporting;

/// <summary>Everything a report needs end run for the caller: data, company, the caller's department scope and local "today".</summary>
public sealed class ReportContext
{
    public required EssDbContext Db { get; init; }
    public required Guid CompanyId { get; init; }
    /// <summary>Departments the caller may see for this report's permission; null means the whole company.</summary>
    public required IReadOnlyList<Guid>? Scope { get; init; }
    public required DateOnly Today { get; init; }
    public required int UtcOffsetHours { get; init; }
    public required IServiceProvider Services { get; init; }
    public IReadOnlyList<Guid>? DepartmentWithChildren { get; set; }

    public DateTime ToUtc(DateOnly localDate) => localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-UtcOffsetHours);
    public DateTime ToLocal(DateTime utc) => utc.AddHours(UtcOffsetHours);

    /// <summary>Employees of this company the caller may see, narrowed by the department, location and employee filters.</summary>
    public IQueryable<Employee> Employees(ReportFilter f)
    {
        var q = Db.Employees.AsNoTracking().Where(e => e.CompanyId == CompanyId);
        if (Scope is not null) q = q.Where(e => e.DepartmentId != null && Scope.Contains(e.DepartmentId.Value));
        if (DepartmentWithChildren is not null) q = q.Where(e => e.DepartmentId != null && DepartmentWithChildren.Contains(e.DepartmentId.Value));
        if (f.LocationId is not null) q = q.Where(e => e.LocationId == f.LocationId);
        if (f.EmployeeId is not null) q = q.Where(e => e.Id == f.EmployeeId);
        return q;
    }
}

public sealed record ReportFilterSupport(bool DateRange = false, bool Department = true, bool Location = true, bool Employee = false,
    IReadOnlyList<string>? Statuses = null, bool Period = false);

public sealed record ReportDefinition(
    string Code, string Name, string Category, string Description, string Permission, ReportFilterSupport Filters,
    Func<ReportContext, ReportFilter, CancellationToken, Task<ReportTable>> Run);

/// <summary>
/// The report catalogue (spec §13). Every report reads portal data or NAV through the integration layer, applies the caller's
/// department scope, and leaves out personal data that the report does not need (no birth dates, phone numbers or bank details).
/// </summary>
public static class ReportCatalog
{
    public const int MaxRows = 20_000;

    public static readonly IReadOnlyList<ReportDefinition> All = new ReportDefinition[]
    {
        new("employee-directory", "Employee directory", "People", "Staff list with department, position, location and status.", Permissions.ReportsHr,
            new(Statuses: Enum.GetNames<EmploymentStatus>()), EmployeeDirectory),
        new("headcount", "Headcount", "People", "Staff numbers by department and location, with joiners and leavers in the period.", Permissions.ReportsHr,
            new(DateRange: true), Headcount),
        new("leave-utilisation", "Leave utilisation", "Leave", "Entitlement, days taken, pending and available per employee and leave type, from the latest NAV balances.", Permissions.ReportsHr,
            new(Employee: true), LeaveUtilisation),
        new("leave-liability", "Leave liability (days)", "Leave", "Outstanding annual leave days per employee. The money value needs daily rates from NAV payroll.", Permissions.ReportsHr,
            new(), LeaveLiability),
        new("leave-applications", "Leave applications", "Leave", "Applications in the period with dates, days and status.", Permissions.ReportsHr,
            new(DateRange: true, Employee: true, Statuses: Enum.GetNames<RequestStatus>()), LeaveApplications),
        new("attendance-summary", "Attendance summary", "Time", "Days present, late arrivals, hours worked and approved overtime per employee.", Permissions.ReportsHr,
            new(DateRange: true, Employee: true), AttendanceSummary),
        new("expense-claims", "Expense claims", "Finance", "Claims in the period with amounts, approval and NAV status.", Permissions.ReportsFinance,
            new(DateRange: true, Employee: true, Statuses: Enum.GetNames<RequestStatus>()), ExpenseClaims),
        new("pending-approvals", "Pending approvals", "Workflow", "Requests waiting for a decision, who has them and how long they have waited.", Permissions.ApprovalsMonitor,
            new(), PendingApprovals),
        new("hr-requests", "HR requests", "Service", "Requests in the period with SLA performance. Subjects of confidential request types are hidden.", Permissions.ReportsHr,
            new(DateRange: true, Statuses: Enum.GetNames<RequestStatus>()), HrRequests),
        new("training-participation", "Training participation", "Development", "Who applied for, attended and completed each programme, with feedback.", Permissions.ReportsHr,
            new(DateRange: true, Employee: true, Statuses: Enum.GetNames<TrainingParticipationStatus>()), TrainingParticipation),
        new("document-expiry", "Document and certificate expiry", "Compliance", "Documents and professional certificates that have expired or expire in the period.", Permissions.ReportsHr,
            new(DateRange: true), DocumentExpiry),
        new("contract-expiry", "Contract expiry", "People", "Fixed-term contracts ending in the period.", Permissions.ReportsHr,
            new(DateRange: true), ContractExpiry),
        new("payroll-summary", "Payroll summary", "Payroll", "Gross pay, statutory deductions and net pay by department for one period, read from NAV.", Permissions.ReportsPayroll,
            new(Period: true, Location: false), PayrollSummary),
    };

    public static ReportDefinition? Find(string code) => All.FirstOrDefault(r => r.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ helpers

    private static (DateOnly From, DateOnly To) Range(ReportContext c, ReportFilter f, int defaultDaysBack = 30, int defaultDaysAhead = 0)
    {
        var start = f.From ?? c.Today.AddDays(-defaultDaysBack);
        var end = f.To ?? c.Today.AddDays(defaultDaysAhead);
        if (end < start) (start, end) = (end, start);
        return (start, end);
    }

    private static string Fmt(DateOnly d) => d.ToString("d MMM yyyy");

    private static async Task<List<KeyValuePair<string, string>>> FilterLabels(ReportContext c, ReportFilter f, (DateOnly, DateOnly)? range = null)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (range is { } r) list.Add(new("Period", $"{Fmt(r.Item1)} to {Fmt(r.Item2)}"));
        if (f.DepartmentId is not null) list.Add(new("Department", await c.Db.Departments.Where(d => d.Id == f.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync() ?? "?"));
        if (f.LocationId is not null) list.Add(new("Location", await c.Db.Locations.Where(d => d.Id == f.LocationId).Select(d => d.Name).FirstOrDefaultAsync() ?? "?"));
        if (f.EmployeeId is not null) list.Add(new("Employee", await c.Db.Employees.Where(d => d.Id == f.EmployeeId).Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync() ?? "?"));
        if (!string.IsNullOrEmpty(f.Status)) list.Add(new("Status", f.Status));
        if (!string.IsNullOrEmpty(f.PeriodCode)) list.Add(new("Payroll period", f.PeriodCode));
        if (c.Scope is not null) list.Add(new("Scope", "Departments you are responsible for"));
        return list;
    }

    private static ReportTable Table(string title, ReportColumn[] cols, List<object?[]> rows, List<KeyValuePair<string, string>> filters, string? note = null) =>
        new(title, cols, rows.Take(MaxRows).ToList(), filters, note, rows.Count > MaxRows);

    private static TEnum? ParseStatus<TEnum>(ReportFilter f) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(f.Status, true, out var s) ? s : null;

    private sealed record EmpRow(Guid Id, string No, string Name, string? Department, string? Position, string? Location, EmploymentStatus Status,
        DateOnly? EmploymentDate, DateOnly? ContractEndDate, DateOnly? TerminationDate, string? Email);

    private static IQueryable<EmpRow> EmpRows(ReportContext c, IQueryable<Employee> q) =>
        from e in q
        join d in c.Db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
        join p in c.Db.Positions on e.PositionId equals p.Id into pj from p in pj.DefaultIfEmpty()
        join l in c.Db.Locations on e.LocationId equals l.Id into lj from l in lj.DefaultIfEmpty()
        select new EmpRow(e.Id, e.NavKey, e.FirstName + " " + e.LastName, d != null ? d.Name : null, p != null ? p.Title : null, l != null ? l.Name : null,
            e.Status, e.EmploymentDate, e.ContractEndDate, e.TerminationDate, e.WorkEmail);

    private static string StatusLabel(EmploymentStatus s) => s switch { EmploymentStatus.OnProbation => "On probation", _ => s.ToString() };

    // ------------------------------------------------------------------ people

    private static async Task<ReportTable> EmployeeDirectory(ReportContext c, ReportFilter f, CancellationToken ct)
    {
        var q = c.Employees(f);
        if (ParseStatus<EmploymentStatus>(f) is { } s) q = q.Where(e => e.Status == s);
        var rows = (await EmpRows(c, q).ToListAsync(ct)).OrderBy(r => r.Name).ToList();
        return Table("Employee directory",
            new ReportColumn[] { new("no", "Employee no."), new("name", "Name"), new("department", "Department"), new("position", "Position"), new("location", "Location"),
                new("status", "Status"), new("employed", "Employed since", T.Date), new("email", "Work email") },
            rows.Select(r => new object?[] { r.No, r.Name, r.Department, r.Position, r.Location, StatusLabel(r.Status), r.EmploymentDate, r.Email }).ToList(),
            await FilterLabels(c, f));
    }

    private static async Task<ReportTable> Headcount(ReportContext c, ReportFilter f, CancellationToken ct)
    {
        var (start, end) = Range(c, f, 365);
        var rows = await EmpRows(c, c.Employees(f)).ToListAsync(ct);
        static bool Current(EmpRow r) => r.Status is EmploymentStatus.Active or EmploymentStatus.OnProbation or EmploymentStatus.Suspended;
        var groups = rows.GroupBy(r => (r.Department ?? "No department", r.Location ?? "No location")).OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2)
            .Select(g => new object?[]
            {
                g.Key.Item1, g.Key.Item2, g.Count(r => r.Status == EmploymentStatus.Active), g.Count(r => r.Status == EmploymentStatus.OnProbation),
                g.Count(r => r.Status == EmploymentStatus.Suspended), g.Count(Current),
                g.Count(r => r.EmploymentDate >= start && r.EmploymentDate <= end), g.Count(r => r.TerminationDate >= start && r.TerminationDate <= end)
            }).ToList();
        var table = Table("Headcount",
            new ReportColumn[] { new("department", "Department"), new("location", "Location"), new("active", "Active", T.Number), new("probation", "On probation", T.Number),
                new("suspended", "Suspended", T.Number), new("headcount", "Headcount", T.Number), new("joiners", "Joiners", T.Number), new("leavers", "Leavers", T.Number) },
            groups, await FilterLabels(c, f, (start, end)), "Headcount is current staff today. Joiners and leavers are counted in the period, from NAV employment and termination dates.");
        return table with { Totals = new object?[] { "Total", null, rows.Count(r => r.Status == EmploymentStatus.Active), rows.Count(r => r.Status == EmploymentStatus.OnProbation),
            rows.Count(r => r.Status == EmploymentStatus.Suspended), rows.Count(Current), rows.Count(r => r.EmploymentDate >= start && r.EmploymentDate <= end),
            rows.Count(r => r.TerminationDate >= start && r.TerminationDate <= end) } };
    }

    private static async Task<ReportTable> ContractExpiry(ReportContext c, ReportFilter f, CancellationToken ct)
    {
        var (start, end) = Range(c, f, 0, 90);
        var rows = await EmpRows(c, c.Employees(f).Where(e => e.ContractEndDate >= start && e.ContractEndDate <= end && e.Status != EmploymentStatus.Terminated))
            .ToListAsync(ct);
        rows = rows.OrderBy(r => r.ContractEndDate).ToList();
        return Table("Contract expiry",
            new ReportColumn[] { new("no", "Employee no."), new("name", "Name"), new("department", "Department"), new("position", "Position"), new("ends", "Contract ends", T.Date), new("days", "Days left", T.Number) },
            rows.Select(r => new object?[] { r.No, r.Name, r.Department, r.Position, r.ContractEndDate, r.ContractEndDate!.Value.DayNumber - c.Today.DayNumber }).ToList(),
            await FilterLabels(c, f, (start, end)), "Contract end dates come from NAV. If this report is empty, check that the contract end date field is mapped.");
    }

    // ------------------------------------------------------------------ leave

    private static async Task<ReportTable> LeaveUtilisation(ReportContext c, ReportFilter f, CancellationToken ct)
    {
        var emps = c.Employees(f).Where(e => e.Status != EmploymentStatus.Terminated);
        var rows = await (from b in c.Db.LeaveBalancesCache.AsNoTracking()
                          join e in emps on b.EmployeeId equals e.Id
                          join t in c.Db.LeaveTypes on b.LeaveTypeId equals t.Id
                          join d in c.Db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                          orderby e.LastName, e.FirstName, t.Name
                          select new { e.NavKey, Name = e.FirstName + " " + e.LastName, Dept = d != null ? d.Name : null, Type = t.Name, b.LeavePeriodCode,
                              b.Entitlement, b.BroughtForward, b.Taken, b.PendingApproval, b.Available, b.RetrievedAtUtc }).Take(MaxRows + 1).ToListAsync(ct);
        return Table("Leave utilisation",
            new ReportColumn[] { new("no", "Employee no."), new("name", "Name"), new("department", "Department"), new("type", "Leave type"), new("period", "Leave period"),
                new("entitlement", "Entitlement", T.Decimal), new("bf", "Brought forward", T.Decimal), new("taken", "Taken", T.Decimal), new("pending", "Pending", T.Decimal),
                new("available", "Available", T.Decimal), new("used", "Used", T.Percent), new("asof", "Balance as of", T.DateTime) },
            rows.Select(r => new object?[] { r.NavKey, r.Name, r.Dept, r.Type, r.LeavePeriodCode, r.Entitlement, r.BroughtForward, r.Taken, r.PendingApproval, r.Available,
                r.Entitlement + r.BroughtForward == 0 ? null : Math.Round(r.Taken * 100m / (r.Entitlement + r.BroughtForward), 1), c.ToLocal(r.RetrievedAtUtc) }).ToList(),
            await FilterLabels(c, f), "Balances are the latest figures read from NAV; NAV remains the source of truth.");
    }

    private static async Task<ReportTable> LeaveLiability(ReportContext c, ReportFilter f, CancellationToken ct)
    {
        var emps = c.Employees(f).Where(e => e.Status != EmploymentStatus.Terminated);
        var rows = await (from b in c.Db.LeaveBalancesCache.AsNoTracking()
                          join e in emps on b.EmployeeId equals e.Id
                          join t in c.Db.LeaveTypes on b.LeaveTypeId equals t.Id
                          join d in c.Db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                          where t.NavKey == "ANNUAL" || t.Name.ToLower().Contains("annual")
                          orderby d!.Name, e.LastName
                          select new { e.NavKey, Name = e.FirstName + " " + e.LastName, Dept = d != null ? d.Name : null, b.LeavePeriodCode, b.Available, b.PendingApproval }).Take(MaxRows + 1).ToListAsync(ct);
        var table = Table("Leave liability (days)",
            new ReportColumn[] { new("no", "Employee no."), new("name", "Name"), new("department", "Department"), new("period", "Leave period"), new("available", "Days outstanding", T.Decimal), new("pending", "Days pending approval", T.Decimal) },
            rows.Select(r => new object?[] { r.NavKey, r.Name, r.Dept, r.LeavePeriodCode, r.Available, r.PendingApproval }).ToList(),
            await FilterLabels(c, f), "Days only. The money value of the liability needs each employee's daily rate, which stays in NAV payroll.");
        return table with { Totals = new object?[] { "Total", null, null, null, rows.Sum(r => r.Available), rows.Sum(r => r.PendingApproval) } };
    }

    private static async Task<ReportTable> LeaveApplications(ReportContext c, ReportFilter f, CancellationToken ct)
    {
        var (start, end) = Range(c, f, 30, 60);
        var status = ParseStatus<RequestStatus>(f);
        var rows = await (from a in c.Db.LeaveApplications.AsNoTracking()
                          join e in c.Employees(f) on a.EmployeeId equals e.Id
                          join t in c.Db.LeaveTypes on a.LeaveTypeId equals t.Id
                          join d in c.Db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                          where a.CompanyId == c.CompanyId && a.StartDate <= end && a.EndDate >= start && a.Status != RequestStatus.Draft && (status == null || a.Status == status)
                          orderby a.StartDate
                          select new { a.Reference, Name = e.FirstName + " " + e.LastName, Dept = d != null ? d.Name : null, Type = t.Name, a.StartDate, a.EndDate, a.DaysRequested, a.Status, a.NavStatus, a.NavDocumentNo, a.SubmittedAtUtc })
            .Take(MaxRows + 1).ToListAsync(ct);
        return Table("Leave applications",
            new ReportColumn[] { new("ref", "Reference"), new("name", "Employee"), new("department", "Department"), new("type", "Leave type"), new("start", "From", T.Date), new("end", "To", T.Date),
                new("days", "Days", T.Decimal), new("status", "Status"), new("nav", "NAV status"), new("navno", "NAV document"), new("submitted", "Submitted", T.DateTime) },
            rows.Select(r => new object?[] { r.Reference, r.Name, r.Dept, r.Type, r.StartDate, r.EndDate, r.DaysRequested, r.Status.ToString(), r.NavStatus.ToString(), r.NavDocumentNo,
                r.SubmittedAtUtc is null ? null : c.ToLocal(r.SubmittedAtUtc.Value) }).ToList(),
            await FilterLabels(c, f, (start, end)));
    }

    // ------------------------------------------------------------------ time

    private static async Task<ReportTable> AttendanceSummary(ReportContext c, ReportFilter f, CancellationToken ct)
    {
        var (start, end) = Range(c, f, 30);
        var emps = await EmpRows(c, c.Employees(f).Where(e => e.Status != EmploymentStatus.Terminated)).ToListAsync(ct);
        var ids = emps.Select(e => e.Id).ToList();
        var att = await c.Db.AttendanceRecords.AsNoTracking()
            .Where(a => a.CompanyId == c.CompanyId && ids.Contains(a.EmployeeId) && a.WorkDate >= start && a.WorkDate <= end)
            .GroupBy(a => a.EmployeeId)
            .Select(g => new { g.Key, Days = g.Count(a => a.ClockInUtc != null), Late = g.Count(a => a.LateMinutes > 0), LateMin = g.Sum(a => a.LateMinutes), Hours = g.Sum(a => a.WorkedHours) })
            .ToDictionaryAsync(x => x.Key, ct);
        var ot = await c.Db.AttendanceRequests.AsNoTracking()
            .Where(r => r.CompanyId == c.CompanyId && ids.Contains(r.EmployeeId) && r.WorkDate >= start && r.WorkDate <= end && r.Type == Domain.Attendance.AttendanceRequestType.Overtime && r.Status == RequestStatus.Approved)
            .GroupBy(r => r.EmployeeId).Select(g => new { g.Key, Hours = g.Sum(r => r.OvertimeHours ?? 0) }).ToDictionaryAsync(x => x.Key, x => x.Hours, ct);
        var rows = emps.OrderBy(e => e.Name).Select(e =>
        {
            att.TryGetValue(e.Id, out var a);
            return new object?[] { e.No, e.Name, e.Department, a?.Days ?? 0, a?.Late ?? 0, a?.LateMin ?? 0, a?.Hours ?? 0m, ot.GetValueOrDefault(e.Id) };
        }).ToList();
        return Table("Attendance summary",
            new ReportColumn[] { new("no", "Employee no."), new("name", "Name"), new("department", "Department"), new("days", "Days present", T.Number), new("late", "Late days", T.Number),
                new("lateMin", "Late minutes", T.Number), new("hours", "Hours worked", T.Decimal), new("ot", "Approved overtime (h)", T.Decimal) },
            rows, await FilterLabels(c, f, (start, end)), "From portal clock-ins and approved corrections. Biometric data appears here once a device adapter is connected.");
    }

    // ------------------------------------------------------------------ finance

    private static async Task<ReportTable> ExpenseClaims(ReportContext c, ReportFilter f, CancellationToken ct)
    {
        var (start, end) = Range(c, f, 30);
        var (startUtc, endUtc) = (c.ToUtc(start), c.ToUtc(end.AddDays(1)));
        var status = ParseStatus<RequestStatus>(f);
        var rows = await (from x in c.Db.ExpenseClaims.AsNoTracking()
                          join e in c.Employees(f) on x.EmployeeId equals e.Id
                          join d in c.Db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                          where x.CompanyId == c.CompanyId && x.SubmittedAtUtc >= startUtc && x.SubmittedAtUtc < endUtc && (status == null || x.Status == status)
                          orderby x.SubmittedAtUtc
                          select new { x.Reference, Name = e.FirstName + " " + e.LastName, Dept = d != null ? d.Name : null, x.Purpose, x.ProjectCode, x.CostCentreCode, x.SubmittedAtUtc,
                              x.Currency, x.TotalAmount, x.Status, x.NavStatus, x.NavDocumentNo, x.PaidAtUtc }).Take(MaxRows + 1).ToListAsync(ct);
        var table = Table("Expense claims",
            new ReportColumn[] { new("ref", "Reference"), new("name", "Employee"), new("department", "Department"), new("purpose", "Purpose"), new("project", "Project"), new("cc", "Cost centre"),
                new("submitted", "Submitted", T.Date), new("amount", "Amount (KES)", T.Money), new("status", "Status"), new("nav", "NAV status"), new("navno", "NAV document"), new("paid", "Paid", T.Date) },
            rows.Select(r => new object?[] { r.Reference, r.Name, r.Dept, r.Purpose, r.ProjectCode, r.CostCentreCode, c.ToLocal(r.SubmittedAtUtc!.Value), r.TotalAmount, r.Status.ToString(),
                r.NavStatus.ToString(), r.NavDocumentNo, r.PaidAtUtc is null ? null : c.ToLocal(r.PaidAtUtc.Value) }).ToList(),
            await FilterLabels(c, f, (start, end)));
        return table with { Totals = new object?[] { "Total", null, null, null, null, null, null, rows.Sum(r => r.TotalAmount), null, null, null, null } };
    }

    // ------------------------------------------------------------------ workflow and service

    private static async Task<ReportTable> PendingApprovals(ReportContext c, ReportFilter f, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var rows = await (from t in c.Db.ApprovalTasks.AsNoTracking()
                          join i in c.Db.WorkflowInstances on t.WorkflowInstanceId equals i.Id
                          join s in c.Db.WorkflowSteps on t.WorkflowStepId equals s.Id
                          join e in c.Employees(f) on i.RequesterEmployeeId equals e.Id
                          join u in c.Db.Users on t.AssigneeUserId equals u.Id
                          join am in c.Db.EmployeeMappings on u.Id equals am.UserId into amj from am in amj.DefaultIfEmpty()
                          join ae in c.Db.Employees on am.EmployeeId equals ae.Id into aej from ae in aej.DefaultIfEmpty()
                          where i.CompanyId == c.CompanyId && t.Status == ApprovalTaskStatus.Pending && i.Status == WorkflowInstanceStatus.Running
                          orderby t.CreatedAtUtc
                          select new { i.DocumentReference, i.DocumentType, i.Summary, Requester = e.FirstName + " " + e.LastName, Step = s.Name,
                              Assignee = ae != null ? ae.FirstName + " " + ae.LastName : u.UserName, t.CreatedAtUtc, t.DueAtUtc, i.Amount }).Take(MaxRows + 1).ToListAsync(ct);
        return Table("Pending approvals",
            new ReportColumn[] { new("ref", "Reference"), new("type", "Type"), new("summary", "Summary"), new("requester", "Requester"), new("step", "Step"), new("with", "Waiting with"),
                new("since", "Waiting since", T.DateTime), new("days", "Days waiting", T.Decimal), new("due", "Due", T.DateTime), new("overdue", "Overdue") },
            rows.Select(r => new object?[] { r.DocumentReference, r.DocumentType.StartsWith("HrRequest") ? "HR request" : r.DocumentType, r.Summary, r.Requester, r.Step, r.Assignee,
                c.ToLocal(r.CreatedAtUtc), Math.Round((decimal)(now - r.CreatedAtUtc).TotalDays, 1), r.DueAtUtc is null ? null : c.ToLocal(r.DueAtUtc.Value), r.DueAtUtc < now }).ToList(),
            await FilterLabels(c, f));
    }

    private static async Task<ReportTable> HrRequests(ReportContext c, ReportFilter f, CancellationToken ct)
    {
        var (start, end) = Range(c, f, 30);
        var (startUtc, endUtc) = (c.ToUtc(start), c.ToUtc(end.AddDays(1)));
        var status = ParseStatus<RequestStatus>(f);
        var rows = await (from r in c.Db.HrRequests.AsNoTracking()
                          join t in c.Db.HrRequestTypes on r.RequestTypeId equals t.Id
                          join e in c.Employees(f) on r.EmployeeId equals e.Id
                          join d in c.Db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                          where r.CompanyId == c.CompanyId && r.SubmittedAtUtc >= startUtc && r.SubmittedAtUtc < endUtc && r.Status != RequestStatus.Withdrawn && (status == null || r.Status == status)
                          orderby r.SubmittedAtUtc
                          select new { r.Reference, Type = t.Name, t.IsSensitive, r.Subject, Name = e.FirstName + " " + e.LastName, Dept = d != null ? d.Name : null,
                              r.SubmittedAtUtc, t.SlaHours, r.ResolvedAtUtc, r.Status }).Take(MaxRows + 1).ToListAsync(ct);
        var now = DateTime.UtcNow;
        return Table("HR requests",
            new ReportColumn[] { new("ref", "Reference"), new("type", "Type"), new("subject", "Subject"), new("name", "Employee"), new("department", "Department"),
                new("submitted", "Submitted", T.DateTime), new("resolved", "Resolved", T.DateTime), new("hours", "Hours to resolve", T.Decimal), new("sla", "Within SLA"), new("status", "Status") },
            rows.Select(r =>
            {
                var end = r.ResolvedAtUtc ?? now;
                var hours = Math.Round((decimal)(end - r.SubmittedAtUtc!.Value).TotalHours, 1);
                return new object?[] { r.Reference, r.Type, r.IsSensitive ? "(confidential)" : r.Subject, r.Name, r.Dept, c.ToLocal(r.SubmittedAtUtc.Value),
                    r.ResolvedAtUtc is null ? null : c.ToLocal(r.ResolvedAtUtc.Value), r.ResolvedAtUtc is null ? null : hours,
                    r.SlaHours is null ? null : hours <= r.SlaHours ? "Yes" : r.ResolvedAtUtc is null ? "Overdue" : "No", r.Status.ToString() };
            }).ToList(),
            await FilterLabels(c, f, (start, end)), "SLA is measured in calendar hours from submission.");
    }

    private static async Task<ReportTable> TrainingParticipation(ReportContext c, ReportFilter f, CancellationToken ct)
    {
        var (start, end) = Range(c, f, 180, 180);
        var (startUtc, endUtc) = (c.ToUtc(start), c.ToUtc(end.AddDays(1)));
        var status = ParseStatus<TrainingParticipationStatus>(f);
        var rows = await (from t in c.Db.EmployeeTraining.AsNoTracking()
                          join p in c.Db.TrainingPrograms on t.TrainingProgramId equals p.Id
                          join e in c.Employees(f) on t.EmployeeId equals e.Id
                          join d in c.Db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                          where t.CompanyId == c.CompanyId && (p.StartsAtUtc == null || (p.StartsAtUtc >= startUtc && p.StartsAtUtc < endUtc)) && (status == null || t.Status == status)
                          orderby p.StartsAtUtc, p.Title, e.LastName
                          select new { p.Code, p.Title, p.StartsAtUtc, p.Provider, Name = e.FirstName + " " + e.LastName, Dept = d != null ? d.Name : null, t.Status, t.FeedbackRating, p.CostPerParticipant })
            .Take(MaxRows + 1).ToListAsync(ct);
        return Table("Training participation",
            new ReportColumn[] { new("code", "Programme code"), new("title", "Programme"), new("starts", "Starts", T.DateTime), new("provider", "Provider"), new("name", "Employee"),
                new("department", "Department"), new("status", "Status"), new("rating", "Rating (1-5)", T.Number), new("cost", "Cost (KES)", T.Money) },
            rows.Select(r => new object?[] { r.Code, r.Title, r.StartsAtUtc is null ? null : c.ToLocal(r.StartsAtUtc.Value), r.Provider, r.Name, r.Dept, r.Status.ToString(), r.FeedbackRating,
                r.Status is TrainingParticipationStatus.Rejected or TrainingParticipationStatus.Cancelled ? null : r.CostPerParticipant }).ToList(),
            await FilterLabels(c, f, (start, end)));
    }

    private static async Task<ReportTable> DocumentExpiry(ReportContext c, ReportFilter f, CancellationToken ct)
    {
        var (start, end) = (f.From ?? DateOnly.MinValue, f.To ?? c.Today.AddDays(90));
        var emps = c.Employees(f).Where(e => e.Status != EmploymentStatus.Terminated);
        var docs = await (from d in c.Db.EmployeeDocuments.AsNoTracking()
                          join cat in c.Db.DocumentCategories on d.CategoryId equals cat.Id
                          join e in emps on d.EmployeeId equals e.Id
                          where d.CompanyId == c.CompanyId && d.ExpiryDate != null && d.ExpiryDate >= start && d.ExpiryDate <= end
                          select new { Kind = "Document", Item = d.Title, Category = cat.Name, Name = e.FirstName + " " + e.LastName, e.NavKey, Expires = d.ExpiryDate!.Value }).ToListAsync(ct);
        var certs = await (from q in c.Db.EmployeeQualifications.AsNoTracking()
                           join e in emps on q.EmployeeId equals e.Id
                           where q.CompanyId == c.CompanyId && q.ExpiresOn != null && q.ExpiresOn >= start && q.ExpiresOn <= end
                           select new { Kind = q.IsProfessionalCertification ? "Certification" : "Qualification", Item = q.Name, Category = q.Institution ?? "", Name = e.FirstName + " " + e.LastName, e.NavKey, Expires = q.ExpiresOn!.Value }).ToListAsync(ct);
        var rows = docs.Concat(certs).OrderBy(r => r.Expires).Select(r => new object?[] { r.NavKey, r.Name, r.Kind, r.Item, r.Category, r.Expires, r.Expires.DayNumber - c.Today.DayNumber, r.Expires < c.Today ? "Expired" : "Expiring" }).ToList();
        return Table("Document and certificate expiry",
            new ReportColumn[] { new("no", "Employee no."), new("name", "Employee"), new("kind", "Kind"), new("item", "Document"), new("category", "Category or institution"),
                new("expires", "Expires", T.Date), new("days", "Days left", T.Number), new("state", "State") },
            rows, await FilterLabels(c, f, (f.From ?? c.Today, end)), f.From is null ? "Includes everything already expired." : null);
    }

    // ------------------------------------------------------------------ payroll

    private static async Task<ReportTable> PayrollSummary(ReportContext c, ReportFilter f, CancellationToken ct)
    {
        var period = f.PeriodCode is not null
            ? await c.Db.PayrollPeriodsCache.AsNoTracking().FirstOrDefaultAsync(p => p.CompanyId == c.CompanyId && p.NavKey == f.PeriodCode, ct)
            : await c.Db.PayrollPeriodsCache.AsNoTracking().Where(p => p.CompanyId == c.CompanyId && p.IsClosed).OrderByDescending(p => p.EndDate).FirstOrDefaultAsync(ct);
        if (period is null) throw new KeyNotFoundException("Choose a payroll period. Only periods synced from NAV can be reported.");
        if (!period.IsClosed) throw new ReportUnavailableException($"{period.Name} is still open in NAV. Payroll reports cover closed periods only.");

        var nav = c.Services.GetRequiredService<INavPayrollService>();
        var result = await nav.GetPeriodTotalsAsync(c.CompanyId, period.NavKey, ct);
        if (!result.Success) throw new Services.NavUnavailableException($"NAV did not return payroll totals: {result.Error!.Message}");

        var emps = await (from e in c.Employees(f)
                          join d in c.Db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                          select new { e.NavKey, Dept = d != null ? d.Name : "No department" }).ToDictionaryAsync(x => x.NavKey, x => x.Dept, StringComparer.OrdinalIgnoreCase, ct);
        var slips = result.Value!.Where(s => emps.ContainsKey(s.EmployeeNo)).ToList();
        decimal Stat(Application.Integration.NavPayslipTotals s, string code) => s.Statutory.Where(kv => kv.Key.StartsWith(code, StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);
        var rows = slips.GroupBy(s => emps[s.EmployeeNo]).OrderBy(g => g.Key).Select(g => new object?[]
        {
            g.Key, g.Count(), g.Sum(s => s.GrossPay), g.Sum(s => Stat(s, "PAYE")), g.Sum(s => Stat(s, "NSSF")), g.Sum(s => Stat(s, "SHIF")), g.Sum(s => Stat(s, "AHL")),
            g.Sum(s => s.TotalDeductions), g.Sum(s => s.NetPay)
        }).ToList();
        var table = Table($"Payroll summary: {period.Name}",
            new ReportColumn[] { new("department", "Department"), new("employees", "Employees", T.Number), new("gross", "Gross pay", T.Money), new("paye", "PAYE", T.Money), new("nssf", "NSSF", T.Money),
                new("shif", "SHIF", T.Money), new("ahl", "Housing Levy", T.Money), new("deductions", "Total deductions", T.Money), new("net", "Net pay", T.Money) },
            rows, await FilterLabels(c, f), "Figures as calculated and posted in NAV payroll. Department totals only; no individual pay is shown.");
        return table with { Totals = new object?[] { "Total", slips.Count, slips.Sum(s => s.GrossPay), slips.Sum(s => Stat(s, "PAYE")), slips.Sum(s => Stat(s, "NSSF")),
            slips.Sum(s => Stat(s, "SHIF")), slips.Sum(s => Stat(s, "AHL")), slips.Sum(s => s.TotalDeductions), slips.Sum(s => s.NetPay) } };
    }
}
