using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Domain.Integration;
using Ess.Infrastructure.Persistence;
using Ess.Integration.Nav.Configuration;
using Ess.Integration.Nav.Setup;
using Ess.Integration.Nav.Sync;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers.Admin;

/// <summary>
/// The NAV / Business Central connection assistant. Connect, discover and fields only read from NAV and save nothing;
/// apply saves the connection, the company link and the mappings in one go and can start the first sync.
/// Secrets travel only in the request body (HTTPS) and are encrypted on apply. When editing a saved connection the
/// assistant may leave them empty, and the stored ones are used.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/nav/setup")]
[Route("api/admin/nav/setup")]
[HasPermission(Permissions.IntegrationConfigure)]
public sealed class NavSetupController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly NavSetupAssistant _assistant;
    private readonly ISecretProtector _protector;
    private readonly IAuditWriter _audit;
    private readonly ICurrentUser _user;

    public NavSetupController(EssDbContext db, NavSetupAssistant assistant, ISecretProtector protector, IAuditWriter audit, ICurrentUser user)
    {
        _db = db;
        _assistant = assistant;
        _protector = protector;
        _audit = audit;
        _user = user;
    }

    public sealed record ConnectRequest(NavSetupConnection Connection, Guid? SettingsId);
    public sealed record DiscoverRequest(NavSetupConnection Connection, Guid? SettingsId, string Company, Guid? CompanyId);
    public sealed record GatewayRequest(NavSetupConnection Connection, Guid? SettingsId, string Company, Guid? CompanyId, string ServiceName, IReadOnlyList<string>? Codeunits);
    public sealed record FieldsRequest(NavSetupConnection Connection, Guid? SettingsId, string Company, Guid? CompanyId, string LogicalName, string Protocol, string ServiceName);

    /// <summary>Saved values the assistant starts from when editing a connection (never the secrets).</summary>
    public sealed record StartView(IReadOnlyList<PortalCompany> PortalCompanies, SavedConnection? Saved);
    public sealed record PortalCompany(Guid Id, string Code, string DisplayName, Guid? IntegrationSettingsId, string NavCompanyName, Guid? NavCompanyGuid);
    public sealed record SavedConnection(Guid Id, string Name, NavPlatform Platform, string? ServerHost, string? ServiceInstance, bool Https, int? SoapPort, int? ODataPort,
        string? TenantId, string? EnvironmentName, NavAuthMethod AuthMethod, string? UserName, string? Domain, bool HasPassword, string? ClientId, bool HasClientSecret,
        bool ValidateServerCertificate, IReadOnlyList<SavedMapping> Mappings);
    public sealed record SavedMapping(string LogicalName, string Protocol, string ServiceName, string? Method, string? ModifiedField, string? FieldMapJson, bool IsEnabled);

    [HttpGet("start")]
    public async Task<StartView> Start([FromQuery] Guid? settingsId, CancellationToken ct)
    {
        var companies = await _db.Companies.AsNoTracking().OrderBy(c => c.Code)
            .Select(c => new PortalCompany(c.Id, c.Code, c.DisplayName, c.IntegrationSettingsId, c.NavCompanyName, c.NavCompanyGuid)).ToListAsync(ct);
        SavedConnection? saved = null;
        if (settingsId is not null && await _db.NavIntegrationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == settingsId && !s.UseSimulator, ct) is { } s)
        {
            var url = new[] { s.ODataBaseUrl, s.ApiBaseUrl, s.SoapBaseUrl }.FirstOrDefault(u => u is not null);
            Uri.TryCreate(url, UriKind.Absolute, out var u);
            Uri.TryCreate(s.SoapBaseUrl, UriKind.Absolute, out var soap);
            Uri.TryCreate(s.ODataBaseUrl, UriKind.Absolute, out var od);
            var mappings = await _db.NavEndpointMappings.AsNoTracking().Where(m => m.IntegrationSettingsId == s.Id).OrderBy(m => m.LogicalName)
                .Select(m => new SavedMapping(m.LogicalName, m.Protocol, m.ServiceName, m.Method, m.ModifiedField, m.FieldMapJson, m.IsEnabled)).ToListAsync(ct);
            saved = new SavedConnection(s.Id, s.Name, s.Platform, string.IsNullOrWhiteSpace(s.ServerHost) ? u?.Host : s.ServerHost, s.ServiceInstance,
                u?.Scheme == "https", soap?.Port, od?.Port, s.TenantId, s.EnvironmentName, s.AuthMethod, s.UserName, s.Domain, s.PasswordEncrypted is not null,
                s.ClientId, s.ClientSecretEncrypted is not null, s.ValidateServerCertificate, mappings);
        }
        return new StartView(companies, saved);
    }

    [HttpPost("connect")]
    public async Task<NavConnectResult> Connect(ConnectRequest req, CancellationToken ct) =>
        await _assistant.ConnectAsync(await WithStoredSecrets(req.Connection, req.SettingsId, ct), ct);

    [HttpPost("discover")]
    public async Task<NavDiscoverResult> Discover(DiscoverRequest req, CancellationToken ct) =>
        await _assistant.DiscoverAsync(await WithStoredSecrets(req.Connection, req.SettingsId, ct), req.Company, req.CompanyId, ct);

    [HttpPost("gateway")]
    public async Task<NavGatewaySuggestion> Gateway(GatewayRequest req, CancellationToken ct) =>
        await _assistant.SuggestGatewayAsync(await WithStoredSecrets(req.Connection, req.SettingsId, ct), req.Company, req.CompanyId,
            req.Codeunits ?? Array.Empty<string>(), req.ServiceName, ct);

    [HttpPost("fields")]
    public async Task<NavFieldsResult> Fields(FieldsRequest req, CancellationToken ct) =>
        await _assistant.FieldsAsync(await WithStoredSecrets(req.Connection, req.SettingsId, ct), req.Company, req.CompanyId, req.LogicalName,
            req.Protocol, req.ServiceName, ct);

    public sealed record MappingInput(string LogicalName, string Protocol, string ServiceName, string? Method, string? ModifiedField, string? FieldMapJson, bool IsEnabled);
    public sealed record ApplyRequest(NavSetupConnection Connection, Guid? SettingsId, string Name, Guid PortalCompanyId, string NavCompany, Guid? NavCompanyId,
        IReadOnlyList<MappingInput> Mappings, bool RunFirstSync = true);
    public sealed record ApplyResult(Guid SettingsId, int MappingsSaved, int MappingsEnabled, string? SyncJobId);

    [HttpPost("apply")]
    public async Task<IActionResult> Apply(ApplyRequest req, [FromServices] NavConnectionManager connections, [FromServices] IBackgroundJobClient jobs, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length is 0 or > 50) return Field("name", "Give the connection a name of up to 50 characters, for example NAV 2016 Live.");
        if (await _db.NavIntegrationSettings.AnyAsync(x => x.Name == name && x.Id != req.SettingsId, ct)) return Field("name", "Another NAV connection has this name.");
        if (string.IsNullOrWhiteSpace(req.NavCompany)) return Field("navCompany", "Choose the NAV company.");
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == req.PortalCompanyId, ct);
        if (company is null) return Field("portalCompanyId", "Choose the portal company.");
        var c = req.Connection;
        var profile = NavVersionProfile.For(c.Platform);
        if (!profile.AllowedAuth.Contains(c.AuthMethod)) return Field("authMethod", $"This version does not support {NavSetupAssistant.AuthName(c.AuthMethod)}.");
        var addresses = NavSetupAssistant.ResolveAddresses(c, out var problem);
        if (problem is not null) return Field("serverHost", problem);
        if (c.Platform is (NavPlatform.BusinessCentralOnPrem or NavPlatform.BusinessCentralOnline) && req.NavCompanyId is null)
            return Field("navCompany", "Business Central needs the company's ID; choose the company from the list.");
        foreach (var m in req.Mappings)
        {
            if (m.Protocol is not ("OData" or "Soap" or "BcApi")) return Field("mappings", $"{m.LogicalName}: protocol must be OData, Soap or BcApi.");
            if (m.IsEnabled && string.IsNullOrWhiteSpace(m.ServiceName)) return Field("mappings", $"{m.LogicalName}: choose a NAV service or switch it off.");
            if (!string.IsNullOrWhiteSpace(m.FieldMapJson))
            {
                try { _ = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(m.FieldMapJson); }
                catch (System.Text.Json.JsonException) { return Field("mappings", $"{m.LogicalName}: the field map is not valid."); }
            }
        }
        if (!req.Mappings.Any(m => m.LogicalName == "Employee" && m.IsEnabled))
            return Field("mappings", "Choose the NAV service for Employees: the portal needs it to know who works there.");

        var s = req.SettingsId is null ? null : await _db.NavIntegrationSettings.FirstOrDefaultAsync(x => x.Id == req.SettingsId, ct);
        if (req.SettingsId is not null && s is null) return NotFound();
        if (s?.UseSimulator == true) return Field("settingsId", "The simulator connection cannot be changed here.");
        var created = s is null;
        s ??= new NavIntegrationSettings { Name = name };
        s.Name = name;
        s.Platform = c.Platform;
        s.ServerHost = c.Platform == NavPlatform.BusinessCentralOnline ? null : c.ServerHost?.Trim();
        s.ServiceInstance = c.ServiceInstance?.Trim();
        s.SoapBaseUrl = addresses.Soap;
        s.ODataBaseUrl = addresses.OData;
        s.ApiBaseUrl = addresses.Api;
        s.TenantId = c.TenantId?.Trim();
        s.EnvironmentName = c.EnvironmentName?.Trim();
        s.AuthMethod = c.AuthMethod;
        s.UserName = string.IsNullOrWhiteSpace(c.UserName) ? null : c.UserName.Trim();
        s.Domain = string.IsNullOrWhiteSpace(c.Domain) ? null : c.Domain.Trim();
        s.ClientId = string.IsNullOrWhiteSpace(c.ClientId) ? null : c.ClientId.Trim();
        if (!string.IsNullOrEmpty(c.Password)) s.PasswordEncrypted = _protector.Protect(c.Password);
        else if (c.AuthMethod == NavAuthMethod.Windows && s.UserName is null) s.PasswordEncrypted = null; // the app pool account needs none
        if (!string.IsNullOrEmpty(c.ClientSecret)) s.ClientSecretEncrypted = _protector.Protect(c.ClientSecret);
        if (c.AuthMethod is (NavAuthMethod.NavUserPassword or NavAuthMethod.WebServiceAccessKey) && s.PasswordEncrypted is null)
            return Field("password", "Enter the password or access key.");
        if (c.AuthMethod == NavAuthMethod.OAuthClientCredentials && s.ClientSecretEncrypted is null) return Field("clientSecret", "Enter the client secret.");
        s.ValidateServerCertificate = c.ValidateServerCertificate;
        s.IsEnabled = true;
        s.ModifiedAtUtc = DateTime.UtcNow;
        if (created) _db.NavIntegrationSettings.Add(s);
        await _db.SaveChangesAsync(ct);

        var existing = await _db.NavEndpointMappings.Where(m => m.IntegrationSettingsId == s.Id).ToListAsync(ct);
        foreach (var input in req.Mappings)
        {
            var m = existing.FirstOrDefault(x => x.LogicalName.Equals(input.LogicalName, StringComparison.OrdinalIgnoreCase));
            if (m is null && !input.IsEnabled && string.IsNullOrWhiteSpace(input.ServiceName)) continue; // nothing to remember
            if (m is null)
            {
                m = new NavEndpointMapping { IntegrationSettingsId = s.Id, LogicalName = input.LogicalName, Protocol = input.Protocol, ServiceName = "" };
                _db.NavEndpointMappings.Add(m);
            }
            m.Protocol = input.Protocol;
            m.ServiceName = string.IsNullOrWhiteSpace(input.ServiceName) ? m.ServiceName : input.ServiceName.Trim();
            m.Method = string.IsNullOrWhiteSpace(input.Method) ? null : input.Method.Trim();
            m.ModifiedField = string.IsNullOrWhiteSpace(input.ModifiedField) ? null : input.ModifiedField.Trim();
            m.FieldMapJson = string.IsNullOrWhiteSpace(input.FieldMapJson) ? null : input.FieldMapJson.Trim();
            m.IsEnabled = input.IsEnabled;
        }

        company.IntegrationSettingsId = s.Id;
        company.NavCompanyName = req.NavCompany.Trim();
        company.NavCompanyGuid = req.NavCompanyId;
        await _db.SaveChangesAsync(ct);
        connections.Invalidate();

        await _audit.WriteAsync(created ? "nav.settings.created" : "nav.settings.updated", nameof(NavIntegrationSettings), s.Id.ToString(),
            new { s.Name, s.Platform, s.SoapBaseUrl, s.ODataBaseUrl, s.ApiBaseUrl, s.AuthMethod, s.UserName, via = "assistant",
                  mappings = req.Mappings.Where(m => m.IsEnabled).Select(m => new { m.LogicalName, m.Protocol, m.ServiceName, m.Method }) }, ct: ct);
        await _audit.WriteAsync("nav.company.linked", "Company", company.Id.ToString(), new { company.IntegrationSettingsId, company.NavCompanyName }, ct: ct);

        string? jobId = null;
        if (req.RunFirstSync)
        {
            var who = _user.UserName ?? "unknown";
            var companyId = company.Id;
            jobId = jobs.Enqueue<NavSyncService>(x => x.SyncCompanyAsync(companyId, true, "assistant:" + who, CancellationToken.None));
        }
        return Ok(new ApplyResult(s.Id, req.Mappings.Count, req.Mappings.Count(m => m.IsEnabled), jobId));
    }

    /// <summary>Fills an empty password or client secret from the saved connection being edited.</summary>
    private async Task<NavSetupConnection> WithStoredSecrets(NavSetupConnection c, Guid? settingsId, CancellationToken ct)
    {
        if (settingsId is null || (!string.IsNullOrEmpty(c.Password) && !string.IsNullOrEmpty(c.ClientSecret))) return c;
        var s = await _db.NavIntegrationSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == settingsId, ct);
        if (s is null) return c;
        return c with
        {
            Password = string.IsNullOrEmpty(c.Password) && s.PasswordEncrypted is not null ? _protector.Unprotect(s.PasswordEncrypted) : c.Password,
            ClientSecret = string.IsNullOrEmpty(c.ClientSecret) && s.ClientSecretEncrypted is not null ? _protector.Unprotect(s.ClientSecretEncrypted) : c.ClientSecret
        };
    }

    private IActionResult Field(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = new[] { message } }));
}
