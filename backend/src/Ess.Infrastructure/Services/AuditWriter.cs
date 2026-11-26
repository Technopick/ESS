using System.Text.Json;
using Ess.Application.Abstractions;
using Ess.Domain.Audit;
using Ess.Infrastructure.Persistence;

namespace Ess.Infrastructure.Services;

public sealed class AuditWriter : IAuditWriter
{
    private readonly EssDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public AuditWriter(EssDbContext db, ICurrentUser user, IClock clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public async Task WriteAsync(string action, string? entityType = null, string? entityId = null, object? changes = null,
        bool success = true, CancellationToken ct = default)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            OccurredAtUtc = _clock.UtcNow,
            UserId = _user.UserId,
            UserName = _user.UserName,
            CompanyId = _user.CompanyId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ChangesJson = changes is null ? null : JsonSerializer.Serialize(changes),
            Success = success,
            IpAddress = _user.IpAddress,
            CorrelationId = _user.CorrelationId
        });
        await _db.SaveChangesAsync(ct);
    }
}
