using System.Collections.Concurrent;
using Ess.Application.Abstractions;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ess.Integration.Nav.Configuration;

/// <summary>
/// NAV Connection Manager (spec §3, component 1). Resolves which NAV instance, company and endpoint mappings
/// serve a portal company, decrypts credentials in memory, and caches the result briefly.
/// </summary>
public sealed class NavConnectionManager
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<Guid, (NavConnectionContext Ctx, DateTime At)> _byCompany = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly ISecretProtector _protector;
    private readonly IHostEnvironment _env;
    private readonly NavEngineOptions _options;

    public NavConnectionManager(IServiceScopeFactory scopes, ISecretProtector protector, IHostEnvironment env, IOptions<NavEngineOptions> options)
    {
        _scopes = scopes;
        _protector = protector;
        _env = env;
        _options = options.Value;
    }

    public async Task<NavConnectionContext> GetAsync(Guid companyId, CancellationToken ct)
    {
        if (_byCompany.TryGetValue(companyId, out var c) && DateTime.UtcNow - c.At < CacheTtl) return c.Ctx;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == companyId, ct)
                      ?? throw new NavConfigurationException($"Company {companyId} not found.");
        if (company.IntegrationSettingsId is null)
            throw new NavConfigurationException($"Company '{company.Code}' has no NAV integration settings assigned.");

        var ctx = await BuildAsync(db, company.IntegrationSettingsId.Value, companyId, company.NavCompanyName, company.NavCompanyGuid, ct);
        _byCompany[companyId] = (ctx, DateTime.UtcNow);
        return ctx;
    }

    /// <summary>Builds a context for a settings record directly (used by "Test connection" before a company is assigned).</summary>
    public async Task<NavConnectionContext> GetForSettingsAsync(Guid settingsId, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.IntegrationSettingsId == settingsId, ct);
        return await BuildAsync(db, settingsId, company?.Id ?? Guid.Empty, company?.NavCompanyName ?? "", company?.NavCompanyGuid, ct);
    }

    public void Invalidate() => _byCompany.Clear();

    private async Task<NavConnectionContext> BuildAsync(EssDbContext db, Guid settingsId, Guid companyId, string navCompany, Guid? navCompanyGuid, CancellationToken ct)
    {
        var s = await db.NavIntegrationSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == settingsId, ct)
                ?? throw new NavConfigurationException($"NAV integration settings {settingsId} not found.");
        if (!s.IsEnabled) throw new NavConfigurationException($"NAV instance '{s.Name}' is disabled.");
        if (s.UseSimulator && (!_options.AllowSimulator || _env.IsProduction()))
            throw new NavConfigurationException("The NAV simulator is not allowed in this environment.");

        var profile = NavVersionProfile.For(s.Platform);
        if (!profile.AllowedAuth.Contains(s.AuthMethod))
            throw new NavConfigurationException($"Authentication method {s.AuthMethod} is not supported on {s.Platform}.");

        var endpoints = await db.NavEndpointMappings.AsNoTracking()
            .Where(m => m.IntegrationSettingsId == settingsId)
            .ToDictionaryAsync(m => m.LogicalName, StringComparer.OrdinalIgnoreCase, ct);

        return new NavConnectionContext
        {
            CompanyId = companyId,
            SettingsId = s.Id,
            Settings = s,
            Profile = profile,
            NavCompanyName = navCompany,
            NavCompanyGuid = navCompanyGuid,
            Endpoints = endpoints,
            Password = s.PasswordEncrypted is null ? null : _protector.Unprotect(s.PasswordEncrypted),
            ClientSecret = s.ClientSecretEncrypted is null ? null : _protector.Unprotect(s.ClientSecretEncrypted)
        };
    }
}
