using System.Text.Json;
using Ess.Application.Integration;
using Ess.Integration.Nav.Configuration;
using Ess.Integration.Nav.Transport;

namespace Ess.Integration.Nav.Clients;

/// <summary>
/// OData Client (spec §3, component 5) for NAV OData v3/v4 web services and Business Central API pages
/// (BC APIs are OData v4 with a different URL shape). Handles server-driven paging.
/// </summary>
public sealed class NavODataClient
{
    private readonly NavTransport _transport;

    public NavODataClient(NavTransport transport) => _transport = transport;

    public sealed record Query(string? Filter = null, string? OrderBy = null, int? Top = null, string? Select = null, string? Expand = null, int? Skip = null);

    public sealed record ODataPage(IReadOnlyList<JsonElement> Rows, string? NextLink);

    /// <summary>Reads one page. Pass the previous page's NextLink as <paramref name="nextLink"/> to continue.</summary>
    public async Task<NavResult<ODataPage>> GetPageAsync(NavConnectionContext ctx, string logicalName, Query q, string? nextLink, CancellationToken ct)
    {
        var endpoint = ctx.RequireEndpoint(logicalName);
        var protocol = endpoint.Protocol.Equals("BcApi", StringComparison.OrdinalIgnoreCase) ? NavProtocol.BcApi : NavProtocol.OData;
        var url = nextLink ?? BuildUrl(ctx, protocol, endpoint.ServiceName, q);

        var r = await _transport.SendAsync(ctx, new NavCall(protocol, logicalName, HttpMethod.Get, url, null), ct);
        if (!r.Success) return NavResult<ODataPage>.Fail(r.Error!);
        return NavResult<ODataPage>.Ok(Parse(r.Body!));
    }

    /// <summary>Reads all pages (for small reference sets such as departments or leave types).</summary>
    public async Task<NavResult<IReadOnlyList<JsonElement>>> GetAllAsync(NavConnectionContext ctx, string logicalName, Query q, CancellationToken ct, int maxPages = 200)
    {
        var all = new List<JsonElement>();
        string? next = null;
        for (var i = 0; i < maxPages; i++)
        {
            var page = await GetPageAsync(ctx, logicalName, q, next, ct);
            if (!page.Success) return NavResult<IReadOnlyList<JsonElement>>.Fail(page.Error!);
            all.AddRange(page.Value!.Rows);
            next = page.Value.NextLink;
            if (next is null) return NavResult<IReadOnlyList<JsonElement>>.Ok(all);
        }
        return NavResult<IReadOnlyList<JsonElement>>.Fail(NavErrorCategory.Configuration, $"Paging limit reached reading {logicalName}.");
    }

    /// <summary>POST to a BC API page or OData v4 entity set (used for BC custom API pages that run AL business logic).</summary>
    public async Task<NavResult<JsonElement>> PostAsync(NavConnectionContext ctx, string logicalName, object body, string idempotencyKey, CancellationToken ct)
    {
        var endpoint = ctx.RequireEndpoint(logicalName);
        var protocol = endpoint.Protocol.Equals("BcApi", StringComparison.OrdinalIgnoreCase) ? NavProtocol.BcApi : NavProtocol.OData;
        var url = BuildUrl(ctx, protocol, endpoint.ServiceName, new Query());
        var json = JsonSerializer.Serialize(body);
        var r = await _transport.SendAsync(ctx, new NavCall(protocol, logicalName, HttpMethod.Post, url, json,
            "application/json", new Dictionary<string, string> { ["X-ESS-Idempotency-Key"] = idempotencyKey }, IsWrite: true), ct);
        if (!r.Success) return NavResult<JsonElement>.Fail(r.Error!);
        using var doc = JsonDocument.Parse(r.Body!);
        return NavResult<JsonElement>.Ok(doc.RootElement.Clone());
    }

    public static string BuildUrl(NavConnectionContext ctx, NavProtocol protocol, string serviceName, Query q)
    {
        string baseUrl;
        if (protocol == NavProtocol.BcApi)
        {
            var api = ctx.Settings.ApiBaseUrl?.TrimEnd('/') ?? throw new NavConfigurationException("API base URL is not configured.");
            var cut = serviceName.LastIndexOf('/');
            if (cut < 0) throw new NavConfigurationException($"BC API service '{serviceName}' must be '<route>/<entitySet>', e.g. 'v2.0/employees'.");
            var route = serviceName[..cut];
            var entity = serviceName[(cut + 1)..];
            var companyId = ctx.NavCompanyGuid ?? throw new NavConfigurationException("Business Central company ID is not set for this company.");
            baseUrl = $"{api}/api/{route}/companies({companyId})/{entity}";
        }
        else
        {
            var od = ctx.Settings.ODataBaseUrl?.TrimEnd('/') ?? throw new NavConfigurationException("OData URL is not configured.");
            var company = Uri.EscapeDataString(ctx.NavCompanyName.Replace("'", "''"));
            baseUrl = $"{od}/Company('{company}')/{serviceName}";
        }

        var parts = new List<string>();
        if (protocol == NavProtocol.OData && ctx.Profile.ODataVersion == "v3") parts.Add("$format=json");
        if (q.Filter is not null) parts.Add("$filter=" + Uri.EscapeDataString(q.Filter));
        if (q.OrderBy is not null) parts.Add("$orderby=" + Uri.EscapeDataString(q.OrderBy));
        if (q.Skip is not null and > 0) parts.Add("$skip=" + q.Skip);
        if (q.Top is not null) parts.Add("$top=" + q.Top);
        if (q.Select is not null) parts.Add("$select=" + Uri.EscapeDataString(q.Select));
        if (q.Expand is not null) parts.Add("$expand=" + Uri.EscapeDataString(q.Expand));
        return parts.Count == 0 ? baseUrl : baseUrl + "?" + string.Join('&', parts);
    }

    /// <summary>Parses OData v4 / BC ("value"), v3 JSON light ("value"/"odata.nextLink") and v3 verbose ("d.results"/"__next").</summary>
    public static ODataPage Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        JsonElement rows;
        string? next = null;
        if (root.TryGetProperty("value", out rows))
        {
            if (root.TryGetProperty("@odata.nextLink", out var n4)) next = n4.GetString();
            else if (root.TryGetProperty("odata.nextLink", out var n3)) next = n3.GetString();
        }
        else if (root.TryGetProperty("d", out var d))
        {
            rows = d.ValueKind == JsonValueKind.Array ? d : d.GetProperty("results");
            if (d.ValueKind == JsonValueKind.Object && d.TryGetProperty("__next", out var nv)) next = nv.GetString();
        }
        else
        {
            return new ODataPage(new[] { root.Clone() }, null); // single entity
        }
        return new ODataPage(rows.EnumerateArray().Select(e => e.Clone()).ToList(), next);
    }

    /// <summary>Escapes a literal for use inside an OData $filter string.</summary>
    public static string Literal(string value) => "'" + value.Replace("'", "''") + "'";
}
