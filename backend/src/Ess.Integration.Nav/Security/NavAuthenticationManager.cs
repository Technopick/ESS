using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Ess.Domain.Integration;
using Ess.Integration.Nav.Configuration;
using Microsoft.Extensions.Logging;

namespace Ess.Integration.Nav.Security;

/// <summary>
/// NAV Authentication Manager (spec §3, component 2). Produces an authenticated HttpClient per NAV instance:
/// Windows (NTLM/Negotiate), NavUserPassword / web service access key (Basic), or OAuth 2.0 client credentials
/// (Microsoft Entra ID) with token caching. Clients are cached per settings version to avoid socket exhaustion.
/// </summary>
public sealed class NavAuthenticationManager : IDisposable
{
    private readonly ConcurrentDictionary<string, HttpClient> _clients = new();
    private readonly ConcurrentDictionary<Guid, (string Token, DateTime ExpiresUtc)> _tokens = new();
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<NavAuthenticationManager> _log;

    private readonly Simulator.NavSimulatorHandler? _simulator;

    public NavAuthenticationManager(IHttpClientFactory factory, ILogger<NavAuthenticationManager> log, Simulator.NavSimulatorHandler? simulator = null)
    {
        _factory = factory;
        _log = log;
        _simulator = simulator;
    }

    public HttpClient GetClient(NavConnectionContext ctx)
    {
        var s = ctx.Settings;
        var key = $"{s.Id}:{s.ModifiedAtUtc?.Ticks ?? s.CreatedAtUtc.Ticks}";
        return _clients.GetOrAdd(key, _ =>
        {
            if (s.UseSimulator)
            {
                // NavConnectionManager has already refused the simulator outside allowed environments.
                if (_simulator is null) throw new NavConfigurationException("The NAV simulator is not registered in this environment.");
                return new HttpClient(_simulator, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) };
            }
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = Math.Max(1, s.MaxConcurrency),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PreAuthenticate = true
            };
            if (!s.ValidateServerCertificate)
            {
                // Only for self-signed certificates on internal NAV servers; the admin screen warns about it.
                handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                _log.LogWarning("TLS certificate validation is disabled for NAV instance {Instance}", s.Name);
            }
            if (s.AuthMethod == NavAuthMethod.Windows)
            {
                var cred = new NetworkCredential(s.UserName, ctx.Password, s.Domain);
                var cache = new CredentialCache();
                foreach (var baseUrl in new[] { s.SoapBaseUrl, s.ODataBaseUrl, s.ApiBaseUrl }.Where(u => !string.IsNullOrWhiteSpace(u)))
                {
                    var uri = new Uri(baseUrl!);
                    var root = new Uri(uri.GetLeftPart(UriPartial.Authority));
                    cache.Add(root, "Negotiate", cred);
                    cache.Add(root, "NTLM", cred);
                }
                handler.Credentials = string.IsNullOrEmpty(s.UserName) ? CredentialCache.DefaultNetworkCredentials : cache;
            }
            return new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(Math.Clamp(s.TimeoutSeconds, 5, 600)) };
        });
    }

    /// <summary>Adds the Authorization header (Basic or Bearer) to a request. Windows auth is handled by the handler.</summary>
    public async Task AuthorizeAsync(HttpRequestMessage request, NavConnectionContext ctx, CancellationToken ct)
    {
        var s = ctx.Settings;
        switch (s.AuthMethod)
        {
            case NavAuthMethod.NavUserPassword:
            case NavAuthMethod.WebServiceAccessKey:
                var raw = Encoding.UTF8.GetBytes($"{s.UserName}:{ctx.Password}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
                break;
            case NavAuthMethod.OAuthClientCredentials:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(ctx, ct));
                break;
        }
    }

    public void InvalidateToken(Guid settingsId) => _tokens.TryRemove(settingsId, out _);

    private async Task<string> GetTokenAsync(NavConnectionContext ctx, CancellationToken ct)
    {
        var s = ctx.Settings;
        if (_tokens.TryGetValue(s.Id, out var t) && t.ExpiresUtc > DateTime.UtcNow.AddMinutes(2)) return t.Token;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_tokens.TryGetValue(s.Id, out t) && t.ExpiresUtc > DateTime.UtcNow.AddMinutes(2)) return t.Token;
            if (string.IsNullOrWhiteSpace(s.TenantId) || string.IsNullOrWhiteSpace(s.ClientId) || string.IsNullOrEmpty(ctx.ClientSecret))
                throw new NavConfigurationException("OAuth requires Tenant ID, Client ID and Client Secret.");

            using var http = _factory.CreateClient("nav-oauth");
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = s.ClientId!,
                ["client_secret"] = ctx.ClientSecret!,
                ["scope"] = "https://api.businesscentral.dynamics.com/.default"
            });
            using var resp = await http.PostAsync($"https://login.microsoftonline.com/{Uri.EscapeDataString(s.TenantId!)}/oauth2/v2.0/token", form, ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Entra ID token request failed with {(int)resp.StatusCode}.", null, resp.StatusCode == HttpStatusCode.BadRequest ? HttpStatusCode.Unauthorized : resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                       ?? throw new HttpRequestException("Empty token response.");
            _tokens[s.Id] = (body.access_token, DateTime.UtcNow.AddSeconds(body.expires_in));
            return body.access_token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private sealed record TokenResponse(string access_token, int expires_in);

    public void Dispose()
    {
        foreach (var c in _clients.Values) c.Dispose();
        _tokenLock.Dispose();
    }
}
