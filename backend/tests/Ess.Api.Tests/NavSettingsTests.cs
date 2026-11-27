using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ess.Domain.Identity;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Tests;

/// <summary>Setting up a real NAV 2016 connection from the portal.</summary>
[Collection("api")]
public sealed class NavSettingsTests : ApiTestBase
{
    public NavSettingsTests(EssApiFactory f) : base(f) { }

    [SkippableFact]
    public async Task Integration_admin_sets_up_a_NAV_2016_instance_without_exposing_the_password()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        await UserAsync(Chebet);
        await GrantRoleAsync(Chebet, SystemRoles.IntegrationAdministrator);
        var admin = await UserAsync(Chebet);
        var staff = await UserAsync(Baraka);
        (await staff.GetAsync("/api/admin/nav")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var urls = await admin.GetFromJsonAsync<JsonElement>("/api/admin/nav/suggest-urls?platform=Nav2016&host=navsrv01", Json);
        urls.GetProperty("soapBaseUrl").GetString().Should().Be("http://navsrv01:7047/DynamicsNAV90/WS");

        var name = "NAV2016-" + Guid.NewGuid().ToString("N")[..6];
        var body = new
        {
            name, platform = "Nav2016", serverHost = "navsrv01", serviceInstance = "DynamicsNAV90",
            soapBaseUrl = urls.GetProperty("soapBaseUrl").GetString(), oDataBaseUrl = urls.GetProperty("oDataBaseUrl").GetString(),
            authMethod = "OAuthClientCredentials", userName = "ESS-SVC", password = "Secret-Key-123"
        };
        var refused = await admin.PostAsJsonAsync("/api/admin/nav", body, Json);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, "NAV 2016 has no OAuth");

        var created = await admin.PostAsJsonAsync("/api/admin/nav", body with { authMethod = "NavUserPassword" }, Json);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var text = await created.Content.ReadAsStringAsync();
        text.Should().NotContain("Secret-Key-123");
        var view = JsonDocument.Parse(text).RootElement;
        view.GetProperty("hasPassword").GetBoolean().Should().BeTrue();
        var id = view.GetProperty("id").GetGuid();

        var stored = await WithDbAsync(db => db.NavIntegrationSettings.AsNoTracking().SingleAsync(s => s.Id == id));
        stored.PasswordEncrypted.Should().NotBeNullOrEmpty().And.NotContain("Secret-Key-123");

        // Updating without a password keeps the stored one.
        var updated = await admin.PutAsJsonAsync($"/api/admin/nav/{id}", body with { authMethod = "NavUserPassword", password = (string?)null, serviceInstance = "ESS" }, Json);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await WithDbAsync(db => db.NavIntegrationSettings.AsNoTracking().SingleAsync(s => s.Id == id))).PasswordEncrypted.Should().Be(stored.PasswordEncrypted);

        var preset = await (await admin.PostAsync($"/api/admin/nav/{id}/endpoints/preset", null)).Content.ReadFromJsonAsync<JsonElement>(Json);
        preset.GetProperty("added").GetInt32().Should().Be(17);
        var again = await (await admin.PostAsync($"/api/admin/nav/{id}/endpoints/preset", null)).Content.ReadFromJsonAsync<JsonElement>(Json);
        again.GetProperty("added").GetInt32().Should().Be(0);

        var badMap = await admin.PutAsJsonAsync($"/api/admin/nav/{id}/endpoints/Employee", new { protocol = "OData", serviceName = "ESSEmployees", fieldMapJson = "not json" }, Json);
        badMap.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var map = await admin.PutAsJsonAsync($"/api/admin/nav/{id}/endpoints/Employee",
            new { protocol = "OData", serviceName = "ESSEmployees", modifiedField = "Last_Date_Modified", fieldMapJson = """{"ContractEndDate":"Contract_Expiry_Date"}""" }, Json);
        map.StatusCode.Should().Be(HttpStatusCode.OK);

        var audit = await WithDbAsync(db => db.AuditLogs.CountAsync(a => a.EntityId == id.ToString() && a.Action.StartsWith("nav.")));
        audit.Should().BeGreaterThanOrEqualTo(3);
        var auditText = await WithDbAsync(db => db.AuditLogs.Where(a => a.EntityId == id.ToString()).Select(a => a.ChangesJson).ToListAsync());
        string.Join("", auditText).Should().NotContain("Secret-Key-123");
    }
}
