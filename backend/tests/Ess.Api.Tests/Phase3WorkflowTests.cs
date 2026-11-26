using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ess.Application.Workflow;
using Ess.Domain.Common;
using Ess.Domain.Identity;
using Ess.Domain.Workflow;
using Ess.Infrastructure.Persistence;
using Ess.Integration.Nav.Queue;
using Ess.Integration.Nav.Sync;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ess.Api.Tests;

/// <summary>Phase 3 acceptance tests: approval engine with leave and profile changes, end to end to the NAV simulator.</summary>
[Collection("api")]
public sealed class Phase3WorkflowTests : ApiTestBase
{
    public Phase3WorkflowTests(EssApiFactory f) : base(f) { }

    // ---------------------------------------------------------------- helpers

    private async Task<Guid> AnnualLeaveTypeAsync(HttpClient c)
    {
        var types = await c.GetFromJsonAsync<List<JsonElement>>("/api/leave/types", Json);
        return types!.Single(t => t.GetProperty("code").GetString() == "ANNUAL").GetProperty("id").GetGuid();
    }

    /// <summary>A Monday-to-Wednesday range some weeks ahead, so tests don't overlap each other.</summary>
    private static (DateOnly Start, DateOnly End) Week(int weeksAhead)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7 * weeksAhead);
        return (monday, monday.AddDays(2));
    }

    private static async Task<HttpResponseMessage> ApplyAsync(HttpClient c, Guid type, (DateOnly Start, DateOnly End) w, string reason = "Family visit") =>
        await c.PostAsJsonAsync("/api/leave", new { leaveTypeId = type, startDate = w.Start, endDate = w.End, startHalfDay = false, endHalfDay = false, reason });

    private static async Task<List<JsonElement>> InboxAsync(HttpClient c)
    {
        var r = await c.GetAsync("/api/approvals/inbox");
        if (!r.IsSuccessStatusCode) throw new Exception($"inbox {(int)r.StatusCode} {r.Headers.WwwAuthenticate} {await r.Content.ReadAsStringAsync()} auth={c.DefaultRequestHeaders.Authorization}");
        return (await r.Content.ReadFromJsonAsync<List<JsonElement>>(Json))!;
    }

    // ---------------------------------------------------------------- tests

    [SkippableFact]
    public async Task Leave_goes_to_the_line_manager_and_reaches_NAV_only_after_approval()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        var daudi = await UserAsync(Daudi);
        var type = await AnnualLeaveTypeAsync(baraka);
        var week = Week(3);

        var check = await (await baraka.PostAsJsonAsync("/api/leave/check", new { leaveTypeId = type, startDate = week.Start, endDate = week.End, startHalfDay = false, endHalfDay = false }))
            .Content.ReadFromJsonAsync<JsonElement>(Json);
        check.GetProperty("days").GetDecimal().Should().Be(3);
        check.GetProperty("errors").GetArrayLength().Should().Be(0);

        var created = await ApplyAsync(baraka, type, week);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var leave = await created.Content.ReadFromJsonAsync<JsonElement>(Json);
        var leaveId = leave.GetProperty("id").GetGuid();
        leave.GetProperty("status").GetString().Should().Be("InApproval");
        leave.GetProperty("navStatus").GetString().Should().Be("NotRequired", "nothing goes to NAV before approval");

        // Overlapping application is refused.
        (await ApplyAsync(baraka, type, (week.Start.AddDays(1), week.End.AddDays(1)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The requester has no task; the manager has one, with the details needed to decide.
        (await InboxAsync(baraka)).Should().BeEmpty();
        var task = (await InboxAsync(daudi)).Single(t => t.GetProperty("reference").GetString() == leave.GetProperty("reference").GetString());
        var instance = await daudi.GetFromJsonAsync<JsonElement>($"/api/approvals/instances/{task.GetProperty("instanceId").GetGuid()}", Json);
        instance.GetProperty("details").EnumerateArray().Select(d => d.GetProperty("label").GetString()).Should().Contain(new[] { "Leave type", "Days" });
        instance.GetProperty("myTaskId").GetGuid().Should().Be(task.GetProperty("taskId").GetGuid());

        // The requester cannot act on someone else's task.
        (await baraka.PostAsJsonAsync($"/api/approvals/tasks/{task.GetProperty("taskId").GetGuid()}/approve", new { comment = (string?)null })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await daudi.PostAsJsonAsync($"/api/approvals/tasks/{task.GetProperty("taskId").GetGuid()}/approve", new { comment = "Enjoy" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        leave = await baraka.GetFromJsonAsync<JsonElement>($"/api/leave/{leaveId}", Json);
        leave.GetProperty("status").GetString().Should().Be("Approved");
        leave.GetProperty("navStatus").GetString().Should().Be("PendingIntegration", "approved but not posted until NAV confirms");

        using (var scope = _f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IntegrationQueueProcessor>().ProcessBatchAsync(default);
        leave = await baraka.GetFromJsonAsync<JsonElement>($"/api/leave/{leaveId}", Json);
        leave.GetProperty("navStatus").GetString().Should().Be("Posted");
        leave.GetProperty("navDocumentNo").GetString().Should().StartWith("SIM-LV");

        (await baraka.GetFromJsonAsync<JsonElement>("/api/leave/mine", Json)).GetProperty("items").EnumerateArray().Should().Contain(l => l.GetProperty("id").GetGuid() == leaveId);
        var calendar = await daudi.GetFromJsonAsync<List<JsonElement>>($"/api/leave/calendar?from={week.Start:yyyy-MM-dd}&to={week.End:yyyy-MM-dd}", Json);
        calendar!.Should().Contain(c => c.GetProperty("employeeName").GetString() == "Baraka Otieno");
        var notes = await baraka.GetFromJsonAsync<JsonElement>("/api/notifications", Json);
        notes.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("eventCode").GetString()).Should().Contain(new[] { "request.approved", "nav.posted" });
    }

    [SkippableFact]
    public async Task Returned_leave_can_be_corrected_and_rejection_needs_a_reason()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        var daudi = await UserAsync(Daudi);
        var type = await AnnualLeaveTypeAsync(baraka);
        var leave = await (await ApplyAsync(baraka, type, Week(5))).Content.ReadFromJsonAsync<JsonElement>(Json);
        var reference = leave.GetProperty("reference").GetString();
        Guid TaskId(List<JsonElement> inbox) => inbox.Single(t => t.GetProperty("reference").GetString() == reference).GetProperty("taskId").GetGuid();

        var r = await daudi.PostAsJsonAsync($"/api/approvals/tasks/{TaskId(await InboxAsync(daudi))}/return", new { comment = "" });
        r.StatusCode.Should().Be(HttpStatusCode.Conflict, "returning needs a comment");
        (await daudi.PostAsJsonAsync($"/api/approvals/tasks/{TaskId(await InboxAsync(daudi))}/return", new { comment = "Shift by a week, month-end close" })).EnsureSuccessStatusCode();
        (await baraka.GetFromJsonAsync<JsonElement>($"/api/leave/{leave.GetProperty("id")}", Json)).GetProperty("status").GetString().Should().Be("ReturnedForCorrection");

        var w = Week(6);
        var put = await baraka.PutAsJsonAsync($"/api/leave/{leave.GetProperty("id")}", new { leaveTypeId = type, startDate = w.Start, endDate = w.End, startHalfDay = false, endHalfDay = false, reason = "Moved" });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await put.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("status").GetString().Should().Be("InApproval");

        (await daudi.PostAsJsonAsync($"/api/approvals/tasks/{TaskId(await InboxAsync(daudi))}/reject", new { comment = "Too many people away that week" })).EnsureSuccessStatusCode();
        var final = await baraka.GetFromJsonAsync<JsonElement>($"/api/leave/{leave.GetProperty("id")}", Json);
        final.GetProperty("status").GetString().Should().Be("Rejected");

        var timeline = await baraka.GetFromJsonAsync<JsonElement>($"/api/approvals/instances/{final.GetProperty("workflowInstanceId")}", Json);
        timeline.GetProperty("timeline").EnumerateArray().Select(t => t.GetProperty("action").GetString()).Should().Equal("Resubmitted", "Rejected");
        (await WithDbAsync(db => db.IntegrationQueue.AnyAsync(q => q.EntityId == leave.GetProperty("id").GetGuid()))).Should().BeFalse("rejected leave never goes to NAV");
    }

    [SkippableFact]
    public async Task Overdue_approval_escalates_and_the_first_decision_wins()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        var daudi = await UserAsync(Daudi);
        var amani = await UserAsync(Amani);
        await GrantRoleAsync(Amani, SystemRoles.HrOfficer);
        var type = await AnnualLeaveTypeAsync(baraka);
        var leave = await (await ApplyAsync(baraka, type, Week(8))).Content.ReadFromJsonAsync<JsonElement>(Json);
        var instanceId = leave.GetProperty("workflowInstanceId").GetGuid();

        await WithDbAsync(async db =>
        {
            await db.ApprovalTasks.Where(t => t.WorkflowInstanceId == instanceId).ExecuteUpdateAsync(s => s.SetProperty(t => t.DueAtUtc, DateTime.UtcNow.AddHours(-1)));
            return 0;
        });
        using (var scope = _f.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<IWorkflowEngine>().ProcessDeadlinesAsync(default)).Should().BeGreaterThan(0);

        var escalated = (await InboxAsync(amani)).Single(t => t.GetProperty("instanceId").GetGuid() == instanceId);
        escalated.GetProperty("escalated").GetBoolean().Should().BeTrue();
        (await amani.PostAsJsonAsync($"/api/approvals/tasks/{escalated.GetProperty("taskId").GetGuid()}/approve", new { comment = "Approved on Daudi's behalf" })).EnsureSuccessStatusCode();

        (await InboxAsync(daudi)).Should().NotContain(t => t.GetProperty("instanceId").GetGuid() == instanceId, "the original task is superseded");
        (await baraka.GetFromJsonAsync<JsonElement>($"/api/leave/{leave.GetProperty("id")}", Json)).GetProperty("status").GetString().Should().Be("Approved");
    }

    [SkippableFact]
    public async Task Delegated_approvals_go_to_the_delegate_and_record_on_whose_behalf()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        var daudi = await UserAsync(Daudi);
        var amani = await UserAsync(Amani);
        await GrantRoleAsync(Daudi, SystemRoles.DepartmentManager);
        await GrantRoleAsync(Amani, SystemRoles.HrOfficer);
        daudi = await UserAsync(Daudi); // fresh token with the new permissions
        var amaniId = await WithDbAsync(db => db.Users.Where(u => u.NormalizedEmail == Amani.ToUpperInvariant()).Select(u => u.Id).SingleAsync());

        (await daudi.PostAsJsonAsync("/api/approvals/delegations", new { toUserId = amaniId, startsAtUtc = DateTime.UtcNow.AddMinutes(-1), endsAtUtc = DateTime.UtcNow.AddDays(7), documentType = "Leave", reason = "Annual leave" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        try
        {
            var leave = await (await ApplyAsync(baraka, await AnnualLeaveTypeAsync(baraka), Week(10))).Content.ReadFromJsonAsync<JsonElement>(Json);
            var task = (await InboxAsync(amani)).Single(t => t.GetProperty("instanceId").GetGuid() == leave.GetProperty("workflowInstanceId").GetGuid());
            task.GetProperty("onBehalfOf").GetString().Should().NotBeNullOrEmpty();
            (await InboxAsync(daudi)).Should().NotContain(t => t.GetProperty("instanceId").GetGuid() == leave.GetProperty("workflowInstanceId").GetGuid());
        }
        finally
        {
            foreach (var d in (await daudi.GetFromJsonAsync<List<JsonElement>>("/api/approvals/delegations", Json))!)
                await daudi.DeleteAsync($"/api/approvals/delegations/{d.GetProperty("id")}");
        }
    }

    [SkippableFact]
    public async Task Bank_change_is_reviewed_by_HR_then_sent_to_NAV_encrypted_and_nobody_approves_their_own()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var chebet = await UserAsync(Chebet);
        var amani = await UserAsync(Amani);
        await GrantRoleAsync(Amani, SystemRoles.HrOfficer);

        var r = await chebet.PostAsJsonAsync("/api/employees/me/change-requests", new
        {
            section = "BankDetails",
            changes = new Dictionary<string, string> { ["bankName"] = "Equity Bank", ["accountNo"] = "0123456789012" },
            reason = "Changed banks"
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var reference = (await r.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("reference").GetString();

        var task = (await InboxAsync(amani)).Single(t => t.GetProperty("reference").GetString() == reference);
        var instance = await amani.GetFromJsonAsync<JsonElement>($"/api/approvals/instances/{task.GetProperty("instanceId")}", Json);
        instance.GetProperty("details").EnumerateArray().Should().Contain(d => d.GetProperty("value").GetString() == "0123456789012", "HR needs the full number to verify it");
        (await amani.PostAsJsonAsync($"/api/approvals/tasks/{task.GetProperty("taskId")}/approve", new { comment = "Verified with bank letter" })).EnsureSuccessStatusCode();

        var item = await WithDbAsync(db => db.IntegrationQueue.SingleAsync(q => q.Operation == "Employee.Change" && q.PayloadEncrypted
            && db.ProfileChangeRequests.Any(p => p.Id == q.EntityId && p.Reference == reference)));
        item.PayloadJson.Should().NotContain("0123456789012", "sensitive payloads are encrypted in the outbox");

        // Amani is the only HR approver: her own change cannot be routed to herself.
        var own = await amani.PostAsJsonAsync("/api/employees/me/change-requests", new { section = "Contact", changes = new Dictionary<string, string> { ["mobilePhone"] = "+254700000001" } });
        own.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await own.Content.ReadAsStringAsync()).Should().Contain("Nobody can approve");
    }
}
