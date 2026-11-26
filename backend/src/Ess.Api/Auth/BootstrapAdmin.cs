using Ess.Domain.Identity;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Auth;

/// <summary>
/// Creates the first System + Integration Administrator when the Users table is empty and
/// Bootstrap:AdminUserName / Bootstrap:AdminPassword are provided (environment variables, never committed).
/// Remove the variables after first start.
/// </summary>
public static class BootstrapAdmin
{
    public static async Task EnsureAsync(IServiceProvider sp, IConfiguration config, ILogger log)
    {
        var name = config["Bootstrap:AdminUserName"];
        var password = config["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password)) return;

        var db = sp.GetRequiredService<EssDbContext>();
        if (await db.Users.IgnoreQueryFilters().AnyAsync()) return;
        if (password.Length < 12) throw new InvalidOperationException("Bootstrap:AdminPassword must be at least 12 characters.");

        var email = config["Bootstrap:AdminEmail"] ?? name;
        var user = new User
        {
            UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true,
            DefaultCompanyId = await db.Companies.OrderBy(c => c.CreatedAtUtc).Select(c => (Guid?)c.Id).FirstOrDefaultAsync()
        };
        user.PasswordHash = sp.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, password);
        db.Users.Add(user);
        foreach (var role in new[] { SystemRoles.SystemAdministrator, SystemRoles.IntegrationAdministrator })
        {
            var roleId = await db.Roles.Where(r => r.Name == role).Select(r => r.Id).SingleAsync();
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        }
        await db.SaveChangesAsync();
        log.LogWarning("Bootstrap administrator '{User}' created. Remove the Bootstrap:* settings now.", name);
    }
}
