using Ess.Domain.Common;

namespace Ess.Domain.Identity;

public class User : AuditableEntity
{
    public required string UserName { get; set; }
    public required string NormalizedUserName { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public string? PasswordHash { get; set; }
    public bool EmailConfirmed { get; set; }
    public string? PhoneNumber { get; set; }
    public bool MfaEnabled { get; set; }
    /// <summary>TOTP secret, encrypted at rest by the infrastructure layer.</summary>
    public string? MfaSecretEncrypted { get; set; }
    /// <summary>SHA-256 hashes of unused one-time recovery codes, separated by ';'.</summary>
    public string? MfaRecoveryCodeHashes { get; set; }
    /// <summary>Last accepted TOTP time step, to block replay of the same code.</summary>
    public long? MfaLastTimeStep { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime? PasswordChangedAtUtc { get; set; }
    public int AccessFailedCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsActive { get; set; } = true;
    /// <summary>Optional external identity (Entra ID object id) for SSO.</summary>
    public string? ExternalSubjectId { get; set; }
    public string? ExternalProvider { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public Guid? DefaultCompanyId { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

public class Role : AuditableEntity
{
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public string? Description { get; set; }
    /// <summary>System roles (spec §9) cannot be deleted.</summary>
    public bool IsSystem { get; set; }
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class Permission : Entity
{
    /// <summary>Dotted code, e.g. "leave.apply", "payroll.view.own", "integration.nav.retry".</summary>
    public required string Code { get; set; }
    public required string Module { get; set; }
    public string? Description { get; set; }
}

public class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public Role? Role { get; set; }
    public Permission? Permission { get; set; }
}

/// <summary>Role assignment, optionally limited to a company and an organisational scope (department) for HR data isolation.</summary>
public class UserRole : AuditableEntity
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? ScopeDepartmentId { get; set; }
    public User? User { get; set; }
    public Role? Role { get; set; }
}

public class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    /// <summary>SHA-256 of the token; the raw token is only ever sent to the client.</summary>
    public required string TokenHash { get; set; }
    public required string FamilyId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByHash { get; set; }
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;
}

/// <summary>The ten system roles from spec §9.</summary>
public static class SystemRoles
{
    public const string Employee = "Employee";
    public const string Supervisor = "Supervisor";
    public const string DepartmentManager = "DepartmentManager";
    public const string HrOfficer = "HrOfficer";
    public const string HrManager = "HrManager";
    public const string FinanceOfficer = "FinanceOfficer";
    public const string FinanceManager = "FinanceManager";
    public const string SystemAdministrator = "SystemAdministrator";
    public const string ManagementViewer = "ManagementViewer";
    public const string IntegrationAdministrator = "IntegrationAdministrator";

    public static readonly string[] All =
    {
        Employee, Supervisor, DepartmentManager, HrOfficer, HrManager,
        FinanceOfficer, FinanceManager, SystemAdministrator, ManagementViewer, IntegrationAdministrator
    };
}

/// <summary>Single-use password reset / account invitation token, stored hashed.</summary>
public class PasswordResetToken : Entity
{
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    /// <summary>"reset" or "invite".</summary>
    public required string Purpose { get; set; }
    public string? RequestedFromIp { get; set; }
}
