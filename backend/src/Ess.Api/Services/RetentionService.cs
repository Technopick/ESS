using Ess.Application.Abstractions;
using Ess.Application.Documents;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Services;

/// <summary>Retention periods (Kenya Data Protection Act s.39: keep personal data no longer than the purpose needs). See docs/retention-schedule.md.</summary>
public sealed class RetentionOptions
{
    public bool Enabled { get; set; } = true;
    public int AuditLogYears { get; set; } = 7;
    public int DocumentAccessLogYears { get; set; } = 7;
    public int NotificationDays { get; set; } = 365;
    public int NavSyncLogDays { get; set; } = 180;
    public int ExpiredTokenDays { get; set; } = 30;
    public int DeletedDocumentFileDays { get; set; } = 30;
    public int PayslipSnapshotDays { get; set; } = 90;
    public int BatchSize { get; set; } = 500;
}

public sealed record RetentionResult(int DocumentsExpired, int FilesPurged, int PayslipSnapshotsCleared, int Notifications, int Tokens,
    int NavSyncLogs, int DocumentAccessLogs, int AuditLogs);

/// <summary>
/// Nightly retention job. Removes what the portal no longer needs: documents past their category's retention period,
/// the files of deleted documents, cached payslip snapshots (NAV keeps the payroll record), old notifications, spent
/// sign-in tokens, NAV call logs and, after the statutory period, audit trails. NAV data is never touched.
/// </summary>
public sealed class RetentionService
{
    private readonly EssDbContext _db;
    private readonly IFileStorage _files;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;
    private readonly RetentionOptions _o;
    private readonly ILogger<RetentionService> _log;

    public RetentionService(EssDbContext db, IFileStorage files, IAuditWriter audit, IClock clock, IConfiguration config, ILogger<RetentionService> log)
    {
        _db = db;
        _files = files;
        _audit = audit;
        _clock = clock;
        _log = log;
        _o = config.GetSection("Retention").Get<RetentionOptions>() ?? new RetentionOptions();
    }

    public async Task<RetentionResult> RunAsync(CancellationToken ct)
    {
        if (!_o.Enabled) return new RetentionResult(0, 0, 0, 0, 0, 0, 0, 0);
        var now = _clock.UtcNow;

        var expired = await ExpireDocumentsAsync(now, ct);
        var files = await PurgeDeletedFilesAsync(now.AddDays(-_o.DeletedDocumentFileDays), ct);

        var snapshots = await _db.PayslipMetadata.IgnoreQueryFilters()
            .Where(m => m.SnapshotEncrypted != null && m.RetrievedAtUtc < now.AddDays(-_o.PayslipSnapshotDays))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.SnapshotEncrypted, (string?)null), ct);

        var notifications = await _db.Notifications.IgnoreQueryFilters().Where(n => n.CreatedAtUtc < now.AddDays(-_o.NotificationDays)).ExecuteDeleteAsync(ct);
        var tokenCutoff = now.AddDays(-_o.ExpiredTokenDays);
        var tokens = await _db.RefreshTokens.Where(t => t.ExpiresAtUtc < tokenCutoff || (t.RevokedAtUtc != null && t.RevokedAtUtc < tokenCutoff)).ExecuteDeleteAsync(ct)
                     + await _db.PasswordResetTokens.Where(t => t.ExpiresAtUtc < tokenCutoff).ExecuteDeleteAsync(ct);
        var navLogs = await _db.NavSyncLogs.Where(l => l.OccurredAtUtc < now.AddDays(-_o.NavSyncLogDays)).ExecuteDeleteAsync(ct);
        var accessLogs = await _db.DocumentAccessLogs.Where(l => l.AccessedAtUtc < now.AddYears(-_o.DocumentAccessLogYears)).ExecuteDeleteAsync(ct);
        var audit = await _db.AuditLogs.Where(l => l.OccurredAtUtc < now.AddYears(-_o.AuditLogYears)).ExecuteDeleteAsync(ct);

        var result = new RetentionResult(expired, files, snapshots, notifications, tokens, navLogs, accessLogs, audit);
        await _audit.WriteAsync("retention.run", "Retention", null, result, ct: ct);
        _log.LogInformation("Retention run: {@Result}", result);
        return result;
    }

    /// <summary>Soft-deletes documents whose category has a retention period that has passed since the latest version was uploaded.</summary>
    private async Task<int> ExpireDocumentsAsync(DateTime now, CancellationToken ct)
    {
        var due = await (from d in _db.EmployeeDocuments
                         join c in _db.DocumentCategories on d.CategoryId equals c.Id
                         where c.RetentionMonths != null
                         let latest = _db.DocumentVersions.Where(v => v.DocumentId == d.Id).Max(v => (DateTime?)v.UploadedAtUtc)
                         where latest != null && latest < now.AddMonths(-c.RetentionMonths!.Value)
                         select new { d.Id, d.CategoryId }).Take(_o.BatchSize).ToListAsync(ct);
        if (due.Count == 0) return 0;
        // A set-based soft delete: marking tracked entities Deleted would cascade to their version rows and lose the
        // file keys before the grace period ends.
        var ids = due.Select(d => d.Id).ToList();
        await _db.EmployeeDocuments.Where(d => ids.Contains(d.Id)).ExecuteUpdateAsync(s => s
            .SetProperty(d => d.IsDeleted, true).SetProperty(d => d.DeletedAtUtc, now).SetProperty(d => d.DeletedBy, "retention"), ct);
        foreach (var d in due)
            await _audit.WriteAsync("document.retention-expired", "EmployeeDocument", d.Id.ToString(), new { d.CategoryId }, ct: ct);
        return due.Count;
    }

    /// <summary>Deletes stored files (and their version rows) of documents deleted before the cutoff. The tombstone row stays for the audit trail.</summary>
    private async Task<int> PurgeDeletedFilesAsync(DateTime cutoff, CancellationToken ct)
    {
        var versions = await (from v in _db.DocumentVersions
                              join d in _db.EmployeeDocuments.IgnoreQueryFilters() on v.DocumentId equals d.Id
                              where d.IsDeleted && d.DeletedAtUtc != null && d.DeletedAtUtc < cutoff
                              select v).Take(_o.BatchSize).ToListAsync(ct);
        var purged = 0;
        foreach (var v in versions)
        {
            try
            {
                await _files.DeleteAsync(v.StorageKey, ct);
                _db.DocumentVersions.Remove(v);
                purged++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Could not purge file for document {DocumentId} version {Version}; will retry next run", v.DocumentId, v.Version);
            }
        }
        await _db.SaveChangesAsync(ct);

        var pdfs = await _db.PayslipMetadata.IgnoreQueryFilters().Where(m => m.PdfStorageKey != null && m.RetrievedAtUtc < _clock.UtcNow.AddDays(-_o.PayslipSnapshotDays))
            .Take(_o.BatchSize).ToListAsync(ct);
        foreach (var m in pdfs)
        {
            try
            {
                await _files.DeleteAsync(m.PdfStorageKey!, ct);
                m.PdfStorageKey = null;
                m.PdfSha256 = null;
                purged++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Could not purge cached payslip PDF {Id}; will retry next run", m.Id);
            }
        }
        await _db.SaveChangesAsync(ct);
        return purged;
    }
}
