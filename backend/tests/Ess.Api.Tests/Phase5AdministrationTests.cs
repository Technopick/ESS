using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ess.Application.Notifications;
using Ess.Domain.Identity;
using Ess.Domain.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ess.Api.Tests;

/// <summary>HR portal, management dashboard, reports, announcements and notification templates.</summary>
[Collection("api")]
public sealed class Phase5AdministrationTests : ApiTestBase
{
    public Phase5AdministrationTests(EssApiFactory f) : base(f) { }

    private async Task<HttpClient> HrManagerAsync()
    {
        await UserAsync(Amani);
        await GrantRoleAsync(Amani, SystemRoles.HrManager);
        return await UserAsync(Amani);
    }

    [SkippableFact]
    public async Task HR_sees_employee_records_within_scope_and_views_are_audited()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var hr = await HrManagerAsync();
        var baraka = await UserAsync(Baraka);

        (await baraka.GetAsync("/api/hr/employees")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var all = await hr.GetFromJsonAsync<JsonElement>("/api/hr/employees?pageSize=50", Json);
        all.GetProperty("totalCount").GetInt32().Should().Be(6, "HR managers see every employee, including leavers");
        var terminated = await hr.GetFromJsonAsync<JsonElement>("/api/hr/employees?status=Terminated", Json);
        terminated.GetProperty("items").EnumerateArray().Single().GetProperty("employeeNo").GetString().Should().Be("SIM-006");

        var barakaId = await WithDbAsync(db => db.Employees.Where(e => e.NavKey == "SIM-002").Select(e => e.Id).SingleAsync());
        var detail = await hr.GetFromJsonAsync<JsonElement>($"/api/hr/employees/{barakaId}", Json);
        detail.GetProperty("employee").GetProperty("manager").GetString().Should().Be("Daudi Mwangi");
        detail.GetProperty("account").GetProperty("email").GetString().Should().Be(Baraka);
        (await WithDbAsync(db => db.AuditLogs.AnyAsync(a => a.Action == "employee.record.viewed" && a.EntityId == barakaId.ToString()))).Should().BeTrue();

        // An HR officer scoped to Finance sees Finance staff only.
        await UserAsync(Chebet);
        await WithDbAsync(async db =>
        {
            var u = await db.Users.SingleAsync(x => x.NormalizedEmail == Chebet.ToUpperInvariant());
            var role = await db.Roles.SingleAsync(r => r.Name == SystemRoles.HrOfficer);
            var fin = await db.Departments.SingleAsync(d => d.NavKey == "FIN");
            db.UserRoles.RemoveRange(db.UserRoles.Where(x => x.UserId == u.Id && x.RoleId == role.Id));
            db.UserRoles.Add(new UserRole { UserId = u.Id, RoleId = role.Id, ScopeDepartmentId = fin.Id });
            return await db.SaveChangesAsync();
        });
        var scoped = await UserAsync(Chebet);
        var fin = await scoped.GetFromJsonAsync<JsonElement>("/api/hr/employees?pageSize=50", Json);
        fin.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("department").GetString()).Should().OnlyContain(d => d == "Finance");
        var amaniId = await WithDbAsync(db => db.Employees.Where(e => e.NavKey == "SIM-001").Select(e => e.Id).SingleAsync());
        (await scoped.GetAsync($"/api/hr/employees/{amaniId}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "HR staff cannot open records outside their departments");
    }

    [SkippableFact]
    public async Task Reports_run_with_permissions_and_export_to_csv_excel_and_pdf()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var hr = await HrManagerAsync();
        var baraka = await UserAsync(Baraka);

        (await baraka.GetFromJsonAsync<JsonElement>("/api/reports", Json)).GetProperty("reports").GetArrayLength().Should().Be(0);
        (await baraka.GetAsync("/api/reports/headcount")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var catalog = await hr.GetFromJsonAsync<JsonElement>("/api/reports", Json);
        catalog.GetProperty("canExport").GetBoolean().Should().BeTrue();
        catalog.GetProperty("reports").EnumerateArray().Select(r => r.GetProperty("code").GetString()).Should().Contain(new[] { "headcount", "payroll-summary", "leave-utilisation" });

        var headcount = await hr.GetFromJsonAsync<JsonElement>("/api/reports/headcount?from=2026-01-01&to=2026-12-31", Json);
        var totals = headcount.GetProperty("totals");
        totals[5].GetInt32().Should().Be(5, "five current staff");
        totals[7].GetInt32().Should().Be(1, "one leaver in 2026");

        var contracts = await hr.GetFromJsonAsync<JsonElement>("/api/reports/contract-expiry?from=2027-01-01&to=2027-03-31", Json);
        contracts.GetProperty("rows").EnumerateArray().Select(r => r[1].GetString()).Should().Equal("Esther Njeri");

        var xlsx = await hr.GetAsync("/api/reports/employee-directory/export?format=Xlsx");
        xlsx.StatusCode.Should().Be(HttpStatusCode.OK);
        xlsx.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        (await xlsx.Content.ReadAsByteArrayAsync()).Take(2).Should().Equal((byte)'P', (byte)'K');
        var pdf = await hr.GetByteArrayAsync("/api/reports/headcount/export?format=Pdf&from=2026-01-01&to=2026-12-31");
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        var csv = await hr.GetStringAsync("/api/reports/employee-directory/export?format=Csv");
        csv.Should().Contain("Esther Njeri").And.NotContain("Birth");
        (await WithDbAsync(db => db.AuditLogs.CountAsync(a => a.Action == "report.exported"))).Should().BeGreaterThanOrEqualTo(3);

        // Payroll summary: department totals for a closed period, read from NAV; open periods are refused.
        (await hr.GetAsync("/api/admin/payroll/periods")).IsSuccessStatusCode.Should().BeTrue(); // periods arrive with the NAV sync
        var payroll = await hr.GetFromJsonAsync<JsonElement>("/api/reports/payroll-summary?periodCode=2026-10", Json);
        payroll.GetProperty("totals")[1].GetInt32().Should().Be(5, "the leaver has no October payslip");
        payroll.GetProperty("totals")[2].GetDecimal().Should().BeGreaterThan(0);
        payroll.GetProperty("rows").EnumerateArray().Select(r => r[0].GetString()).Should().BeEquivalentTo("Finance", "Human Resources");
        (await hr.GetAsync("/api/reports/payroll-summary?periodCode=2026-11")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Viewing is allowed without the export permission; exporting is not.
        await UserAsync(Daudi);
        await GrantRoleAsync(Daudi, SystemRoles.ManagementViewer);
        var viewer = await UserAsync(Daudi);
        (await viewer.GetAsync("/api/reports/headcount")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync("/api/reports/headcount/export?format=Csv")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task Management_dashboard_counts_staff_turnover_and_exports()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var hr = await HrManagerAsync();
        var d = await hr.GetFromJsonAsync<JsonElement>("/api/management/dashboard?from=2026-01-01&to=2026-12-31", Json);
        d.GetProperty("activeEmployees").GetInt32().Should().Be(5);
        d.GetProperty("leavers").GetInt32().Should().Be(1);
        d.GetProperty("turnoverPercent").GetDecimal().Should().BeGreaterThan(0);
        d.GetProperty("byDepartment").EnumerateArray().Sum(x => x.GetProperty("count").GetInt32()).Should().Be(5);
        d.GetProperty("headcountByMonth").GetArrayLength().Should().Be(12);

        var fin = await WithDbAsync(db => db.Departments.Where(x => x.NavKey == "FIN").Select(x => x.Id).SingleAsync());
        var filtered = await hr.GetFromJsonAsync<JsonElement>($"/api/management/dashboard?departmentId={fin}", Json);
        filtered.GetProperty("activeEmployees").GetInt32().Should().Be(2);

        var pdf = await hr.GetByteArrayAsync("/api/management/dashboard/export?format=Pdf");
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        (await (await UserAsync(Baraka)).GetAsync("/api/management/dashboard")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task Announcements_are_sanitised_targeted_and_notified()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var hr = await HrManagerAsync();
        var baraka = await UserAsync(Baraka);
        var chebet = await UserAsync(Chebet);
        var fin = await WithDbAsync(db => db.Departments.Where(x => x.NavKey == "FIN").Select(x => x.Id).SingleAsync());

        var created = await hr.PostAsJsonAsync("/api/announcements", new
        {
            title = "Finance year-end close", body = "<p>Submit claims by <strong>15 Dec</strong>.</p><script>alert(1)</script><a href=\"javascript:alert(1)\">x</a>",
            targetDepartmentId = fin, notifyOnPublish = true, notifyByEmail = false, isPinned = true
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var feed = await baraka.GetFromJsonAsync<List<JsonElement>>("/api/announcements", Json);
        var a = feed!.Single(x => x.GetProperty("title").GetString() == "Finance year-end close");
        a.GetProperty("bodyHtml").GetString().Should().Contain("<strong>15 Dec</strong>").And.NotContain("script").And.NotContain("javascript");
        (await chebet.GetFromJsonAsync<List<JsonElement>>("/api/announcements", Json))!.Should().NotContain(x => x.GetProperty("title").GetString() == "Finance year-end close");

        var barakaUser = await WithDbAsync(db => db.Users.Where(u => u.NormalizedEmail == Baraka.ToUpperInvariant()).Select(u => u.Id).SingleAsync());
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.RecipientUserId == barakaUser && n.EventCode == NotificationEvents.AnnouncementPublished && n.Channel == NotificationChannel.InApp)))
            .Should().Be(1);
        (await WithDbAsync(db => db.Notifications.AnyAsync(n => n.RecipientUserId == barakaUser && n.EventCode == NotificationEvents.AnnouncementPublished && n.Channel == NotificationChannel.Email)))
            .Should().BeFalse();
        (await baraka.PostAsJsonAsync("/api/announcements", new { title = "x", body = "y" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task Notification_templates_change_wording_and_SMS_is_opt_in()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var hr = await HrManagerAsync();
        var overview = await hr.GetFromJsonAsync<JsonElement>("/api/admin/notifications/templates", Json);
        overview.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("code").GetString()).Should().Contain(NotificationEvents.ContractExpiring);

        (await hr.PutAsJsonAsync($"/api/admin/notifications/templates/{NotificationEvents.RequestApproved}/InApp",
            new { subject = "Good news: {subject}", body = "{body} Reference: {reference}.", isActive = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await hr.PutAsJsonAsync($"/api/admin/notifications/templates/{NotificationEvents.PayslipAvailable}/Sms",
            new { body = "Your payslip of KES 50,000 is ready", isActive = true })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "payslip SMS must not carry amounts");
        (await hr.PutAsJsonAsync($"/api/admin/notifications/templates/{NotificationEvents.RequestApproved}/Sms",
            new { body = "{subject}", isActive = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var company = await SimCompanyAsync();
        var user = await WithDbAsync(db => db.Users.Where(u => u.NormalizedEmail == Baraka.ToUpperInvariant()).Select(u => u.Id).SingleAsync());
        using (var scope = _f.Services.CreateScope())
        {
            var notify = scope.ServiceProvider.GetRequiredService<INotificationService>();
            await notify.QueueAsync(new NotificationRequest(company, user, NotificationEvents.RequestApproved, "LV-1 approved", "Your leave was approved.", "/leave",
                Email: false, Values: new Dictionary<string, string> { ["reference"] = "LV-1" }));
            await scope.ServiceProvider.GetRequiredService<Ess.Infrastructure.Persistence.EssDbContext>().SaveChangesAsync();
        }
        var rows = await WithDbAsync(db => db.Notifications.Where(n => n.RecipientUserId == user && n.Subject!.EndsWith("LV-1 approved")).ToListAsync());
        rows.Should().Contain(n => n.Channel == NotificationChannel.InApp && n.Subject == "Good news: LV-1 approved" && n.Body == "Your leave was approved. Reference: LV-1.");
        rows.Should().Contain(n => n.Channel == NotificationChannel.Sms, "an active SMS template switches SMS on for the event");

        var deliveries = await hr.GetFromJsonAsync<JsonElement>("/api/admin/notifications/deliveries?channel=Sms", Json);
        deliveries.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);
        (await hr.DeleteAsync($"/api/admin/notifications/templates/{NotificationEvents.RequestApproved}/Sms")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await hr.DeleteAsync($"/api/admin/notifications/templates/{NotificationEvents.RequestApproved}/InApp")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public async Task HR_configures_leave_rules_holidays_and_department_heads()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var hr = await HrManagerAsync();
        var types = await hr.GetFromJsonAsync<List<JsonElement>>("/api/hr/leave/types", Json);
        var maternity = types!.Single(t => t.GetProperty("code").GetString() == "MATERNITY");
        (await hr.PutAsJsonAsync($"/api/hr/leave/types/{maternity.GetProperty("id")}", new
        {
            countsWorkingDaysOnly = false, requiresAttachment = true, maxDaysPerApplication = 90, minNoticeDays = 0, allowNegativeBalance = false,
            allowHalfDay = false, eligibleGender = "Female", isActive = true
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Baraka is male in NAV, so maternity leave is refused by the rule above.
        var baraka = await UserAsync(Baraka);
        var check = await baraka.PostAsJsonAsync("/api/leave/check", new { leaveTypeId = maternity.GetProperty("id"), startDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"), endDate = DateTime.UtcNow.AddDays(31).ToString("yyyy-MM-dd"), startHalfDay = false, endHalfDay = false });
        (await check.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("errors").EnumerateArray().Should().Contain(e => e.GetString()!.Contains("not available to you"));

        var added = await hr.PostAsJsonAsync("/api/hr/leave/holidays", new { date = "2027-06-01", name = "Madaraka Day" });
        added.StatusCode.Should().Be(HttpStatusCode.Created);
        (await hr.PostAsJsonAsync("/api/hr/leave/holidays", new { date = "2027-06-01", name = "Again" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var id = (await added.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        (await hr.DeleteAsync($"/api/hr/leave/holidays/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var depts = await hr.GetFromJsonAsync<List<JsonElement>>("/api/hr/organization/departments", Json);
        var hrDept = depts!.Single(d => d.GetProperty("code").GetString() == "HR").GetProperty("id").GetGuid();
        var finDept = depts!.Single(d => d.GetProperty("code").GetString() == "FIN").GetProperty("id").GetGuid();
        var daudi = await WithDbAsync(db => db.Employees.Where(e => e.NavKey == "SIM-004").Select(e => e.Id).SingleAsync());
        (await hr.PutAsJsonAsync($"/api/hr/organization/departments/{finDept}", new { headEmployeeId = daudi, parentDepartmentId = hrDept })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await hr.PutAsJsonAsync($"/api/hr/organization/departments/{hrDept}", new { headEmployeeId = (Guid?)null, parentDepartmentId = finDept }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a department cannot sit under its own sub-department");
        (await hr.PutAsJsonAsync($"/api/hr/organization/departments/{finDept}", new { headEmployeeId = daudi, parentDepartmentId = (Guid?)null })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var apps = await hr.GetFromJsonAsync<JsonElement>("/api/hr/leave/applications", Json);
        apps.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }
}
