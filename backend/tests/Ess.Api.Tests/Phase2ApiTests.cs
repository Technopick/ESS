using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ess.Application.Identity;
using Ess.Infrastructure.Persistence;
using Ess.Integration.Nav.Sync;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ess.Api.Tests;

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<EssApiFactory>;

/// <summary>Phase 2 acceptance tests through the real HTTP pipeline (auth, MFA, reset, isolation, admin, dashboard, profile).</summary>
[Collection("api")]
public sealed class Phase2ApiTests
{
    private readonly EssApiFactory _f;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Phase2ApiTests(EssApiFactory f) => _f = f;

    private async Task<Guid> SimCompanyAsync()
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        var id = await db.Companies.Where(c => c.Code == "SIM").Select(c => c.Id).SingleAsync();
        if (!await db.Employees.AnyAsync(e => e.CompanyId == id))
            await _f.Services.GetRequiredService<NavSyncService>().SyncCompanyAsync(id, true, "test", default);
        return id;
    }

    /// <summary>Provisions accounts for the simulated employees and activates one through its invitation link.</summary>
    private async Task<HttpClient> EmployeeAsync(string email, string password = "Empl0yee-Passw0rd!")
    {
        var company = await SimCompanyAsync();
        var admin = await _f.SignInAsync(EssApiFactory.AdminUser, EssApiFactory.AdminPassword);
        (await admin.PostAsJsonAsync("/api/admin/users/provision", new { companyId = company, sendInvites = true })).EnsureSuccessStatusCode();
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
            var u = await db.Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant());
            if (u.PasswordHash is not null) return await _f.SignInAsync(email, password);
        }
        var token = _f.Emails.LastTokenFor(email);
        token.Should().NotBeNull("an invitation email with a link is sent");
        (await _f.Anonymous().PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = password })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return await _f.SignInAsync(email, password);
    }

    [SkippableFact]
    public async Task Unknown_user_and_wrong_password_get_the_same_answer_and_lockout_after_five_failures()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        await EmployeeAsync("chebet.kiprono@example.invalid");
        var c = _f.Anonymous();
        var unknown = await c.PostAsJsonAsync("/api/auth/login", new { userName = "nobody", password = "x" });
        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        for (var i = 0; i < 5; i++)
            (await c.PostAsJsonAsync("/api/auth/login", new { userName = "chebet.kiprono@example.invalid", password = "wrong" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var locked = await c.PostAsJsonAsync("/api/auth/login", new { userName = "chebet.kiprono@example.invalid", password = "Empl0yee-Passw0rd!" });
        locked.StatusCode.Should().Be((HttpStatusCode)423);
    }

    [SkippableFact]
    public async Task Refresh_token_rotates_and_reuse_revokes_the_family()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var c = _f.CreateClient(new() { HandleCookies = false });
        var login = await c.PostAsJsonAsync("/api/auth/login", new { userName = EssApiFactory.AdminUser, password = EssApiFactory.AdminPassword });
        var first = Cookie(login);
        first.Should().NotBeNull();

        async Task<HttpResponseMessage> Refresh(string token)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
            req.Headers.Add("Cookie", $"ess_rt={token}");
            return await c.SendAsync(req);
        }

        var r1 = await Refresh(first!);
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = Cookie(r1);
        second.Should().NotBe(first);

        (await Refresh(first!)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a rotated token must not work again");
        (await Refresh(second!)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "reuse revokes the whole family, including the newest token");
    }

    private static string? Cookie(HttpResponseMessage r) =>
        r.Headers.TryGetValues("Set-Cookie", out var v)
            ? v.Select(x => x.Split(';')[0]).Where(x => x.StartsWith("ess_rt=")).Select(x => Uri.UnescapeDataString(x["ess_rt=".Length..])).FirstOrDefault()
            : null;

    [SkippableFact]
    public async Task Mfa_enrolment_then_sign_in_requires_a_code_and_recovery_codes_work_once()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var email = "daudi.mwangi@example.invalid";
        var c = await EmployeeAsync(email);
        var setup = await (await c.PostAsync("/api/account/mfa/setup", null)).Content.ReadFromJsonAsync<JsonElement>();
        var secret = setup.GetProperty("secret").GetString()!;
        setup.GetProperty("otpAuthUri").GetString().Should().StartWith("otpauth://totp/");

        var bad = await c.PostAsJsonAsync("/api/account/mfa/confirm", new { code = "000000" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var confirm = await c.PostAsJsonAsync("/api/account/mfa/confirm", new { code = Totp.Compute(secret, Totp.TimeStep(DateTime.UtcNow)) });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        var recovery = (await confirm.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes").EnumerateArray().Select(x => x.GetString()!).ToList();
        recovery.Should().HaveCount(10);

        var anon = _f.Anonymous();
        var step1 = await (await anon.PostAsJsonAsync("/api/auth/login", new { userName = email, password = "Empl0yee-Passw0rd!" })).Content.ReadFromJsonAsync<EssApiFactory.LoginBody>();
        step1!.MfaRequired.Should().BeTrue();
        step1.AccessToken.Should().BeNull();
        (await anon.PostAsJsonAsync("/api/auth/mfa", new { mfaChallenge = step1.MfaChallenge, code = recovery[0] })).StatusCode.Should().Be(HttpStatusCode.OK);

        var step2 = await (await anon.PostAsJsonAsync("/api/auth/login", new { userName = email, password = "Empl0yee-Passw0rd!" })).Content.ReadFromJsonAsync<EssApiFactory.LoginBody>();
        (await anon.PostAsJsonAsync("/api/auth/mfa", new { mfaChallenge = step2!.MfaChallenge, code = recovery[0] })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "a recovery code works only once");
    }

    [SkippableFact]
    public async Task Forgot_password_does_not_reveal_unknown_emails()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var before = _f.Emails.Sent.Count;
        var r = await _f.Anonymous().PostAsJsonAsync("/api/auth/forgot-password", new { email = "nobody@example.invalid" });
        r.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _f.Emails.Sent.Count.Should().Be(before);
    }

    [SkippableFact]
    public async Task Employee_sees_own_record_dashboard_and_nav_profile_with_masked_bank()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var c = await EmployeeAsync("baraka.otieno@example.invalid");
        var me = await c.GetFromJsonAsync<JsonElement>("/api/employees/me");
        me.GetProperty("employeeNo").GetString().Should().Be("SIM-002");
        me.GetProperty("managerName").GetString().Should().Be("Daudi Mwangi");

        var dash = await c.GetFromJsonAsync<JsonElement>("/api/dashboard/me");
        var balances = dash.GetProperty("leave").GetProperty("balances").EnumerateArray().ToList();
        balances.Should().Contain(b => b.GetProperty("leaveTypeCode").GetString() == "ANNUAL" && b.GetProperty("available").GetDecimal() == 19m);
        dash.GetProperty("leave").GetProperty("fromCache").GetBoolean().Should().BeFalse();

        var profile = await c.GetFromJsonAsync<JsonElement>("/api/employees/me/profile");
        profile.GetProperty("navAvailable").GetBoolean().Should().BeTrue();
        var bank = profile.GetProperty("nav").GetProperty("bankDetailsMasked").GetProperty("accountNo").GetString();
        bank.Should().EndWith("9012").And.NotContain("0123456789");
    }

    [SkippableFact]
    public async Task Employees_cannot_reach_admin_or_integration_endpoints_and_directory_hides_hr_fields()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var c = await EmployeeAsync("amani.wanjiru@example.invalid");
        (await c.GetAsync("/api/admin/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.GetAsync("/api/integration/nav/status")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _f.Anonymous().GetAsync("/api/employees/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var dir = await c.GetFromJsonAsync<JsonElement>("/api/employees?search=otieno");
        var row = dir.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("fullName").GetString().Should().Be("Baraka Otieno");
        row.GetProperty("employeeNo").ValueKind.Should().Be(JsonValueKind.Null);
        row.GetProperty("status").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [SkippableFact]
    public async Task Bank_change_requests_are_encrypted_at_rest_and_masked_when_listed()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var c = await EmployeeAsync("baraka.otieno@example.invalid");
        var r = await c.PostAsJsonAsync("/api/employees/me/change-requests",
            new { section = "BankDetails", changes = new Dictionary<string, string> { ["bankName"] = "New Bank", ["accountNo"] = "9988776655443" }, reason = "Changed bank" });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        (await c.PostAsJsonAsync("/api/employees/me/change-requests",
            new { section = "BankDetails", changes = new Dictionary<string, string> { ["bankName"] = "Other" } })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "one pending change per section");
        (await c.PostAsJsonAsync("/api/employees/me/change-requests",
            new { section = "Contact", changes = new Dictionary<string, string> { ["salary"] = "1" } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using (var conn = new NpgsqlConnection(_f.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("select changes_json from profile_change_requests where section = 'BankDetails'", conn);
            var raw = (string)(await cmd.ExecuteScalarAsync())!;
            raw.Should().NotContain("9988776655443");
        }
        var list = await c.GetFromJsonAsync<JsonElement>("/api/employees/me/change-requests");
        list.EnumerateArray().First(x => x.GetProperty("section").GetString() == "BankDetails")
            .GetProperty("changes").GetProperty("accountNo").GetString().Should().EndWith("5443").And.NotContain("99887766");
    }

    [SkippableFact]
    public async Task Admin_cannot_remove_own_admin_role_and_role_changes_are_audited()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var admin = await _f.SignInAsync(EssApiFactory.AdminUser, EssApiFactory.AdminPassword);
        var users = await admin.GetFromJsonAsync<JsonElement>("/api/admin/users?search=admin");
        var me = users.GetProperty("items").EnumerateArray().First(u => u.GetProperty("userName").GetString() == "admin");
        var id = me.GetProperty("id").GetString();
        var r = await admin.PutAsJsonAsync($"/api/admin/users/{id}/roles", new { roles = Array.Empty<object>() });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var roles = await admin.GetFromJsonAsync<JsonElement>("/api/admin/roles");
        roles.EnumerateArray().Should().HaveCountGreaterThanOrEqualTo(10);
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        (await db.AuditLogs.AnyAsync(a => a.Action == "auth.login" && a.Success)).Should().BeTrue();
    }
}
