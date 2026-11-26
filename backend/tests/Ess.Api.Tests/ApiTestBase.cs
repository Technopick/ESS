using System.Net.Http.Json;
using System.Text.Json;
using Ess.Domain.Identity;
using Ess.Infrastructure.Persistence;
using Ess.Integration.Nav.Sync;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ess.Api.Tests;

/// <summary>Shared helpers: the simulated company's employees as signed-in users, role grants and direct DB access.</summary>
public abstract class ApiTestBase
{
    protected const string Password = "Empl0yee-Passw0rd!";
    protected const string Baraka = "baraka.otieno@example.invalid";   // SIM-002, reports to Daudi
    protected const string Daudi = "daudi.mwangi@example.invalid";     // SIM-004, Finance Manager, no manager
    protected const string Chebet = "chebet.kiprono@example.invalid";  // SIM-003, reports to Amani
    protected const string Amani = "amani.wanjiru@example.invalid";    // SIM-001, HR Manager
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    protected readonly EssApiFactory _f;

    protected ApiTestBase(EssApiFactory f) => _f = f;

    protected async Task<Guid> SimCompanyAsync()
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        var id = await db.Companies.Where(c => c.Code == "SIM").Select(c => c.Id).SingleAsync();
        if (!await db.Employees.AnyAsync(e => e.CompanyId == id))
            await _f.Services.GetRequiredService<NavSyncService>().SyncCompanyAsync(id, true, "test", default);
        return id;
    }

    protected async Task<HttpClient> UserAsync(string email)
    {
        var company = await SimCompanyAsync();
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
            var u = await db.Users.FirstOrDefaultAsync(x => x.NormalizedEmail == email.ToUpperInvariant());
            if (u?.PasswordHash is not null)
            {
                // Other test classes share these users (lockout and MFA tests); reset that state.
                u.LockoutEndUtc = null;
                u.AccessFailedCount = 0;
                u.MfaEnabled = false;
                u.MfaSecretEncrypted = null;
                await db.SaveChangesAsync();
                return await _f.SignInAsync(email, Password);
            }
        }
        var admin = await _f.SignInAsync(EssApiFactory.AdminUser, EssApiFactory.AdminPassword);
        (await admin.PostAsJsonAsync("/api/admin/users/provision", new { companyId = company, sendInvites = true })).EnsureSuccessStatusCode();
        var token = _f.Emails.LastTokenFor(email);
        token.Should().NotBeNull();
        (await _f.Anonymous().PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = Password })).EnsureSuccessStatusCode();
        return await _f.SignInAsync(email, Password);
    }

    protected async Task GrantRoleAsync(string email, string role)
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        var u = await db.Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant());
        var r = await db.Roles.SingleAsync(x => x.Name == role);
        if (!await db.UserRoles.AnyAsync(x => x.UserId == u.Id && x.RoleId == r.Id))
        {
            db.UserRoles.Add(new UserRole { UserId = u.Id, RoleId = r.Id });
            await db.SaveChangesAsync();
        }
    }

    protected async Task<T> WithDbAsync<T>(Func<EssDbContext, Task<T>> f)
    {
        using var scope = _f.Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<EssDbContext>());
    }

}
