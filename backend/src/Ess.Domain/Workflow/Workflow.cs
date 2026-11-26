using Ess.Domain.Common;

namespace Ess.Domain.Workflow;

/// <summary>Which system owns approval for a document type (spec §5: no duplicate approval processes).</summary>
public enum WorkflowOwner
{
    /// <summary>The portal runs the approvals, then submits the approved document to NAV.</summary>
    Portal = 0,
    /// <summary>NAV's approval workflow runs; the portal submits on create and mirrors NAV's approval status.</summary>
    Nav = 1
}

public enum ApproverType
{
    LineManager = 0,
    DepartmentHead = 1,
    Role = 2,
    SpecificUser = 3,
    ManagerOfManager = 4
}

public enum StepMode { AnyOne = 0, All = 1 }

public enum ApprovalAction
{
    Submitted = 0, Approved = 1, Rejected = 2, Returned = 3, Delegated = 4,
    Escalated = 5, Cancelled = 6, Resubmitted = 7, AutoApproved = 8, NavStatusMirrored = 9
}

public enum WorkflowInstanceStatus { Running = 0, Approved = 1, Rejected = 2, Returned = 3, Cancelled = 4 }

public class WorkflowDefinition : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>Document type this workflow applies to, e.g. "Leave", "Expense", "ProfileChange", "HrRequest:BANK".</summary>
    public required string DocumentType { get; set; }
    public WorkflowOwner Owner { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
}

/// <summary>
/// One level of approval. Steps with the same <see cref="Sequence"/> run in parallel.
/// Optional amount bounds give role-based approval limits.
/// </summary>
public class WorkflowStep : Entity
{
    public Guid WorkflowDefinitionId { get; set; }
    public int Sequence { get; set; }
    public required string Name { get; set; }
    public ApproverType ApproverType { get; set; }
    public string? ApproverRole { get; set; }
    public Guid? ApproverUserId { get; set; }
    public StepMode Mode { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public int? DeadlineHours { get; set; }
    public int? ReminderEveryHours { get; set; }
    public string? EscalateToRole { get; set; }
    public bool SkipIfApproverIsRequester { get; set; } = true;
}

public class WorkflowInstance : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid WorkflowDefinitionId { get; set; }
    public int DefinitionVersion { get; set; }
    public required string DocumentType { get; set; }
    public Guid DocumentId { get; set; }
    public required string DocumentReference { get; set; }
    public Guid RequesterUserId { get; set; }
    public Guid RequesterEmployeeId { get; set; }
    public decimal? Amount { get; set; }
    public int CurrentSequence { get; set; }
    public WorkflowInstanceStatus Status { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public uint RowVersion { get; set; }
    public ICollection<ApprovalTask> Tasks { get; set; } = new List<ApprovalTask>();
}

public enum ApprovalTaskStatus { Pending = 0, Approved = 1, Rejected = 2, Returned = 3, Skipped = 4, Superseded = 5 }

/// <summary>A pending or completed approval assigned to one user.</summary>
public class ApprovalTask : AuditableEntity
{
    public Guid WorkflowInstanceId { get; set; }
    public Guid WorkflowStepId { get; set; }
    public int Sequence { get; set; }
    public Guid AssigneeUserId { get; set; }
    /// <summary>If the task was routed to a delegate, the original approver.</summary>
    public Guid? OnBehalfOfUserId { get; set; }
    public ApprovalTaskStatus Status { get; set; }
    public DateTime? DueAtUtc { get; set; }
    public DateTime? LastReminderAtUtc { get; set; }
    public DateTime? ActedAtUtc { get; set; }
    public string? Comment { get; set; }
}

/// <summary>Append-only history of approval actions, timestamped and attributed (spec §5).</summary>
public class ApprovalHistory : Entity
{
    public Guid WorkflowInstanceId { get; set; }
    public Guid? ApprovalTaskId { get; set; }
    public ApprovalAction Action { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid? OnBehalfOfUserId { get; set; }
    public string? Comment { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? IpAddress { get; set; }
}

public class ApprovalDelegation : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    /// <summary>Null means all document types.</summary>
    public string? DocumentType { get; set; }
    public string? Reason { get; set; }
}
