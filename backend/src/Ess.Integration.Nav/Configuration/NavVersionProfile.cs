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
    public static NavVersionProfile For(NavPlatform p) => p switch
    {
        // NAV 2013/2013 R2: OData v3 (read-only), SOAP for all writes. JSON via $format=json (verify on the build).
        NavPlatform.Nav2013 => new(p, "v3", false, false, false, false,
            new[] { NavAuthMethod.Windows, NavAuthMethod.NavUserPassword }, NavProtocol.OData, NavProtocol.Soap),
        // NAV 2015–2018: OData v4 endpoint with JSON; writes via OData possible but the portal uses SOAP codeunits
        // so NAV business logic always runs.
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
