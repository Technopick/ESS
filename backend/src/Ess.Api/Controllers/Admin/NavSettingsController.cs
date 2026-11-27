using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Domain.Integration;
using Ess.Infrastructure.Persistence;
using Ess.Integration.Nav.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers.Admin;

/// <summary>
/// NAV connection set-up: the NAV instance (URLs, authentication), its endpoint mappings and which portal company
/// reads which NAV company. Passwords and client secrets are write-only: they are encrypted on save and never returned.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/nav")]
[Route("api/admin/nav")]
[HasPermission(Permissions.IntegrationConfigure)]
public sealed class NavSettingsController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly ISecretProtector _protector;

    public NavSettingsController(EssDbContext db, IAuditWriter audit, ISecretProtector protector)
    {
        _db = db;
        _audit = audit;
        _protector = protector;
    }

    public sealed record InstanceView(Guid Id, string Name, NavPlatform Platform, string? ServerHost, string? ServiceInstance, string? SoapBaseUrl,
        string? ODataBaseUrl, string? ApiBaseUrl, string? TenantId, string? EnvironmentName, NavAuthMethod AuthMethod, string? UserName, string? Domain,
        bool HasPassword, string? ClientId, bool HasClientSecret, int TimeoutSeconds, bool ValidateServerCertificate, int MaxConcurrency, bool IsEnabled,
        bool UseSimulator, DateTime? LastHealthCheckAtUtc, bool? LastHealthCheckOk, string? LastHealthCheckMessage, NavAuthMethod[] AllowedAuth, int Endpoints);

    public sealed record CompanyLink(Guid Id, string Code, string DisplayName, string NavCompanyName, Guid? NavCompanyGuid, Guid? IntegrationSettingsId);

    public sealed record Overview(IReadOnlyList<InstanceView> Instances, IReadOnlyList<CompanyLink> Companies);

    [HttpGet]
    public async Task<Overview> Get(CancellationToken ct)
    {
        var counts = await _db.NavEndpointMappings.GroupBy(m => m.IntegrationSettingsId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var instances = (await _db.NavIntegrationSettings.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct))
            .Select(s => View(s, counts.GetValueOrDefault(s.Id))).ToList();
        var companies = await _db.Companies.AsNoTracking().OrderBy(c => c.Code)
            .Select(c => new CompanyLink(c.Id, c.Code, c.DisplayName, c.NavCompanyName, c.NavCompanyGuid, c.IntegrationSettingsId)).ToListAsync(ct);
        return new Overview(instances, companies);
    }

    private static InstanceView View(NavIntegrationSettings s, int endpoints) => new(s.Id, s.Name, s.Platform, s.ServerHost, s.ServiceInstance, s.SoapBaseUrl,
        s.ODataBaseUrl, s.ApiBaseUrl, s.TenantId, s.EnvironmentName, s.AuthMethod, s.UserName, s.Domain, s.PasswordEncrypted is not null, s.ClientId,
        s.ClientSecretEncrypted is not null, s.TimeoutSeconds, s.ValidateServerCertificate, s.MaxConcurrency, s.IsEnabled, s.UseSimulator,
        s.LastHealthCheckAtUtc, s.LastHealthCheckOk, s.LastHealthCheckMessage, NavVersionProfile.For(s.Platform).AllowedAuth, endpoints);

    /// <summary>Password and ClientSecret: null keeps the stored value, an empty string clears it.</summary>
    public sealed record InstanceInput(string Name, NavPlatform Platform, string? ServerHost, string? ServiceInstance, string? SoapBaseUrl, string? ODataBaseUrl,
        string? ApiBaseUrl, string? TenantId, string? EnvironmentName, NavAuthMethod AuthMethod, string? UserName, string? Domain, string? Password,
        string? ClientId, string? ClientSecret, int TimeoutSeconds = 60, bool ValidateServerCertificate = true, int MaxConcurrency = 4, bool IsEnabled = true);

    [HttpPost]
    public async Task<IActionResult> Create(InstanceInput input, CancellationToken ct)
    {
        var s = new NavIntegrationSettings { Name = input.Name?.Trim() ?? "" };
        var error = await ApplyAsync(s, input, ct);
        if (error is not null) return error;
        _db.NavIntegrationSettings.Add(s);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("nav.settings.created", nameof(NavIntegrationSettings), s.Id.ToString(), Summary(s), ct: ct);
        return Created($"/api/admin/nav/{s.Id}", View(s, 0));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, InstanceInput input, CancellationToken ct)
    {
        var s = await _db.NavIntegrationSettings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound();
        if (s.UseSimulator) return Conflict(new ProblemDetails { Title = "The simulator instance is managed by the system.", Status = 409 });
        var error = await ApplyAsync(s, input, ct);
        if (error is not null) return error;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("nav.settings.updated", nameof(NavIntegrationSettings), s.Id.ToString(), Summary(s), ct: ct);
        return Ok(View(s, await _db.NavEndpointMappings.CountAsync(m => m.IntegrationSettingsId == s.Id, ct)));
    }

    private static object Summary(NavIntegrationSettings s) => new { s.Name, s.Platform, s.SoapBaseUrl, s.ODataBaseUrl, s.AuthMethod, s.UserName, s.IsEnabled, s.ValidateServerCertificate };

    private async Task<IActionResult?> ApplyAsync(NavIntegrationSettings s, InstanceInput i, CancellationToken ct)
    {
        var name = i.Name?.Trim() ?? "";
        if (name.Length is 0 or > 50) return Field("name", "Give the NAV instance a name of up to 50 characters.");
        if (await _db.NavIntegrationSettings.AnyAsync(x => x.Name == name && x.Id != s.Id, ct)) return Field("name", "Another NAV instance has this name.");
        var profile = NavVersionProfile.For(i.Platform);
        if (!profile.AllowedAuth.Contains(i.AuthMethod))
            return Field("authMethod", $"{i.Platform} does not support {i.AuthMethod}. Use one of: {string.Join(", ", profile.AllowedAuth)}.");

        string? Url(string? u) => string.IsNullOrWhiteSpace(u) ? null : u.Trim().TrimEnd('/');
        foreach (var (field, value) in new[] { ("soapBaseUrl", i.SoapBaseUrl), ("oDataBaseUrl", i.ODataBaseUrl), ("apiBaseUrl", i.ApiBaseUrl) })
            if (Url(value) is { } u && (!Uri.TryCreate(u, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(parsed.UserInfo)))
                return Field(field, "Enter a full http or https address without a user name or password in it.");
        if (profile.DefaultWriteProtocol == NavProtocol.Soap && Url(i.SoapBaseUrl) is null) return Field("soapBaseUrl", "NAV writes go through SOAP web services; enter the SOAP address.");
        if (profile.DefaultReadProtocol == NavProtocol.OData && Url(i.ODataBaseUrl) is null) return Field("oDataBaseUrl", "Enter the OData address.");
        if (profile.SupportsBcApi && Url(i.ApiBaseUrl) is null && profile.DefaultReadProtocol == NavProtocol.BcApi) return Field("apiBaseUrl", "Enter the Business Central API address.");
        if (i.AuthMethod != NavAuthMethod.Windows && i.AuthMethod != NavAuthMethod.OAuthClientCredentials && string.IsNullOrWhiteSpace(i.UserName))
            return Field("userName", "Enter the NAV service user.");
        if (i.AuthMethod is NavAuthMethod.NavUserPassword or NavAuthMethod.WebServiceAccessKey && i.Password == null && s.PasswordEncrypted is null)
            return Field("password", i.AuthMethod == NavAuthMethod.WebServiceAccessKey ? "Enter the web service access key." : "Enter the password.");

        s.Name = name;
        s.Platform = i.Platform;
        s.ServerHost = i.ServerHost?.Trim();
        s.ServiceInstance = i.ServiceInstance?.Trim();
        s.SoapBaseUrl = Url(i.SoapBaseUrl);
        s.ODataBaseUrl = Url(i.ODataBaseUrl);
        s.ApiBaseUrl = Url(i.ApiBaseUrl);
        s.TenantId = i.TenantId?.Trim();
        s.EnvironmentName = i.EnvironmentName?.Trim();
        s.AuthMethod = i.AuthMethod;
        s.UserName = string.IsNullOrWhiteSpace(i.UserName) ? null : i.UserName.Trim();
        s.Domain = string.IsNullOrWhiteSpace(i.Domain) ? null : i.Domain.Trim();
        s.ClientId = i.ClientId?.Trim();
        if (i.Password is not null) s.PasswordEncrypted = i.Password.Length == 0 ? null : _protector.Protect(i.Password);
        if (i.ClientSecret is not null) s.ClientSecretEncrypted = i.ClientSecret.Length == 0 ? null : _protector.Protect(i.ClientSecret);
        s.TimeoutSeconds = Math.Clamp(i.TimeoutSeconds, 5, 600);
        s.ValidateServerCertificate = i.ValidateServerCertificate;
        s.MaxConcurrency = Math.Clamp(i.MaxConcurrency, 1, 32);
        s.IsEnabled = i.IsEnabled;
        return null;
    }

    /// <summary>Default SOAP and OData addresses for an on-premises NAV server (ports 7047/7048, e.g. DynamicsNAV90 for NAV 2016).</summary>
    [HttpGet("suggest-urls")]
    public IActionResult SuggestUrls([FromQuery] NavPlatform platform, [FromQuery] string host, [FromQuery] string? instance, [FromQuery] bool https = false)
    {
        var s = NavVersionProfile.SuggestedUrls(platform, host ?? "", instance, https);
        return s is null ? Field("host", "Enter the NAV server name.")
            : Ok(new { soapBaseUrl = s.Value.Soap, oDataBaseUrl = s.Value.OData, serviceInstance = string.IsNullOrWhiteSpace(instance) ? NavVersionProfile.DefaultInstance(platform) : instance });
    }

    // ------------------------------------------------------------------ endpoint mappings
    public sealed record EndpointView(Guid Id, string LogicalName, string Protocol, string ServiceName, string? Method, string? ModifiedField, string? FieldMapJson, bool IsEnabled);

    [HttpGet("{id:guid}/endpoints")]
    public async Task<IActionResult> Endpoints(Guid id, CancellationToken ct)
    {
        if (!await _db.NavIntegrationSettings.AnyAsync(s => s.Id == id, ct)) return NotFound();
        var rows = await _db.NavEndpointMappings.AsNoTracking().Where(m => m.IntegrationSettingsId == id).OrderBy(m => m.LogicalName)
            .Select(m => new EndpointView(m.Id, m.LogicalName, m.Protocol, m.ServiceName, m.Method, m.ModifiedField, m.FieldMapJson, m.IsEnabled)).ToListAsync(ct);
        var known = NavEndpointPresets.StandardGateway.Select(p => p.LogicalName).ToList();
        return Ok(new { endpoints = rows, missing = known.Except(rows.Select(r => r.LogicalName)).ToList() });
    }

    /// <summary>Adds the standard ESS gateway mappings that are missing. Existing mappings are not changed.</summary>
    [HttpPost("{id:guid}/endpoints/preset")]
    public async Task<IActionResult> ApplyPreset(Guid id, CancellationToken ct)
    {
        var s = await _db.NavIntegrationSettings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound();
        var existing = (await _db.NavEndpointMappings.Where(m => m.IntegrationSettingsId == id).Select(m => m.LogicalName).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var p in NavEndpointPresets.StandardGateway.Where(p => !existing.Contains(p.LogicalName)))
        {
            _db.NavEndpointMappings.Add(new NavEndpointMapping
            {
                IntegrationSettingsId = id, LogicalName = p.LogicalName, Protocol = p.Protocol, ServiceName = p.ServiceName, Method = p.Method, ModifiedField = p.ModifiedField
            });
            added++;
        }
        s.ModifiedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("nav.endpoints.preset", nameof(NavIntegrationSettings), id.ToString(), new { added }, ct: ct);
        return Ok(new { added });
    }

    public sealed record EndpointInput(string Protocol, string ServiceName, string? Method, string? ModifiedField, string? FieldMapJson, bool IsEnabled = true);

    [HttpPut("{id:guid}/endpoints/{logicalName}")]
    public async Task<IActionResult> SaveEndpoint(Guid id, string logicalName, EndpointInput input, CancellationToken ct)
    {
        var s = await _db.NavIntegrationSettings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound();
        if (input.Protocol is not ("OData" or "Soap" or "BcApi")) return Field("protocol", "Choose OData, Soap or BcApi.");
        if (string.IsNullOrWhiteSpace(input.ServiceName) || input.ServiceName.Length > 200) return Field("serviceName", "Enter the published service name.");
        if (input.Protocol == "Soap" && string.IsNullOrWhiteSpace(input.Method)) return Field("method", "SOAP endpoints need the codeunit method name.");
        if (!string.IsNullOrWhiteSpace(input.FieldMapJson))
        {
            try { _ = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(input.FieldMapJson); }
            catch (System.Text.Json.JsonException) { return Field("fieldMapJson", "The field map must be a JSON object of portal field to NAV field, e.g. {\"ContractEndDate\": \"Contract_Expiry_Date\"}."); }
        }
        var m = await _db.NavEndpointMappings.FirstOrDefaultAsync(x => x.IntegrationSettingsId == id && x.LogicalName == logicalName, ct);
        if (m is null)
        {
            m = new NavEndpointMapping { IntegrationSettingsId = id, LogicalName = logicalName, Protocol = input.Protocol, ServiceName = input.ServiceName.Trim() };
            _db.NavEndpointMappings.Add(m);
        }
        m.Protocol = input.Protocol;
        m.ServiceName = input.ServiceName.Trim();
        m.Method = string.IsNullOrWhiteSpace(input.Method) ? null : input.Method.Trim();
        m.ModifiedField = string.IsNullOrWhiteSpace(input.ModifiedField) ? null : input.ModifiedField.Trim();
        m.FieldMapJson = string.IsNullOrWhiteSpace(input.FieldMapJson) ? null : input.FieldMapJson.Trim();
        m.IsEnabled = input.IsEnabled;
        s.ModifiedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("nav.endpoint.saved", nameof(NavEndpointMapping), m.Id.ToString(), new { logicalName, m.Protocol, m.ServiceName, m.Method, m.IsEnabled }, ct: ct);
        return Ok(new EndpointView(m.Id, m.LogicalName, m.Protocol, m.ServiceName, m.Method, m.ModifiedField, m.FieldMapJson, m.IsEnabled));
    }

    // ------------------------------------------------------------------ company link
    public sealed record CompanyInput(Guid? IntegrationSettingsId, string NavCompanyName, Guid? NavCompanyGuid);

    /// <summary>Points a portal company at a NAV instance and the NAV company name exactly as NAV shows it.</summary>
    [HttpPut("companies/{companyId:guid}")]
    public async Task<IActionResult> LinkCompany(Guid companyId, CompanyInput input, CancellationToken ct)
    {
        var c = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId, ct);
        if (c is null) return NotFound();
        if (string.IsNullOrWhiteSpace(input.NavCompanyName) || input.NavCompanyName.Length > 100) return Field("navCompanyName", "Enter the NAV company name exactly as NAV shows it.");
        if (input.IntegrationSettingsId is { } sid && !await _db.NavIntegrationSettings.AnyAsync(s => s.Id == sid, ct)) return Field("integrationSettingsId", "Unknown NAV instance.");
        c.IntegrationSettingsId = input.IntegrationSettingsId;
        c.NavCompanyName = input.NavCompanyName.Trim();
        c.NavCompanyGuid = input.NavCompanyGuid;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("nav.company.linked", "Company", c.Id.ToString(), new { c.IntegrationSettingsId, c.NavCompanyName }, ct: ct);
        return Ok(new CompanyLink(c.Id, c.Code, c.DisplayName, c.NavCompanyName, c.NavCompanyGuid, c.IntegrationSettingsId));
    }

    private IActionResult Field(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = new[] { message } }));
}
