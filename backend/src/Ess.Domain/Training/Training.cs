using Ess.Domain.Common;

namespace Ess.Domain.Training;

public enum TrainingParticipationStatus { Applied = 0, Approved = 1, Rejected = 2, Registered = 3, Attended = 4, Completed = 5, NoShow = 6, Cancelled = 7 }

public class TrainingProgram : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? Provider { get; set; }
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public string? Venue { get; set; }
    public int? Capacity { get; set; }
    public decimal? CostPerParticipant { get; set; }
    public bool RequiresApproval { get; set; } = true;
    public bool IsPublished { get; set; }
    /// <summary>NAV training/course code, when training is maintained in NAV.</summary>
    public string? NavKey { get; set; }
}

public class EmployeeTraining : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public required string Reference { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid TrainingProgramId { get; set; }
    public TrainingParticipationStatus Status { get; set; }
    public Guid? WorkflowInstanceId { get; set; }
    public int? FeedbackRating { get; set; }
    public string? FeedbackComment { get; set; }
    public Guid? CertificateDocumentId { get; set; }
}

/// <summary>Qualifications and professional certifications (ICPAK, LSK, EBK, ...) with expiry reminders.</summary>
public class EmployeeQualification : AuditableEntity, ICompanyScoped, INavMirrored
{
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public required string NavKey { get; set; }
    public required string Name { get; set; }
    public string? Institution { get; set; }
    public bool IsProfessionalCertification { get; set; }
    public DateOnly? AwardedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public Guid? DocumentId { get; set; }
    public DateTime LastSyncedAtUtc { get; set; }
    public string? NavVersionToken { get; set; }
}
