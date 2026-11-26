using System.Net;
using Ess.Application.Integration;
using Ess.Application.Security;
using Ess.Domain.Identity;
using Ess.Integration.Nav.Clients;
using Ess.Integration.Nav.Configuration;
using Ess.Integration.Nav.Mapping;
using Ess.Integration.Nav.Queue;
using Ess.Integration.Nav.Security;
using Ess.Integration.Nav.Transport;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Ess.UnitTests.Unit;

public class SensitiveDataMaskerTests
{
    private readonly SensitiveDataMasker _m = new(Options.Create(new NavEngineOptions { AdditionalSensitiveFields = new[] { "Custom_Secret" } }));

    [Fact]
    public void Masks_json_secrets_personal_ids_and_pay()
    {
        var masked = _m.MaskBody("""{"No":"E1","password":"p@ss","KRA_PIN":"A123","Net_Pay":1000.5,"nested":{"Bank_Account_No":"0123456789"},"Custom_Secret":"x"}""")!;
        masked.Should().Contain("\"No\":\"E1\"");
        masked.Should().NotContain("p@ss").And.NotContain("A123").And.NotContain("1000.5").And.NotContain("0123456789");
        masked.Should().NotContain("\"x\"");
    }

    [Fact]
    public void Masks_soap_elements()
    {
        var masked = _m.MaskBody("<s:Envelope><s:Body><m:Submit><m:employeeNo>E1</m:employeeNo><m:password>secret</m:password><m:netPay>99</m:netPay></m:Submit></s:Body></s:Envelope>")!;
        masked.Should().Contain("<m:employeeNo>E1</m:employeeNo>");
        masked.Should().NotContain("secret").And.NotContain(">99<");
    }

    [Fact]
    public void Masks_query_string_secrets() =>
        _m.MaskUrl("https://x/api?code=abc&$top=1&sig=zzz").Should().Be("https://x/api?code=***&$top=1&sig=***");
}

public class RetryAndClassificationTests
{
    [Fact]
    public void Backoff_grows_and_is_capped()
    {
        var rng = new Random(1);
        var d1 = RetrySchedule.NextDelay(1, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5), rng);
        var d5 = RetrySchedule.NextDelay(5, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5), rng);
        var d20 = RetrySchedule.NextDelay(20, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5), rng);
        d1.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5)).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(10));
        d5.Should().BeGreaterThan(d1);
        d20.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(5));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, false, NavErrorCategory.Transient)]
    [InlineData(HttpStatusCode.GatewayTimeout, true, NavErrorCategory.Unknown)]
    [InlineData(HttpStatusCode.Unauthorized, false, NavErrorCategory.Authentication)]
    [InlineData(HttpStatusCode.BadRequest, true, NavErrorCategory.Validation)]
    [InlineData(HttpStatusCode.InternalServerError, true, NavErrorCategory.Unknown)]
    [InlineData(HttpStatusCode.InternalServerError, false, NavErrorCategory.Transient)]
    public void Http_status_classification(HttpStatusCode status, bool write, NavErrorCategory expected) =>
        NavErrorClassifier.FromStatus(status, write).Should().Be(expected);

    [Fact]
    public void Timeout_after_sending_a_write_is_unknown_not_retryable() =>
        NavErrorClassifier.FromException(new TaskCanceledException(), requestWasWrite: true, requestSent: true).Should().Be(NavErrorCategory.Unknown);

    [Fact]
    public void Connection_refused_is_transient() =>
        NavErrorClassifier.FromException(new HttpRequestException("refused"), true, true).Should().Be(NavErrorCategory.Transient);

    [Theory]
    [InlineData("The Leave Application Header table is locked by another user.", NavErrorCategory.Transient)]
    [InlineData("Leave days exceed the available balance.", NavErrorCategory.Validation)]
    [InlineData("You do not have the following permissions on TableData 5200", NavErrorCategory.Authentication)]
    public void Soap_fault_classification(string fault, NavErrorCategory expected) =>
        NavErrorClassifier.FromSoapFault(fault).Should().Be(expected);

    [Fact]
    public void Idempotency_key_is_deterministic_and_fits_nav_code50()
    {
        var id = Guid.NewGuid();
        var k1 = IntegrationQueue.KeyFor("Leave.Submit", id);
        k1.Should().Be(IntegrationQueue.KeyFor("Leave.Submit", id));
        k1.Length.Should().BeLessThanOrEqualTo(50);
        IntegrationQueue.KeyFor("Leave.Submit", id, 1).Should().NotBe(k1);
        IntegrationQueue.KeyFor("HrRequest.Submit.Something.Long", id, 99).Length.Should().BeLessThanOrEqualTo(50);
    }
}

public class ProtocolParsingTests
{
    [Fact]
    public void Parses_odata_v4_page_with_next_link()
    {
        var p = NavODataClient.Parse("""{"value":[{"No":"1"},{"No":"2"}],"@odata.nextLink":"http://x/next"}""");
        p.Rows.Should().HaveCount(2);
        p.NextLink.Should().Be("http://x/next");
    }

    [Fact]
    public void Parses_odata_v3_verbose()
    {
        var p = NavODataClient.Parse("""{"d":{"results":[{"No":"1"}],"__next":"http://x/n"}}""");
        p.Rows.Should().ContainSingle();
        p.NextLink.Should().Be("http://x/n");
    }

    [Fact]
    public void Parses_soap_result_and_out_parameters()
    {
        var xml = """
            <Soap:Envelope xmlns:Soap="http://schemas.xmlsoap.org/soap/envelope/"><Soap:Body>
            <SubmitLeaveApplication_Result xmlns="urn:microsoft-dynamics-schemas/codeunit/ESSPortalGateway">
            <return_value>LV0001</return_value><statusReason>Open</statusReason></SubmitLeaveApplication_Result></Soap:Body></Soap:Envelope>
            """;
        var r = NavSoapClient.ParseResponse(xml, "SubmitLeaveApplication");
        r.ReturnValue.Should().Be("LV0001");
        r.OutParameters["statusReason"].Should().Be("Open");
    }

    [Fact]
    public void Soap_formats_values_invariantly()
    {
        NavSoapClient.Format(new DateOnly(2026, 3, 9)).Should().Be("2026-03-09");
        NavSoapClient.Format(1234.5m).Should().Be("1234.5");
        NavSoapClient.Format(true).Should().Be("true");
    }

    [Fact]
    public void Odata_literal_escapes_quotes() => NavODataClient.Literal("O'Brien").Should().Be("'O''Brien'");

    [Fact]
    public void Field_mapper_applies_overrides_and_treats_nav_blank_date_as_null()
    {
        var map = new NavFieldMapper(new Dictionary<string, string> { ["DepartmentCode"] = "Global_Dimension_1_Code", ["BirthDate"] = "Birth_Date" },
            """{"DepartmentCode":"HR_Department"}""");
        using var doc = System.Text.Json.JsonDocument.Parse("""{"HR_Department":"FIN","Birth_Date":"0001-01-01"}""");
        map.GetString(doc.RootElement, "DepartmentCode").Should().Be("FIN");
        map.GetDate(doc.RootElement, "BirthDate").Should().BeNull();
    }

    [Theory]
    [InlineData(Ess.Domain.Integration.NavPlatform.BusinessCentralOnline, Ess.Domain.Integration.NavAuthMethod.NavUserPassword, false)]
    [InlineData(Ess.Domain.Integration.NavPlatform.BusinessCentralOnline, Ess.Domain.Integration.NavAuthMethod.OAuthClientCredentials, true)]
    [InlineData(Ess.Domain.Integration.NavPlatform.Nav2013, Ess.Domain.Integration.NavAuthMethod.Windows, true)]
    public void Version_profile_auth_rules(Ess.Domain.Integration.NavPlatform p, Ess.Domain.Integration.NavAuthMethod a, bool allowed) =>
        NavVersionProfile.For(p).AllowedAuth.Contains(a).Should().Be(allowed);
}

public class PermissionMatrixTests
{
    [Fact]
    public void Every_system_role_has_a_default_permission_set() =>
        SystemRoles.All.Should().OnlyContain(r => Permissions.DefaultMatrix.ContainsKey(r));

    [Fact]
    public void Matrix_only_uses_known_permissions() =>
        Permissions.DefaultMatrix.Values.SelectMany(v => v).Should().OnlyContain(p => Permissions.All().Contains(p));

    [Fact]
    public void Plain_employees_cannot_see_others_payroll_or_operate_integration()
    {
        var emp = Permissions.DefaultMatrix[SystemRoles.Employee];
        emp.Should().NotContain(new[] { Permissions.PayrollViewAll, Permissions.IntegrationOperate, Permissions.EmployeesViewAll, Permissions.ApprovalsAct });
    }
}
