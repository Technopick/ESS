using Ess.Domain.Common;

namespace Ess.Domain.Payroll;

/// <summary>Payroll period mirrored from NAV. The portal never calculates payroll.</summary>
public class PayrollPeriodCache : Entity, ICompanyScoped, INavMirrored
{
    public Guid CompanyId { get; set; }
    public required string NavKey { get; set; }
    public required string Name { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly? PayDate { get; set; }
    public bool IsClosed { get; set; }
    /// <summary>Payslips are visible to employees only after HR/payroll publishes the period.</summary>
    public bool IsPublished { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime LastSyncedAtUtc { get; set; }
    public string? NavVersionToken { get; set; }
}

/// <summary>
/// Metadata for one employee's payslip. The figures themselves are fetched from NAV on demand
/// (or from an encrypted snapshot when the company enables payslip snapshotting), so the portal
/// does not hold a second copy of payroll by default.
/// </summary>
public class PayslipMetadata : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid PayrollPeriodId { get; set; }
    public required string NavEmployeeNo { get; set; }
    public required string NavPeriodKey { get; set; }
    public decimal? NetPay { get; set; }
    public string Currency { get; set; } = "KES";
    /// <summary>Encrypted JSON snapshot of the payslip lines, only when snapshotting is enabled.</summary>
    public string? SnapshotEncrypted { get; set; }
    public string? PdfStorageKey { get; set; }
    public string? PdfSha256 { get; set; }
    public DateTime RetrievedAtUtc { get; set; }
}
