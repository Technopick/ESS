using Ess.Application.Security;
using Ess.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ess.Infrastructure.Persistence;

/// <summary>
/// Seeds reference data that every installation needs: the permission catalogue and the ten system roles.
/// It is idempotent and never creates demo employees or users (demo data lives in a separate, opt-in seeder).
/// </summary>
public sealed class DatabaseSeeder
{
    private readonly EssDbContext _db;
    private readonly ILogger<DatabaseSeeder> _log;

    public DatabaseSeeder(EssDbContext db, ILogger<DatabaseSeeder> log)
    {
        _db = db;
        _log = log;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var existing = await _db.Permissions.ToDictionaryAsync(p => p.Code, ct);
        foreach (var code in Permissions.All().Where(c => !existing.ContainsKey(c)))
        {
            var p = new Permission { Code = code, Module = Permissions.ModuleOf(code) };
            _db.Permissions.Add(p);
            existing[code] = p;
        }

        var roles = await _db.Roles.Include(r => r.RolePermissions).ToDictionaryAsync(r => r.NormalizedName, ct);
        foreach (var name in SystemRoles.All)
        {
            var norm = name.ToUpperInvariant();
            if (roles.ContainsKey(norm)) continue; // never overwrite an administrator's edits
            var role = new Role { Name = name, NormalizedName = norm, IsSystem = true };
            foreach (var code in Permissions.DefaultMatrix[name])
                role.RolePermissions.Add(new RolePermission { Role = role, PermissionId = existing[code].Id });
            _db.Roles.Add(role);
            _log.LogInformation("Seeded system role {Role}", name);
        }

        await _db.SaveChangesAsync(ct);
    }
}
