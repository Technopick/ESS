using System.Linq.Expressions;
using Ess.Domain.Attendance;
using Ess.Domain.Audit;
using Ess.Domain.Common;
using Ess.Domain.Documents;
using Ess.Domain.Expenses;
using Ess.Domain.Identity;
using Ess.Domain.Integration;
using Ess.Domain.Leave;
using Ess.Domain.Notifications;
using Ess.Domain.Organization;
using Ess.Domain.Payroll;
using Ess.Domain.Requests;
using Ess.Domain.Training;
using Ess.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Ess.Infrastructure.Persistence;

/// <summary>
/// Portal database (PostgreSQL). Holds portal-owned data and caches of NAV reference data only;
/// NAV stays the system of record for employee, leave ledger and payroll data (spec §3.5).
/// </summary>
public class EssDbContext : DbContext
{
    public EssDbContext(DbContextOptions<EssDbContext> options) : base(options) { }

    // Identity & access
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Organisation (NAV mirrors)
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeMapping> EmployeeMappings => Set<EmployeeMapping>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Location> Locations => Set<Location>();

    // Leave
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveApplication> LeaveApplications => Set<LeaveApplication>();
    public DbSet<LeaveBalanceCache> LeaveBalancesCache => Set<LeaveBalanceCache>();
    public DbSet<PublicHoliday> PublicHolidays => Set<PublicHoliday>();

    // Payroll
    public DbSet<PayrollPeriodCache> PayrollPeriodsCache => Set<PayrollPeriodCache>();
    public DbSet<PayslipMetadata> PayslipMetadata => Set<PayslipMetadata>();

    // Attendance
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<AttendanceRequest> AttendanceRequests => Set<AttendanceRequest>();
    public DbSet<Shift> Shifts => Set<Shift>();

    // Expenses
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<ExpenseClaim> ExpenseClaims => Set<ExpenseClaim>();
    public DbSet<ExpenseClaimLine> ExpenseClaimLines => Set<ExpenseClaimLine>();

    // Documents
    public DbSet<DocumentCategory> DocumentCategories => Set<DocumentCategory>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<DocumentAccessLog> DocumentAccessLogs => Set<DocumentAccessLog>();

    // Requests
    public DbSet<HrRequestType> HrRequestTypes => Set<HrRequestType>();
    public DbSet<HrRequest> HrRequests => Set<HrRequest>();
    public DbSet<ProfileChangeRequest> ProfileChangeRequests => Set<ProfileChangeRequest>();

    // Workflow
    public DbSet<WorkflowDefinition> ApprovalWorkflows => Set<WorkflowDefinition>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<ApprovalTask> ApprovalTasks => Set<ApprovalTask>();
    public DbSet<ApprovalHistory> ApprovalHistory => Set<ApprovalHistory>();
    public DbSet<ApprovalDelegation> ApprovalDelegations => Set<ApprovalDelegation>();

    // Notifications
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<Announcement> Announcements => Set<Announcement>();

    // Training
    public DbSet<TrainingProgram> TrainingPrograms => Set<TrainingProgram>();
    public DbSet<EmployeeTraining> EmployeeTraining => Set<EmployeeTraining>();
    public DbSet<EmployeeQualification> EmployeeQualifications => Set<EmployeeQualification>();

    // Integration
    public DbSet<NavIntegrationSettings> NavIntegrationSettings => Set<NavIntegrationSettings>();
    public DbSet<NavEndpointMapping> NavEndpointMappings => Set<NavEndpointMapping>();
    public DbSet<NavSyncJob> NavSyncJobs => Set<NavSyncJob>();
    public DbSet<NavSyncLog> NavSyncLogs => Set<NavSyncLog>();
    public DbSet<IntegrationQueueItem> IntegrationQueue => Set<IntegrationQueueItem>();

    // Audit & settings
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<ReferenceSequence> ReferenceSequences => Set<ReferenceSequence>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Soft-delete filter on every auditable entity.
        foreach (var et in b.Model.GetEntityTypes().Where(t => typeof(AuditableEntity).IsAssignableFrom(t.ClrType)))
        {
            var p = Expression.Parameter(et.ClrType, "e");
            var body = Expression.Not(Expression.Property(p, nameof(AuditableEntity.IsDeleted)));
            b.Entity(et.ClrType).HasQueryFilter(Expression.Lambda(body, p));
        }

        // All string columns get a sane default max length unless configured otherwise.
        foreach (var prop in b.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(string) && p.GetMaxLength() is null))
        {
            var n = prop.Name;
            if (n.EndsWith("Json") || n.EndsWith("Encrypted") || n.EndsWith("Masked") || n is "Body" or "BodyHtml" or "Description"
                || n is "Error" or "LastError" or "Value" or "Comment" or "Resolution" or "Reason" or "LastHealthCheckMessage" or "ChangesJson")
                continue; // text
            prop.SetMaxLength(n.EndsWith("Url") || n is "StorageKey" or "Endpoint" or "LinkUrl" or "UserAgent" ? 1024 : 256);
        }

        b.ApplyConfigurationsFromAssembly(typeof(EssDbContext).Assembly);
    }
}
