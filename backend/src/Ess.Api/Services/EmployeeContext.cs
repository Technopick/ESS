using Ess.Application.Abstractions;
using Ess.Domain.Organization;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Services;

/// <summary>
/// Resolves "me" for employee self-service. The employee is always taken from the signed-in user's
/// verified mapping, never from request input (strict employee data isolation, spec §9).
/// </summary>
public sealed class EmployeeContext
{
    private readonly EssDbContext _db;
    private readonly ICurrentUser _user;
    private Employee? _cached;

    public EmployeeContext(EssDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<Employee?> GetAsync(CancellationToken ct)
    {
        if (_cached is not null) return _cached;
        if (_user.UserId is null || _user.CompanyId is null) return null;
        _cached = await (from m in _db.EmployeeMappings
                         join e in _db.Employees on m.EmployeeId equals e.Id
                         where m.UserId == _user.UserId && m.CompanyId == _user.CompanyId && e.CompanyId == _user.CompanyId
                         select e).AsNoTracking().FirstOrDefaultAsync(ct);
        return _cached;
    }

    public async Task<Employee> RequireAsync(CancellationToken ct) =>
        await GetAsync(ct) ?? throw new KeyNotFoundException("Your account is not linked to an employee record. Contact HR.");

    /// <summary>Department ids the caller may see for a scoped HR permission; null = no restriction.</summary>
    public async Task<IReadOnlyList<Guid>?> DepartmentScopeAsync(string permission, CancellationToken ct)
    {
        if (_user.UserId is null) return Array.Empty<Guid>();
        var grants = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == _user.UserId && (ur.CompanyId == null || ur.CompanyId == _user.CompanyId)
                         && ur.Role!.RolePermissions.Any(rp => rp.Permission!.Code == permission))
            .Select(ur => ur.ScopeDepartmentId).ToListAsync(ct);
        if (grants.Count == 0) return Array.Empty<Guid>();
        if (grants.Any(g => g is null)) return null;
        // Include sub-departments of each scoped department.
        var all = await _db.Departments.AsNoTracking().Where(d => d.CompanyId == _user.CompanyId).Select(d => new { d.Id, d.ParentDepartmentId }).ToListAsync(ct);
        var result = new HashSet<Guid>(grants.Select(g => g!.Value));
        bool added;
        do
        {
            added = false;
            foreach (var d in all.Where(d => d.ParentDepartmentId is not null && result.Contains(d.ParentDepartmentId.Value) && !result.Contains(d.Id)))
                added |= result.Add(d.Id);
        } while (added);
        return result.ToList();
    }
}
