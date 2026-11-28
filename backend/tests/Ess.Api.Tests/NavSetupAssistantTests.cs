using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ess.Domain.Identity;
using Ess.Domain.Organization;
using Ess.Integration.Nav.Setup;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Tests;

/// <summary>The NAV connection assistant against a fake NAV 2016 server (see FakeNavSetupHttp).</summary>
[Collection("api")]
public sealed class NavSetupAssistantTests : ApiTestBase
{
    public NavSetupAssistantTests(EssApiFactory f) : base(f) { }

    private static object Conn(string host = "navsrv", string auth = "NavUserPassword", string? user = "ESS-SVC", string? password = "Right-Pass-1") => new
    {
        platform = "Nav2016", serverHost = host, serviceInstance = "", https = false, authMethod = auth, userName = user, password
    };

    private async Task<HttpClient> IntegrationAdminAsync()
    {
        await UserAsync(Chebet);
        await GrantRoleAsync(Chebet, SystemRoles.IntegrationAdministrator);
        return await UserAsync(Chebet);
    }

    private static async Task<JsonElement> Post(HttpClient c, string url, object body)
    {
        var r = await c.PostAsJsonAsync(url, body, Json);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private static IEnumerable<JsonElement> Checks(JsonElement r) => r.GetProperty("checks").EnumerateArray();

    [SkippableFact]
    public async Task Connect_explains_each_failure_in_plain_words()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var admin = await IntegrationAdminAsync();
        (await (await UserAsync(Baraka)).PostAsJsonAsync("/api/admin/nav/setup/connect", new { connection = Conn() }, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var unknown = await Post(admin, "/api/admin/nav/setup/connect", new { connection = Conn(host: "nosuchhost") });
        unknown.GetProperty("ok").GetBoolean().Should().BeFalse();
        Checks(unknown).Last().GetProperty("message").GetString().Should().Contain("was not found");

        var wrongPassword = await Post(admin, "/api/admin/nav/setup/connect", new { connection = Conn(password: "nope") });
        Checks(wrongPassword).Last().GetProperty("message").GetString().Should().Contain("did not accept the user name or password");

        var windows = await Post(admin, "/api/admin/nav/setup/connect", new { connection = Conn(auth: "Windows", user: null, password: null) });
        Checks(windows).Last().GetProperty("message").GetString().Should().Contain("set up for user name and password");

        var good = await Post(admin, "/api/admin/nav/setup/connect", new { connection = Conn() });
        good.GetProperty("ok").GetBoolean().Should().BeTrue();
        good.GetProperty("addresses").GetProperty("oData").GetString().Should().Be("http://navsrv:7048/DynamicsNAV90/ODataV4");
        good.GetProperty("addresses").GetProperty("soap").GetString().Should().Be("http://navsrv:7047/DynamicsNAV90/WS");
        good.GetProperty("companies")[0].GetProperty("name").GetString().Should().Be(FakeNavSetupHttp.Company);
        good.GetRawText().Should().NotContain("Right-Pass-1");
    }

    [SkippableFact]
    public async Task Discover_suggests_services_fields_and_gateway_methods_then_apply_saves_everything()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var admin = await IntegrationAdminAsync();
        var company = FakeNavSetupHttp.Company;

        var found = await Post(admin, "/api/admin/nav/setup/discover", new { connection = Conn(), company });
        var features = found.GetProperty("features").EnumerateArray().ToDictionary(f => f.GetProperty("logicalName").GetString()!);
        features["Employee"].GetProperty("service").GetProperty("name").GetString().Should().Be("EmployeeCard");
        features["Employee"].GetProperty("status").GetString().Should().Be("suggested");
        features["LeaveType"].GetProperty("service").GetProperty("name").GetString().Should().Be("CausesofAbsence");
        features["LeaveBalance"].GetProperty("service").GetProperty("name").GetString().Should().Be("HRLeaveBalances");
        features["PayrollPeriod"].GetProperty("status").GetString().Should().Be("missing");
        var gateway = found.GetProperty("gateway");
        gateway.GetProperty("serviceName").GetString().Should().Be("ESSPortalGateway");
        var methods = gateway.GetProperty("methods").EnumerateArray().ToDictionary(m => m.GetProperty("method").GetString()!, m => m.GetProperty("available").GetBoolean());
        methods["SubmitLeaveApplication"].Should().BeTrue();
        methods["CancelLeaveApplication"].Should().BeFalse();

        var fields = await Post(admin, "/api/admin/nav/setup/fields",
            new { connection = Conn(), company, logicalName = "LeaveBalance", protocol = "OData", serviceName = "HRLeaveBalances" });
        fields.GetProperty("ok").GetBoolean().Should().BeTrue();
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(fields.GetProperty("fieldMapJson").GetString()!)!;
        map.Should().Contain(new KeyValuePair<string, string>("EmployeeNo", "Staff_No"))
            .And.Contain(new KeyValuePair<string, string>("LeaveTypeCode", "Leave_Code"))
            .And.Contain(new KeyValuePair<string, string>("PeriodCode", "Leave_Year"))
            .And.Contain(new KeyValuePair<string, string>("Available", "Remaining_Days"));
        map.Should().NotContainKey("Entitlement", "Entitlement already has the standard name");

        // Apply to a fresh portal company, so the simulator company used by other tests is untouched.
        var portalCompanyId = await WithDbAsync(async db =>
        {
            var c = new Company { Code = "T" + Guid.NewGuid().ToString("N")[..6], DisplayName = "Assistant test", NavCompanyName = "" };
            db.Companies.Add(c);
            await db.SaveChangesAsync();
            return c.Id;
        });
        var name = "Assistant-" + Guid.NewGuid().ToString("N")[..6];
        var mappings = new object[]
        {
            new { logicalName = "Employee", protocol = "OData", serviceName = "EmployeeCard", modifiedField = "Last_Date_Modified", isEnabled = true },
            new { logicalName = "LeaveBalance", protocol = "OData", serviceName = "HRLeaveBalances", fieldMapJson = fields.GetProperty("fieldMapJson").GetString(), isEnabled = true },
            new { logicalName = "PayrollPeriod", protocol = "OData", serviceName = "", isEnabled = false },
            new { logicalName = "Leave.Submit", protocol = "Soap", serviceName = "ESSPortalGateway", method = "SubmitLeaveApplication", isEnabled = true },
            new { logicalName = "Leave.Cancel", protocol = "Soap", serviceName = "ESSPortalGateway", method = "CancelLeaveApplication", isEnabled = false },
        };
        var noEmployee = await admin.PostAsJsonAsync("/api/admin/nav/setup/apply",
            new { connection = Conn(), name, portalCompanyId, navCompany = company, mappings = mappings.Skip(1).ToArray(), runFirstSync = false }, Json);
        noEmployee.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var applied = await Post(admin, "/api/admin/nav/setup/apply",
            new { connection = Conn(), name, portalCompanyId, navCompany = company, mappings, runFirstSync = true });
        applied.GetRawText().Should().NotContain("Right-Pass-1");
        var id = applied.GetProperty("settingsId").GetGuid();
        applied.GetProperty("mappingsEnabled").GetInt32().Should().Be(3);
        applied.GetProperty("syncJobId").GetString().Should().NotBeNullOrEmpty();

        var s = await WithDbAsync(db => db.NavIntegrationSettings.AsNoTracking().SingleAsync(x => x.Id == id));
        s.ODataBaseUrl.Should().Be("http://navsrv:7048/DynamicsNAV90/ODataV4");
        s.PasswordEncrypted.Should().NotBeNullOrEmpty().And.NotContain("Right-Pass-1");
        var rows = await WithDbAsync(db => db.NavEndpointMappings.AsNoTracking().Where(m => m.IntegrationSettingsId == id).ToListAsync());
        rows.Select(r => r.LogicalName).Should().BeEquivalentTo("Employee", "LeaveBalance", "Leave.Submit", "Leave.Cancel");
        rows.Single(r => r.LogicalName == "Leave.Cancel").IsEnabled.Should().BeFalse();
        var linked = await WithDbAsync(db => db.Companies.AsNoTracking().SingleAsync(c => c.Id == portalCompanyId));
        linked.IntegrationSettingsId.Should().Be(id);
        linked.NavCompanyName.Should().Be(company);

        // Editing later: the assistant can leave the password empty and the stored one is used.
        var again = await Post(admin, "/api/admin/nav/setup/connect", new { connection = Conn(password: null), settingsId = id });
        again.GetProperty("ok").GetBoolean().Should().BeTrue();
        var start = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/nav/setup/start?settingsId={id}", Json);
        start.GetProperty("saved").GetProperty("hasPassword").GetBoolean().Should().BeTrue();
        start.GetRawText().Should().NotContain("Right-Pass-1");
    }

    [Fact]
    public void Field_matching_prefers_standard_names_then_spellings()
    {
        var defaults = new Dictionary<string, string> { ["No"] = "No", ["ManagerNo"] = "Manager_No", ["CompanyEmail"] = "Company_E_Mail", ["ContractEndDate"] = "Contract_End_Date" };
        var r = NavSetupAssistant.MatchFields(defaults, new[] { "No", "Supervisor_No", "E_Mail", "Contract_Expiry_Date" }, new[] { "No" });
        r.Single(x => x.EssField == "No").Source.Should().Be("default");
        r.Single(x => x.EssField == "ManagerNo").NavField.Should().Be("Supervisor_No");
        r.Single(x => x.EssField == "CompanyEmail").NavField.Should().Be("E_Mail");
        r.Single(x => x.EssField == "ContractEndDate").NavField.Should().Be("Contract_Expiry_Date");
        NavSetupAssistant.BuildFieldMap(r).Should().Be("{\"ManagerNo\":\"Supervisor_No\",\"CompanyEmail\":\"E_Mail\",\"ContractEndDate\":\"Contract_Expiry_Date\"}");
    }
}
