using Ess.Domain.Identity;

namespace Ess.Application.Security;

/// <summary>
/// Catalogue of granular permissions (spec §9). Codes are stored in the Permissions table and
/// checked by policy-based authorization. Suffix ".own" means "only the caller's own records".
/// </summary>
public static class Permissions
{
    public const string ProfileViewOwn = "profile.view.own";
    public const string ProfileRequestChange = "profile.change.request";
    public const string LeaveApply = "leave.apply";
    public const string LeaveViewOwn = "leave.view.own";
    public const string LeaveViewTeam = "leave.view.team";
    public const string LeaveViewAll = "leave.view.all";
    public const string LeaveConfigure = "leave.configure";
    public const string PayrollViewOwn = "payroll.view.own";
    public const string PayrollViewAll = "payroll.view.all";
    public const string PayrollPublish = "payroll.publish";
    public const string ExpenseSubmit = "expense.submit";
    public const string ExpenseViewAll = "expense.view.all";
    public const string ExpenseConfigure = "expense.configure";
    public const string AttendanceClock = "attendance.clock";
    public const string AttendanceViewTeam = "attendance.view.team";
    public const string AttendanceManage = "attendance.manage";
    public const string DocumentsViewOwn = "documents.view.own";
    public const string DocumentsManage = "documents.manage";
    public const string TrainingApply = "training.apply";
    public const string TrainingManage = "training.manage";
    public const string RequestsSubmit = "requests.submit";
    public const string RequestsManage = "requests.manage";
    public const string ApprovalsAct = "approvals.act";
    public const string ApprovalsMonitor = "approvals.monitor";
    public const string WorkflowConfigure = "workflow.configure";
    public const string EmployeesViewDirectory = "employees.view.directory";
    public const string EmployeesViewAll = "employees.view.all";
    public const string AnnouncementsManage = "announcements.manage";
    public const string ReportsHr = "reports.hr";
    public const string ReportsFinance = "reports.finance";
    public const string ReportsPayroll = "reports.payroll";
    public const string DashboardManagement = "dashboard.management";
    public const string DataExport = "data.export";
    public const string NotificationsConfigure = "notifications.configure";
    public const string UsersManage = "users.manage";
    public const string SettingsManage = "settings.manage";
    public const string AuditView = "audit.view";
    public const string IntegrationView = "integration.nav.view";
    public const string IntegrationConfigure = "integration.nav.configure";
    public const string IntegrationOperate = "integration.nav.operate";

    private static readonly string[] EmployeeBase =
    {
        ProfileViewOwn, ProfileRequestChange, LeaveApply, LeaveViewOwn, PayrollViewOwn, ExpenseSubmit,
        AttendanceClock, DocumentsViewOwn, TrainingApply, RequestsSubmit, EmployeesViewDirectory
    };

    /// <summary>Default role → permission matrix seeded on first run. Administrators can change it afterwards.</summary>
    public static IReadOnlyDictionary<string, string[]> DefaultMatrix { get; } = new Dictionary<string, string[]>
    {
        [SystemRoles.Employee] = EmployeeBase,
        [SystemRoles.Supervisor] = EmployeeBase.Concat(new[] { ApprovalsAct, LeaveViewTeam, AttendanceViewTeam }).ToArray(),
        [SystemRoles.DepartmentManager] = EmployeeBase.Concat(new[] { ApprovalsAct, LeaveViewTeam, AttendanceViewTeam, DashboardManagement }).ToArray(),
        [SystemRoles.HrOfficer] = EmployeeBase.Concat(new[]
        {
            ApprovalsAct, ApprovalsMonitor, LeaveViewAll, EmployeesViewAll, DocumentsManage, RequestsManage,
            TrainingManage, AnnouncementsManage, AttendanceManage, ReportsHr, IntegrationView
        }).ToArray(),
        [SystemRoles.HrManager] = EmployeeBase.Concat(new[]
        {
            ApprovalsAct, ApprovalsMonitor, LeaveViewAll, LeaveConfigure, EmployeesViewAll, DocumentsManage, RequestsManage,
            TrainingManage, AnnouncementsManage, AttendanceManage, ReportsHr, ReportsPayroll, PayrollViewAll, PayrollPublish,
            WorkflowConfigure, NotificationsConfigure, DashboardManagement, DataExport, IntegrationView
        }).ToArray(),
        [SystemRoles.FinanceOfficer] = EmployeeBase.Concat(new[] { ApprovalsAct, ExpenseViewAll, ReportsFinance }).ToArray(),
        [SystemRoles.FinanceManager] = EmployeeBase.Concat(new[] { ApprovalsAct, ExpenseViewAll, ExpenseConfigure, ReportsFinance, DataExport }).ToArray(),
        [SystemRoles.ManagementViewer] = new[] { DashboardManagement, ReportsHr, EmployeesViewDirectory },
        [SystemRoles.IntegrationAdministrator] = new[] { IntegrationView, IntegrationConfigure, IntegrationOperate, AuditView },
        [SystemRoles.SystemAdministrator] = new[]
        {
            UsersManage, SettingsManage, AuditView, WorkflowConfigure, NotificationsConfigure,
            IntegrationView, IntegrationConfigure, IntegrationOperate, LeaveConfigure, ExpenseConfigure
        },
    };

    public static IEnumerable<string> All() =>
        typeof(Permissions).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!);

    public static string ModuleOf(string code) => code.Split('.')[0];
}
