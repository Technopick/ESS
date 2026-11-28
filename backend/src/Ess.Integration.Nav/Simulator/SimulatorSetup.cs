using Ess.Domain.Integration;
using Ess.Domain.Organization;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Integration.Nav.Simulator;

/// <summary>
/// DEVELOPMENT ONLY. Creates a "SIMULATOR" NAV instance and a "SIM" company wired to the simulator, with endpoint
/// mappings that mirror the recommended NAV customisation (docs/nav-customizations.md). Called only when
/// DemoData:Enabled is true and the environment is not Production. Every record is clearly labelled.
/// </summary>
public static class SimulatorSetup
{
    public const string Gateway = Configuration.NavEndpointPresets.Gateway;

    public static async Task EnsureAsync(EssDbContext db, CancellationToken ct = default)
    {
        // Idempotent: creates the simulator once, and adds mappings introduced by later releases to an existing one.
        var settings = await db.NavIntegrationSettings.FirstOrDefaultAsync(s => s.Name == "SIMULATOR", ct);
        var existing = new HashSet<string>();
        if (settings is not null)
            existing.UnionWith(await db.NavEndpointMappings.Where(m => m.IntegrationSettingsId == settings.Id).Select(m => m.LogicalName).ToListAsync(ct));
        else
        {
            settings = new NavIntegrationSettings
            {
                Name = "SIMULATOR",
                // Behaves like the customer's NAV 2016 (9.0) service tier.
                Platform = NavPlatform.Nav2016,
                ServerHost = "nav-simulator.invalid",
                ServiceInstance = "DynamicsNAV90",
                SoapBaseUrl = "http://nav-simulator.invalid:7047/DynamicsNAV90/WS",
                ODataBaseUrl = "http://nav-simulator.invalid:7048/DynamicsNAV90/ODataV4",
                AuthMethod = NavAuthMethod.NavUserPassword,
                UserName = "SIMULATOR",
                UseSimulator = true
            };
            db.NavIntegrationSettings.Add(settings);
            db.Companies.Add(new Company { Code = "SIM", DisplayName = "Simulated Company (demo data)", NavCompanyName = "CRONUS Simulated Ltd", IntegrationSettingsId = settings.Id });
        }

        void Map(string logical, string protocol, string service, string? method = null, string? modified = null)
        {
            if (!existing.Add(logical)) return;
            db.NavEndpointMappings.Add(new NavEndpointMapping
            {
                IntegrationSettingsId = settings.Id, LogicalName = logical, Protocol = protocol, ServiceName = service, Method = method, ModifiedField = modified
            });
        }

        // The simulator implements the standard gateway except claim status (claims are tracked by finance in NAV).
        foreach (var m in Configuration.NavEndpointPresets.StandardGateway.Where(p => p.LogicalName != "Expense.Status"))
            Map(m.LogicalName, m.Protocol, m.ServiceName, m.Method, m.ModifiedField);
        await db.SaveChangesAsync(ct);
    }
}
