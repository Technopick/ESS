using System.Globalization;
using System.Xml.Linq;
using Ess.Application.Integration;
using Ess.Integration.Nav.Configuration;
using Ess.Integration.Nav.Transport;

namespace Ess.Integration.Nav.Clients;

/// <summary>
/// SOAP Service Client (spec §3, component 4) for codeunits and pages published as NAV SOAP web services.
/// Builds envelopes in the urn:microsoft-dynamics-schemas namespace that every NAV version since 2009 uses.
/// </summary>
public sealed class NavSoapClient
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private readonly NavTransport _transport;

    public NavSoapClient(NavTransport transport) => _transport = transport;

    public sealed record SoapResponse(string? ReturnValue, IReadOnlyDictionary<string, string> OutParameters);

    /// <summary>
    /// Calls a codeunit method mapped under <paramref name="logicalName"/>. Parameter names must match the NAV
    /// procedure's parameter names as NAV exposes them (camelCase with special characters removed).
    /// </summary>
    public async Task<NavResult<SoapResponse>> InvokeCodeunitAsync(NavConnectionContext ctx, string logicalName,
        IReadOnlyList<(string Name, object? Value)> parameters, bool isWrite, CancellationToken ct)
    {
        var endpoint = ctx.RequireEndpoint(logicalName);
        var method = endpoint.Method ?? throw new NavConfigurationException($"Endpoint '{logicalName}' has no codeunit method configured.");
        var service = endpoint.ServiceName;
        XNamespace ns = $"urn:microsoft-dynamics-schemas/codeunit/{service}";

        var envelope = new XDocument(
            new XElement(Soap + "Envelope", new XAttribute(XNamespace.Xmlns + "soap", Soap.NamespaceName),
                new XElement(Soap + "Body",
                    new XElement(ns + method, parameters.Select(p => new XElement(ns + p.Name, Format(p.Value)))))));

        var url = BuildUrl(ctx, "Codeunit", service);
        var r = await _transport.SendAsync(ctx, new NavCall(NavProtocol.Soap, logicalName, HttpMethod.Post, url,
            envelope.ToString(SaveOptions.DisableFormatting), "text/xml",
            new Dictionary<string, string> { ["SOAPAction"] = $"\"urn:microsoft-dynamics-schemas/codeunit/{service}:{method}\"" },
            isWrite), ct);
        if (!r.Success) return NavResult<SoapResponse>.Fail(r.Error!);
        return NavResult<SoapResponse>.Ok(ParseResponse(r.Body!, method));
    }

    public static string BuildUrl(NavConnectionContext ctx, string objectType, string service)
    {
        var baseUrl = ctx.Settings.SoapBaseUrl?.TrimEnd('/') ?? throw new NavConfigurationException("SOAP URL is not configured.");
        return $"{baseUrl}/{Uri.EscapeDataString(ctx.NavCompanyName)}/{objectType}/{service}";
    }

    public static SoapResponse ParseResponse(string xml, string method)
    {
        var doc = XDocument.Parse(xml);
        var result = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == method + "_Result")
                     ?? throw new FormatException($"SOAP response has no {method}_Result element.");
        string? ret = null;
        var outs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in result.Elements())
        {
            if (el.Name.LocalName == "return_value") ret = el.Value;
            else outs[el.Name.LocalName] = el.Value;
        }
        return new SoapResponse(ret, outs);
    }

    public static string Format(object? v) => v switch
    {
        null => "",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? ""
    };
}
