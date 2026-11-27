using System.Text.Json;
using System.Xml.Linq;
using Ess.Application.Integration;
using Ess.Domain.Integration;
using Ess.Integration.Nav.Adapters;
using Ess.Integration.Nav.Configuration;
using Ess.Integration.Nav.Mapping;
using FluentAssertions;

namespace Ess.UnitTests.Unit;

/// <summary>Behaviour specific to the customer's NAV 2016 (9.0) OData v4 and SOAP services.</summary>
public class Nav2016Tests
{
    [Theory]
    [InlineData("Sick_x0020_Leave", "Sick Leave")]
    [InlineData("_blank_", null)]
    [InlineData("_x0020_", null)]
    [InlineData("Female", "Female")]
    [InlineData("SIM_001", "SIM_001")]
    public void OData_v4_option_values_are_decoded(string raw, string? expected)
    {
        var map = new NavFieldMapper(new Dictionary<string, string> { ["Gender"] = "Gender" }, null);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, string> { ["Gender"] = raw }));
        map.GetString(doc.RootElement, "Gender").Should().Be(expected);
    }

    [Fact]
    public void Version_token_falls_back_to_the_odata_etag_annotation()
    {
        var map = new NavFieldMapper(NavEmployeeAdapter.ODataDefaults, null);
        using var doc = JsonDocument.Parse("""{"No":"E1","@odata.etag":"W/\"JzQ0OzE7Jw==\""}""");
        map.GetString(doc.RootElement, "VersionToken").Should().Be("W/\"JzQ0OzE7Jw==\"");
    }

    [Theory]
    [InlineData(500, 500, null, 0, "skip:500")]   // NAV stopped at $top: ask for the next 500
    [InlineData(500, 500, null, 500, "skip:1000")]
    [InlineData(120, 500, null, 1000, null)]      // short page: done
    [InlineData(100, 500, "http://nav/next", 0, "http://nav/next")] // server-driven paging wins
    public void Employee_sync_pages_past_top(int rows, int pageSize, string? nextLink, int skip, string? expected) =>
        NavEmployeeAdapter.NextContinuation(rows, pageSize, nextLink, skip).Should().Be(expected);

    [Fact]
    public void Nav_2016_profile_uses_odata_v4_soap_writes_xml_parameters_and_no_oauth()
    {
        var p = NavVersionProfile.For(NavPlatform.Nav2016);
        p.ODataVersion.Should().Be("v4");
        p.DefaultWriteProtocol.Should().Be(NavProtocol.Soap);
        p.SupportsSystemModifiedAt.Should().BeFalse();
        p.StructuredParametersAsXml.Should().BeTrue();
        p.AllowedAuth.Should().NotContain(NavAuthMethod.OAuthClientCredentials);
        NavVersionProfile.For(NavPlatform.BusinessCentralOnline).StructuredParametersAsXml.Should().BeFalse();
    }

    [Fact]
    public void Nav_2016_default_urls_use_instance_DynamicsNAV90()
    {
        var u = NavVersionProfile.SuggestedUrls(NavPlatform.Nav2016, "navsrv01", null, false)!.Value;
        u.Soap.Should().Be("http://navsrv01:7047/DynamicsNAV90/WS");
        u.OData.Should().Be("http://navsrv01:7048/DynamicsNAV90/ODataV4");
        NavVersionProfile.SuggestedUrls(NavPlatform.Nav2016, "navsrv01", "ESS", true)!.Value.OData.Should().Be("https://navsrv01:7048/ESS/ODataV4");
        NavVersionProfile.SuggestedUrls(NavPlatform.BusinessCentralOnline, "x", null, true).Should().BeNull();
    }

    [Fact]
    public void Expense_lines_for_cal_are_culture_invariant_xml()
    {
        var claim = new NavExpenseClaimSubmission("K", "EC-1", "E1", "Trip", "KES", null, null, true, "Approver",
            new[] { new NavExpenseClaimLine(10, "TRAVEL", new DateOnly(2026, 11, 3), "Taxi <airport> & back", 1234.5m, "KES") });
        var x = XElement.Parse(NavExpenseAdapter.LinesXml(claim));
        var line = x.Elements("Line").Single();
        line.Attribute("amount")!.Value.Should().Be("1234.5");
        line.Attribute("date")!.Value.Should().Be("2026-11-03");
        line.Attribute("expenseCode")!.Value.Should().Be("TRAVEL");
        line.Value.Should().Be("Taxi <airport> & back");
    }
}
