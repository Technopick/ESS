using Ess.Domain.Common;

namespace Ess.Domain.Requests;

/// <summary>Configurable HR request type (confirmation letter, bank detail change, ...).</summary>
public class HrRequestType : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    /// <summary>JSON schema of the request's form fields, rendered dynamically by the frontend.</summary>
    public string? FormSchemaJson { get; set; }
    public Guid? WorkflowDefinitionId { get; set; }
    /// <summary>When set, approval triggers this NAV operation (e.g. "Employee.UpdateBankDetails").</summary>
    public string? NavOperation { get; set; }
    public bool IsSensitive { get; set; }
    public int? SlaHours { get; set; }
    public bool IsActive { get; set; } = true;
}

public class HrRequest : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public required string Reference { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid RequestTypeId { get; set; }
    public required string Subject { get; set; }
    /// <summary>Form payload. Sensitive payloads (bank details) are encrypted at rest.</summary>
    public string? PayloadJson { get; set; }
    public bool PayloadEncrypted { get; set; }
    public RequestStatus Status { get; set; }
    public Guid? WorkflowInstanceId { get; set; }
    public NavSyncStatus NavStatus { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? Resolution { get; set; }
    public uint RowVersion { get; set; }
}

/// <summary>A proposed change to the employee's NAV record. Never applied to NAV until approved (spec §4.2).</summary>
public class ProfileChangeRequest : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public required string Reference { get; set; }
    public Guid EmployeeId { get; set; }
    /// <summary>Section of the profile, e.g. "Contact", "Address", "BankDetails", "NextOfKin".</summary>
    public required string Section { get; set; }
    public required string ChangesJson { get; set; }
    public bool ChangesEncrypted { get; set; }
    public RequestStatus Status { get; set; }
    public Guid? WorkflowInstanceId { get; set; }
    public NavSyncStatus NavStatus { get; set; }
}
