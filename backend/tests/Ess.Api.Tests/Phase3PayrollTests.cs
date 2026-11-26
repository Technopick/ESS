using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ess.Domain.Identity;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Tests;

/// <summary>Payslips: publication gate, own-record isolation, Kenyan statutory lines, encrypted snapshots, audited HR access.</summary>
[Collection("api")]
public sealed class Phase3PayrollTests : ApiTestBase
{
    public Phase3PayrollTests(EssApiFactory f) : base(f) { }

    [SkippableFact]
    public async Task Payslips_appear_only_after_publication_and_show_Kenyan_statutory_deductions()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        await UserAsync(Amani);
        await GrantRoleAsync(Amani, SystemRoles.HrManager);
        var hr = await UserAsync(Amani);

        var periods = await hr.GetFromJsonAsync<List<JsonElement>>("/api/admin/payroll/periods", Json);
        var october = periods!.Single(p => p.GetProperty("code").GetString() == "2026-10");
        var november = periods!.Single(p => p.GetProperty("code").GetString() == "2026-11");

        // Not published yet: the employee cannot see it.
        if (!october.GetProperty("published").GetBoolean())
            (await baraka.GetAsync("/api/payroll/payslips/2026-10")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // An open period cannot be published.
        (await hr.PostAsJsonAsync($"/api/admin/payroll/periods/{november.GetProperty("id")}/publish", new { notify = true })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await hr.PostAsJsonAsync($"/api/admin/payroll/periods/{october.GetProperty("id")}/publish", new { notify = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var r = await baraka.GetAsync("/api/payroll/payslips/2026-10");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Headers.CacheControl!.NoStore.Should().BeTrue("payslips must never be cached");
        var slip = await r.Content.ReadFromJsonAsync<JsonElement>(Json);
        slip.GetProperty("employeeNo").GetString().Should().Be("SIM-002");
        var lines = slip.GetProperty("lines").EnumerateArray().ToList();
        lines.Where(l => l.GetProperty("group").GetString() == "Statutory").Select(l => l.GetProperty("code").GetString())
            .Should().Contain(new[] { "PAYE", "NSSF-I", "NSSF-II", "SHIF", "AHL" });
        var gross = slip.GetProperty("grossPay").GetDecimal();
        (gross - slip.GetProperty("totalDeductions").GetDecimal()).Should().Be(slip.GetProperty("netPay").GetDecimal());
        lines.Single(l => l.GetProperty("code").GetString() == "SHIF").GetProperty("amount").GetDecimal().Should().Be(Math.Round(gross * 0.0275m, 2));

        var summary = await baraka.GetFromJsonAsync<JsonElement>("/api/payroll/statutory?year=2026", Json);
        summary.GetProperty("totals").GetProperty("PAYE").GetDecimal().Should().BeGreaterThan(0);
        summary.GetProperty("months").GetArrayLength().Should().Be(1, "only published periods count");

        // The snapshot kept for NAV outages is encrypted; no figures are stored in clear.
        var meta = await WithDbAsync(db => db.PayslipMetadata.SingleAsync(m => m.NavEmployeeNo == "SIM-002" && m.NavPeriodKey == "2026-10"));
        meta.SnapshotEncrypted.Should().NotContain(gross.ToString(System.Globalization.CultureInfo.InvariantCulture));
        meta.NetPay.Should().BeNull();

        (await baraka.GetFromJsonAsync<JsonElement>("/api/notifications", Json)).GetProperty("items").EnumerateArray()
            .Should().Contain(n => n.GetProperty("eventCode").GetString() == "payslip.available");
        (await WithDbAsync(db => db.AuditLogs.AnyAsync(a => a.Action == "payroll.payslip_viewed" && a.EntityId == "SIM-002/2026-10"))).Should().BeTrue();
    }

    [SkippableFact]
    public async Task Employees_cannot_read_other_payslips_and_HR_must_give_a_reason()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        await UserAsync(Amani);
        await GrantRoleAsync(Amani, SystemRoles.HrManager);
        var hr = await UserAsync(Amani);
        var daudiId = await WithDbAsync(db => db.Employees.Where(e => e.NavKey == "SIM-004").Select(e => e.Id).SingleAsync());

        (await baraka.GetAsync($"/api/payroll/employees/{daudiId}/payslips/2026-10?reason=curious")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await hr.GetAsync($"/api/payroll/employees/{daudiId}/payslips/2026-11")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var ok = await hr.GetAsync($"/api/payroll/employees/{daudiId}/payslips/2026-11?reason=Query%20from%20employee%20about%20SHIF");
        ok.StatusCode.Should().Be(HttpStatusCode.OK, "HR may view unpublished periods with a recorded reason");
        (await ok.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("employeeNo").GetString().Should().Be("SIM-004");
    }
}
