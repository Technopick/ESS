using System.Text.Json;
using Ess.Application.Abstractions;
using Ess.Application.Integration;
using Ess.Domain.Organization;
using Ess.Domain.Payroll;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Services;

public sealed record PayslipLineView(string Code, string Description, string Group, decimal Amount, decimal? YearToDate);

public sealed record PayslipView(string PeriodCode, string PeriodName, DateOnly? PaymentDate, string Currency, string EmployeeNo, string EmployeeName,
    string? JobTitle, string? Department, decimal BasicPay, decimal GrossPay, decimal TotalDeductions, decimal NetPay,
    IReadOnlyList<PayslipLineView> Lines, bool FromCache, DateTime RetrievedAtUtc, bool PdfAvailable);

public sealed record PayrollPeriodView(Guid Id, string Code, string Name, DateOnly StartDate, DateOnly EndDate, DateOnly? PayDate, bool Closed, bool Published, DateTime? PublishedAtUtc);

public sealed record StatutoryMonth(string PeriodCode, string PeriodName, decimal GrossPay, decimal TaxablePay, IReadOnlyDictionary<string, decimal> Amounts, bool Available);

public sealed record StatutorySummary(int Year, IReadOnlyList<string> Codes, IReadOnlyDictionary<string, string> Labels, IReadOnlyList<StatutoryMonth> Months,
    IReadOnlyDictionary<string, decimal> Totals, decimal TotalGross, decimal TotalTaxable);

/// <summary>
/// Payslips (spec §6). NAV calculates payroll; the portal only shows what NAV returns, for the signed-in employee's own
/// record, and only for periods payroll staff have published. Each view is audited. An encrypted snapshot lets an
/// employee still see a published payslip when NAV is offline; nothing in it is readable at rest.
/// </summary>
public sealed class PayrollService
{
    private readonly EssDbContext _db;
    private readonly INavPayrollService _nav;
    private readonly ISecretProtector _secrets;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;
    private readonly IConfiguration _config;

    public PayrollService(EssDbContext db, INavPayrollService nav, ISecretProtector secrets, IAuditWriter audit, IClock clock, IConfiguration config)
    {
        _db = db;
        _nav = nav;
        _secrets = secrets;
        _audit = audit;
        _clock = clock;
        _config = config;
    }

    public Task<List<PayrollPeriodView>> PublishedPeriodsAsync(Guid companyId, CancellationToken ct) =>
        _db.PayrollPeriodsCache.AsNoTracking().Where(p => p.CompanyId == companyId && p.IsPublished).OrderByDescending(p => p.StartDate)
            .Select(p => new PayrollPeriodView(p.Id, p.NavKey, p.Name, p.StartDate, p.EndDate, p.PayDate, p.IsClosed, p.IsPublished, p.PublishedAtUtc)).ToListAsync(ct);

    /// <summary>Returns null when NAV has no payslip for the employee in that period. Throws KeyNotFound for unknown or unpublished periods.</summary>
    public async Task<PayslipView?> GetAsync(Employee employee, string periodCode, bool requirePublished, string auditReason, CancellationToken ct)
    {
        var period = await _db.PayrollPeriodsCache.AsNoTracking().FirstOrDefaultAsync(p => p.CompanyId == employee.CompanyId && p.NavKey == periodCode, ct);
        if (period is null || (requirePublished && !period.IsPublished)) throw new KeyNotFoundException("This payslip is not available yet.");

        NavPayslip? slip = null;
        var fromCache = false;
        var retrieved = _clock.UtcNow;
        var meta = await _db.PayslipMetadata.FirstOrDefaultAsync(m => m.EmployeeId == employee.Id && m.PayrollPeriodId == period.Id, ct);

        // A closed period no longer changes in NAV, so a recent snapshot is enough; open periods are always read live.
        var snapshotFresh = meta?.SnapshotEncrypted is not null && period.IsClosed
                            && meta.RetrievedAtUtc > _clock.UtcNow.AddHours(-_config.GetValue("Payroll:SnapshotHours", 24));
        var liveFailed = false;
        if (!snapshotFresh)
        {
            var live = await _nav.GetPayslipAsync(employee.CompanyId, employee.NavKey, periodCode, ct);
            if (live.Success)
            {
                slip = live.Value;
                if (slip is not null && !string.Equals(slip.EmployeeNo, employee.NavKey, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("NAV returned a payslip for a different employee."); // never shown
                if (slip is not null) await SaveSnapshotAsync(employee, period, meta, slip, ct);
            }
            else if (meta?.SnapshotEncrypted is not null) liveFailed = true;
            else
                throw new NavUnavailableException(live.Error!.Category == NavErrorCategory.Configuration
                    ? "Payslips are not set up in NAV yet. Contact payroll."
                    : "NAV could not be reached. Try again in a few minutes.");
        }
        if (snapshotFresh || liveFailed)
        {
            slip = JsonSerializer.Deserialize<NavPayslip>(_secrets.Unprotect(meta!.SnapshotEncrypted!));
            fromCache = liveFailed;
            retrieved = meta!.RetrievedAtUtc;
        }
        if (slip is null) return null;

        await _audit.WriteAsync("payroll.payslip_viewed", "Payslip", $"{employee.NavKey}/{periodCode}", new { reason = auditReason }, ct: ct);

        var org = await (from e in _db.Employees.AsNoTracking()
                         join d in _db.Departments on e.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                         join p in _db.Positions on e.PositionId equals p.Id into pj from p in pj.DefaultIfEmpty()
                         where e.Id == employee.Id
                         select new { Dept = d != null ? d.Name : null, Title = p != null ? p.Title : null }).FirstAsync(ct);
        var pdf = await _db.NavEndpointMappings.AnyAsync(m => m.LogicalName == "Payslip.Pdf"
            && _db.Companies.Any(c => c.Id == employee.CompanyId && c.IntegrationSettingsId == m.IntegrationSettingsId), ct);

        return new PayslipView(periodCode, period.Name, slip.PaymentDate ?? period.PayDate, slip.Currency, employee.NavKey, employee.FullName, org.Title, org.Dept,
            slip.BasicPay, slip.GrossPay, slip.TotalDeductions, slip.NetPay,
            slip.Lines.Select(l => new PayslipLineView(l.Code, l.Description, l.Group, l.Amount, l.YearToDate)).ToList(), fromCache, retrieved, pdf);
    }

    public async Task<byte[]?> GetPdfAsync(Employee employee, string periodCode, bool requirePublished, CancellationToken ct)
    {
        var period = await _db.PayrollPeriodsCache.AsNoTracking().FirstOrDefaultAsync(p => p.CompanyId == employee.CompanyId && p.NavKey == periodCode, ct);
        if (period is null || (requirePublished && !period.IsPublished)) throw new KeyNotFoundException("This payslip is not available yet.");
        var r = await _nav.GetPayslipPdfAsync(employee.CompanyId, employee.NavKey, periodCode, ct);
        if (!r.Success) throw new NavUnavailableException("NAV could not produce the payslip PDF. Use Print instead.");
        if (r.Value is not null) await _audit.WriteAsync("payroll.payslip_downloaded", "Payslip", $"{employee.NavKey}/{periodCode}", ct: ct);
        return r.Value;
    }

    /// <summary>Statutory deductions per month for a year (PAYE, NSSF, SHIF, Housing Levy), for the employee's own records and returns.</summary>
    public async Task<StatutorySummary> StatutoryAsync(Employee employee, int year, CancellationToken ct)
    {
        var periods = await _db.PayrollPeriodsCache.AsNoTracking()
            .Where(p => p.CompanyId == employee.CompanyId && p.IsPublished && p.EndDate.Year == year).OrderBy(p => p.StartDate).ToListAsync(ct);
        var months = new List<StatutoryMonth>();
        var labels = new Dictionary<string, string>();
        foreach (var p in periods)
        {
            PayslipView? slip;
            try { slip = await GetAsync(employee, p.NavKey, true, "statutory summary", ct); }
            catch (NavUnavailableException) { slip = null; }
            if (slip is null)
            {
                months.Add(new StatutoryMonth(p.NavKey, p.Name, 0, 0, new Dictionary<string, decimal>(), false));
                continue;
            }
            var statutory = slip.Lines.Where(l => l.Group.Equals("Statutory", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var l in statutory) labels.TryAdd(l.Code, l.Description);
            var taxable = slip.Lines.FirstOrDefault(l => l.Group.Equals("Information", StringComparison.OrdinalIgnoreCase) && l.Code.Contains("TAXABLE", StringComparison.OrdinalIgnoreCase))?.Amount ?? 0;
            months.Add(new StatutoryMonth(p.NavKey, p.Name, slip.GrossPay, taxable,
                statutory.GroupBy(l => l.Code).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount)), true));
        }
        var codes = labels.Keys.OrderBy(c => c switch { "PAYE" => 0, _ when c.StartsWith("NSSF") => 1, "SHIF" or "NHIF" => 2, "AHL" => 3, _ => 4 }).ThenBy(c => c).ToList();
        var totals = codes.ToDictionary(c => c, c => months.Sum(m => m.Amounts.GetValueOrDefault(c)));
        return new StatutorySummary(year, codes, labels, months, totals, months.Sum(m => m.GrossPay), months.Sum(m => m.TaxablePay));
    }

    private async Task SaveSnapshotAsync(Employee employee, PayrollPeriodCache period, PayslipMetadata? meta, NavPayslip slip, CancellationToken ct)
    {
        meta ??= _db.PayslipMetadata.Add(new PayslipMetadata
        {
            CompanyId = employee.CompanyId, EmployeeId = employee.Id, PayrollPeriodId = period.Id, NavEmployeeNo = employee.NavKey, NavPeriodKey = period.NavKey
        }).Entity;
        meta.SnapshotEncrypted = _secrets.Protect(JsonSerializer.Serialize(slip));
        meta.NetPay = null; // figures stay inside the encrypted snapshot only
        meta.Currency = slip.Currency;
        meta.RetrievedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>NAV could not answer and there is no fallback; shown to the user as a 503 with the message.</summary>
public sealed class NavUnavailableException : ServiceUnavailableException
{
    public NavUnavailableException(string message) : base(message) { }
}

/// <summary>A dependency (NAV, virus scanner) is missing or down; shown as a 503 with the message.</summary>
public class ServiceUnavailableException : Exception
{
    public ServiceUnavailableException(string message) : base(message) { }
}
