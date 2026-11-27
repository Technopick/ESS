using Ess.Api.Services;
using Ess.Application.Documents;
using Ess.Domain.Audit;
using Ess.Domain.Documents;
using Ess.Domain.Integration;
using Ess.Domain.Notifications;
using Ess.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ess.Api.Tests;

/// <summary>Phase 6: the retention job removes what the schedule says, keeps everything newer, and never loses the audit tombstone.</summary>
[Collection("api")]
public sealed class RetentionTests : ApiTestBase
{
    public RetentionTests(EssApiFactory f) : base(f) { }

    [SkippableFact]
    public async Task Retention_job_removes_expired_records_and_keeps_current_ones()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        await UserAsync(Baraka);
        var company = await SimCompanyAsync();
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        var files = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        await scope.ServiceProvider.GetRequiredService<DocumentService>().EnsureCategoriesAsync(company, default);
        var employee = await db.Employees.FirstAsync(e => e.CompanyId == company && e.WorkEmail == Baraka);
        var user = await db.Users.FirstAsync(u => u.NormalizedEmail == Baraka.ToUpperInvariant());
        var medical = await db.DocumentCategories.FirstAsync(c => c.CompanyId == company && c.Code == "MEDICAL"); // 84 months
        var contract = await db.DocumentCategories.FirstAsync(c => c.CompanyId == company && c.Code == "CONTRACT"); // kept indefinitely

        async Task<(EmployeeDocument Doc, string Key)> AddDocAsync(DocumentCategory cat, DateTime uploaded)
        {
            var key = await files.SaveAsync(new byte[] { 1, 2, 3 }, default);
            var d = new EmployeeDocument { CompanyId = company, EmployeeId = employee.Id, CategoryId = cat.Id, Title = "Retention " + cat.Code, Visibility = DocumentVisibility.EmployeeOnly };
            d.Versions.Add(new DocumentVersion { Version = 1, FileName = "a.pdf", ContentType = "application/pdf", SizeBytes = 3, StorageKey = key, Sha256 = "x", UploadedAtUtc = uploaded, UploadedBy = "test" });
            db.EmployeeDocuments.Add(d);
            await db.SaveChangesAsync();
            return (d, key);
        }
        var oldMedical = await AddDocAsync(medical, DateTime.UtcNow.AddMonths(-90));
        var newMedical = await AddDocAsync(medical, DateTime.UtcNow.AddMonths(-2));
        var oldContract = await AddDocAsync(contract, DateTime.UtcNow.AddYears(-12));

        db.Notifications.AddRange(
            new Notification { CompanyId = company, RecipientUserId = user.Id, EventCode = "retention.old", Body = "old", Channel = NotificationChannel.InApp },
            new Notification { CompanyId = company, RecipientUserId = user.Id, EventCode = "retention.new", Body = "new", Channel = NotificationChannel.InApp });
        db.AuditLogs.AddRange(
            new AuditLog { Action = "retention.test.old", OccurredAtUtc = DateTime.UtcNow.AddYears(-8), CompanyId = company, Success = true },
            new AuditLog { Action = "retention.test.new", OccurredAtUtc = DateTime.UtcNow.AddYears(-6), CompanyId = company, Success = true });
        db.NavSyncLogs.AddRange(
            new NavSyncLog { Protocol = "OData", Operation = "retention.old", HttpMethod = "GET", Endpoint = "x", OccurredAtUtc = DateTime.UtcNow.AddDays(-200) },
            new NavSyncLog { Protocol = "OData", Operation = "retention.new", HttpMethod = "GET", Endpoint = "x", OccurredAtUtc = DateTime.UtcNow.AddDays(-10) });
        await db.SaveChangesAsync();
        await db.Notifications.Where(n => n.EventCode == "retention.old").ExecuteUpdateAsync(s => s.SetProperty(n => n.CreatedAtUtc, DateTime.UtcNow.AddDays(-400)));

        // The job runs in its own scope in production (Hangfire), so it does here too.
        async Task<RetentionResult> RunAsync()
        {
            using var jobScope = _f.Services.CreateScope();
            return await jobScope.ServiceProvider.GetRequiredService<RetentionService>().RunAsync(default);
        }
        var first = await RunAsync();
        first.DocumentsExpired.Should().BeGreaterOrEqualTo(1);
        db.ChangeTracker.Clear();
        (await db.EmployeeDocuments.AnyAsync(d => d.Id == oldMedical.Doc.Id)).Should().BeFalse("a medical document past 84 months is retired");
        (await db.EmployeeDocuments.AnyAsync(d => d.Id == newMedical.Doc.Id)).Should().BeTrue();
        (await db.EmployeeDocuments.AnyAsync(d => d.Id == oldContract.Doc.Id)).Should().BeTrue("contracts have no retention limit in the portal");
        (await files.ReadAsync(oldMedical.Key, default)).Should().NotBeEmpty("the file is kept for the grace period so a mistake can be undone");

        (await db.Notifications.AnyAsync(n => n.EventCode == "retention.old")).Should().BeFalse();
        (await db.Notifications.AnyAsync(n => n.EventCode == "retention.new")).Should().BeTrue();
        (await db.AuditLogs.AnyAsync(a => a.Action == "retention.test.old")).Should().BeFalse("audit trails go after seven years");
        (await db.AuditLogs.AnyAsync(a => a.Action == "retention.test.new")).Should().BeTrue();
        (await db.NavSyncLogs.AnyAsync(l => l.Operation == "retention.old")).Should().BeFalse();
        (await db.NavSyncLogs.AnyAsync(l => l.Operation == "retention.new")).Should().BeTrue();
        (await db.AuditLogs.AnyAsync(a => a.Action == "retention.run")).Should().BeTrue("each run is itself audited");

        // After the grace period the file and version rows go; the soft-deleted document row stays as a tombstone.
        await db.EmployeeDocuments.IgnoreQueryFilters().Where(d => d.Id == oldMedical.Doc.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.DeletedAtUtc, DateTime.UtcNow.AddDays(-31)));
        var second = await RunAsync();
        second.FilesPurged.Should().BeGreaterOrEqualTo(1);
        (await db.DocumentVersions.AnyAsync(v => v.DocumentId == oldMedical.Doc.Id)).Should().BeFalse();
        (await db.EmployeeDocuments.IgnoreQueryFilters().AnyAsync(d => d.Id == oldMedical.Doc.Id)).Should().BeTrue();
        var read = async () => await files.ReadAsync(oldMedical.Key, default);
        await read.Should().ThrowAsync<Exception>();
        (await files.ReadAsync(newMedical.Key, default)).Should().NotBeEmpty();
    }
}
