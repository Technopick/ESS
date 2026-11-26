using Ess.Domain.Integration;

namespace Ess.Integration.Nav.Configuration;

/// <summary>Resolved, in-memory connection details for one call. Secrets are decrypted only here and never logged.</summary>
public sealed class NavConnectionContext
{
    public required Guid CompanyId { get; init; }
    public required Guid SettingsId { get; init; }
    public required NavIntegrationSettings Settings { get; init; }
    public required NavVersionProfile Profile { get; init; }
    public required string NavCompanyName { get; init; }
    public Guid? NavCompanyGuid { get; init; }
    public required IReadOnlyDictionary<string, NavEndpointMapping> Endpoints { get; init; }
    internal string? Password { get; init; }
    internal string? ClientSecret { get; init; }

    public NavEndpointMapping RequireEndpoint(string logicalName) =>
        Endpoints.TryGetValue(logicalName, out var m) && m.IsEnabled
            ? m
            : throw new NavConfigurationException($"No enabled NAV endpoint mapping for '{logicalName}' on instance '{Settings.Name}'.");

    public NavEndpointMapping? FindEndpoint(string logicalName) =>
        Endpoints.TryGetValue(logicalName, out var m) && m.IsEnabled ? m : null;
}

public sealed class NavConfigurationException : Exception
{
    public NavConfigurationException(string message) : base(message) { }
}
