using Ess.Api.Reporting;
using Ess.Api.Services;
using Ess.Application.Abstractions;
using FluentAssertions;

namespace Ess.Api.Tests;

public sealed class Phase5UnitTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+254700000000", "+254700000000")] // a number, not a formula
    [InlineData("-12.50", "-12.50")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("Otieno, Baraka", "\"Otieno, Baraka\"")]
    [InlineData("plain", "plain")]
    public void Csv_cells_neutralise_formulas_and_quote_separators(string input, string expected) =>
        ReportExporter.Cell(input).Should().Be(expected);

    [Theory]
    [InlineData("0712 345 678", "+254712345678")]
    [InlineData("0112345678", "+254112345678")]
    [InlineData("254712345678", "+254712345678")]
    [InlineData("+44 20 7946 0958", "+442079460958")]
    [InlineData("712345678", "+254712345678")]
    [InlineData("12345", null)]
    [InlineData("", null)]
    public void Kenyan_phone_numbers_are_normalised(string input, string? expected) =>
        PhoneNumbers.ToE164(input).Should().Be(expected);

    [Fact]
    public void Announcement_html_keeps_basic_formatting_only()
    {
        var html = AnnouncementService.Sanitize("<h3>Title</h3><p onclick=\"x()\" style=\"color:red\">Hi <a href=\"https://intranet.example\">link</a> <img src=x onerror=alert(1)></p><iframe src=\"https://evil\"></iframe>");
        html.Should().Contain("<h3>Title</h3>").And.Contain("href=\"https://intranet.example\"").And.Contain("rel=\"noopener noreferrer\"");
        html.Should().NotContain("onclick").And.NotContain("style").And.NotContain("img").And.NotContain("iframe");
        AnnouncementService.Sanitize("Line one\n\nLine <two>").Should().Be("<p>Line one</p><p>Line &lt;two&gt;</p>");
    }

    [Fact]
    public void Exports_produce_valid_files_with_totals()
    {
        var t = new ReportTable("Test report", new[] { new ReportColumn("n", "Name"), new ReportColumn("a", "Amount", ReportColumnType.Money), new ReportColumn("d", "Date", ReportColumnType.Date) },
            new List<object?[]> { new object?[] { "=cmd", 1234.5m, new DateOnly(2026, 11, 27) } }, new List<KeyValuePair<string, string>> { new("Period", "Nov 2026") })
        { Totals = new object?[] { "Total", 1234.5m, null } };
        var csv = System.Text.Encoding.UTF8.GetString(ReportExporter.Export(t, ExportFormat.Csv, "tester", DateTime.Now).Content);
        csv.Should().Contain("\"'=cmd\",1234.5,2026-11-27").And.Contain("Total,1234.5,");
        ReportExporter.Export(t, ExportFormat.Xlsx, "tester", DateTime.Now).Content.Take(2).Should().Equal((byte)'P', (byte)'K');
        var pdf = ReportExporter.ExportMany(new[] { t, t with { Title = "Second" } }, "Two", ExportFormat.Pdf, "tester", DateTime.Now).Content;
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }
}
