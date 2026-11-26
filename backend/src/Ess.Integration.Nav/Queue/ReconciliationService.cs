using Ess.Application.Abstractions;
using Ess.Domain.Integration;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ess.Integration.Nav.Queue;

/// <summary>
/// Transaction Reconciliation Engine (spec §3, component 11). For items whose outcome is unknown it asks NAV
/// (by the idempotency key) whether the document exists: if yes it is marked Succeeded with NAV's number;
/// if NAV confirms it does not exist the item is safely re-queued. This is what makes retries duplicate-free.
/// </summary>
public sealed class ReconciliationService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IntegrationQueueProcessor _processor;
    private readonly IClock _clock;
    private readonly ILogger<ReconciliationService> _log;

    public ReconciliationService(IServiceScopeFactory scopes, IntegrationQueueProcessor processor, IClock clock, ILogger<ReconciliationService> log)
    {
        _scopes = scopes;
        _processor = processor;
        _clock = clock;
        _log = log;
    }

    public async Task<int> RunAsync(CancellationToken ct)
    {
        List<Guid> ids;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
            var now = _clock.UtcNow;
            ids = await db.IntegrationQueue.AsNoTracking()
                .Where(q => q.Status == QueueItemStatus.RequiresReconciliation && q.NextAttemptAtUtc <= now)
                .OrderBy(q => q.CreatedAtUtc).Select(q => q.Id).Take(50).ToListAsync(ct);
        }
        foreach (var id in ids)
        {
            // RetryNowAsync performs the NAV lookup first and only resends when NAV confirms nothing was created.
            await _processor.RetryNowAsync(id, "reconciliation", ct);
        }
        if (ids.Count > 0) _log.LogInformation("Reconciled {Count} integration item(s)", ids.Count);
        return ids.Count;
    }
}
