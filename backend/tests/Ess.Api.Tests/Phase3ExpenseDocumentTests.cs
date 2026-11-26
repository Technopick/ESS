using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ess.Domain.Identity;
using Ess.Integration.Nav.Queue;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ess.Api.Tests;

/// <summary>Documents (validation, encryption at rest, access rules) and expense claims through two approval levels to NAV.</summary>
[Collection("api")]
public sealed class Phase3ExpenseDocumentTests : ApiTestBase
{
    public Phase3ExpenseDocumentTests(EssApiFactory f) : base(f) { }

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF SECRET-RECEIPT-CONTENT");
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 73, 72, 68, 82 };

    private static MultipartFormDataContent Form(byte[] bytes, string fileName, Dictionary<string, string>? fields = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        foreach (var (k, v) in fields ?? new()) form.Add(new StringContent(v), k);
        return form;
    }

    [SkippableFact]
    public async Task Documents_are_validated_by_content_encrypted_at_rest_and_private_to_the_employee()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        var daudi = await UserAsync(Daudi);
        var categories = await baraka.GetFromJsonAsync<List<JsonElement>>("/api/documents/categories", Json);
        var id = categories!.Single(c => c.GetProperty("code").GetString() == "ID").GetProperty("id").GetString()!;
        var contract = categories!.Single(c => c.GetProperty("code").GetString() == "CONTRACT").GetProperty("id").GetString()!;

        // An executable renamed to .pdf is refused; so is a category only HR may upload to.
        (await baraka.PostAsync("/api/documents", Form(Encoding.ASCII.GetBytes("MZ\x90\0fake exe"), "id.pdf", new() { ["categoryId"] = id }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await baraka.PostAsync("/api/documents", Form(Pdf, "contract.pdf", new() { ["categoryId"] = contract }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var up = await baraka.PostAsync("/api/documents", Form(Pdf, "../../etc/national id.pdf", new() { ["categoryId"] = id, ["title"] = "National ID", ["expiryDate"] = "2030-01-31" }));
        up.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = await up.Content.ReadFromJsonAsync<JsonElement>(Json);
        doc.GetProperty("fileName").GetString().Should().Be("national id.pdf", "paths are stripped from file names");

        var download = await baraka.GetAsync($"/api/documents/{doc.GetProperty("id")}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(Pdf);
        (await daudi.GetAsync($"/api/documents/{doc.GetProperty("id")}/download")).StatusCode.Should().Be(HttpStatusCode.NotFound, "colleagues cannot read each other's documents");

        var stored = Directory.GetFiles(_f.StorageRoot, "*", SearchOption.AllDirectories).Select(File.ReadAllBytes);
        stored.Should().NotContain(b => Encoding.ASCII.GetString(b).Contains("SECRET-RECEIPT-CONTENT"), "files are encrypted at rest");
        (await WithDbAsync(db => db.DocumentAccessLogs.AnyAsync(l => l.DocumentId == doc.GetProperty("id").GetGuid() && l.Action == "download"))).Should().BeTrue();

        (await baraka.GetFromJsonAsync<List<JsonElement>>("/api/documents/mine", Json))!.Should().Contain(d => d.GetProperty("id").GetGuid() == doc.GetProperty("id").GetGuid());

        // A new version keeps history.
        (await baraka.PostAsync($"/api/documents/{doc.GetProperty("id")}/versions", Form(Png, "id-scan.png"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await baraka.GetAsync($"/api/documents/{doc.GetProperty("id")}/download?version=1")).Content.ReadAsByteArrayAsync()).Should().Equal(Pdf);
        (await (await baraka.GetAsync($"/api/documents/{doc.GetProperty("id")}/download")).Content.ReadAsByteArrayAsync()).Should().Equal(Png);
    }

    [SkippableFact]
    public async Task Expense_claim_needs_receipts_goes_through_manager_and_finance_then_NAV()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var baraka = await UserAsync(Baraka);
        var daudi = await UserAsync(Daudi);
        await UserAsync(Chebet);
        await GrantRoleAsync(Chebet, SystemRoles.FinanceOfficer);
        var finance = await UserAsync(Chebet);

        var cats = (await baraka.GetFromJsonAsync<List<JsonElement>>("/api/expenses/categories", Json))!;
        Guid Cat(string code) => cats.Single(c => c.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));

        var draft = await baraka.PostAsJsonAsync("/api/expenses", new
        {
            purpose = "Client visit to Mombasa",
            lines = new object[]
            {
                new { categoryId = Cat("TRAVEL"), expenseDate = day, description = "SGR ticket", amount = 3000m },
                new { categoryId = Cat("COMMS"), expenseDate = day, description = "Airtime", amount = 500m },
            }
        });
        draft.StatusCode.Should().Be(HttpStatusCode.Created);
        var claim = await draft.Content.ReadFromJsonAsync<JsonElement>(Json);
        var claimId = claim.GetProperty("id").GetGuid();
        claim.GetProperty("totalAmount").GetDecimal().Should().Be(3500m);

        // Travel needs a receipt before submission.
        (await baraka.PostAsync($"/api/expenses/{claimId}/submit", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var receipt = await (await baraka.PostAsync("/api/documents/receipts", Form(Pdf, "sgr.pdf"))).Content.ReadFromJsonAsync<JsonElement>(Json);
        var receiptId = receipt.GetProperty("id").GetGuid();
        (await baraka.PutAsJsonAsync($"/api/expenses/{claimId}", new
        {
            purpose = "Client visit to Mombasa",
            lines = new object[]
            {
                new { categoryId = Cat("TRAVEL"), expenseDate = day, description = "SGR ticket", amount = 3000m, receiptDocumentId = receiptId },
                new { categoryId = Cat("COMMS"), expenseDate = day, description = "Airtime", amount = 500m },
            }
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await baraka.PostAsync($"/api/expenses/{claimId}/submit", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The line manager can open receipts because the claim is assigned to them; finance can through expense.view.all.
        (await daudi.GetAsync($"/api/documents/{receiptId}/download")).StatusCode.Should().Be(HttpStatusCode.OK);

        var instanceId = (await baraka.GetFromJsonAsync<JsonElement>($"/api/expenses/{claimId}", Json)).GetProperty("workflowInstanceId").GetGuid();
        async Task Approve(HttpClient c)
        {
            var task = (await c.GetFromJsonAsync<List<JsonElement>>("/api/approvals/inbox", Json))!.Single(t => t.GetProperty("instanceId").GetGuid() == instanceId);
            (await c.PostAsJsonAsync($"/api/approvals/tasks/{task.GetProperty("taskId")}/approve", new { comment = "OK" })).EnsureSuccessStatusCode();
        }
        await Approve(daudi);
        (await finance.GetAsync($"/api/documents/{receiptId}/download")).StatusCode.Should().Be(HttpStatusCode.OK);
        await Approve(finance);

        using (var scope = _f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IntegrationQueueProcessor>().ProcessBatchAsync(default);
        var final = await baraka.GetFromJsonAsync<JsonElement>($"/api/expenses/{claimId}", Json);
        final.GetProperty("status").GetString().Should().Be("Approved");
        final.GetProperty("navStatus").GetString().Should().Be("Posted");
        final.GetProperty("navDocumentNo").GetString().Should().StartWith("SIM-EC");

        (await baraka.GetFromJsonAsync<JsonElement>("/api/expenses/mine", Json)).GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);
        (await finance.GetFromJsonAsync<JsonElement>("/api/expenses", Json)).GetProperty("items").EnumerateArray().Should().Contain(c => c.GetProperty("id").GetGuid() == claimId);

        // A second claim with the same expense is flagged as a possible duplicate.
        var dup = await baraka.PostAsJsonAsync("/api/expenses", new
        {
            purpose = "Duplicate check",
            lines = new object[] { new { categoryId = Cat("COMMS"), expenseDate = day, description = "Airtime", amount = 500m } }
        });
        (await dup.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("warnings").EnumerateArray().Should().ContainSingle(w => w.GetString()!.Contains("duplicate"));
    }
}
