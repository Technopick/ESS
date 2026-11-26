using Ess.Application.Abstractions;
using Ess.Application.Integration;
using Ess.Domain.Common;
using Ess.Domain.Integration;
using Ess.Infrastructure.Persistence;
using Ess.Integration.Nav.Configuration;
using Ess.Integration.Nav.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ess.Integration.Nav.Queue;

/// <summary>
/// Dispatches due outbox items to NAV (Retry and Error Handling Engine, spec §3 component 9).
/// Safe to run on several nodes: items are claimed with FOR UPDATE SKIP LOCKED and a lease.
/// Outcome rules:
///  • NAV confirmed            → Succeeded, entity NavStatus = Posted (only now, rule 11)
///  • Transient / auth / config → back to Pending with exponential backoff; DeadLetter after MaxAttempts
///  • Validation (NAV said no)  → Failed, entity NavStatus = Failed, user and HR are told why
///  • Unknown (maybe applied)   → RequiresReconciliation; never blindly resent
/// Nothing is ever deleted.
/// </summary>
public sealed class IntegrationQueueProcessor
{
    private readonly IServiceScopeFactory _scopes;
    private readonly NavEngineOptions _opt;
    private readonly IClock _clock;
    private readonly ILogger<IntegrationQueueProcessor> _log;
    private readonly string _worker = $"{Environment.MachineName}:{Environment.ProcessId}";

    public IntegrationQueueProcessor(IServiceScopeFactory scopes, IOptions<NavEngineOptions> opt, IClock clock, ILogger<IntegrationQueueProcessor> log)
    {
        _scopes = scopes;
        _opt = opt.Value;
        _clock = clock;
        _log = log;
    }

    /// <summary>Processes one batch. Returns the number of items handled.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await RecoverExpiredLeasesAsync(ct);
        var claimed = await ClaimAsync(ct);
        foreach (var id in claimed)
        {
            if (ct.IsCancellationRequested) break;
            await ProcessOneAsync(id, lookupFirst: false, ct);
        }
        return claimed.Count;
    }

    /// <summary>Processes a single item now (admin "Retry" button). Looks NAV up first if the last outcome was uncertain.</summary>
    public async Task<bool> RetryNowAsync(Guid itemId, string requestedBy, CancellationToken ct)
    {
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
            var now = _clock.UtcNow;
            var leaseUntil = now.AddSeconds(_opt.QueueLockSeconds);
            var n = await db.IntegrationQueue
                .Where(q => q.Id == itemId && q.Status != QueueItemStatus.Succeeded && q.Status != QueueItemStatus.Cancelled
                            && (q.Status != QueueItemStatus.Processing || q.LockedUntilUtc < now))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.Status, QueueItemStatus.Processing)
                    .SetProperty(q => q.LockedBy, _worker + ":manual:" + requestedBy)
                    .SetProperty(q => q.LockedUntilUtc, leaseUntil)
                    .SetProperty(q => q.Attempts, q => q.Attempts + 1), ct);
            if (n == 0) return false;
        }
        await ProcessOneAsync(itemId, lookupFirst: true, ct);
        return true;
    }

    private async Task<List<Guid>> ClaimAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        var now = _clock.UtcNow;
        var lease = now.AddSeconds(_opt.QueueLockSeconds);
        return await db.Database.SqlQuery<Guid>($"""
            UPDATE integration_queue q
               SET status = {(int)QueueItemStatus.Processing}, locked_by = {_worker}, locked_until_utc = {lease}, attempts = q.attempts + 1
             WHERE q.id IN (
                   SELECT id FROM integration_queue
                    WHERE status = {(int)QueueItemStatus.Pending} AND next_attempt_at_utc <= {now}
                    ORDER BY priority DESC, next_attempt_at_utc
                    LIMIT {_opt.QueueBatchSize}
                    FOR UPDATE SKIP LOCKED)
            RETURNING q.id AS "Value"
            """).ToListAsync(ct);
    }

    /// <summary>A worker died mid-call: we cannot know whether NAV applied it, so reconcile instead of resending.</summary>
    private async Task RecoverExpiredLeasesAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        var now = _clock.UtcNow;
        var n = await db.IntegrationQueue
            .Where(q => q.Status == QueueItemStatus.Processing && q.LockedUntilUtc < now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(q => q.Status, QueueItemStatus.RequiresReconciliation)
                .SetProperty(q => q.LastErrorCategory, nameof(NavErrorCategory.Unknown))
                .SetProperty(q => q.LastError, "Worker lease expired during the NAV call; outcome unknown."), ct);
        if (n > 0) _log.LogWarning("{Count} integration item(s) moved to reconciliation after lease expiry", n);
    }

    private async Task ProcessOneAsync(Guid id, bool lookupFirst, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<EssDbContext>();
        var protector = sp.GetRequiredService<ISecretProtector>();
        var item = await db.IntegrationQueue.FirstAsync(q => q.Id == id, ct);
        var handler = sp.GetServices<INavOperationHandler>().FirstOrDefault(h => h.Operation == item.Operation);

        using var _ = NavCallScope.Begin(queueItemId: item.Id, correlationId: item.CorrelationId);
        if (handler is null)
        {
            await CompleteAsync(db, item, null, QueueItemStatus.DeadLetter, NavSyncStatus.Failed,
                new NavError(NavErrorCategory.Configuration, $"No handler registered for operation '{item.Operation}'."), ct);
            return;
        }

        try
        {
            if (lookupFirst || item.LastErrorCategory == nameof(NavErrorCategory.Unknown))
            {
                var found = await handler.LookupAsync(item, ct);
                if (found.Success && found.Value is not null)
                {
                    await CompleteAsync(db, item, handler, QueueItemStatus.Succeeded, NavSyncStatus.Posted, null, ct, found.Value);
                    return;
                }
                if (!found.Success && found.Error!.Category != NavErrorCategory.NotFound)
                {
                    await ScheduleRetryAsync(db, item, handler, found.Error, ct);
                    return;
                }
            }

            var payload = item.PayloadEncrypted ? protector.Unprotect(item.PayloadJson) : item.PayloadJson;
            await handler.ApplyOutcomeAsync(db, item, NavSyncStatus.InProgress, null, ct);
            await db.SaveChangesAsync(ct);

            var result = await handler.ExecuteAsync(item, payload, ct);
            if (result.Success)
            {
                await CompleteAsync(db, item, handler, QueueItemStatus.Succeeded, NavSyncStatus.Posted, null, ct, result.Value);
                return;
            }

            switch (result.Error!.Category)
            {
                case NavErrorCategory.Validation:
                case NavErrorCategory.NotFound:
                    await CompleteAsync(db, item, handler, QueueItemStatus.Failed, NavSyncStatus.Failed, result.Error, ct);
                    break;
                case NavErrorCategory.Unknown:
                    await CompleteAsync(db, item, handler, QueueItemStatus.RequiresReconciliation, NavSyncStatus.RequiresReconciliation, result.Error, ct);
                    break;
                default:
                    await ScheduleRetryAsync(db, item, handler, result.Error, ct);
                    break;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogError(ex, "Unhandled error processing integration item {Id} ({Operation})", item.Id, item.Operation);
            db.ChangeTracker.Clear();
            item = await db.IntegrationQueue.FirstAsync(q => q.Id == id, ct);
            await ScheduleRetryAsync(db, item, handler, new NavError(NavErrorCategory.Unknown, ex.Message), ct);
        }
    }

    private async Task ScheduleRetryAsync(EssDbContext db, IntegrationQueueItem item, INavOperationHandler? handler, NavError error, CancellationToken ct)
    {
        if (item.Attempts >= item.MaxAttempts)
        {
            await CompleteAsync(db, item, handler, QueueItemStatus.DeadLetter, NavSyncStatus.Failed, error, ct);
            return;
        }
        var delay = RetrySchedule.NextDelay(item.Attempts, TimeSpan.FromSeconds(_opt.RetryBaseDelaySeconds), TimeSpan.FromMinutes(_opt.RetryMaxDelayMinutes));
        if (error.Category is NavErrorCategory.Authentication or NavErrorCategory.Configuration)
            delay = TimeSpan.FromMinutes(_opt.RetryMaxDelayMinutes); // needs an admin; don't hammer NAV
        item.Status = error.Category == NavErrorCategory.Unknown ? QueueItemStatus.RequiresReconciliation : QueueItemStatus.Pending;
        item.NextAttemptAtUtc = _clock.UtcNow.Add(delay);
        item.LockedBy = null;
        item.LockedUntilUtc = null;
        item.LastError = error.Message;
        item.LastErrorCategory = error.Category.ToString();
        if (handler is not null)
            await handler.ApplyOutcomeAsync(db, item, error.Category == NavErrorCategory.Unknown ? NavSyncStatus.RequiresReconciliation : NavSyncStatus.PendingIntegration, null, ct);
        await db.SaveChangesAsync(ct);
        _log.LogWarning("Integration item {Id} ({Operation}) attempt {Attempt} failed ({Category}); next try at {Next:u}",
            item.Id, item.Operation, item.Attempts, error.Category, item.NextAttemptAtUtc);
    }

    private async Task CompleteAsync(EssDbContext db, IntegrationQueueItem item, INavOperationHandler? handler, QueueItemStatus status,
        NavSyncStatus entityStatus, NavError? error, CancellationToken ct, NavSubmissionReceipt? receipt = null)
    {
        item.Status = status;
        item.LockedBy = null;
        item.LockedUntilUtc = null;
        item.CompletedAtUtc = status is QueueItemStatus.Succeeded or QueueItemStatus.Failed or QueueItemStatus.DeadLetter ? _clock.UtcNow : null;
        item.NavDocumentNo = receipt?.NavDocumentNo ?? item.NavDocumentNo;
        item.LastError = error?.Message;
        item.LastErrorCategory = error?.Category.ToString();
        if (handler is not null) await handler.ApplyOutcomeAsync(db, item, entityStatus, receipt, ct);
        await db.SaveChangesAsync(ct);

        if (status == QueueItemStatus.Succeeded)
            _log.LogInformation("Integration item {Id} ({Operation}) confirmed by NAV as {DocNo}", item.Id, item.Operation, item.NavDocumentNo);
        else
            _log.LogWarning("Integration item {Id} ({Operation}) ended as {Status}: {Error}", item.Id, item.Operation, status, error?.Message);
    }
}
