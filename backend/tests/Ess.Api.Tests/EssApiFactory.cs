using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Ess.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Ess.Api.Tests;

/// <summary>Boots the real API (Testing environment) on a throw-away PostgreSQL database with the NAV simulator.</summary>
public sealed class EssApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly string? BaseCs = Environment.GetEnvironmentVariable("ESS_TEST_DB");
    public const string AdminUser = "admin";
    public const string AdminPassword = "Adm1n-Test-Passw0rd!";
    private readonly string _dbName = "ess_api_test_" + Guid.NewGuid().ToString("N")[..8];
    public CapturingEmailSender Emails { get; } = new();
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "ess-test-files-" + Guid.NewGuid().ToString("N")[..8]);
    public string ConnectionString => new NpgsqlConnectionStringBuilder(BaseCs) { Database = _dbName }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting (not ConfigureAppConfiguration) so values are visible while Program.cs is still configuring services.
        foreach (var (k, v) in new Dictionary<string, string?>
        {
            ["ConnectionStrings:Ess"] = ConnectionString,
            ["Database:MigrateOnStartup"] = "true",
            ["DemoData:Enabled"] = "true",
            ["NavIntegration:AllowSimulator"] = "true",
            ["Hosting:RunBackgroundWorkers"] = "false",
            ["Jwt:SigningKey"] = "test-signing-key-0123456789-0123456789-abcdef",
            ["Bootstrap:AdminUserName"] = AdminUser,
            ["Bootstrap:AdminPassword"] = AdminPassword,
            ["Bootstrap:AdminEmail"] = "admin@example.invalid",
            ["Security:EnforceRoleMfa"] = "false",
            ["App:PublicUrl"] = "http://ess.test",
            ["RateLimiting:AuthPerMinute"] = "1000",
            ["RateLimiting:PerUserPerMinute"] = "10000",
            ["Storage:RootPath"] = StorageRoot,
        })
            builder.UseSetting(k, v);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IEmailSender>();
            s.AddSingleton<IEmailSender>(Emails);
        });
    }

    public async Task InitializeAsync()
    {
        if (BaseCs is null) return;
        await using var conn = new NpgsqlConnection(BaseCs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {_dbName}", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, true);
        if (BaseCs is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(BaseCs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_dbName} WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public HttpClient Anonymous() => CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("http://localhost") });

    public async Task<HttpClient> SignInAsync(string user, string password)
    {
        var c = Anonymous();
        var r = await c.PostAsJsonAsync("/api/auth/login", new { userName = user, password });
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadFromJsonAsync<LoginBody>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return c;
    }

    public sealed record LoginBody(string? AccessToken, bool MfaRequired, string? MfaChallenge);
}

public sealed class CapturingEmailSender : IEmailSender
{
    public ConcurrentBag<EmailMessage> Sent { get; } = new();
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }

    public string? LastTokenFor(string email) =>
        Sent.Where(m => m.To.Equals(email, StringComparison.OrdinalIgnoreCase)).Select(m => Regex.Match(m.TextBody ?? "", @"token=([A-Za-z0-9_\-]+)"))
            .Where(m => m.Success).Select(m => m.Groups[1].Value).LastOrDefault();
}
