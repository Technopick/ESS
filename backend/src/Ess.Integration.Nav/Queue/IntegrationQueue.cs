using System.Text.Json;
using Ess.Application.Abstractions;
using Ess.Application.Integration;
using Ess.Domain.Integration;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Integration.Nav.Queue;

/// <summary>
/// Integration Queue (spec §3, component 8): a transactional outbox. Enqueue adds the row to the caller's DbContext,
/// so it commits atomically with the business change (e.g. the leave application moving to Approved).
/// A unique index on (company, operation, idempotency key) makes enqueueing idempotent.
/// </summary>
public sealed class IntegrationQueue : IIntegrationQueue
{
    private readonly EssDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IClock _clock;
    private readonly ICurrentUser _user;

    public IntegrationQueue(EssDbContext db, ISecretProtector protector, IClock clock, ICurrentUser user)
    {
        _db = db;
        _protector = protector;
        _clock = clock;
        _user = user;
    }

    public async Task<Guid> EnqueueAsync(Guid companyId, string operation, string entityType, Guid entityId, string idempotencyKey,
        object payload, bool sensitive, int priority = 0, CancellationToken ct = default)
    {
        var existing = await _db.IntegrationQueue.AsNoTracking()
            .FirstOrDefaultAsync(q => q.CompanyId == companyId && q.Operation == operation && q.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null) return existing.Id;

        var json = JsonSerializer.Serialize(payload);
        var item = new IntegrationQueueItem
        {
            CompanyId = companyId,
            Operation = operation,
            EntityType = entityType,
            EntityId = entityId,
            IdempotencyKey = idempotencyKey,
            PayloadJson = sensitive ? _protector.Protect(json) : json,
            PayloadEncrypted = sensitive,
            Status = QueueItemStatus.Pending,
            Priority = priority,
            CreatedAtUtc = _clock.UtcNow,
            NextAttemptAtUtc = _clock.UtcNow,
            CorrelationId = _user.CorrelationId,
            CreatedBy = _user.UserName
        };
        _db.IntegrationQueue.Add(item);
        return item.Id; // committed by the caller's SaveChanges
    }

    /// <summary>Deterministic idempotency key: the same business action always yields the same key.</summary>
    /// <remarks>Kept within 50 characters so it fits a NAV Code[50]/Text[50] "External Reference" field.</remarks>
    public static string KeyFor(string operation, Guid entityId, int revision = 0)
    {
        var op = new string(operation.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        if (op.Length > 10) op = op[..10];
        return $"ESS-{op}-{entityId:N}-{revision}"; // max 4 + 10 + 1 + 32 + 1 + 2 = 50
    }
}
