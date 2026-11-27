using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Application.Abstractions;
using Ess.Application.Integration;
using Ess.Application.Security;
using Ess.Domain.Integration;
using Ess.Infrastructure.Persistence;
using Ess.Integration.Nav.Monitoring;
using Ess.Integration.Nav.Queue;
using Ess.Integration.Nav.Sync;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers;

/// <summary>NAV Integration administration (spec §11, §14).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/integration/nav")]
[Route("api/integration/nav")]
public sealed class NavIntegrationController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly ICurrentUser _user;

    public NavIntegrationController(EssDbContext db, IAuditWriter audit, ICurrentUser user)
    {
        _db = db;
        _audit = audit;
        _user = user;
    }

    [HttpGet("status")]
    [HasPermission(Permissions.IntegrationView)]
    public Task<IntegrationDashboard> Status([FromServices] IntegrationDashboardService svc, CancellationToken ct) => svc.GetAsync(ct);

    public sealed record TestRequest(Guid SettingsId);

    [HttpPost("test")]
    [HasPermission(Permissions.IntegrationOperate)]
    public async Task<NavConnectionTestResult> Test(TestRequest req, [FromServices] INavConnectivityService svc, CancellationToken ct)
    {
        var r = await svc.TestAsync(req.SettingsId, ct);
        await _audit.WriteAsync("nav.test", nameof(NavIntegrationSettings), req.SettingsId.ToString(), new { r.Success, r.Message }, r.Success, ct);
        return r;
    }

    public sealed record SyncRequest(Guid CompanyId, string? Entity, bool Full = false);

    /// <summary>Queues an on-demand sync as a background job and returns immediately.</summary>
    [HttpPost("sync")]
    [HasPermission(Permissions.IntegrationOperate)]
    public async Task<IActionResult> Sync(SyncRequest req, [FromServices] IBackgroundJobClient jobs, CancellationToken ct)
    {
        if (req.Entity is not null && !NavSyncService.Entities.Contains(req.Entity))
            return ValidationProblem($"Entity must be one of: {string.Join(", ", NavSyncService.Entities)}.");
        if (!await _db.Companies.AnyAsync(c => c.Id == req.CompanyId, ct)) return NotFound();

        var who = _user.UserName ?? "unknown";
        var entity = req.Entity;
        var jobId = entity is null
            ? jobs.Enqueue<NavSyncService>(s => s.SyncCompanyAsync(req.CompanyId, req.Full, "manual:" + who, CancellationToken.None))
            : jobs.Enqueue<NavSyncService>(s => s.SyncAsync(req.CompanyId, entity, req.Full, "manual:" + who, CancellationToken.None));
        await _audit.WriteAsync("nav.sync.manual", "Company", req.CompanyId.ToString(), new { req.Entity, req.Full }, ct: ct);
        return Accepted(new { jobId });
    }

    [HttpGet("logs")]
    [HasPermission(Permissions.IntegrationView)]
    public async Task<PagedResult<NavSyncLog>> Logs([FromQuery] bool? success, [FromQuery] string? operation, [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var q = _db.NavSyncLogs.AsNoTracking();
        if (success is not null) q = q.Where(l => l.Success == success);
        if (!string.IsNullOrWhiteSpace(operation)) q = q.Where(l => l.Operation == operation);
        if (fromUtc is not null) q = q.Where(l => l.OccurredAtUtc >= fromUtc);
        if (toUtc is not null) q = q.Where(l => l.OccurredAtUtc < toUtc);
        return await PagedResult<NavSyncLog>.CreateAsync(q.OrderByDescending(l => l.OccurredAtUtc), page, pageSize, ct);
    }

    /// <summary>Exports integration call logs (already masked) for a period, up to 50,000 rows.</summary>
    [HttpGet("logs/export")]
    [HasPermission(Permissions.IntegrationView)]
    public async Task<IActionResult> ExportLogs([FromQuery] bool? success, [FromQuery] string? operation, [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
        [FromServices] Ess.Api.Reporting.ReportRunner runner, [FromQuery] Ess.Api.Reporting.ExportFormat format = Ess.Api.Reporting.ExportFormat.Csv, CancellationToken ct = default)
    {
        var start = fromUtc ?? DateTime.UtcNow.AddDays(-7);
        var q = _db.NavSyncLogs.AsNoTracking().Where(l => l.OccurredAtUtc >= start);
        if (toUtc is not null) q = q.Where(l => l.OccurredAtUtc < toUtc);
        if (success is not null) q = q.Where(l => l.Success == success);
        if (!string.IsNullOrWhiteSpace(operation)) q = q.Where(l => l.Operation == operation);
        var rows = await q.OrderByDescending(l => l.OccurredAtUtc).Take(50_000)
            .Select(l => new object?[] { l.OccurredAtUtc.AddHours(runner.Offset), l.Operation, l.Protocol, l.HttpMethod, l.Endpoint, l.StatusCode, l.Success, l.DurationMs, l.Error, l.CorrelationId })
            .ToListAsync(ct);
        var table = new Ess.Api.Reporting.ReportTable("NAV integration log", new Ess.Api.Reporting.ReportColumn[]
        {
            new("at", "Time", Ess.Api.Reporting.ReportColumnType.DateTime), new("op", "Operation"), new("protocol", "Protocol"), new("method", "Method"), new("endpoint", "Endpoint"),
            new("status", "HTTP status", Ess.Api.Reporting.ReportColumnType.Number), new("ok", "Succeeded"), new("ms", "Duration (ms)", Ess.Api.Reporting.ReportColumnType.Number),
            new("error", "Error"), new("corr", "Correlation id")
        }, rows.Select(r => { r[7] = (decimal)(long)r[7]!; if (r[5] is int i) r[5] = (decimal)i; return r; }).ToList(),
            new List<KeyValuePair<string, string>> { new("From (UTC)", start.ToString("u")) }, "Request and response bodies are not exported.");
        var (content, type, ext) = Ess.Api.Reporting.ReportExporter.Export(table, format, await runner.GeneratedByAsync(ct), runner.LocalNow);
        await _audit.WriteAsync("nav.logs.exported", null, null, new { start, toUtc, success, operation, rows = rows.Count, format = format.ToString() }, ct: ct);
        return File(content, type, $"nav-integration-log-{runner.LocalNow:yyyyMMdd-HHmm}.{ext}");
    }

    public sealed record QueueItemView(Guid Id, string Operation, string EntityType, Guid EntityId, string IdempotencyKey, QueueItemStatus Status,
        int Attempts, int MaxAttempts, DateTime CreatedAtUtc, DateTime NextAttemptAtUtc, DateTime? CompletedAtUtc, string? NavDocumentNo,
        string? LastError, string? LastErrorCategory);

    /// <summary>Queue items (failed, dead-letter, pending...). Payloads are never returned: they may hold personal data.</summary>
    [HttpGet("transactions")]
    [HasPermission(Permissions.IntegrationView)]
    public async Task<PagedResult<QueueItemView>> Transactions([FromQuery] QueueItemStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var q = _db.IntegrationQueue.AsNoTracking();
        if (status is not null) q = q.Where(x => x.Status == status);
        var projected = q.OrderByDescending(x => x.CreatedAtUtc).Select(x => new QueueItemView(x.Id, x.Operation, x.EntityType, x.EntityId,
            x.IdempotencyKey, x.Status, x.Attempts, x.MaxAttempts, x.CreatedAtUtc, x.NextAttemptAtUtc, x.CompletedAtUtc, x.NavDocumentNo,
            x.LastError, x.LastErrorCategory));
        return await PagedResult<QueueItemView>.CreateAsync(projected, page, pageSize, ct);
    }

    /// <summary>Retries a failed transaction. NAV is checked by idempotency key first, so a retry can never create a duplicate.</summary>
    [HttpPost("retry/{id:guid}")]
    [HasPermission(Permissions.IntegrationOperate)]
    public async Task<IActionResult> Retry(Guid id, [FromServices] IntegrationQueueProcessor processor, CancellationToken ct)
    {
        var item = await _db.IntegrationQueue.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id, ct);
        if (item is null) return NotFound();
        if (item.Status is QueueItemStatus.Succeeded or QueueItemStatus.Cancelled)
            return Conflict(new { message = $"Transaction is already {item.Status}." });

        // Give the item a fresh attempt budget for this manual retry.
        await _db.IntegrationQueue.Where(q => q.Id == id).ExecuteUpdateAsync(s => s.SetProperty(q => q.MaxAttempts, q => q.Attempts + 3), ct);
        var started = await processor.RetryNowAsync(id, _user.UserName ?? "unknown", ct);
        await _audit.WriteAsync("nav.retry", nameof(IntegrationQueueItem), id.ToString(), new { previousStatus = item.Status.ToString() }, started, ct);
        var after = await _db.IntegrationQueue.AsNoTracking().Where(q => q.Id == id)
            .Select(q => new { q.Status, q.NavDocumentNo, q.LastError }).FirstAsync(ct);
        return Ok(after);
    }
}
