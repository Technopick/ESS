using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ess.Domain.Identity;
using FluentAssertions;

namespace Ess.Api.Tests;

/// <summary>HR requests, attendance and training through the approval engine.</summary>
[Collection("api")]
public sealed class Phase3ServiceRequestTests : ApiTestBase
{
    public Phase3ServiceRequestTests(EssApiFactory f) : base(f) { }

    private static async Task ApproveAsync(HttpClient c, Guid instanceId)
    {
        var task = (await c.GetFromJsonAsync<List<JsonElement>>("/api/approvals/inbox", Json))!.Single(t => t.GetProperty("instanceId").GetGuid() == instanceId);
        (await c.PostAsJsonAsync($"/api/approvals/tasks/{task.GetProperty("taskId")}/approve", new { comment = "OK" })).EnsureSuccessStatusCode();
    }

    [SkippableFact]
    public async Task Employment_letter_is_approved_by_HR_then_resolved()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        await UserAsync(Amani);
        await GrantRoleAsync(Amani, SystemRoles.HrOfficer);
        var hr = await UserAsync(Amani);

        var types = (await baraka.GetFromJsonAsync<List<JsonElement>>("/api/requests/types", Json))!;
        var letter = types.Single(t => t.GetProperty("code").GetString() == "EMPLOYMENT_LETTER");
        var bad = await baraka.PostAsJsonAsync("/api/requests", new { typeId = letter.GetProperty("id"), subject = "Visa", fields = new Dictionary<string, string> { ["includeSalary"] = "Maybe" } });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest, "required fields and allowed options are enforced");

        var created = await baraka.PostAsJsonAsync("/api/requests", new
        {
            typeId = letter.GetProperty("id"), subject = "Schengen visa",
            fields = new Dictionary<string, string> { ["addressedTo"] = "Embassy of Germany", ["purpose"] = "Visa application", ["includeSalary"] = "No", ["unexpected"] = "dropped" }
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var req = await created.Content.ReadFromJsonAsync<JsonElement>(Json);
        req.GetProperty("status").GetString().Should().Be("InApproval");
        req.GetProperty("fields").TryGetProperty("unexpected", out _).Should().BeFalse();

        await ApproveAsync(hr, req.GetProperty("workflowInstanceId").GetGuid());
        (await hr.PostAsJsonAsync($"/api/requests/{req.GetProperty("id")}/resolve", new { resolution = "Letter signed and ready for collection at reception" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await baraka.GetFromJsonAsync<JsonElement>($"/api/requests/{req.GetProperty("id")}", Json)).GetProperty("status").GetString().Should().Be("Completed");
    }

    [SkippableFact]
    public async Task Clocking_and_a_missed_punch_correction_approved_by_the_manager()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        var daudi = await UserAsync(Daudi);

        (await baraka.PostAsJsonAsync("/api/attendance/clock", new { action = "in" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await baraka.PostAsJsonAsync("/api/attendance/clock", new { action = "in" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var outR = await baraka.PostAsJsonAsync("/api/attendance/clock", new { action = "out" });
        outR.StatusCode.Should().Be(HttpStatusCode.OK);
        (await outR.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("clockOutUtc").ValueKind.Should().NotBe(JsonValueKind.Null);

        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
        var r = await baraka.PostAsJsonAsync("/api/attendance/requests", new { type = "MissingPunch", workDate = day, clockIn = "08:05:00", clockOut = "17:10:00", reason = "Forgot to clock, was at a client site" });
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        var req = await r.Content.ReadFromJsonAsync<JsonElement>(Json);
        await ApproveAsync(daudi, req.GetProperty("workflowInstanceId").GetGuid());

        var history = (await baraka.GetFromJsonAsync<List<JsonElement>>($"/api/attendance/mine?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}", Json))!;
        history.Should().ContainSingle().Which.GetProperty("workedHours").GetDecimal().Should().Be(9.08m);
    }

    [SkippableFact]
    public async Task Training_application_needs_manager_and_HR_approval_and_respects_capacity()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        var daudi = await UserAsync(Daudi);
        var chebet = await UserAsync(Chebet);
        await UserAsync(Amani);
        await GrantRoleAsync(Amani, SystemRoles.HrOfficer);
        var hr = await UserAsync(Amani);

        var code = "IFRS-" + Guid.NewGuid().ToString("N")[..6];
        var created = await hr.PostAsJsonAsync("/api/training/programs", new
        {
            code, title = "IFRS 17 essentials", provider = "ICPAK", startsAtUtc = DateTime.UtcNow.AddDays(30), endsAtUtc = DateTime.UtcNow.AddDays(31),
            venue = "Nairobi", capacity = 1, costPerParticipant = 25000m, requiresApproval = true, isPublished = true
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var programId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        (await baraka.PostAsync($"/api/training/programs/{programId}/apply", null)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await chebet.PostAsync($"/api/training/programs/{programId}/apply", null)).StatusCode.Should().Be(HttpStatusCode.Conflict, "the only place is taken");

        var mine = (await baraka.GetFromJsonAsync<List<JsonElement>>("/api/training/mine", Json))!.Single(t => t.GetProperty("programId").GetGuid() == programId);
        var instance = mine.GetProperty("workflowInstanceId").GetGuid();
        await ApproveAsync(daudi, instance);
        await ApproveAsync(hr, instance);
        (await baraka.GetFromJsonAsync<List<JsonElement>>("/api/training/mine", Json))!.Single(t => t.GetProperty("programId").GetGuid() == programId)
            .GetProperty("status").GetString().Should().Be("Approved");
    }
}

[Collection("api")]
public sealed class Phase3AdminEndpointTests : ApiTestBase
{
    public Phase3AdminEndpointTests(EssApiFactory f) : base(f) { }

    [SkippableFact]
    public async Task HR_can_monitor_approvals_and_edit_a_route_which_creates_a_new_version()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        await UserAsync(Amani);
        await GrantRoleAsync(Amani, SystemRoles.HrManager);
        var hr = await UserAsync(Amani);

        (await hr.GetAsync("/api/approvals/monitor")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await hr.GetAsync("/api/approvals/decided")).StatusCode.Should().Be(HttpStatusCode.OK);
        var routes = (await hr.GetFromJsonAsync<List<JsonElement>>("/api/admin/workflows", Json))!;
        routes.Select(r => r.GetProperty("documentType").GetString()).Should().Contain(new[] { "Leave", "Expense", "ProfileChange", "HrRequest", "Attendance", "Training" });

        var training = routes.Single(r => r.GetProperty("documentType").GetString() == "Training");
        var before = training.GetProperty("version").GetInt32();
        var save = await hr.PutAsJsonAsync("/api/admin/workflows/Training", new
        {
            name = "Training request",
            owner = "Portal",
            steps = new object[]
            {
                new { sequence = 1, name = "Line manager", approverType = "LineManager", mode = "AnyOne", deadlineHours = 48, reminderEveryHours = 24, escalateToRole = "HrOfficer", skipIfApproverIsRequester = true },
                new { sequence = 2, name = "HR (over KES 50,000)", approverType = "Role", approverRole = "HrManager", mode = "AnyOne", minAmount = 50000m, skipIfApproverIsRequester = true },
            }
        });
        save.StatusCode.Should().Be(HttpStatusCode.NoContent, await save.Content.ReadAsStringAsync());
        var after = (await hr.GetFromJsonAsync<List<JsonElement>>("/api/admin/workflows", Json))!.Single(r => r.GetProperty("documentType").GetString() == "Training");
        after.GetProperty("version").GetInt32().Should().Be(training.GetProperty("isDefault").GetBoolean() ? 1 : before + 1);
        after.GetProperty("steps").GetArrayLength().Should().Be(2);

        (await hr.PutAsJsonAsync("/api/admin/workflows/Training", new { name = "x", owner = "Nav", steps = Array.Empty<object>() })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "only leave and expenses can hand approval to NAV");
    }
}
