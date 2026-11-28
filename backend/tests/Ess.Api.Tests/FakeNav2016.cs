using System.Net;
using System.Net.Sockets;
using System.Text;
using Ess.Integration.Nav.Configuration;
using Ess.Integration.Nav.Security;
using Ess.Integration.Nav.Setup;

namespace Ess.Api.Tests;

/// <summary>
/// Answers the connection assistant like a NAV 2016 server "navsrv" with NavUserPassword sign-in (user ESS-SVC, password
/// "Right-Pass-1"), one company, a few published pages, a gateway codeunit with only some of the ESS methods, and $metadata.
/// </summary>
public sealed class FakeNavSetupHttp : NavSetupHttp
{
    public const string Company = "CRONUS Kenya Ltd.";
    public FakeNavSetupHttp(NavAuthenticationManager auth) : base(auth) { }

    public override HttpClient Create(NavConnectionContext ctx) => new(new Handler(ctx));

    public override Task<SocketError?> ProbeTcpAsync(string host, int port, TimeSpan timeout, CancellationToken ct) =>
        Task.FromResult<SocketError?>(host switch
        {
            "navsrv" when port is 7047 or 7048 => null,
            "navsrv" => SocketError.ConnectionRefused,
            _ => SocketError.HostNotFound
        });

    private sealed class Handler : HttpMessageHandler
    {
        private readonly NavConnectionContext _ctx;
        public Handler(NavConnectionContext ctx) => _ctx = ctx;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var auth = req.Headers.Authorization;
            var ok = auth?.Scheme == "Basic" && Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter!)) == "ESS-SVC:Right-Pass-1";
            if (!ok)
            {
                var denied = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                denied.Headers.WwwAuthenticate.ParseAdd("Basic realm=\"\"");
                return Task.FromResult(denied);
            }
            var path = Uri.UnescapeDataString(req.RequestUri!.AbsolutePath);
            var (status, body, type) = path switch
            {
                "/DynamicsNAV90/ODataV4/Company" => (200, $"{{\"value\":[{{\"Name\":\"{Company}\"}}]}}", "application/json"),
                "/DynamicsNAV90/ODataV4/" or "/DynamicsNAV90/ODataV4" => (200,
                    "{\"value\":[{\"name\":\"Company\",\"kind\":\"EntitySet\",\"url\":\"Company\"},{\"name\":\"EmployeeCard\",\"kind\":\"EntitySet\",\"url\":\"EmployeeCard\"}," +
                    "{\"name\":\"CausesofAbsence\",\"kind\":\"EntitySet\",\"url\":\"CausesofAbsence\"},{\"name\":\"HRLeaveBalances\",\"kind\":\"EntitySet\",\"url\":\"HRLeaveBalances\"}]}", "application/json"),
                "/DynamicsNAV90/ODataV4/$metadata" => (200,
                    "<edmx:Edmx xmlns:edmx=\"http://docs.oasis-open.org/odata/ns/edmx\"><edmx:DataServices><Schema Namespace=\"NAV\" xmlns=\"http://docs.oasis-open.org/odata/ns/edm\">" +
                    "<EntityType Name=\"HRLeaveBalances\"><Property Name=\"Staff_No\"/><Property Name=\"Leave_Code\"/><Property Name=\"Leave_Year\"/><Property Name=\"Entitlement\"/><Property Name=\"Remaining_Days\"/></EntityType>" +
                    "<EntityContainer Name=\"NAV\"><EntitySet Name=\"HRLeaveBalances\" EntityType=\"NAV.HRLeaveBalances\"/></EntityContainer></Schema></edmx:DataServices></edmx:Edmx>", "application/xml"),
                $"/DynamicsNAV90/WS/{Company}/Services" => (200,
                    "<discovery xmlns=\"http://schemas.xmlsoap.org/disco/\"><contractRef ref=\"http://navsrv:7047/DynamicsNAV90/WS/CRONUS%20Kenya%20Ltd./Page/EmployeeCard\" xmlns=\"http://schemas.xmlsoap.org/disco/scl/\"/>" +
                    "<contractRef ref=\"http://navsrv:7047/DynamicsNAV90/WS/CRONUS%20Kenya%20Ltd./Codeunit/ESSPortalGateway\" xmlns=\"http://schemas.xmlsoap.org/disco/scl/\"/></discovery>", "text/xml"),
                $"/DynamicsNAV90/WS/{Company}/Codeunit/ESSPortalGateway" => (200,
                    "<definitions xmlns=\"http://schemas.xmlsoap.org/wsdl/\"><portType name=\"ESSPortalGateway_Port\"><operation name=\"SubmitLeaveApplication\"/><operation name=\"GetEmployeeProfile\"/>" +
                    "<operation name=\"FindLeaveByExternalRef\"/></portType></definitions>", "text/xml"),
                _ => (404, "{\"error\":{\"code\":\"BadRequest_NotFound\",\"message\":\"The request URI is not valid.\"}}", "application/json")
            };
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, type) });
        }
    }
}
