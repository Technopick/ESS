using System.Text.Json;
using Ess.Application.Integration;
using Ess.Integration.Nav.Clients;
using Ess.Integration.Nav.Configuration;
using Ess.Integration.Nav.Mapping;
using Microsoft.Extensions.Logging;

namespace Ess.Integration.Nav.Adapters;

/// <summary>Shared plumbing for adapters: resolves the connection, maps configuration errors and unexpected exceptions to NavResult.</summary>
public abstract class NavAdapterBase
{
    protected NavConnectionManager Connections { get; }
    protected NavODataClient OData { get; }
    protected NavSoapClient Soap { get; }
    protected ILogger Log { get; }

    protected NavAdapterBase(NavConnectionManager connections, NavODataClient odata, NavSoapClient soap, ILogger log)
    {
        Connections = connections;
        OData = odata;
        Soap = soap;
        Log = log;
    }

    protected async Task<NavResult<T>> RunAsync<T>(Guid companyId, string operation, Func<NavConnectionContext, Task<NavResult<T>>> body, CancellationToken ct)
    {
        try
        {
            var ctx = await Connections.GetAsync(companyId, ct);
            return await body(ctx);
        }
        catch (NavConfigurationException ex)
        {
            Log.LogWarning("NAV configuration problem in {Operation}: {Message}", operation, ex.Message);
            return NavResult<T>.Fail(NavErrorCategory.Configuration, ex.Message);
        }
        catch (FormatException ex)
        {
            // NAV answered but the shape did not match the mapping: an admin needs to fix the field map.
            Log.LogError(ex, "Unexpected NAV response shape in {Operation}", operation);
            return NavResult<T>.Fail(NavErrorCategory.Configuration, $"Unexpected NAV response for {operation}: {ex.Message}");
        }
    }

    protected static bool IsSoap(NavConnectionContext ctx, string logicalName) =>
        ctx.RequireEndpoint(logicalName).Protocol.Equals("Soap", StringComparison.OrdinalIgnoreCase);

    protected static NavFieldMapper Mapper(NavConnectionContext ctx, string logicalName, IReadOnlyDictionary<string, string> odataDefaults,
        IReadOnlyDictionary<string, string> bcApiDefaults)
    {
        var ep = ctx.RequireEndpoint(logicalName);
        var defaults = ep.Protocol.Equals("BcApi", StringComparison.OrdinalIgnoreCase) ? bcApiDefaults : odataDefaults;
        return new NavFieldMapper(defaults, ep.FieldMapJson);
    }

    /// <summary>Builds a date filter in the dialect the NAV version expects.</summary>
    protected static string DateFilter(NavConnectionContext ctx, string navField, string op, DateTime valueUtc, bool dateOnly)
    {
        if (ctx.Profile.ODataVersion == "v3")
            return $"{navField} {op} datetime'{valueUtc:yyyy-MM-ddTHH:mm:ss}'";
        return dateOnly ? $"{navField} {op} {valueUtc:yyyy-MM-dd}" : $"{navField} {op} {valueUtc:yyyy-MM-ddTHH:mm:ssZ}";
    }

    protected static NavSubmissionReceipt ReceiptFrom(string? documentNo, string? status = null)
    {
        if (string.IsNullOrWhiteSpace(documentNo))
            throw new FormatException("NAV did not return a document number, so the submission cannot be treated as confirmed.");
        return new NavSubmissionReceipt(documentNo, status, DateTime.UtcNow);
    }

    protected static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
