using Ess.Domain.Common;

namespace Ess.Domain.Organization;

/// <summary>A NAV company served by the portal. One portal can serve several companies and NAV instances.</summary>
public class Company : AuditableEntity
{
    public required string Code { get; set; }
    public required string DisplayName { get; set; }
    /// <summary>Company name exactly as NAV knows it (used in SOAP/OData URLs).</summary>
    public required string NavCompanyName { get; set; }
    /// <summary>Business Central company GUID, when using BC APIs.</summary>
    public Guid? NavCompanyGuid { get; set; }
    public Guid? IntegrationSettingsId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Department : AuditableEntity, ICompanyScoped, INavMirrored
{
    public Guid CompanyId { get; set; }
    public required string NavKey { get; set; }
    public required string Name { get; set; }
    public Guid? ParentDepartmentId { get; set; }
    public Guid? HeadEmployeeId { get; set; }
    public DateTime LastSyncedAtUtc { get; set; }
    public string? NavVersionToken { get; set; }
}

public class Position : AuditableEntity, ICompanyScoped, INavMirrored
{
    public Guid CompanyId { get; set; }
    public required string NavKey { get; set; }
    public required string Title { get; set; }
    public Guid? DepartmentId { get; set; }
    public DateTime LastSyncedAtUtc { get; set; }
    public string? NavVersionToken { get; set; }
}

public class Location : AuditableEntity, ICompanyScoped, INavMirrored
{
    public Guid CompanyId { get; set; }
    public required string NavKey { get; set; }
    public required string Name { get; set; }
    public DateTime LastSyncedAtUtc { get; set; }
    public string? NavVersionToken { get; set; }
}

public enum EmploymentStatus { Active = 0, Inactive = 1, Terminated = 2, OnProbation = 3, Suspended = 4 }

/// <summary>
/// Cached, read-mostly copy of the NAV Employee record. Only fields needed for portal display,
/// routing and authorization are kept; NAV remains authoritative and changes go through change requests.
/// </summary>
public class Employee : AuditableEntity, ICompanyScoped, INavMirrored
{
    public Guid CompanyId { get; set; }
    /// <summary>NAV Employee "No.".</summary>
    public required string NavKey { get; set; }
    public required string FirstName { get; set; }
    public string? MiddleName { get; set; }
    public required string LastName { get; set; }
    public string? WorkEmail { get; set; }
    public string? MobilePhone { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? PositionId { get; set; }
    public Guid? LocationId { get; set; }
    /// <summary>Line manager, resolved from NAV "Manager No." or equivalent.</summary>
    public Guid? ManagerEmployeeId { get; set; }
    public EmploymentStatus Status { get; set; }
    public DateOnly? EmploymentDate { get; set; }
    /// <summary>End of the current fixed-term contract (HR add-on field; null for permanent staff or when NAV has no such field).</summary>
    public DateOnly? ContractEndDate { get; set; }
    /// <summary>Date employment ended (standard NAV "Termination Date"). Used for turnover figures.</summary>
    public DateOnly? TerminationDate { get; set; }
    public string? Gender { get; set; }
    /// <summary>Stored only if company privacy settings allow birthday display.</summary>
    public DateOnly? BirthDate { get; set; }
    public Guid? PhotoDocumentId { get; set; }
    public DateTime LastSyncedAtUtc { get; set; }
    public string? NavVersionToken { get; set; }

    public string FullName => string.Join(' ', new[] { FirstName, MiddleName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>Links a portal user to exactly one NAV employee per company (spec §10 EmployeeMappings).</summary>
public class EmployeeMapping : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public Guid EmployeeId { get; set; }
    public required string NavEmployeeNo { get; set; }
    public bool IsPrimary { get; set; } = true;
    public DateTime? VerifiedAtUtc { get; set; }
}
