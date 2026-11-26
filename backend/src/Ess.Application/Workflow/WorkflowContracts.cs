using Ess.Domain.Workflow;

namespace Ess.Application.Workflow;

/// <summary>Document types that go through approval. HR requests use "HrRequest:{type code}" and fall back to "HrRequest".</summary>
public static class DocumentTypes
{
    public const string Leave = "Leave";
    public const string Expense = "Expense";
    public const string ProfileChange = "ProfileChange";
    public const string HrRequest = "HrRequest";
    public const string Attendance = "Attendance";
    public const string Training = "Training";
}

public sealed record WorkflowStartRequest(
    Guid CompanyId,
    string DocumentType,
    Guid DocumentId,
    string DocumentReference,
    Guid RequesterUserId,
    Guid RequesterEmployeeId,
    decimal? Amount,
    string Summary,
    string? LinkUrl);

public enum WorkflowOutcome
{
    /// <summary>Waiting for approvers.</summary>
    Pending = 0,
    /// <summary>No approval step applied (or every step was skipped): approved immediately.</summary>
    Approved = 1,
    /// <summary>NAV owns approval for this document type: submit to NAV now and mirror its status.</summary>
    NavOwned = 2
}

public sealed record WorkflowStartResult(Guid? InstanceId, WorkflowOutcome Outcome);

public enum ApprovalDecision { Approve = 0, Reject = 1, Return = 2 }

public sealed record WorkflowCompletion(Guid InstanceId, Guid CompanyId, string DocumentType, Guid DocumentId,
    WorkflowInstanceStatus Status, Guid ActorUserId, string? Comment);

/// <summary>
/// Implemented once per document type. Called inside the same unit of work as the final approval action, so the
/// document's status change, and any NAV outbox entry it adds, commit atomically with the approval.
/// Handlers must not call SaveChanges.
/// </summary>
public interface IWorkflowDocumentHandler
{
    /// <summary>The document type, or a prefix such as "HrRequest" that matches "HrRequest:BANK".</summary>
    string DocumentType { get; }
    Task OnCompletedAsync(WorkflowCompletion completion, CancellationToken ct);

    /// <summary>Label/value pairs an approver needs to decide (e.g. dates and balance for leave). Only called for users assigned to the request or allowed to view it.</summary>
    Task<IReadOnlyList<DocumentDetail>> DescribeAsync(Guid documentId, CancellationToken ct);
}

public sealed record DocumentDetail(string Label, string? Value);

/// <summary>
/// Portal approval engine (spec §5): multi-level and parallel steps, approval limits by amount, delegation,
/// deadlines with reminders and escalation, and an append-only history. It never lets a requester approve their own request.
/// Start, act and cancel add changes to the caller's unit of work and save them.
/// </summary>
public interface IWorkflowEngine
{
    Task<WorkflowStartResult> StartAsync(WorkflowStartRequest request, CancellationToken ct);
    Task ActAsync(Guid taskId, Guid actorUserId, ApprovalDecision decision, string? comment, CancellationToken ct);
    Task CancelAsync(Guid instanceId, Guid actorUserId, string? reason, CancellationToken ct);
    Task<int> ProcessDeadlinesAsync(CancellationToken ct);
}

/// <summary>A business-rule failure the user can act on (shown as a 400/409 with the message).</summary>
public sealed class WorkflowException : InvalidOperationException
{
    public WorkflowException(string message) : base(message) { }
}
