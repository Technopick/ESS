using System.Security.Cryptography;
using Ess.Application.Abstractions;
using Ess.Application.Documents;
using Ess.Application.Notifications;
using Ess.Application.Security;
using Ess.Domain.Documents;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Services;

public sealed record DocumentView(Guid Id, string Title, Guid CategoryId, string Category, Guid? EmployeeId, string? EmployeeName, DocumentVisibility Visibility,
    DateOnly? ExpiryDate, int CurrentVersion, string FileName, string ContentType, long SizeBytes, DateTime UploadedAtUtc, string UploadedBy, bool Scanned,
    bool CanDelete);

/// <summary>
/// Employee documents (spec §6): validated by content, scanned for malware, encrypted at rest, versioned, and every
/// download logged. Who may read a document is decided here and nowhere else.
/// </summary>
public sealed class DocumentService
{
    public const string ReceiptCategory = "RECEIPT";
    public const string ExpenseClaimOwner = "ExpenseClaim";

    private readonly EssDbContext _db;
    private readonly IFileStorage _files;
    private readonly IMalwareScanner _scanner;
    private readonly ICurrentUser _user;
    private readonly EmployeeContext _me;
    private readonly IHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly INotificationService _notify;

    public DocumentService(EssDbContext db, IFileStorage files, IMalwareScanner scanner, ICurrentUser user, EmployeeContext me, IHostEnvironment env,
        IConfiguration config, INotificationService notify)
    {
        _notify = notify;
        _db = db;
        _files = files;
        _scanner = scanner;
        _user = user;
        _me = me;
        _env = env;
        _config = config;
    }

    private static FluentValidation.ValidationException Rejected(string message) =>
        new(new[] { new FluentValidation.Results.ValidationFailure("file", message) });

    /// <summary>Default categories, created per company on first use; HR can add more.</summary>
    public async Task EnsureCategoriesAsync(Guid companyId, CancellationToken ct)
    {
        if (await _db.DocumentCategories.AnyAsync(c => c.CompanyId == companyId, ct)) return;
        (string Code, string Name, bool Upload, int? Retention)[] defaults =
        {
            ("CONTRACT", "Employment contract", false, null), ("ID", "Identification (ID, passport, KRA PIN)", true, null),
            ("CERT", "Certificates and qualifications", true, null), ("MEDICAL", "Medical documents", true, 84),
            ("POLICY", "Company policies", false, null), ("LETTER", "Letters from HR", false, null),
            (ReceiptCategory, "Expense receipts", true, 84), ("OTHER", "Other", true, 84),
        };
        foreach (var d in defaults)
            _db.DocumentCategories.Add(new DocumentCategory { CompanyId = companyId, Code = d.Code, Name = d.Name, EmployeeCanUpload = d.Upload, RetentionMonths = d.Retention });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<(byte[] Content, string ContentType, string FileName)> ValidateAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0) throw Rejected("The file is empty.");
        if (file.Length > FileRules.MaxBytes) throw Rejected("Files can be at most 10 MB.");
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var type = FileRules.Sniff(bytes.AsSpan(0, Math.Min(16, bytes.Length)), file.FileName)
                   ?? throw Rejected("Upload a PDF, JPEG, PNG, Word (.docx) or Excel (.xlsx) file.");
        return (bytes, type, FileRules.SafeName(file.FileName, type));
    }

    /// <summary>Stores a new document or a new version of an existing one. Throws a validation error for files that are refused.</summary>
    public async Task<EmployeeDocument> StoreAsync(EmployeeDocument doc, IFormFile file, CancellationToken ct)
    {
        var (bytes, type, name) = await ValidateAsync(file, ct);
        var scan = await _scanner.ScanAsync(bytes, ct);
        if (scan.Verdict == ScanVerdict.Infected) throw Rejected("The file was rejected by the virus scanner.");
        if (scan.Verdict == ScanVerdict.NotScanned && _env.IsProduction() && !_config.GetValue("Storage:AllowUnscanned", false))
            throw new ServiceUnavailableException("Uploads are disabled until a virus scanner is configured.");

        var key = await _files.SaveAsync(bytes, ct);
        var isNew = _db.Entry(doc).State == EntityState.Detached;
        var version = isNew ? 1 : doc.CurrentVersion + 1;
        doc.CurrentVersion = version;
        if (isNew) _db.EmployeeDocuments.Add(doc);
        _db.DocumentVersions.Add(new DocumentVersion
        {
            DocumentId = doc.Id, Version = version, FileName = name, ContentType = type, SizeBytes = bytes.LongLength, StorageKey = key,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), UploadedAtUtc = DateTime.UtcNow, UploadedBy = _user.UserName ?? "unknown",
            MalwareScanPassed = scan.Verdict == ScanVerdict.Clean
        });
        await _db.SaveChangesAsync(ct);
        return doc;
    }

    public async Task<bool> CanReadAsync(EmployeeDocument doc, CancellationToken ct)
    {
        if (doc.CompanyId != _user.CompanyId) return false;
        var me = await _me.GetAsync(ct);
        if (doc.EmployeeId is not null && doc.EmployeeId == me?.Id) return true;
        if (await CanManageAsync(doc, ct)) return true;
        if (doc.Visibility == DocumentVisibility.Company) return true;
        if (doc.Visibility == DocumentVisibility.Department && me?.DepartmentId is not null && doc.DepartmentId == me.DepartmentId) return true;

        if (doc.OwnerEntityType == ExpenseClaimOwner && doc.OwnerEntityId is { } claimId)
        {
            if (_user.HasPermission(Permissions.ExpenseViewAll)) return true;
            var instance = await _db.ExpenseClaims.Where(c => c.Id == claimId).Select(c => c.WorkflowInstanceId).FirstOrDefaultAsync(ct);
            if (instance is not null && await _db.ApprovalTasks.AnyAsync(t => t.WorkflowInstanceId == instance && t.AssigneeUserId == _user.UserId, ct)) return true;
        }
        return false;
    }

    /// <summary>HR with documents.manage, within department scope for employee documents.</summary>
    public async Task<bool> CanManageAsync(EmployeeDocument doc, CancellationToken ct)
    {
        if (!_user.HasPermission(Permissions.DocumentsManage) || doc.CompanyId != _user.CompanyId) return false;
        var scope = await _me.DepartmentScopeAsync(Permissions.DocumentsManage, ct);
        if (scope is null) return true;
        if (doc.EmployeeId is null) return doc.DepartmentId is not null && scope.Contains(doc.DepartmentId.Value);
        var dept = await _db.Employees.Where(e => e.Id == doc.EmployeeId).Select(e => e.DepartmentId).FirstOrDefaultAsync(ct);
        return dept is not null && scope.Contains(dept.Value);
    }

    public async Task<(byte[] Content, DocumentVersion Version)> ReadAsync(EmployeeDocument doc, int? version, string action, CancellationToken ct)
    {
        var v = await _db.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(x => x.DocumentId == doc.Id && x.Version == (version ?? doc.CurrentVersion), ct)
                ?? throw new KeyNotFoundException("That version does not exist.");
        var bytes = await _files.ReadAsync(v.StorageKey, ct);
        if (!SHA256.HashData(bytes).AsSpan().SequenceEqual(Convert.FromHexString(v.Sha256))) throw new InvalidOperationException("The stored file failed its integrity check.");
        _db.DocumentAccessLogs.Add(new DocumentAccessLog { DocumentId = doc.Id, Version = v.Version, UserId = _user.UserId!.Value, AccessedAtUtc = DateTime.UtcNow, Action = action, IpAddress = _user.IpAddress });
        await _db.SaveChangesAsync(ct);
        return (bytes, v);
    }

    public IQueryable<DocumentView> Views(IQueryable<EmployeeDocument> q, Guid? myEmployeeId, bool manager) =>
        from d in q
        join c in _db.DocumentCategories on d.CategoryId equals c.Id
        join e in _db.Employees on d.EmployeeId equals e.Id into ej from e in ej.DefaultIfEmpty()
        let v = _db.DocumentVersions.Where(x => x.DocumentId == d.Id && x.Version == d.CurrentVersion).First()
        select new DocumentView(d.Id, d.Title, c.Id, c.Name, d.EmployeeId, e != null ? e.FirstName + " " + e.LastName : null, d.Visibility, d.ExpiryDate,
            d.CurrentVersion, v.FileName, v.ContentType, v.SizeBytes, v.UploadedAtUtc, v.UploadedBy, v.MalwareScanPassed,
            manager || (d.EmployeeId == myEmployeeId && c.EmployeeCanUpload && d.OwnerEntityId == null));

    /// <summary>Daily: tells employees and HR about documents expiring in the next 30 days (once, at 30 and 7 days).</summary>
    public async Task<int> NotifyExpiringAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var targets = new[] { today.AddDays(30), today.AddDays(7) };
        var docs = await (from d in _db.EmployeeDocuments.AsNoTracking()
                          join m in _db.EmployeeMappings on d.EmployeeId equals m.EmployeeId
                          where d.ExpiryDate != null && targets.Contains(d.ExpiryDate.Value)
                          select new { d.CompanyId, d.Title, d.ExpiryDate, m.UserId }).ToListAsync(ct);
        foreach (var d in docs)
            await _notify.QueueAsync(new NotificationRequest(d.CompanyId, d.UserId, NotificationEvents.DocumentExpiring, $"{d.Title} expires on {d.ExpiryDate:d MMM yyyy}",
                $"Your document \"{d.Title}\" expires on {d.ExpiryDate:d MMM yyyy}. Upload a renewed copy under Documents.", "/documents"), ct);
        await _db.SaveChangesAsync(ct);
        return docs.Count;
    }
}
