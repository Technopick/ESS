using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ess.Domain.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Ess.Api.Tests;

/// <summary>
/// Phase 6 security tests: every endpoint is checked for authentication and permissions, then probed as an anonymous
/// caller, an ordinary employee and the administrator. A new endpoint without a permission fails these tests until it is
/// added to an allow list here with a reason.
/// </summary>
[Collection("api")]
public sealed class SecurityTests : ApiTestBase
{
    private readonly ITestOutputHelper _out;
    public SecurityTests(EssApiFactory f, ITestOutputHelper output) : base(f) => _out = output;

    /// <summary>The only routes reachable without signing in.</summary>
    private static readonly HashSet<string> AnonymousAllowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST api/auth/login", "POST api/auth/mfa", "POST api/auth/refresh", "POST api/auth/logout",
        "POST api/auth/forgot-password", "POST api/auth/reset-password",
        "GET health/live", "GET health/ready",
        // Name, colours and logo only: the sign-in page needs them before anyone is signed in.
        "GET api/branding", "GET api/branding/logo",
    };

    /// <summary>Signed-in routes that intentionally need no specific permission (each one scopes its data to the caller).</summary>
    private static readonly string[] SelfServicePrefixes =
    {
        "api/auth/", "api/account", "api/me", "api/notifications", "api/lookups/", "api/announcements", "api/approvals",
        "api/dashboard", "api/leave", "api/payslips", "api/expenses", "api/documents", "api/requests", "api/attendance", "api/training",
        "api/employees", "api/profile", "api/workflows/instances", "hubs/", "admin/jobs", "api/reports",
    };

    [SkippableFact]
    public async Task Staff_see_an_empty_report_catalogue()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        var cat = await baraka.GetFromJsonAsync<JsonElement>("/api/reports", Json);
        cat.GetProperty("reports").GetArrayLength().Should().Be(0);
        cat.GetProperty("canExport").GetBoolean().Should().BeFalse();
        foreach (var code in new[] { "employee-directory", "payroll-summary", "expense-claims", "pending-approvals", "leave-liability" })
        {
            // 404 (the report is not offered to this user) or 403; never data.
            (await baraka.GetAsync($"/api/reports/{code}")).StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
            (await baraka.GetAsync($"/api/reports/{code}/export?format=Csv")).StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        }
    }

    /// <summary>Administration areas: every route here must name a permission, not just "signed in".</summary>
    private static readonly string[] AdminPrefixes = { "api/admin/", "api/hr/", "api/reports", "api/management", "api/integration/" };

    /// <summary>Reviewed exceptions inside the administration areas.</summary>
    private static readonly HashSet<string> AdminExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET api/reports", // the catalogue lists only the reports the caller may run (empty for staff)
        "GET api/reports/{code}", // each report names its own permission (HR, finance, payroll, approvals); checked in ReportRunner
    };

    public sealed record Route(string Method, string Pattern, bool Anonymous, string? Policy);

    private IReadOnlyList<Route> Routes()
    {
        var sources = _f.Services.GetServices<EndpointDataSource>();
        var list = new List<Route>();
        foreach (var e in sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>())
        {
            var pattern = e.RoutePattern.RawText?.TrimStart('/') ?? "";
            if (pattern.Contains("{version")) continue;           // versioned duplicate of the same action
            if (pattern.StartsWith("{*path")) continue;            // SPA fallback (only when the frontend is deployed)
            var methods = e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" };
            var anonymous = e.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var policy = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).LastOrDefault(p => p is not null);
            foreach (var m in methods) list.Add(new Route(m, pattern, anonymous, policy));
        }
        return list.DistinctBy(r => (r.Method, r.Pattern)).OrderBy(r => r.Pattern).ThenBy(r => r.Method).ToList();
    }

    [SkippableFact]
    public void Every_endpoint_is_signed_in_and_admin_areas_need_a_permission()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var routes = Routes();
        routes.Should().HaveCountGreaterThan(100);
        foreach (var r in routes) _out.WriteLine($"{r.Method,-6} /{r.Pattern,-70} {(r.Anonymous ? "ANONYMOUS" : r.Policy ?? "signed in")}");

        routes.Where(r => r.Anonymous && !AnonymousAllowed.Contains($"{r.Method} {r.Pattern}"))
            .Select(r => $"{r.Method} {r.Pattern}").Should().BeEmpty("only sign-in, password reset, health checks and public branding may be anonymous");

        routes.Where(r => AdminPrefixes.Any(p => r.Pattern.StartsWith(p, StringComparison.OrdinalIgnoreCase)) && r.Policy?.StartsWith("perm:") != true
                          && !AdminExceptions.Contains($"{r.Method} {r.Pattern}"))
            .Select(r => $"{r.Method} {r.Pattern}").Should().BeEmpty("administration endpoints must require a named permission");

        routes.Where(r => !r.Anonymous && r.Policy is null && !SelfServicePrefixes.Any(p => r.Pattern.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .Select(r => $"{r.Method} {r.Pattern}").Should().BeEmpty("a signed-in-only endpoint outside self-service needs a review");
    }

    private static string Fill(string pattern) =>
        System.Text.RegularExpressions.Regex.Replace(pattern, @"\{([^}:]+)(:([^}]+))?\}", m =>
            m.Groups[3].Value.StartsWith("guid") ? Guid.NewGuid().ToString() : m.Groups[3].Value.StartsWith("int") ? "1" : "x");

    private static HttpRequestMessage Request(Route r)
    {
        var req = new HttpRequestMessage(new HttpMethod(r.Method), "/" + Fill(r.Pattern));
        if (r.Method is "POST" or "PUT" or "PATCH") req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return req;
    }

    [SkippableFact]
    public async Task Anonymous_callers_get_401_everywhere_except_sign_in_and_health()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var anon = _f.Anonymous();
        var leaks = new List<string>();
        foreach (var r in Routes().Where(r => !r.Anonymous && !r.Pattern.StartsWith("hubs/") && !r.Pattern.StartsWith("admin/jobs")))
        {
            using var res = await anon.SendAsync(Request(r));
            if (res.StatusCode != HttpStatusCode.Unauthorized) leaks.Add($"{r.Method} /{r.Pattern} -> {(int)res.StatusCode}");
        }
        leaks.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task An_employee_is_refused_every_permission_they_do_not_hold()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        var me = await baraka.GetFromJsonAsync<JsonElement>("/api/auth/me", Json);
        var held = me.GetProperty("permissions").EnumerateArray().Select(p => "perm:" + p.GetString()).ToHashSet();
        var leaks = new List<string>();
        var checkedCount = 0;
        foreach (var r in Routes().Where(r => r.Policy is { } p && p.StartsWith("perm:") && !held.Contains(p)))
        {
            using var res = await baraka.SendAsync(Request(r));
            checkedCount++;
            if (res.StatusCode != HttpStatusCode.Forbidden) leaks.Add($"{r.Method} /{r.Pattern} ({r.Policy}) -> {(int)res.StatusCode}");
        }
        _out.WriteLine($"{checkedCount} protected endpoints refused to an employee.");
        checkedCount.Should().BeGreaterThan(50);
        leaks.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task No_endpoint_fails_with_a_server_error_on_unknown_ids_or_empty_input()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        await UserAsync(Amani);
        await GrantRoleAsync(Amani, SystemRoles.SystemAdministrator);
        var admin = await UserAsync(Amani);
        var errors = new List<string>();
        foreach (var r in Routes().Where(r => !r.Pattern.StartsWith("hubs/") && !r.Pattern.StartsWith("admin/jobs") && r.Pattern != "api/auth/logout"))
        {
            using var res = await admin.SendAsync(Request(r));
            if ((int)res.StatusCode >= 500) errors.Add($"{r.Method} /{r.Pattern} -> {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}"[..Math.Min(300, 40 + r.Pattern.Length + 200)]);
        }
        errors.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Tampered_unsigned_and_foreign_tokens_are_rejected()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        var token = baraka.DefaultRequestHeaders.Authorization!.Parameter!;
        var parts = token.Split('.');

        string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string Json(string b64) => Encoding.UTF8.GetString(Convert.FromBase64String(b64.Replace('-', '+').Replace('_', '/').PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=')));
        var payload = Json(parts[1]);
        var elevated = payload.TrimEnd('}') + ",\"perm\":\"users.manage\"}";
        elevated.Should().NotBe(payload);

        var attempts = new Dictionary<string, string>
        {
            ["alg none"] = $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{parts[1]}.",
            ["changed payload, old signature"] = $"{parts[0]}.{B64(elevated)}.{parts[2]}",
            ["signature removed"] = $"{parts[0]}.{parts[1]}.",
            ["garbage"] = "not-a-token",
            ["signed with another key"] = SignHs256(parts[0], parts[1], "some-other-key-0123456789-0123456789-abcdef"),
        };
        foreach (var (name, t) in attempts)
        {
            var c = _f.Anonymous();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", t);
            (await c.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, name);
        }
    }

    private static string SignHs256(string header, string payload, string key)
    {
        using var h = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(key));
        var sig = Convert.ToBase64String(h.ComputeHash(Encoding.ASCII.GetBytes($"{header}.{payload}"))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{header}.{payload}.{sig}";
    }

    [SkippableFact]
    public async Task Responses_carry_security_headers_and_hide_the_server()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        using var res = await baraka.GetAsync("/api/auth/me");
        res.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        res.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        res.Headers.GetValues("Referrer-Policy").Should().Contain("no-referrer");
        res.Headers.Contains("Content-Security-Policy").Should().BeTrue();
        res.Headers.Contains("X-Powered-By").Should().BeFalse();
        res.Headers.CacheControl?.NoStore.Should().BeTrue("API responses with personal data must not be cached by browsers or proxies");
    }

    [SkippableFact]
    public async Task Injection_style_input_is_handled_as_data()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        await UserAsync(Amani);
        await GrantRoleAsync(Amani, SystemRoles.HrManager);
        var hr = await UserAsync(Amani);
        foreach (var probe in new[] { "' OR 1=1 --", "\"; DROP TABLE employees; --", "<script>alert(1)</script>", "%00", "../../etc/passwd", new string('a', 5000) })
        {
            var q = Uri.EscapeDataString(probe);
            foreach (var url in new[] { $"/api/hr/employees?search={q}", $"/api/employees/directory?search={q}", $"/api/hr/leave/applications?search={q}" })
            {
                using var res = await hr.GetAsync(url);
                ((int)res.StatusCode).Should().BeLessThan(500, url);
                if (res.IsSuccessStatusCode && probe.Contains("OR 1=1"))
                    (await res.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("items").GetArrayLength().Should().Be(0, url);
            }
        }
    }

    [SkippableFact]
    public async Task Lookups_only_show_the_callers_own_company()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        var other = await WithDbAsync(async db =>
        {
            var c = new Ess.Domain.Organization.Company { Code = "OTH" + Guid.NewGuid().ToString("N")[..4], DisplayName = "Other Company", NavCompanyName = "Other" };
            db.Companies.Add(c);
            db.Departments.Add(new Ess.Domain.Organization.Department { CompanyId = c.Id, NavKey = "SECRET", Name = "Secret Projects" });
            await db.SaveChangesAsync();
            return c.Id;
        });
        var companies = await baraka.GetFromJsonAsync<JsonElement>("/api/lookups/companies", Json);
        companies.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Should().NotContain(other);
        var depts = await baraka.GetFromJsonAsync<JsonElement>($"/api/lookups/departments?companyId={other}", Json);
        depts.GetArrayLength().Should().Be(0);
    }
}

/// <summary>One employee can never open, change or act on another employee's records by guessing or reusing an id.</summary>
[Collection("api")]
public sealed class DataIsolationTests : ApiTestBase
{
    public DataIsolationTests(EssApiFactory f) : base(f) { }

    [SkippableFact]
    public async Task Employees_cannot_reach_each_others_leave_claims_requests_or_approvals()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var chebet = await UserAsync(Chebet);
        var baraka = await UserAsync(Baraka);

        // Chebet's leave application (goes to her manager Amani for approval).
        var types = await chebet.GetFromJsonAsync<List<JsonElement>>("/api/leave/types", Json);
        var annual = types!.Single(t => t.GetProperty("code").GetString() == "ANNUAL").GetProperty("id").GetGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7 * 30);
        var leave = await chebet.PostAsJsonAsync("/api/leave", new { leaveTypeId = annual, startDate = start, endDate = start.AddDays(1), startHalfDay = false, endHalfDay = false, reason = "Isolation test" });
        leave.IsSuccessStatusCode.Should().BeTrue(await leave.Content.ReadAsStringAsync());
        var leaveBody = await leave.Content.ReadFromJsonAsync<JsonElement>(Json);
        var leaveId = leaveBody.GetProperty("id").GetGuid();
        var instanceId = leaveBody.TryGetProperty("workflowInstanceId", out var wi) && wi.ValueKind == JsonValueKind.String ? wi.GetGuid() : (Guid?)null;

        // Chebet's draft expense claim.
        var cats = await chebet.GetFromJsonAsync<List<JsonElement>>("/api/expenses/categories", Json);
        var comms = cats!.Single(c => c.GetProperty("code").GetString() == "COMMS").GetProperty("id").GetGuid();
        var claim = await chebet.PostAsJsonAsync("/api/expenses", new { purpose = "Isolation test", lines = new object[] { new { categoryId = comms, expenseDate = today.AddDays(-1), description = "Airtime", amount = 100m } } });
        claim.StatusCode.Should().Be(HttpStatusCode.Created);
        var claimId = (await claim.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        // Baraka (a colleague, not her manager, no HR role) tries everything with her ids.
        var attempts = new List<(string What, Task<HttpResponseMessage> Call)>
        {
            ("read leave", baraka.GetAsync($"/api/leave/{leaveId}")),
            ("edit leave", baraka.PutAsJsonAsync($"/api/leave/{leaveId}", new { leaveTypeId = annual, startDate = start, endDate = start, startHalfDay = false, endHalfDay = false, reason = "hijack" })),
            ("cancel leave", baraka.PostAsJsonAsync($"/api/leave/{leaveId}/cancel", new { reason = "hijack" })),
            ("read claim", baraka.GetAsync($"/api/expenses/{claimId}")),
            ("edit claim", baraka.PutAsJsonAsync($"/api/expenses/{claimId}", new { purpose = "hijack", lines = new object[] { new { categoryId = comms, expenseDate = today.AddDays(-1), description = "x", amount = 99999m } } })),
            ("submit claim", baraka.PostAsync($"/api/expenses/{claimId}/submit", null)),
            ("withdraw claim", baraka.PostAsync($"/api/expenses/{claimId}/withdraw", null)),
        };
        if (instanceId is { } inst) attempts.Add(("read approval history", baraka.GetAsync($"/api/approvals/instances/{inst}")));
        foreach (var (what, call) in attempts)
        {
            using var res = await call;
            ((int)res.StatusCode).Should().BeOneOf(new[] { 403, 404 }, $"Baraka must not be able to {what} of another employee");
        }

        // Nothing changed.
        var after = await chebet.GetFromJsonAsync<JsonElement>($"/api/expenses/{claimId}", Json);
        after.GetProperty("purpose").GetString().Should().Be("Isolation test");
        (await chebet.GetFromJsonAsync<JsonElement>($"/api/leave/{leaveId}", Json)).GetProperty("reason").GetString().Should().Be("Isolation test");

        // Approval tasks addressed to Amani cannot be acted on by Baraka or by Chebet herself.
        var amani = await UserAsync(Amani);
        var inbox = await amani.GetFromJsonAsync<List<JsonElement>>("/api/approvals/inbox", Json);
        var task = inbox!.FirstOrDefault(t => t.TryGetProperty("documentId", out var d) && d.ValueKind == JsonValueKind.String && d.GetGuid() == leaveId);
        if (task.ValueKind != JsonValueKind.Undefined)
        {
            var taskId = task.GetProperty("taskId").GetGuid();
            foreach (var c in new[] { baraka, chebet })
                ((int)(await c.PostAsJsonAsync($"/api/approvals/tasks/{taskId}/approve", new { comment = (string?)null })).StatusCode).Should().BeOneOf(403, 404);
        }
    }
}

[Collection("api")]
public sealed class ConcurrencyTests : ApiTestBase
{
    public ConcurrencyTests(EssApiFactory f) : base(f) { }

    /// <summary>A browser fires several requests at once (dashboard tiles, double clicks); none of them may fail with 500.</summary>
    [SkippableFact]
    public async Task Parallel_requests_from_one_user_do_not_fail()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        var calls = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 4; i++)
        {
            var id = Guid.NewGuid();
            calls.Add(baraka.PostAsJsonAsync($"/api/leave/{id}/cancel", new { reason = "x" }));
            calls.Add(baraka.PostAsync($"/api/expenses/{id}/submit", null));
            calls.Add(baraka.GetAsync("/api/dashboard/me"));
            calls.Add(baraka.GetAsync("/api/leave/balances"));
            calls.Add(baraka.GetAsync("/api/notifications"));
        }
        var results = await Task.WhenAll(calls);
        var failures = new List<string>();
        foreach (var r in results)
            if ((int)r.StatusCode >= 500) failures.Add($"{r.RequestMessage!.Method} {r.RequestMessage.RequestUri!.AbsolutePath} {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        failures.Should().BeEmpty();
    }
}
