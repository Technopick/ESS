using Ess.Domain.Integration;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Integration.Nav.Monitoring;

public sealed record IntegrationInstanceStatus(Guid SettingsId, string Name, string Platform, string? Server, bool Enabled, bool Simulator,
    DateTime? LastHealthCheckAtUtc, bool? LastHealthCheckOk, string? LastHealthCheckMessage, IReadOnlyList<string> Companies);

public sealed record IntegrationDashboard(
    IReadOnlyList<IntegrationInstanceStatus> Instances,
    DateTime? LastSuccessfulSyncUtc,
    DateTime? LastFailedSyncUtc,
    int Pending, int Processing, int Succeeded24h, int Failed, int DeadLetter, int RequiresReconciliation,
    double? AverageResponseMs24h, double? Availability24h,
    IReadOnlyList<RecentError> RecentErrors);

public sealed record RecentError(DateTime OccurredAtUtc, string Operation, string? Error, int? StatusCode);

/// <summary>Data for the NAV Integration Administration Dashboard (spec §11).</summary>
public sealed class IntegrationDashboardService
{
    private readonly EssDbContext _db;

    public IntegrationDashboardService(EssDbContext db) => _db = db;

    public async Task<IntegrationDashboard> GetAsync(CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        var companies = await _db.Companies.AsNoTracking().Where(c => c.IntegrationSettingsId != null).ToListAsync(ct);
        var instances = (await _db.NavIntegrationSettings.AsNoTracking().ToListAsync(ct))
            .Select(s => new IntegrationInstanceStatus(s.Id, s.Name, s.Platform.ToString(), s.ServerHost, s.IsEnabled, s.UseSimulator,
                s.LastHealthCheckAtUtc, s.LastHealthCheckOk, s.LastHealthCheckMessage,
                companies.Where(c => c.IntegrationSettingsId == s.Id).Select(c => c.DisplayName).ToList()))
            .ToList();

        var counts = await _db.IntegrationQueue.AsNoTracking().GroupBy(q => q.Status)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var succeeded24h = await _db.IntegrationQueue.CountAsync(q => q.Status == QueueItemStatus.Succeeded && q.CompletedAtUtc >= since, ct);

        var calls = _db.NavSyncLogs.AsNoTracking().Where(l => l.OccurredAtUtc >= since);
        var total = await calls.CountAsync(ct);
        double? avg = total == 0 ? null : await calls.AverageAsync(l => (double)l.DurationMs, ct);
        double? availability = total == 0 ? null : (double)await calls.CountAsync(l => l.Success, ct) / total;

        var lastOk = await _db.NavSyncJobs.Where(j => j.Status == SyncJobStatus.Succeeded).MaxAsync(j => (DateTime?)j.FinishedAtUtc, ct);
        var lastFail = await _db.NavSyncJobs.Where(j => j.Status == SyncJobStatus.Failed).MaxAsync(j => (DateTime?)j.FinishedAtUtc, ct);
        var errors = await _db.NavSyncLogs.AsNoTracking().Where(l => !l.Success).OrderByDescending(l => l.OccurredAtUtc).Take(10)
            .Select(l => new RecentError(l.OccurredAtUtc, l.Operation, l.Error, l.StatusCode)).ToListAsync(ct);

        int C(QueueItemStatus s) => counts.GetValueOrDefault(s);
        return new IntegrationDashboard(instances, lastOk, lastFail, C(QueueItemStatus.Pending), C(QueueItemStatus.Processing), succeeded24h,
            C(QueueItemStatus.Failed), C(QueueItemStatus.DeadLetter), C(QueueItemStatus.RequiresReconciliation), avg, availability, errors);
    }
}
