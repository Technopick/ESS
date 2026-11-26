using System.Text.Json;
using Ess.Application.Integration;
using Ess.Application.Notifications;
using Ess.Domain.Common;
using Ess.Domain.Integration;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Integration.Nav.Queue;

/// <summary>One handler per outbox operation: how to send it to NAV, how to look it up after an uncertain outcome,
/// and how to update the portal record once NAV has answered.</summary>
public interface INavOperationHandler
{
    string Operation { get; }
    Task<NavResult<NavSubmissionReceipt>> ExecuteAsync(IntegrationQueueItem item, string payloadJson, CancellationToken ct);
    Task<NavResult<NavSubmissionReceipt?>> LookupAsync(IntegrationQueueItem item, CancellationToken ct);
    Task ApplyOutcomeAsync(EssDbContext db, IntegrationQueueItem item, NavSyncStatus status, NavSubmissionReceipt? receipt, CancellationToken ct);
}

public sealed class LeaveSubmitHandler : INavOperationHandler
{
    private readonly INavLeaveService _nav;
    private readonly INotificationService _notify;
    public LeaveSubmitHandler(INavLeaveService nav, INotificationService notify)
    {
        _nav = nav;
        _notify = notify;
    }
    public string Operation => NavOperations.LeaveSubmit;

    public Task<NavResult<NavSubmissionReceipt>> ExecuteAsync(IntegrationQueueItem item, string payloadJson, CancellationToken ct) =>
        _nav.SubmitAsync(item.CompanyId, JsonSerializer.Deserialize<NavLeaveSubmission>(payloadJson)!, ct);

    public Task<NavResult<NavSubmissionReceipt?>> LookupAsync(IntegrationQueueItem item, CancellationToken ct) =>
        _nav.FindByExternalReferenceAsync(item.CompanyId, item.IdempotencyKey, ct);

    public async Task ApplyOutcomeAsync(EssDbContext db, IntegrationQueueItem item, NavSyncStatus status, NavSubmissionReceipt? receipt, CancellationToken ct)
    {
        var leave = await db.LeaveApplications.FirstOrDefaultAsync(x => x.Id == item.EntityId, ct);
        if (leave is null) return;
        leave.NavStatus = status;
        if (receipt is not null) leave.NavDocumentNo = receipt.NavDocumentNo;
        if (status is not (NavSyncStatus.Posted or NavSyncStatus.Failed)) return;

        var user = await db.EmployeeMappings.Where(m => m.EmployeeId == leave.EmployeeId).Select(m => (Guid?)m.UserId).FirstOrDefaultAsync(ct);
        if (user is null) return;
        await _notify.QueueAsync(status == NavSyncStatus.Posted
            ? new NotificationRequest(leave.CompanyId, user.Value, NotificationEvents.NavPosted, $"{leave.Reference} is recorded in NAV",
                $"Your leave {leave.Reference} is now recorded in NAV as {receipt?.NavDocumentNo}.", $"/leave/{leave.Id}", Email: false)
            : new NotificationRequest(leave.CompanyId, user.Value, NotificationEvents.NavFailed, $"NAV did not accept {leave.Reference}",
                $"NAV did not accept your leave {leave.Reference}: {item.LastError}. HR has been alerted; you can cancel it and apply again.", $"/leave/{leave.Id}"), ct);
    }
}

public sealed class LeaveCancelHandler : INavOperationHandler
{
    private readonly INavLeaveService _nav;
    public LeaveCancelHandler(INavLeaveService nav) => _nav = nav;
    public string Operation => NavOperations.LeaveCancel;

    private sealed record Payload(string NavDocumentNo, string Reason);

    public Task<NavResult<NavSubmissionReceipt>> ExecuteAsync(IntegrationQueueItem item, string payloadJson, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<Payload>(payloadJson)!;
        return _nav.CancelAsync(item.CompanyId, p.NavDocumentNo, item.IdempotencyKey, p.Reason, ct);
    }

    public async Task<NavResult<NavSubmissionReceipt?>> LookupAsync(IntegrationQueueItem item, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<Payload>(item.PayloadJson);
        if (p is null) return NavResult<NavSubmissionReceipt?>.Ok(null);
        var s = await _nav.GetStatusAsync(item.CompanyId, p.NavDocumentNo, ct);
        if (!s.Success) return NavResult<NavSubmissionReceipt?>.Fail(s.Error!);
        return NavResult<NavSubmissionReceipt?>.Ok(s.Value?.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) == true
            ? new NavSubmissionReceipt(p.NavDocumentNo, "Cancelled", DateTime.UtcNow) : null);
    }

    public async Task ApplyOutcomeAsync(EssDbContext db, IntegrationQueueItem item, NavSyncStatus status, NavSubmissionReceipt? receipt, CancellationToken ct)
    {
        var leave = await db.LeaveApplications.FirstOrDefaultAsync(x => x.Id == item.EntityId, ct);
        if (leave is not null) leave.NavStatus = status;
    }
}

public sealed class ExpenseSubmitHandler : INavOperationHandler
{
    private readonly INavExpenseService _nav;
    public ExpenseSubmitHandler(INavExpenseService nav) => _nav = nav;
    public string Operation => NavOperations.ExpenseSubmit;

    public Task<NavResult<NavSubmissionReceipt>> ExecuteAsync(IntegrationQueueItem item, string payloadJson, CancellationToken ct) =>
        _nav.SubmitClaimAsync(item.CompanyId, JsonSerializer.Deserialize<NavExpenseClaimSubmission>(payloadJson)!, ct);

    public Task<NavResult<NavSubmissionReceipt?>> LookupAsync(IntegrationQueueItem item, CancellationToken ct) =>
        _nav.FindByExternalReferenceAsync(item.CompanyId, item.IdempotencyKey, ct);

    public async Task ApplyOutcomeAsync(EssDbContext db, IntegrationQueueItem item, NavSyncStatus status, NavSubmissionReceipt? receipt, CancellationToken ct)
    {
        var claim = await db.ExpenseClaims.FirstOrDefaultAsync(x => x.Id == item.EntityId, ct);
        if (claim is null) return;
        claim.NavStatus = status;
        if (receipt is not null) claim.NavDocumentNo = receipt.NavDocumentNo;
    }
}

public sealed class EmployeeChangeHandler : INavOperationHandler
{
    private readonly INavEmployeeService _nav;
    public EmployeeChangeHandler(INavEmployeeService nav) => _nav = nav;
    public string Operation => NavOperations.EmployeeChange;

    public Task<NavResult<NavSubmissionReceipt>> ExecuteAsync(IntegrationQueueItem item, string payloadJson, CancellationToken ct) =>
        _nav.SubmitChangeAsync(item.CompanyId, JsonSerializer.Deserialize<NavEmployeeChangeSubmission>(payloadJson)!, ct);

    // Employee changes are applied idempotently by the NAV codeunit (same external ref = no-op), so a resend is safe.
    public Task<NavResult<NavSubmissionReceipt?>> LookupAsync(IntegrationQueueItem item, CancellationToken ct) =>
        Task.FromResult(NavResult<NavSubmissionReceipt?>.Ok(null));

    public async Task ApplyOutcomeAsync(EssDbContext db, IntegrationQueueItem item, NavSyncStatus status, NavSubmissionReceipt? receipt, CancellationToken ct)
    {
        var req = await db.ProfileChangeRequests.FirstOrDefaultAsync(x => x.Id == item.EntityId, ct);
        if (req is null) return;
        req.NavStatus = status;
        if (status == NavSyncStatus.Posted) req.Status = RequestStatus.Completed;
    }
}
