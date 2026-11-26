using Ess.Domain.Attendance;
using Ess.Domain.Audit;
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
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ess.Infrastructure.Persistence.Configurations;

// Unique keys, foreign keys and indexes. Unique indexes on soft-deletable rows are partial
// (WHERE NOT is_deleted) so a deleted row never blocks re-creating the same business key.

internal class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> e)
    {
        e.HasIndex(x => x.NormalizedUserName).IsUnique().HasFilter("NOT is_deleted");
        e.HasIndex(x => x.NormalizedEmail).IsUnique().HasFilter("NOT is_deleted");
        e.HasIndex(x => new { x.ExternalProvider, x.ExternalSubjectId });
        e.HasMany(x => x.UserRoles).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<Company>().WithMany().HasForeignKey(x => x.DefaultCompanyId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal class RoleConfig : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> e)
    {
        e.HasIndex(x => x.NormalizedName).IsUnique().HasFilter("NOT is_deleted");
        e.HasMany(x => x.RolePermissions).WithOne(x => x.Role).HasForeignKey(x => x.RoleId);
    }
}

internal class PermissionConfig : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> e) => e.HasIndex(x => x.Code).IsUnique();
}

internal class RolePermissionConfig : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> e)
    {
        e.HasKey(x => new { x.RoleId, x.PermissionId });
        e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId);
    }
}

internal class UserRoleConfig : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> e)
    {
        e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
        e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId);
        e.HasOne<Department>().WithMany().HasForeignKey(x => x.ScopeDepartmentId);
        e.HasIndex(x => new { x.UserId, x.RoleId, x.CompanyId, x.ScopeDepartmentId }).IsUnique().HasFilter("NOT is_deleted");
    }
}

internal class RefreshTokenConfig : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> e)
    {
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
        e.HasIndex(x => x.FamilyId);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class CompanyConfig : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> e)
    {
        e.HasIndex(x => x.Code).IsUnique().HasFilter("NOT is_deleted");
        e.HasOne<NavIntegrationSettings>().WithMany().HasForeignKey(x => x.IntegrationSettingsId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class EmployeeConfig : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> e)
    {
        e.Ignore(x => x.FullName);
        e.HasIndex(x => new { x.CompanyId, x.NavKey }).IsUnique().HasFilter("NOT is_deleted");
        e.HasIndex(x => new { x.CompanyId, x.DepartmentId, x.Status });
        e.HasIndex(x => x.ManagerEmployeeId);
        e.HasIndex(x => new { x.CompanyId, x.LastName, x.FirstName });
        e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne<Position>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.ManagerEmployeeId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal class EmployeeMappingConfig : IEntityTypeConfiguration<EmployeeMapping>
{
    public void Configure(EntityTypeBuilder<EmployeeMapping> e)
    {
        // A user maps to at most one employee per company, and an employee to at most one user.
        e.HasIndex(x => new { x.UserId, x.CompanyId }).IsUnique().HasFilter("NOT is_deleted");
        e.HasIndex(x => new { x.CompanyId, x.NavEmployeeNo }).IsUnique().HasFilter("NOT is_deleted");
        e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId);
        e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId);
    }
}

internal class DepartmentConfig : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> e)
    {
        e.HasIndex(x => new { x.CompanyId, x.NavKey }).IsUnique().HasFilter("NOT is_deleted");
        e.HasOne<Department>().WithMany().HasForeignKey(x => x.ParentDepartmentId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId);
    }
}

internal class PositionConfig : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> e) => e.HasIndex(x => new { x.CompanyId, x.NavKey }).IsUnique().HasFilter("NOT is_deleted");
}

internal class LocationConfig : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> e) => e.HasIndex(x => new { x.CompanyId, x.NavKey }).IsUnique().HasFilter("NOT is_deleted");
}

internal class LeaveTypeConfig : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> e)
    {
        e.HasIndex(x => new { x.CompanyId, x.NavKey }).IsUnique().HasFilter("NOT is_deleted");
        e.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(x => x.WorkflowDefinitionId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal class LeaveBalanceConfig : IEntityTypeConfiguration<LeaveBalanceCache>
{
    public void Configure(EntityTypeBuilder<LeaveBalanceCache> e)
    {
        e.ToTable("leave_balances_cache");
        e.HasIndex(x => new { x.EmployeeId, x.LeaveTypeId, x.LeavePeriodCode }).IsUnique();
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Cascade);
        e.Property(x => x.Entitlement).HasPrecision(9, 2);
        e.Property(x => x.BroughtForward).HasPrecision(9, 2);
        e.Property(x => x.Taken).HasPrecision(9, 2);
        e.Property(x => x.PendingApproval).HasPrecision(9, 2);
        e.Property(x => x.Available).HasPrecision(9, 2);
    }
}

internal class LeaveApplicationConfig : IEntityTypeConfiguration<LeaveApplication>
{
    public void Configure(EntityTypeBuilder<LeaveApplication> e)
    {
        e.HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique();
        e.HasIndex(x => new { x.EmployeeId, x.StartDate, x.EndDate });
        e.HasIndex(x => new { x.CompanyId, x.Status });
        e.HasIndex(x => x.NavStatus);
        e.Property(x => x.DaysRequested).HasPrecision(9, 2);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.RelieverEmployeeId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal class PublicHolidayConfig : IEntityTypeConfiguration<PublicHoliday>
{
    public void Configure(EntityTypeBuilder<PublicHoliday> e) => e.HasIndex(x => new { x.CompanyId, x.Date }).IsUnique().HasFilter("NOT is_deleted");
}

internal class PayrollPeriodConfig : IEntityTypeConfiguration<PayrollPeriodCache>
{
    public void Configure(EntityTypeBuilder<PayrollPeriodCache> e)
    {
        e.ToTable("payroll_periods_cache");
        e.HasIndex(x => new { x.CompanyId, x.NavKey }).IsUnique();
        e.HasIndex(x => new { x.CompanyId, x.StartDate });
    }
}

internal class PayslipMetadataConfig : IEntityTypeConfiguration<PayslipMetadata>
{
    public void Configure(EntityTypeBuilder<PayslipMetadata> e)
    {
        e.HasIndex(x => new { x.EmployeeId, x.PayrollPeriodId }).IsUnique().HasFilter("NOT is_deleted");
        e.Property(x => x.NetPay).HasPrecision(18, 2);
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<PayrollPeriodCache>().WithMany().HasForeignKey(x => x.PayrollPeriodId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class AttendanceRecordConfig : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> e)
    {
        e.HasIndex(x => new { x.EmployeeId, x.WorkDate });
        e.HasIndex(x => new { x.Source, x.ExternalRef }).IsUnique().HasFilter("external_ref IS NOT NULL");
        e.Property(x => x.WorkedHours).HasPrecision(6, 2);
        e.Property(x => x.Latitude).HasPrecision(9, 6);
        e.Property(x => x.Longitude).HasPrecision(9, 6);
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class AttendanceRequestConfig : IEntityTypeConfiguration<AttendanceRequest>
{
    public void Configure(EntityTypeBuilder<AttendanceRequest> e)
    {
        e.HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique();
        e.Property(x => x.OvertimeHours).HasPrecision(6, 2);
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class ShiftConfig : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> e) => e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("NOT is_deleted");
}

internal class ExpenseCategoryConfig : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> e)
    {
        e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("NOT is_deleted");
        e.Property(x => x.MaxAmountPerLine).HasPrecision(18, 2);
    }
}

internal class ExpenseClaimConfig : IEntityTypeConfiguration<ExpenseClaim>
{
    public void Configure(EntityTypeBuilder<ExpenseClaim> e)
    {
        e.HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique();
        e.HasIndex(x => new { x.EmployeeId, x.Status });
        e.HasIndex(x => x.NavStatus);
        e.Property(x => x.TotalAmount).HasPrecision(18, 2);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ExpenseClaimId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class ExpenseClaimLineConfig : IEntityTypeConfiguration<ExpenseClaimLine>
{
    public void Configure(EntityTypeBuilder<ExpenseClaimLine> e)
    {
        e.HasIndex(x => new { x.ExpenseClaimId, x.LineNo }).IsUnique();
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.ExchangeRate).HasPrecision(18, 6);
        e.HasOne<ExpenseCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<EmployeeDocument>().WithMany().HasForeignKey(x => x.ReceiptDocumentId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal class DocumentCategoryConfig : IEntityTypeConfiguration<DocumentCategory>
{
    public void Configure(EntityTypeBuilder<DocumentCategory> e) => e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("NOT is_deleted");
}

internal class EmployeeDocumentConfig : IEntityTypeConfiguration<EmployeeDocument>
{
    public void Configure(EntityTypeBuilder<EmployeeDocument> e)
    {
        e.HasIndex(x => new { x.CompanyId, x.EmployeeId, x.CategoryId });
        e.HasIndex(x => new { x.OwnerEntityType, x.OwnerEntityId });
        e.HasIndex(x => x.ExpiryDate);
        e.HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<DocumentCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class DocumentVersionConfig : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> e) => e.HasIndex(x => new { x.DocumentId, x.Version }).IsUnique();
}

internal class DocumentAccessLogConfig : IEntityTypeConfiguration<DocumentAccessLog>
{
    public void Configure(EntityTypeBuilder<DocumentAccessLog> e) => e.HasIndex(x => new { x.DocumentId, x.AccessedAtUtc });
}

internal class HrRequestTypeConfig : IEntityTypeConfiguration<HrRequestType>
{
    public void Configure(EntityTypeBuilder<HrRequestType> e)
    {
        e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("NOT is_deleted");
        e.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(x => x.WorkflowDefinitionId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal class HrRequestConfig : IEntityTypeConfiguration<HrRequest>
{
    public void Configure(EntityTypeBuilder<HrRequest> e)
    {
        e.ToTable("hr_requests");
        e.HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique();
        e.HasIndex(x => new { x.EmployeeId, x.Status });
        e.HasIndex(x => new { x.AssignedToUserId, x.Status });
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasOne<HrRequestType>().WithMany().HasForeignKey(x => x.RequestTypeId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class ProfileChangeRequestConfig : IEntityTypeConfiguration<ProfileChangeRequest>
{
    public void Configure(EntityTypeBuilder<ProfileChangeRequest> e)
    {
        e.HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique();
        e.HasIndex(x => new { x.EmployeeId, x.Status });
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class WorkflowDefinitionConfig : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> e)
    {
        e.ToTable("approval_workflows");
        e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("NOT is_deleted");
        e.HasIndex(x => new { x.CompanyId, x.DocumentType, x.IsActive });
        e.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.WorkflowDefinitionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class WorkflowStepConfig : IEntityTypeConfiguration<WorkflowStep>
{
    public void Configure(EntityTypeBuilder<WorkflowStep> e)
    {
        e.HasIndex(x => new { x.WorkflowDefinitionId, x.Sequence });
        e.Property(x => x.MinAmount).HasPrecision(18, 2);
        e.Property(x => x.MaxAmount).HasPrecision(18, 2);
    }
}

internal class WorkflowInstanceConfig : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> e)
    {
        e.HasIndex(x => new { x.DocumentType, x.DocumentId });
        e.HasIndex(x => new { x.RequesterUserId, x.Status });
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(x => x.WorkflowDefinitionId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Tasks).WithOne().HasForeignKey(x => x.WorkflowInstanceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class ApprovalTaskConfig : IEntityTypeConfiguration<ApprovalTask>
{
    public void Configure(EntityTypeBuilder<ApprovalTask> e)
    {
        e.HasIndex(x => new { x.AssigneeUserId, x.Status });
        e.HasIndex(x => new { x.Status, x.DueAtUtc });
        e.HasOne<WorkflowStep>().WithMany().HasForeignKey(x => x.WorkflowStepId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.AssigneeUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class ApprovalHistoryConfig : IEntityTypeConfiguration<ApprovalHistory>
{
    public void Configure(EntityTypeBuilder<ApprovalHistory> e)
    {
        e.ToTable("approval_history");
        e.HasIndex(x => new { x.WorkflowInstanceId, x.OccurredAtUtc });
        e.HasOne<WorkflowInstance>().WithMany().HasForeignKey(x => x.WorkflowInstanceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class ApprovalDelegationConfig : IEntityTypeConfiguration<ApprovalDelegation>
{
    public void Configure(EntityTypeBuilder<ApprovalDelegation> e) => e.HasIndex(x => new { x.FromUserId, x.StartsAtUtc, x.EndsAtUtc });
}

internal class NotificationConfig : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> e)
    {
        e.HasIndex(x => new { x.RecipientUserId, x.Channel, x.ReadAtUtc });
        e.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        e.HasOne<User>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class NotificationTemplateConfig : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> e) =>
        e.HasIndex(x => new { x.CompanyId, x.EventCode, x.Channel }).IsUnique().HasFilter("NOT is_deleted");
}

internal class AnnouncementConfig : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> e) => e.HasIndex(x => new { x.CompanyId, x.PublishAtUtc });
}

internal class TrainingProgramConfig : IEntityTypeConfiguration<TrainingProgram>
{
    public void Configure(EntityTypeBuilder<TrainingProgram> e)
    {
        e.ToTable("training_programs");
        e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("NOT is_deleted");
        e.Property(x => x.CostPerParticipant).HasPrecision(18, 2);
    }
}

internal class EmployeeTrainingConfig : IEntityTypeConfiguration<EmployeeTraining>
{
    public void Configure(EntityTypeBuilder<EmployeeTraining> e)
    {
        e.ToTable("employee_training");
        e.HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique();
        e.HasIndex(x => new { x.EmployeeId, x.TrainingProgramId }).IsUnique().HasFilter("NOT is_deleted");
        e.HasOne<TrainingProgram>().WithMany().HasForeignKey(x => x.TrainingProgramId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class EmployeeQualificationConfig : IEntityTypeConfiguration<EmployeeQualification>
{
    public void Configure(EntityTypeBuilder<EmployeeQualification> e)
    {
        e.HasIndex(x => new { x.EmployeeId, x.NavKey }).IsUnique().HasFilter("NOT is_deleted");
        e.HasIndex(x => x.ExpiresOn);
    }
}

internal class NavIntegrationSettingsConfig : IEntityTypeConfiguration<NavIntegrationSettings>
{
    public void Configure(EntityTypeBuilder<NavIntegrationSettings> e)
    {
        e.ToTable("nav_integration_settings");
        e.HasIndex(x => x.Name).IsUnique().HasFilter("NOT is_deleted");
    }
}

internal class NavEndpointMappingConfig : IEntityTypeConfiguration<NavEndpointMapping>
{
    public void Configure(EntityTypeBuilder<NavEndpointMapping> e)
    {
        e.HasIndex(x => new { x.IntegrationSettingsId, x.LogicalName }).IsUnique().HasFilter("NOT is_deleted");
        e.HasOne<NavIntegrationSettings>().WithMany().HasForeignKey(x => x.IntegrationSettingsId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class NavSyncJobConfig : IEntityTypeConfiguration<NavSyncJob>
{
    public void Configure(EntityTypeBuilder<NavSyncJob> e)
    {
        e.ToTable("nav_sync_jobs");
        e.HasIndex(x => new { x.CompanyId, x.EntityName, x.StartedAtUtc });
        e.HasIndex(x => x.Status);
    }
}

internal class NavSyncLogConfig : IEntityTypeConfiguration<NavSyncLog>
{
    public void Configure(EntityTypeBuilder<NavSyncLog> e)
    {
        e.ToTable("nav_sync_logs");
        e.HasIndex(x => x.OccurredAtUtc);
        e.HasIndex(x => new { x.Success, x.OccurredAtUtc });
        e.HasIndex(x => x.QueueItemId);
        e.HasIndex(x => x.SyncJobId);
        e.HasIndex(x => x.CorrelationId);
    }
}

internal class IntegrationQueueConfig : IEntityTypeConfiguration<IntegrationQueueItem>
{
    public void Configure(EntityTypeBuilder<IntegrationQueueItem> e)
    {
        e.ToTable("integration_queue");
        // Idempotency: the same business action can never be enqueued twice.
        e.HasIndex(x => new { x.CompanyId, x.Operation, x.IdempotencyKey }).IsUnique();
        // Dispatcher scan: due items by priority.
        e.HasIndex(x => new { x.Status, x.NextAttemptAtUtc, x.Priority });
        e.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}

internal class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> e)
    {
        e.HasIndex(x => x.OccurredAtUtc);
        e.HasIndex(x => new { x.UserId, x.OccurredAtUtc });
        e.HasIndex(x => new { x.EntityType, x.EntityId });
        e.HasIndex(x => x.Action);
    }
}

internal class SystemSettingConfig : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> e) =>
        e.HasIndex(x => new { x.CompanyId, x.Key }).IsUnique().HasFilter("NOT is_deleted").AreNullsDistinct(false);
}

internal class ReferenceSequenceConfig : IEntityTypeConfiguration<ReferenceSequence>
{
    public void Configure(EntityTypeBuilder<ReferenceSequence> e) => e.HasKey(x => new { x.CompanyId, x.Prefix, x.Year });
}
