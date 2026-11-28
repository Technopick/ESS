using Ess.Domain.Integration;

namespace Ess.Integration.Nav.Configuration;

public enum NavProtocol { Soap, OData, BcApi }

/// <summary>
/// NAV Version Compatibility Adapter (spec §3, component 12). Describes what each NAV/BC generation
/// supports so the engine picks the right protocol, URL shape and auth method.
/// Facts marked "verify" must be confirmed against the customer's build during Phase 1 discovery.
/// </summary>
public sealed record NavVersionProfile(
    NavPlatform Platform,
    string ODataVersion,
    bool ODataJsonNative,
    bool ODataWritesSupported,
    bool SupportsBcApi,
    bool SupportsSystemModifiedAt,
    NavAuthMethod[] AllowedAuth,
    NavProtocol DefaultReadProtocol,
    NavProtocol DefaultWriteProtocol)
{
    /// <summary>
    /// C/AL platforms (NAV 2013 to 2018) have no JSON types, so structured parameters such as expense lines are sent as XML,
    /// which C/AL reads with an XMLport or XML DOM Management. AL platforms (BC) receive JSON.
    /// </summary>
    public bool StructuredParametersAsXml => Platform is NavPlatform.Nav2013 or NavPlatform.Nav2015 or NavPlatform.Nav2016 or NavPlatform.Nav2017 or NavPlatform.Nav2018;

    /// <summary>
    /// Default service URLs for an on-premises NAV server: SOAP on port 7047 and OData on 7048, under the server instance
    /// (DynamicsNAV90 for NAV 2016). Returned as suggestions for the settings screen; the customer's ports may differ.
    /// </summary>
    public static (string Soap, string OData)? SuggestedUrls(NavPlatform p, string host, string? instance, bool https)
    {
        var inst = string.IsNullOrWhiteSpace(instance) ? DefaultInstance(p) : instance.Trim();
        if (inst is null || string.IsNullOrWhiteSpace(host)) return null;
        var scheme = https ? "https" : "http";
        var odata = For(p).ODataVersion == "v3" ? "OData" : "ODataV4";
        return ($"{scheme}://{host.Trim()}:7047/{inst}/WS", $"{scheme}://{host.Trim()}:7048/{inst}/{odata}");
    }

    /// <summary>The server instance name a default installation uses.</summary>
    public static string? DefaultInstance(NavPlatform p) => p switch
    {
        NavPlatform.Nav2013 => "DynamicsNAV71",
        NavPlatform.Nav2015 => "DynamicsNAV80",
        NavPlatform.Nav2016 => "DynamicsNAV90",
        NavPlatform.Nav2017 => "DynamicsNAV100",
        NavPlatform.Nav2018 => "DynamicsNAV110",
        _ => null
    };

    public static NavVersionProfile For(NavPlatform p) => p switch
    {
        // NAV 2013/2013 R2: OData v3 (read-only), SOAP for all writes. JSON via $format=json (verify on the build).
        NavPlatform.Nav2013 => new(p, "v3", false, false, false, false,
            new[] { NavAuthMethod.Windows, NavAuthMethod.NavUserPassword }, NavProtocol.OData, NavProtocol.Soap),
        // NAV 2015–2018: OData v4 endpoint (/ODataV4) with JSON; writes via OData possible but the portal uses SOAP codeunits
        // so NAV business logic always runs. NAV 2016 (9.0) specifics the adapters handle: "Last Date Modified" is a date
        // without time (incremental sync re-reads the watermark day), option values arrive XML-name-encoded ("_x0020_" for a
        // space, "_blank_" or "_x0020_" for an empty option), $top limits the whole result (no nextLink), and the version
        // token is "@odata.etag". No OAuth: Windows, NavUserPassword or a web service access key.
        NavPlatform.Nav2015 or NavPlatform.Nav2016 or NavPlatform.Nav2017 or NavPlatform.Nav2018 => new(p, "v4", true, true, false, false,
            new[] { NavAuthMethod.Windows, NavAuthMethod.NavUserPassword, NavAuthMethod.WebServiceAccessKey }, NavProtocol.OData, NavProtocol.Soap),
        // BC on-prem: API pages (v2.0 from BC15) plus custom API pages; SOAP/OData still available.
        NavPlatform.BusinessCentralOnPrem => new(p, "v4", true, true, true, true,
            new[] { NavAuthMethod.Windows, NavAuthMethod.NavUserPassword, NavAuthMethod.WebServiceAccessKey, NavAuthMethod.OAuthClientCredentials },
            NavProtocol.BcApi, NavProtocol.BcApi),
        // BC online: OAuth only (basic/web-service-key auth is retired for SaaS).
        NavPlatform.BusinessCentralOnline => new(p, "v4", true, true, true, true,
            new[] { NavAuthMethod.OAuthClientCredentials }, NavProtocol.BcApi, NavProtocol.BcApi),
        _ => throw new ArgumentOutOfRangeException(nameof(p), p, null)
    };
}
