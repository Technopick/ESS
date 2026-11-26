using Ess.Application.Integration;
using Ess.Domain.Leave;
using Ess.Domain.Organization;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Services;

public sealed record LeaveBalanceView(Guid LeaveTypeId, string LeaveTypeCode, string LeaveType, string Period, decimal Entitlement,
    decimal BroughtForward, decimal Taken, decimal Pending, decimal Available);

public sealed record LeaveBalancesResult(IReadOnlyList<LeaveBalanceView> Balances, DateTime? RetrievedAtUtc, bool FromCache, string? NavMessage);

/// <summary>
/// Leave balances always come from NAV. A successful read refreshes the cache; if NAV is unreachable the last
/// cached figures are returned and clearly flagged as such, so the portal keeps working (spec §17).
/// </summary>
public sealed class LeaveBalanceService
{
    private readonly EssDbContext _db;
    private readonly INavLeaveService _nav;

    public LeaveBalanceService(EssDbContext db, INavLeaveService nav)
    {
        _db = db;
        _nav = nav;
    }

    public async Task<LeaveBalancesResult> GetAsync(Employee employee, CancellationToken ct)
    {
        var types = await _db.LeaveTypes.Where(t => t.CompanyId == employee.CompanyId).ToDictionaryAsync(t => t.NavKey, StringComparer.OrdinalIgnoreCase, ct);
        var live = await _nav.GetBalancesAsync(employee.CompanyId, employee.NavKey, ct);
        string? message = null;
        if (live.Success)
        {
            // Defence in depth: only keep rows for this employee.
            var rows = live.Value!.Where(b => string.Equals(b.EmployeeNo, employee.NavKey, StringComparison.OrdinalIgnoreCase) && types.ContainsKey(b.LeaveTypeCode)).ToList();
            var existing = await _db.LeaveBalancesCache.Where(c => c.EmployeeId == employee.Id).ToListAsync(ct);
            _db.LeaveBalancesCache.RemoveRange(existing);
            var now = DateTime.UtcNow;
            foreach (var b in rows)
                _db.LeaveBalancesCache.Add(new LeaveBalanceCache
                {
                    CompanyId = employee.CompanyId, EmployeeId = employee.Id, LeaveTypeId = types[b.LeaveTypeCode].Id, LeavePeriodCode = b.PeriodCode,
                    Entitlement = b.Entitlement, BroughtForward = b.BroughtForward, Taken = b.Taken, PendingApproval = b.Pending, Available = b.Available,
                    RetrievedAtUtc = now
                });
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            message = live.Error!.Category == NavErrorCategory.Configuration
                ? "Leave balances are not available from NAV yet."
                : "NAV could not be reached, so these are the last balances retrieved.";
        }

        var cached = await _db.LeaveBalancesCache.AsNoTracking().Where(c => c.EmployeeId == employee.Id).ToListAsync(ct);
        var byId = types.Values.ToDictionary(t => t.Id);
        var views = cached.Where(c => byId.ContainsKey(c.LeaveTypeId) && byId[c.LeaveTypeId].IsActive)
            .OrderBy(c => byId[c.LeaveTypeId].Name)
            .Select(c => new LeaveBalanceView(c.LeaveTypeId, byId[c.LeaveTypeId].NavKey, byId[c.LeaveTypeId].Name, c.LeavePeriodCode, c.Entitlement,
                c.BroughtForward, c.Taken, c.PendingApproval, c.Available)).ToList();
        return new LeaveBalancesResult(views, cached.Select(c => (DateTime?)c.RetrievedAtUtc).DefaultIfEmpty(null).Max(), !live.Success, message);
    }
}
