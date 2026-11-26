using System.Diagnostics;
using Ess.Application.Integration;
using Ess.Infrastructure.Persistence;
using Ess.Integration.Nav.Clients;
using Ess.Integration.Nav.Configuration;
using Ess.Integration.Nav.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ess.Integration.Nav.Monitoring;

/// <summary>
/// "Test NAV connectivity" (spec §11). Probes every enabled endpoint mapping: OData/BC with $top=1,
/// SOAP by fetching the service WSDL. This doubles as NAV discovery: unpublished services show up as 404.
/// </summary>
public sealed class NavConnectivityService : INavConnectivityService
{
    private readonly NavConnectionManager _connections;
    private readonly NavTransport _transport;
    private readonly IServiceScopeFactory _scopes;

    public NavConnectivityService(NavConnectionManager connections, NavTransport transport, IServiceScopeFactory scopes)
    {
        _connections = connections;
        _transport = transport;
        _scopes = scopes;
    }

    public async Task<NavConnectionTestResult> TestAsync(Guid integrationSettingsId, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        NavConnectionContext ctx;
        try
        {
            ctx = await _connections.GetForSettingsAsync(integrationSettingsId, ct);
        }
        catch (NavConfigurationException ex)
        {
            return await Save(integrationSettingsId, new NavConnectionTestResult(false, ex.Message, null, 0, Array.Empty<NavEndpointProbe>()), ct);
        }

        var probes = new List<NavEndpointProbe>();
        foreach (var ep in ctx.Endpoints.Values.Where(e => e.IsEnabled).OrderBy(e => e.LogicalName))
        {
            string url;
            NavProtocol protocol;
            try
            {
                if (ep.Protocol.Equals("Soap", StringComparison.OrdinalIgnoreCase))
                {
                    protocol = NavProtocol.Soap;
                    url = NavSoapClient.BuildUrl(ctx, "Codeunit", ep.ServiceName);
                }
                else
                {
                    protocol = ep.Protocol.Equals("BcApi", StringComparison.OrdinalIgnoreCase) ? NavProtocol.BcApi : NavProtocol.OData;
                    url = NavODataClient.BuildUrl(ctx, protocol, ep.ServiceName, new NavODataClient.Query(Top: 1));
                }
            }
            catch (NavConfigurationException ex)
            {
                probes.Add(new NavEndpointProbe(ep.LogicalName, ep.Protocol, ep.ServiceName, false, null, ex.Message));
                continue;
            }
            var r = await _transport.SendAsync(ctx, new NavCall(protocol, "Probe:" + ep.LogicalName, HttpMethod.Get, url, null), ct);
            probes.Add(new NavEndpointProbe(ep.LogicalName, ep.Protocol, ep.ServiceName, r.Success, r.StatusCode,
                r.Success ? "OK" : r.Error?.Message));
        }

        var ok = probes.Count > 0 && probes.All(p => p.Reachable);
        var msg = probes.Count == 0 ? "No endpoint mappings are configured for this NAV instance."
            : ok ? $"All {probes.Count} endpoints reachable." : $"{probes.Count(p => !p.Reachable)} of {probes.Count} endpoints failed.";
        return await Save(integrationSettingsId, new NavConnectionTestResult(ok, msg, ctx.Settings.Platform.ToString(), sw.ElapsedMilliseconds, probes), ct);
    }

    private async Task<NavConnectionTestResult> Save(Guid id, NavConnectionTestResult r, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        await db.NavIntegrationSettings.Where(s => s.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.LastHealthCheckAtUtc, DateTime.UtcNow)
            .SetProperty(x => x.LastHealthCheckOk, r.Success)
            .SetProperty(x => x.LastHealthCheckMessage, r.Message), ct);
        return r;
    }
}
