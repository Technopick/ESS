using Ess.Domain.Common;

namespace Ess.Domain.Documents;

public enum DocumentVisibility { EmployeeOnly = 0, HrOnly = 1, Company = 2, Department = 3 }

public class DocumentCategory : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool EmployeeCanUpload { get; set; }
    public int? RetentionMonths { get; set; }
}

/// <summary>Document metadata. File bytes live in the configured file store (disk / S3-compatible / Azure Blob).</summary>
public class EmployeeDocument : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid CategoryId { get; set; }
    public required string Title { get; set; }
    public DocumentVisibility Visibility { get; set; }
    public Guid? DepartmentId { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public int CurrentVersion { get; set; } = 1;
    /// <summary>Set when the document is attached to a leave, claim or request.</summary>
    public string? OwnerEntityType { get; set; }
    public Guid? OwnerEntityId { get; set; }
    public ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
}

public class DocumentVersion : Entity
{
    public Guid DocumentId { get; set; }
    public int Version { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string StorageKey { get; set; }
    public required string Sha256 { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public required string UploadedBy { get; set; }
    public bool MalwareScanPassed { get; set; }
}

public class DocumentAccessLog : Entity
{
    public Guid DocumentId { get; set; }
    public int Version { get; set; }
    public Guid UserId { get; set; }
    public DateTime AccessedAtUtc { get; set; }
    public required string Action { get; set; }
    public string? IpAddress { get; set; }
}
