using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ess.Domain.Integration;
using Ess.Integration.Nav.Adapters;
using Ess.Integration.Nav.Clients;
using Ess.Integration.Nav.Configuration;
using Ess.Integration.Nav.Security;
using Microsoft.Extensions.Logging;

namespace Ess.Integration.Nav.Setup;

/// <summary>What the administrator typed in the connection assistant. Nothing here is saved until Apply.</summary>
public sealed record NavSetupConnection(
    NavPlatform Platform,
    string? ServerHost,
    string? ServiceInstance,
    bool Https,
    int? SoapPort,
    int? ODataPort,
    string? TenantId,
    string? EnvironmentName,
    NavAuthMethod AuthMethod,
    string? UserName,
    string? Domain,
    string? Password,
    string? ClientId,
    string? ClientSecret,
    bool ValidateServerCertificate = true,
    int TimeoutSeconds = 30,
    string? SoapBaseUrl = null,
    string? ODataBaseUrl = null,
    string? ApiBaseUrl = null);

public sealed record NavSetupCheck(string Step, bool Ok, string Message, string? Hint = null);
public sealed record NavSetupAddresses(string? Soap, string? OData, string? Api);
public sealed record NavSetupCompany(string Name, string DisplayName, Guid? Id);
public sealed record NavConnectResult(bool Ok, NavSetupAddresses Addresses, IReadOnlyList<NavSetupCheck> Checks, IReadOnlyList<NavSetupCompany> Companies);

/// <summary>A web service NAV publishes. Protocol is OData (pages and queries), Soap (codeunits) or BcApi (Business Central API pages).</summary>
public sealed record NavPublishedService(string Name, string Kind, string Protocol);

/// <summary>
/// One portal feature and the NAV service suggested for it. Status: "found" (the ESS service name is published),
/// "suggested" (a likely match by name, to be checked) or "missing" (nothing published fits).
/// </summary>
public sealed record NavFeatureSuggestion(string LogicalName, string Label, string Description, bool Required, string Status,
    NavPublishedService? Service, IReadOnlyList<NavPublishedService> Candidates, string? ModifiedField);

public sealed record NavGatewayMethod(string LogicalName, string Method, string Label, bool Available);
public sealed record NavGatewaySuggestion(string? ServiceName, IReadOnlyList<string> Candidates, IReadOnlyList<NavGatewayMethod> Methods, string? Message);

public sealed record NavDiscoverResult(bool Ok, IReadOnlyList<NavSetupCheck> Checks, IReadOnlyList<NavPublishedService> Services,
    IReadOnlyList<NavFeatureSuggestion> Features, NavGatewaySuggestion Gateway);

/// <summary>
/// One portal field and the NAV field it reads. Source: "default" (NAV has the standard name), "matched" (found under another
/// name, so it goes into the field map), "missing" (not on the published page; the portal leaves it empty).
/// </summary>
public sealed record NavFieldSuggestion(string EssField, string Label, bool Required, string DefaultNavField, string? NavField, string Source);

public sealed record NavFieldsResult(bool Ok, string? Message, IReadOnlyList<string> NavFields, IReadOnlyList<NavFieldSuggestion> Fields,
    string? FieldMapJson, string? ModifiedField);

/// <summary>Creates the HTTP client for assistant calls. A separate class so tests can answer the calls without a NAV server.</summary>
public class NavSetupHttp
{
    private readonly NavAuthenticationManager _auth;
    public NavSetupHttp(NavAuthenticationManager auth) => _auth = auth;
    public virtual HttpClient Create(NavConnectionContext ctx) => _auth.CreateClient(ctx);
    public virtual Task AuthorizeAsync(HttpRequestMessage req, NavConnectionContext ctx, CancellationToken ct) => _auth.AuthorizeAsync(req, ctx, ct);
    public virtual void Forget(Guid settingsId) => _auth.InvalidateToken(settingsId);

    /// <summary>Opens and closes a TCP connection, to tell "nothing listening" and "blocked" apart from sign-in problems.</summary>
    public virtual async Task<SocketError?> ProbeTcpAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
    {
        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(host, port, cts.Token);
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return SocketError.TimedOut; }
        catch (SocketException ex) { return ex.SocketErrorCode; }
    }
}

/// <summary>
/// The NAV / Business Central connection assistant: builds the service addresses from the server name, checks each step
/// (reach the server, sign in, list companies) with a plain explanation when one fails, lists the web services NAV publishes,
/// suggests which one serves each portal feature, and matches field names so the administrator does not have to write
/// field maps by hand. It only reads: service documents, metadata and company lists.
/// </summary>
public sealed class NavSetupAssistant
{
    private const string BcOnlineHost = "api.businesscentral.dynamics.com";
    private readonly NavSetupHttp _http;
    private readonly ILogger<NavSetupAssistant> _log;

    public NavSetupAssistant(NavSetupHttp http, ILogger<NavSetupAssistant> log)
    {
        _http = http;
        _log = log;
    }

    // ------------------------------------------------------------------ features the portal reads

    private sealed record Feature(string LogicalName, string Label, string Description, bool Required, string Preset,
        IReadOnlyDictionary<string, string> ODataDefaults, IReadOnlyDictionary<string, string>? BcDefaults, string[] RequiredFields,
        Func<string, int> Score, string? BcApiService = null);

    private static int Has(string n, params string[] all) => all.All(n.Contains) ? 1 : 0;

    private static readonly Feature[] Features =
    {
        new("Employee", "Employees", "Employee list, directory, managers and linking portal users", true, "ESSEmployees",
            NavEmployeeAdapter.ODataDefaults, NavEmployeeAdapter.BcApiDefaults, new[] { "No" },
            n => n is "employeecard" or "employee" or "employees" ? 3 : n is "employeelist" ? 2
                : n.Contains("employee") && !Regex.IsMatch(n, "absence|relative|qualification|ledger|statistic|picture|address|leave|payslip|payroll") ? 1 : 0,
            BcApiService: "v2.0/employees"),
        new("Department", "Departments", "Department names for the directory, reports and approval routes", false, "ESSDepartments",
            NavOrganizationAdapter.RefOData, NavOrganizationAdapter.RefBc, new[] { "Code" }, n => Has(n, "department") * 2),
        new("Position", "Positions", "Job positions", false, "ESSPositions",
            NavOrganizationAdapter.RefOData, NavOrganizationAdapter.RefBc, new[] { "Code" }, n => Has(n, "position") * 2 + Has(n, "jobtitle")),
        new("Location", "Work locations", "Work locations or branches", false, "ESSLocations",
            NavOrganizationAdapter.RefOData, NavOrganizationAdapter.RefBc, new[] { "Code" }, n => Has(n, "worklocation") * 2 + Has(n, "branch")),
        new("LeaveType", "Leave types", "Annual, sick, maternity... (Causes of Absence in standard NAV)", false, "ESSLeaveTypes",
            NavLeaveAdapter.TypeOData, NavLeaveAdapter.TypeBc, new[] { "Code" },
            n => n.Contains("causesofabsence") || n.Contains("causeofabsence") ? 3 : Has(n, "leave", "type") * 2),
        new("LeaveBalance", "Leave balances", "Entitlement, taken and remaining days per employee", false, "ESSLeaveBalances",
            NavLeaveAdapter.BalOData, NavLeaveAdapter.BalBc, new[] { "EmployeeNo", "LeaveTypeCode" },
            n => Has(n, "leave", "balance") * 3 + Has(n, "leave", "entitlement") * 2),
        new("PayrollPeriod", "Payroll periods", "The months payslips are published for (closed periods only)", false, "ESSPayrollPeriods",
            NavPayrollAdapter.PeriodOData, NavPayrollAdapter.PeriodBc, new[] { "Code" },
            n => Has(n, "payroll", "period") * 3 + Has(n, "payperiod") * 2),
        new("Payslip.Header", "Payslip totals", "Gross, deductions and net pay per employee and period", false, "ESSPayslipHeaders",
            NavPayrollAdapter.HeadOData, NavPayrollAdapter.HeadBc, new[] { "EmployeeNo", "PeriodCode" },
            n => Has(n, "payslip", "header") * 3 + (n.Contains("payslip") && !n.Contains("line") ? 1 : 0)),
        new("Payslip.Lines", "Payslip lines", "Earnings, PAYE, NSSF, SHIF, Housing Levy and other deductions", false, "ESSPayslipLines",
            NavPayrollAdapter.LineOData, NavPayrollAdapter.LineBc, new[] { "EmployeeNo", "PeriodCode", "Amount" },
            n => Has(n, "payslip", "line") * 3 + Has(n, "payroll", "line") * 2 + Has(n, "earningdeduction")),
    };

    private static readonly (string Logical, string Method, string Label)[] GatewayMethods =
    {
        ("Employee.Profile", "GetEmployeeProfile", "Full profile (next of kin, dependants, bank)"),
        ("Leave.Submit", "SubmitLeaveApplication", "Submit leave"),
        ("Leave.Cancel", "CancelLeaveApplication", "Cancel leave"),
        ("Leave.Status", "GetLeaveStatus", "Leave status from NAV"),
        ("Leave.FindByRef", "FindLeaveByExternalRef", "Leave reconciliation"),
        ("Expense.Submit", "SubmitExpenseClaim", "Submit expense claims"),
        ("Expense.Status", "GetClaimStatus", "Claim status from NAV"),
        ("Expense.FindByRef", "FindClaimByExternalRef", "Claim reconciliation"),
    };

    public static IEnumerable<(string Logical, string Method)> GatewayMethodNames => GatewayMethods.Select(g => (g.Logical, g.Method));

    /// <summary>Other spellings of each portal field, compared after removing case, spaces and punctuation.</summary>
    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["No"] = new[] { "no", "number", "employeeno", "code" },
        ["FirstName"] = new[] { "firstname", "givenname", "othernames" },
        ["MiddleName"] = new[] { "middlename" },
        ["LastName"] = new[] { "lastname", "surname", "familyname" },
        ["JobTitle"] = new[] { "jobtitle", "title", "designation", "position" },
        ["PositionCode"] = new[] { "positioncode", "jobcode", "position", "jobtitle" },
        ["DepartmentCode"] = new[] { "departmentcode", "department", "deptcode", "globaldimension1code", "shortcutdimension1code" },
        ["LocationCode"] = new[] { "locationcode", "worklocation", "branchcode", "branch", "globaldimension2code", "shortcutdimension2code" },
        ["ManagerNo"] = new[] { "managerno", "supervisorno", "supervisor", "supervisorcode", "reportsto", "reportstono", "linemanager", "linemanagerno", "manager", "managercode" },
        ["CompanyEmail"] = new[] { "companyemail", "workemail", "officeemail", "email", "emailaddress" },
        ["MobilePhone"] = new[] { "mobilephoneno", "mobilephone", "mobileno", "mobile", "cellphone", "cellphoneno", "phoneno" },
        ["Status"] = new[] { "status", "employmentstatus", "employeestatus" },
        ["EmploymentDate"] = new[] { "employmentdate", "dateofemployment", "dateofjoin", "datejoined", "joiningdate", "hiredate", "startdate" },
        ["TerminationDate"] = new[] { "terminationdate", "dateofleaving", "exitdate", "leavingdate" },
        ["ContractEndDate"] = new[] { "contractenddate", "contractexpirydate", "contractexpiry", "contractend", "enddateofcontract" },
        ["BirthDate"] = new[] { "birthdate", "dateofbirth", "dob" },
        ["Gender"] = new[] { "gender", "sex" },
        ["LastModified"] = new[] { "lastdatemodified", "lastmodifieddatetime", "lastmodifieddate", "systemmodifiedat", "lastmodified", "modifiedat" },
        ["Code"] = new[] { "code", "no" },
        ["Name"] = new[] { "name", "description", "displayname" },
        ["Description"] = new[] { "description", "name" },
        ["ParentCode"] = new[] { "parentcode", "parent", "parentdepartment", "parentdepartmentcode" },
        ["DefaultEntitlement"] = new[] { "totalabsencebase", "defaultentitlement", "entitlement", "maxdays", "days", "noofdays" },
        ["Paid"] = new[] { "paid", "ispaid", "paidleave" },
        ["EmployeeNo"] = new[] { "employeeno", "employeenumber", "staffno", "employeecode", "empno", "employee" },
        ["LeaveTypeCode"] = new[] { "leavetype", "leavetypecode", "leavecode", "causeofabsencecode", "causeofabsence", "absencecode" },
        ["PeriodCode"] = new[] { "leaveperiod", "payrollperiod", "periodcode", "period", "leaveyear", "year", "payperiod" },
        ["Entitlement"] = new[] { "entitlement", "daysentitled", "entitled", "allocated", "allocation" },
        ["BroughtForward"] = new[] { "broughtforward", "carriedforward", "balancebf", "bf", "carryforward" },
        ["Taken"] = new[] { "daystaken", "taken", "used", "daysused" },
        ["Pending"] = new[] { "dayspending", "pending" },
        ["Available"] = new[] { "balance", "remaining", "remainingdays", "available", "daysremaining" },
        ["Start"] = new[] { "startingdate", "startdate", "periodstart", "fromdate" },
        ["End"] = new[] { "endingdate", "enddate", "periodend", "todate" },
        ["PayDate"] = new[] { "paydate", "paymentdate" },
        ["Closed"] = new[] { "closed", "isclosed", "status" },
        ["Currency"] = new[] { "currencycode", "currency" },
        ["Basic"] = new[] { "basicpay", "basicsalary", "basic" },
        ["Gross"] = new[] { "grosspay", "gross", "grosssalary" },
        ["Deductions"] = new[] { "totaldeductions", "deductions" },
        ["Net"] = new[] { "netpay", "net", "netsalary" },
        ["PaymentDate"] = new[] { "paymentdate", "paydate" },
        ["Group"] = new[] { "type", "group", "category", "edtype", "linetype" },
        ["Amount"] = new[] { "amount", "value" },
        ["YTD"] = new[] { "ytdamount", "yeartodate", "ytd", "cumulativeamount" },
        ["Sort"] = new[] { "lineno", "sortorder", "sequence", "order" },
    };

    // ------------------------------------------------------------------ addresses

    public static NavSetupAddresses ResolveAddresses(NavSetupConnection c, out string? problem)
    {
        problem = null;
        string? soap = null, odata = null, api = null;
        if (c.Platform == NavPlatform.BusinessCentralOnline)
        {
            if (string.IsNullOrWhiteSpace(c.TenantId)) problem = "Enter the Microsoft Entra tenant ID (or the tenant's domain, e.g. contoso.onmicrosoft.com).";
            else
            {
                var env = string.IsNullOrWhiteSpace(c.EnvironmentName) ? "Production" : c.EnvironmentName.Trim();
                var root = $"https://{BcOnlineHost}/v2.0/{Uri.EscapeDataString(c.TenantId.Trim())}/{Uri.EscapeDataString(env)}";
                (soap, odata, api) = ($"{root}/WS", $"{root}/ODataV4", root);
            }
        }
        else
        {
            var host = CleanHost(c.ServerHost, out var portInHost);
            if (host is null) problem = "Enter the name of the server NAV runs on, e.g. NAVSERVER or navserver.company.local.";
            else
            {
                var inst = string.IsNullOrWhiteSpace(c.ServiceInstance)
                    ? NavVersionProfile.DefaultInstance(c.Platform) ?? "BC"
                    : c.ServiceInstance.Trim().Trim('/');
                var scheme = c.Https ? "https" : "http";
                var soapPort = c.SoapPort ?? 7047;
                var odataPort = c.ODataPort ?? portInHost ?? 7048;
                var odataPath = NavVersionProfile.For(c.Platform).ODataVersion == "v3" ? "OData" : "ODataV4";
                soap = $"{scheme}://{host}:{soapPort}/{inst}/WS";
                odata = $"{scheme}://{host}:{odataPort}/{inst}/{odataPath}";
                if (c.Platform == NavPlatform.BusinessCentralOnPrem) api = $"{scheme}://{host}:{odataPort}/{inst}";
            }
        }
        static string? Trimmed(string? u) => string.IsNullOrWhiteSpace(u) ? null : u.Trim().TrimEnd('/');
        return new NavSetupAddresses(Trimmed(c.SoapBaseUrl) ?? soap, Trimmed(c.ODataBaseUrl) ?? odata, Trimmed(c.ApiBaseUrl) ?? api);
    }

    /// <summary>Accepts "NAVSRV", "navsrv:7048", "http://navsrv:7048/DynamicsNAV90/..." and returns just the host.</summary>
    internal static string? CleanHost(string? input, out int? port)
    {
        port = null;
        var s = input?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (!s.Contains("://")) s = "http://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || string.IsNullOrEmpty(u.Host)) return null;
        if (!u.IsDefaultPort) port = u.Port;
        return u.Host;
    }

    // ------------------------------------------------------------------ step 1: connect

    public async Task<NavConnectResult> ConnectAsync(NavSetupConnection c, CancellationToken ct)
    {
        var addr = ResolveAddresses(c, out var problem);
        var checks = new List<NavSetupCheck>();
        if (problem is not null)
        {
            checks.Add(new("Server address", false, problem));
            return new(false, addr, checks, Array.Empty<NavSetupCompany>());
        }

        var profile = NavVersionProfile.For(c.Platform);
        if (!profile.AllowedAuth.Contains(c.AuthMethod))
        {
            checks.Add(new("Sign-in", false, $"This version does not support {AuthName(c.AuthMethod)}.",
                $"Choose one of: {string.Join(", ", profile.AllowedAuth.Select(AuthName))}."));
            return new(false, addr, checks, Array.Empty<NavSetupCompany>());
        }
        var credentialProblem = CredentialProblem(c);
        if (credentialProblem is not null)
        {
            checks.Add(new("Sign-in", false, credentialProblem));
            return new(false, addr, checks, Array.Empty<NavSetupCompany>());
        }

        // 1. Can we reach the ports at all?
        var mainUrl = new Uri(c.Platform == NavPlatform.BusinessCentralOnline ? addr.Api! : addr.OData!);
        var reach = await ReachAsync(mainUrl, c.Platform == NavPlatform.BusinessCentralOnline ? "Business Central" : "OData", ct);
        checks.Add(reach);
        if (!reach.Ok) return new(false, addr, checks, Array.Empty<NavSetupCompany>());
        if (c.Platform != NavPlatform.BusinessCentralOnline && addr.Soap is not null)
        {
            var soapUri = new Uri(addr.Soap);
            if (soapUri.Port != mainUrl.Port || soapUri.Host != mainUrl.Host)
            {
                var soapReach = await ReachAsync(soapUri, "SOAP", ct);
                // Reading works without SOAP; only submissions need it, so this is a warning, not a stop.
                checks.Add(soapReach.Ok ? soapReach : soapReach with { Hint = (soapReach.Hint + " Reading from NAV works without SOAP; submitting leave and claims needs it.").Trim() });
            }
        }

        // 2. Sign in and list companies.
        var ctx = Context(c, addr, "", null);
        try
        {
            using var http = _http.Create(ctx);
            var companies = new List<NavSetupCompany>();
            NavSetupCheck signIn;
            if (c.Platform is (NavPlatform.BusinessCentralOnPrem or NavPlatform.BusinessCentralOnline) && addr.Api is not null)
            {
                var r = await GetAsync(http, ctx, $"{addr.Api}/api/v2.0/companies", ct);
                if (r.Status == 404 && c.Platform == NavPlatform.BusinessCentralOnPrem)
                    r = await GetAsync(http, ctx, $"{addr.OData}/Company", ct); // APIs turned off: OData still lists companies
                signIn = Explain(r, c, "list the companies");
                if (signIn.Ok) companies.AddRange(ParseCompanies(r.Body!));
            }
            else
            {
                var url = $"{addr.OData}/Company" + (profile.ODataVersion == "v3" ? "?$format=json" : "");
                var r = await GetAsync(http, ctx, url, ct);
                signIn = Explain(r, c, "list the companies");
                if (signIn.Ok) companies.AddRange(ParseCompanies(r.Body!));
            }
            checks.Add(signIn.Ok ? signIn with { Message = $"Signed in as {SignedInAs(c)}." } : signIn);
            if (!signIn.Ok) return new(false, addr, checks, Array.Empty<NavSetupCompany>());

            if (companies.Count == 0)
            {
                checks.Add(new("Companies", false, "NAV answered, but this user cannot see any company.",
                    "Give the user access to the company in NAV (User Setup / permission sets limited to a company), then try again."));
                return new(false, addr, checks, companies);
            }
            checks.Add(new("Companies", true, companies.Count == 1 ? $"Found company \"{companies[0].DisplayName}\"." : $"Found {companies.Count} companies."));
            return new(true, addr, checks, companies);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            checks.Add(FromException("Sign-in", ex, c));
            return new(false, addr, checks, Array.Empty<NavSetupCompany>());
        }
        finally { _http.Forget(ctx.SettingsId); }
    }

    // ------------------------------------------------------------------ step 2: discover services

    public async Task<NavDiscoverResult> DiscoverAsync(NavSetupConnection c, string company, Guid? companyId, CancellationToken ct)
    {
        var addr = ResolveAddresses(c, out var problem);
        var checks = new List<NavSetupCheck>();
        var noGateway = new NavGatewaySuggestion(null, Array.Empty<string>(), GatewayMethods.Select(g => new NavGatewayMethod(g.Logical, g.Method, g.Label, false)).ToList(), null);
        if (problem is not null)
        {
            checks.Add(new("Server address", false, problem));
            return new(false, checks, Array.Empty<NavPublishedService>(), Array.Empty<NavFeatureSuggestion>(), noGateway);
        }
        var ctx = Context(c, addr, company, companyId);
        var services = new List<NavPublishedService>();
        try
        {
            using var http = _http.Create(ctx);

            // Pages and queries: the OData service document lists every published one.
            if (addr.OData is not null)
            {
                var v3 = NavVersionProfile.For(c.Platform).ODataVersion == "v3";
                var r = await GetAsync(http, ctx, addr.OData + "/" + (v3 ? "?$format=json" : ""), ct);
                var check = Explain(r, c, "list the published pages");
                if (check.Ok)
                {
                    var names = ParseServiceDocument(r.Body!).Where(n => !n.Equals("Company", StringComparison.OrdinalIgnoreCase)).ToList();
                    services.AddRange(names.Select(n => new NavPublishedService(n, "Page or query", "OData")));
                    checks.Add(new("Published pages", true, names.Count == 0
                        ? "NAV publishes no pages or queries yet."
                        : $"NAV publishes {names.Count} page{(names.Count == 1 ? "" : "s")} or quer{(names.Count == 1 ? "y" : "ies")}."));
                }
                else checks.Add(check with { Step = "Published pages" });
            }

            // Codeunits: the SOAP discovery document of the company.
            if (addr.Soap is not null)
            {
                var r = await GetAsync(http, ctx, $"{addr.Soap}/{Uri.EscapeDataString(company)}/Services", ct);
                if (r.Status is >= 200 and < 300 && r.Body is not null)
                {
                    var units = ParseSoapDiscovery(r.Body).Where(s => s.Kind == "Codeunit").ToList();
                    services.AddRange(units);
                    checks.Add(new("Published codeunits", true, units.Count == 0 ? "NAV publishes no codeunits yet." : $"NAV publishes {units.Count} codeunit{(units.Count == 1 ? "" : "s")}."));
                }
                else
                {
                    var check = Explain(r, c, "list the published codeunits");
                    checks.Add(check with { Step = "Published codeunits", Hint = (check.Hint + " Reading still works; submitting leave and claims needs SOAP.").Trim() });
                }
            }

            // Business Central: the standard employees API page.
            if (c.Platform is (NavPlatform.BusinessCentralOnPrem or NavPlatform.BusinessCentralOnline) && addr.Api is not null && companyId is not null)
            {
                var r = await GetAsync(http, ctx, $"{addr.Api}/api/v2.0/companies({companyId})/employees?$top=1", ct);
                if (r.Status is >= 200 and < 300) services.Add(new NavPublishedService("v2.0/employees", "API page", "BcApi"));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            checks.Add(FromException("Published services", ex, c));
            return new(false, checks, services, Array.Empty<NavFeatureSuggestion>(), noGateway);
        }
        finally { _http.Forget(ctx.SettingsId); }

        var features = Features.Select(f => Suggest(f, services)).ToList();
        var gateway = await SuggestGatewayAsync(c, company, companyId, services.Where(s => s.Kind == "Codeunit").Select(s => s.Name).ToList(), null, ct);
        return new(checks.All(x => x.Ok) || services.Count > 0, checks, services, features, gateway);
    }

    private static NavFeatureSuggestion Suggest(Feature f, IReadOnlyList<NavPublishedService> services)
    {
        var reads = services.Where(s => s.Protocol is "OData" or "BcApi").ToList();
        var ranked = reads
            .Select(s => (s, score: s.Name.Equals(f.Preset, StringComparison.OrdinalIgnoreCase) ? 100
                : s.Protocol == "BcApi" ? (s.Name == f.BcApiService ? 50 : 0)
                : f.Score(Norm(s.Name))))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenBy(x => x.s.Name.Length)
            .Select(x => x.s).ToList();
        var best = ranked.FirstOrDefault();
        var status = best is null ? "missing" : best.Name.Equals(f.Preset, StringComparison.OrdinalIgnoreCase) ? "found" : "suggested";
        var defaults = best?.Protocol == "BcApi" ? f.BcDefaults ?? f.ODataDefaults : f.ODataDefaults;
        return new(f.LogicalName, f.Label, f.Description, f.Required, status, best, ranked.Take(8).ToList(),
            defaults.TryGetValue("LastModified", out var lm) ? lm : null);
    }

    /// <summary>Picks the gateway codeunit (or checks the chosen one) and lists which ESS methods it has, from its WSDL.</summary>
    public async Task<NavGatewaySuggestion> SuggestGatewayAsync(NavSetupConnection c, string company, Guid? companyId,
        IReadOnlyList<string> codeunits, string? chosen, CancellationToken ct)
    {
        var none = GatewayMethods.Select(g => new NavGatewayMethod(g.Logical, g.Method, g.Label, false)).ToList();
        var pick = chosen
                   ?? codeunits.FirstOrDefault(n => n.Equals(NavEndpointPresets.Gateway, StringComparison.OrdinalIgnoreCase))
                   ?? codeunits.FirstOrDefault(n => Norm(n).Contains("essportal") || (Norm(n).Contains("ess") && Norm(n).Contains("gateway")));
        if (pick is null)
            return new(null, codeunits, none, codeunits.Count == 0
                ? "No codeunit is published yet, so the portal can read from NAV but cannot submit leave, claims or profile changes until the ESS Portal Gateway codeunit is imported and published."
                : "None of the published codeunits looks like the ESS Portal Gateway. Pick it if it has another name.");

        var addr = ResolveAddresses(c, out _);
        if (addr.Soap is null) return new(pick, codeunits, none, "There is no SOAP address.");
        var ctx = Context(c, addr, company, companyId);
        try
        {
            using var http = _http.Create(ctx);
            var r = await GetAsync(http, ctx, $"{addr.Soap}/{Uri.EscapeDataString(company)}/Codeunit/{Uri.EscapeDataString(pick)}", ct);
            if (r.Status is not (>= 200 and < 300) || r.Body is null)
                return new(pick, codeunits, none, Explain(r, c, $"read codeunit {pick}").Message);
            var ops = ParseWsdlOperations(r.Body);
            var methods = GatewayMethods.Select(g => new NavGatewayMethod(g.Logical, g.Method, g.Label, ops.Contains(g.Method))).ToList();
            var missing = methods.Count(m => !m.Available);
            return new(pick, codeunits, methods, missing == 0 ? null
                : $"{pick} is missing {missing} of the {methods.Count} ESS methods; those features stay off until it has them.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new(pick, codeunits, none, FromException("Gateway", ex, c).Message);
        }
        finally { _http.Forget(ctx.SettingsId); }
    }

    // ------------------------------------------------------------------ step 3: fields

    public async Task<NavFieldsResult> FieldsAsync(NavSetupConnection c, string company, Guid? companyId, string logicalName, string protocol,
        string serviceName, CancellationToken ct)
    {
        var f = Features.FirstOrDefault(x => x.LogicalName.Equals(logicalName, StringComparison.OrdinalIgnoreCase));
        if (f is null) return new(false, $"Unknown feature '{logicalName}'.", Array.Empty<string>(), Array.Empty<NavFieldSuggestion>(), null, null);
        var bc = protocol.Equals("BcApi", StringComparison.OrdinalIgnoreCase);
        var defaults = bc ? f.BcDefaults ?? f.ODataDefaults : f.ODataDefaults;

        var addr = ResolveAddresses(c, out var problem);
        if (problem is not null) return new(false, problem, Array.Empty<string>(), Array.Empty<NavFieldSuggestion>(), null, null);
        var ctx = Context(c, addr, company, companyId);
        ctx = WithEndpoint(ctx, logicalName, bc ? "BcApi" : "OData", serviceName);
        List<string> navFields;
        try
        {
            using var http = _http.Create(ctx);
            navFields = bc ? new List<string>() : await MetadataFieldsAsync(http, ctx, addr.OData!, serviceName, ct);
            if (navFields.Count == 0)
            {
                // No metadata (or a BC API page): read one row and take its field names. The values are not kept or returned.
                var url = NavODataClient.BuildUrl(ctx, bc ? NavProtocol.BcApi : NavProtocol.OData, serviceName, new NavODataClient.Query(Top: 1));
                var r = await GetAsync(http, ctx, url, ct);
                var check = Explain(r, c, $"read {serviceName}");
                if (!check.Ok) return new(false, check.Message + (check.Hint is null ? "" : " " + check.Hint), Array.Empty<string>(), Array.Empty<NavFieldSuggestion>(), null, null);
                var page = NavODataClient.Parse(r.Body!);
                if (page.Rows.Count == 0)
                    return new(false, $"{serviceName} has no rows and NAV did not describe its fields, so they cannot be matched yet. Keep the standard names or add a record and try again.",
                        Array.Empty<string>(), Array.Empty<NavFieldSuggestion>(), null, null);
                navFields = page.Rows[0].EnumerateObject().Select(p => p.Name).Where(n => !n.StartsWith('@')).ToList();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var check = FromException("Fields", ex, c);
            return new(false, check.Message, Array.Empty<string>(), Array.Empty<NavFieldSuggestion>(), null, null);
        }
        finally { _http.Forget(ctx.SettingsId); }

        var fields = MatchFields(defaults, navFields, f.RequiredFields);
        var lastModified = fields.FirstOrDefault(x => x.EssField == "LastModified")?.NavField;
        return new(true, null, navFields, fields, BuildFieldMap(fields), lastModified);
    }

    /// <summary>Matches each portal field to a NAV field: the standard name first, then the portal name, then known other spellings.</summary>
    public static IReadOnlyList<NavFieldSuggestion> MatchFields(IReadOnlyDictionary<string, string> defaults, IReadOnlyList<string> navFields, IEnumerable<string> required)
    {
        var req = required.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byNorm = new Dictionary<string, string>();
        foreach (var n in navFields) byNorm.TryAdd(Norm(n), n);
        var list = new List<NavFieldSuggestion>();
        foreach (var (ess, def) in defaults)
        {
            if (ess == "VersionToken") continue; // the record version comes from the OData ETag automatically
            string? nav = null;
            var source = "missing";
            if (navFields.Contains(def)) { nav = def; source = "default"; }
            else
            {
                foreach (var candidate in new[] { Norm(def), Norm(ess) }.Concat(Synonyms.GetValueOrDefault(ess) ?? Array.Empty<string>()))
                    if (byNorm.TryGetValue(candidate, out var hit)) { nav = hit; source = hit == def ? "default" : "matched"; break; }
            }
            list.Add(new NavFieldSuggestion(ess, Label(ess), req.Contains(ess), def, nav, source));
        }
        return list;
    }

    /// <summary>The field map JSON (only the fields that differ from the standard names), or null when none differ.</summary>
    public static string? BuildFieldMap(IEnumerable<NavFieldSuggestion> fields)
    {
        var map = fields.Where(f => f.NavField is not null && f.NavField != f.DefaultNavField).ToDictionary(f => f.EssField, f => f.NavField!);
        return map.Count == 0 ? null : JsonSerializer.Serialize(map);
    }

    private async Task<List<string>> MetadataFieldsAsync(HttpClient http, NavConnectionContext ctx, string odataBase, string service, CancellationToken ct)
    {
        try
        {
            var r = await GetAsync(http, ctx, $"{odataBase}/$metadata", ct, accept: "application/xml");
            if (r.Status is not (>= 200 and < 300) || string.IsNullOrEmpty(r.Body)) return new();
            var doc = XDocument.Parse(r.Body);
            var set = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "EntitySet" && (string?)e.Attribute("Name") == service);
            var typeName = ((string?)set?.Attribute("EntityType"))?.Split('.').Last();
            if (typeName is null) return new();
            var type = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "EntityType" && (string?)e.Attribute("Name") == typeName);
            return type?.Elements().Where(e => e.Name.LocalName == "Property").Select(e => (string)e.Attribute("Name")!).ToList() ?? new();
        }
        catch (System.Xml.XmlException) { return new(); }
    }

    // ------------------------------------------------------------------ parsing

    internal static IEnumerable<NavSetupCompany> ParseCompanies(string body)
    {
        var page = NavODataClient.Parse(body);
        foreach (var row in page.Rows)
        {
            string? Str(params string[] names)
            {
                foreach (var n in names)
                    if (row.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())) return v.GetString();
                return null;
            }
            var name = Str("name", "Name");
            if (name is null) continue;
            Guid? id = Guid.TryParse(Str("id", "Id"), out var g) ? g : null;
            yield return new NavSetupCompany(name, Str("displayName", "Display_Name") ?? name, id);
        }
    }

    internal static IReadOnlyList<string> ParseServiceDocument(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        IEnumerable<JsonElement> items = Array.Empty<JsonElement>();
        if (root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array) items = v.EnumerateArray();
        else if (root.TryGetProperty("d", out var d) && d.TryGetProperty("EntitySets", out var sets)) // OData v3 verbose
            return sets.EnumerateArray().Select(e => e.GetString()!).Where(s => s is not null).ToList();
        else if (root.TryGetProperty("EntitySets", out var sets3)) return sets3.EnumerateArray().Select(e => e.GetString()!).ToList();
        return items
            .Where(e => !e.TryGetProperty("kind", out var k) || k.GetString() == "EntitySet")
            .Select(e => e.TryGetProperty("name", out var n) ? n.GetString() : null)
            .Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();
    }

    internal static IReadOnlyList<NavPublishedService> ParseSoapDiscovery(string xml)
    {
        var list = new List<NavPublishedService>();
        foreach (Match m in Regex.Matches(xml, @"ref=""[^""]*/(?<kind>Page|Codeunit|Query)/(?<name>[^""/?]+)"""))
        {
            var name = Uri.UnescapeDataString(m.Groups["name"].Value);
            if (list.Any(x => x.Name == name && x.Kind == m.Groups["kind"].Value)) continue;
            list.Add(new NavPublishedService(name, m.Groups["kind"].Value, m.Groups["kind"].Value == "Codeunit" ? "Soap" : "OData"));
        }
        return list;
    }

    internal static HashSet<string> ParseWsdlOperations(string wsdl)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var op in XDocument.Parse(wsdl).Descendants().Where(e => e.Name.LocalName == "operation"))
                if ((string?)op.Attribute("name") is { } n) set.Add(n);
        }
        catch (System.Xml.XmlException) { }
        return set;
    }

    // ------------------------------------------------------------------ HTTP and explanations

    private sealed record Answer(int? Status, string? Body, string[] OfferedAuth);

    private async Task<Answer> GetAsync(HttpClient http, NavConnectionContext ctx, string url, CancellationToken ct, string accept = "application/json")
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", accept);
        await _http.AuthorizeAsync(req, ctx, ct);
        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        var offered = resp.Headers.WwwAuthenticate.Select(h => h.Scheme).ToArray();
        return new Answer((int)resp.StatusCode, body, offered);
    }

    private async Task<NavSetupCheck> ReachAsync(Uri url, string what, CancellationToken ct)
    {
        var error = await _http.ProbeTcpAsync(url.Host, url.Port, TimeSpan.FromSeconds(8), ct);
        var where = $"{url.Host}:{url.Port}";
        return error switch
        {
            null => new($"Reach the {what} port", true, $"{where} answers."),
            SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => new($"Reach the {what} port", false,
                $"The server name \"{url.Host}\" was not found.", "Check the spelling, or use the server's IP address or full name (for example navserver.company.local)."),
            SocketError.ConnectionRefused => new($"Reach the {what} port", false,
                $"{url.Host} is reachable, but nothing is listening on port {url.Port}.",
                what == "SOAP"
                    ? "Check that SOAP Services are enabled on the NAV server instance and the port (Server Administration, SOAP Services tab; default 7047), and that the service is running."
                    : "Check that the NAV server instance is running, that OData Services are enabled with \"Enable OData V4 Endpoint\" ticked, and the port (default 7048)."),
            SocketError.TimedOut => new($"Reach the {what} port", false,
                $"No answer from {where} within 8 seconds.", "A firewall is probably blocking the port. On the NAV server, allow inbound TCP 7047 and 7048 (see the guide), or check the server name."),
            _ => new($"Reach the {what} port", false, $"Could not connect to {where} ({error}).", "Check the server name, the port and the firewall.")
        };
    }

    private static NavSetupCheck Explain(Answer r, NavSetupConnection c, string action)
    {
        var step = "Sign-in";
        if (r.Status is >= 200 and < 300) return new(step, true, "OK");
        var navMessage = NavMessage(r.Body);
        var windowsOffered = r.OfferedAuth.Any(a => a is "Negotiate" or "NTLM");
        var basicOffered = r.OfferedAuth.Any(a => a.Equals("Basic", StringComparison.OrdinalIgnoreCase));
        var bearerOffered = r.OfferedAuth.Any(a => a.Equals("Bearer", StringComparison.OrdinalIgnoreCase));
        switch (r.Status)
        {
            case 401 when c.AuthMethod == NavAuthMethod.Windows && basicOffered && !windowsOffered:
                return new(step, false, "NAV is set up for user name and password sign-in, not Windows.",
                    "Choose \"NAV user name and password\" (or \"Web service access key\") and enter the NAV user. (ClientServicesCredentialType is NavUserPassword on this instance.)");
            case 401 when c.AuthMethod is (NavAuthMethod.NavUserPassword or NavAuthMethod.WebServiceAccessKey) && windowsOffered && !basicOffered:
                return new(step, false, "NAV is set up for Windows sign-in, not user name and password.",
                    "Choose \"Windows\". Leave the user empty to sign in as the portal's own Windows account, or enter a domain account.");
            case 401 when c.AuthMethod == NavAuthMethod.OAuthClientCredentials && !bearerOffered && (windowsOffered || basicOffered):
                return new(step, false, "This NAV server does not accept Microsoft Entra (OAuth) sign-in.",
                    windowsOffered ? "Choose \"Windows\"." : "Choose \"NAV user name and password\".");
            case 401 when c.AuthMethod == NavAuthMethod.Windows && string.IsNullOrEmpty(c.UserName):
                return new(step, false, $"NAV did not accept the portal's Windows account ({ProcessAccount()}).",
                    $"In NAV, add {ProcessAccount()} as a Windows user (Users page, or New-NAVServerUser) with the ESS-PORTAL permission set, or enter another Windows account here.");
            case 401 when c.AuthMethod == NavAuthMethod.Windows:
                return new(step, false, $"NAV did not accept the Windows account {Account(c)}.",
                    "Check the domain, user name and password, and that the account is a NAV user (Users page).");
            case 401 when c.AuthMethod == NavAuthMethod.WebServiceAccessKey:
                return new(step, false, $"NAV did not accept the user {c.UserName} with that web service access key.",
                    "Copy the key from the user's card in NAV (Web Service Access Key field) and check it has not expired.");
            case 401 when c.AuthMethod == NavAuthMethod.OAuthClientCredentials:
                return new(step, false, "Business Central did not accept the app's token.",
                    "In Business Central, open \"Microsoft Entra Applications\", add the client ID, set its state to Enabled and give it the ESS-PORTAL permission set.");
            case 401:
                return new(step, false, $"NAV did not accept the user name or password for {c.UserName}.",
                    "Check the NAV user name and password. The user must exist in NAV with a password (NavUserPassword) and must not be disabled.");
            case 403:
                return new(step, false, $"Signed in, but the user may not {action}.", navMessage ?? "Give the user the ESS-PORTAL permission set (read on the published pages, execute on the gateway codeunit).");
            case 404:
                return new("Server address", false, $"NAV answered but could not find what was asked for ({action}).",
                    c.Platform == NavPlatform.BusinessCentralOnline
                        ? "Check the tenant ID and the environment name (Production or the sandbox's name)."
                        : $"Check the server instance name (NAV 2016's default is DynamicsNAV90; Server Administration shows yours) and that \"Enable OData V4 Endpoint\" is ticked.{(navMessage is null ? "" : " NAV said: " + navMessage)}");
            case 400 or 500 when navMessage is not null:
                return new(step, false, $"NAV refused to {action}: {navMessage}", null);
            default:
                return new(step, false, $"NAV answered HTTP {r.Status} when asked to {action}.", navMessage);
        }
    }

    private static NavSetupCheck FromException(string step, Exception ex, NavSetupConnection c)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is AuthenticationException)
                return new(step, false, "The NAV server's HTTPS certificate is not trusted by this server.",
                    "Install the certificate (or its issuing CA) in the portal server's Trusted Root store, or, for an internal self-signed certificate, untick \"Check the NAV server's certificate\".");
        }
        return ex switch
        {
            TaskCanceledException => new(step, false, "NAV did not answer in time.", "The server may be busy or a proxy may be holding the request. Try again, or raise the timeout."),
            NavConfigurationException n => new(step, false, n.Message),
            HttpRequestException h when h.StatusCode == HttpStatusCode.Unauthorized && c.AuthMethod == NavAuthMethod.OAuthClientCredentials =>
                new(step, false, "Microsoft Entra ID did not accept the tenant, client ID or client secret.", "Check all three, and that the secret has not expired."),
            HttpRequestException h when h.Message.Contains("plain HTTP", StringComparison.OrdinalIgnoreCase) || h.InnerException is IOException =>
                new(step, false, "The connection was dropped. HTTPS may be on where NAV expects HTTP, or the other way round.", "Switch HTTPS off or on to match the NAV server's SSL setting."),
            HttpRequestException h => new(step, false, $"Could not talk to NAV: {h.Message}"),
            _ => new(step, false, ex.Message)
        };
    }

    private static string? NavMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var fault = Regex.Match(body, "<faultstring[^>]*>(?<m>[^<]*)</faultstring>");
        if (fault.Success) return WebUtility.HtmlDecode(fault.Groups["m"].Value);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg)) return msg.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    private static string? CredentialProblem(NavSetupConnection c) => c.AuthMethod switch
    {
        NavAuthMethod.NavUserPassword when string.IsNullOrWhiteSpace(c.UserName) => "Enter the NAV user name.",
        NavAuthMethod.NavUserPassword when string.IsNullOrEmpty(c.Password) => "Enter the NAV user's password.",
        NavAuthMethod.WebServiceAccessKey when string.IsNullOrWhiteSpace(c.UserName) => "Enter the NAV user name.",
        NavAuthMethod.WebServiceAccessKey when string.IsNullOrEmpty(c.Password) => "Enter the web service access key from the user's card in NAV.",
        NavAuthMethod.Windows when !string.IsNullOrWhiteSpace(c.UserName) && string.IsNullOrEmpty(c.Password) => "Enter the Windows account's password, or leave the user empty to use the portal's own account.",
        NavAuthMethod.OAuthClientCredentials when string.IsNullOrWhiteSpace(c.TenantId) || string.IsNullOrWhiteSpace(c.ClientId) || string.IsNullOrEmpty(c.ClientSecret)
            => "Enter the tenant ID, client ID and client secret of the Microsoft Entra app.",
        _ => null
    };

    public static string AuthName(NavAuthMethod m) => m switch
    {
        NavAuthMethod.Windows => "Windows",
        NavAuthMethod.NavUserPassword => "NAV user name and password",
        NavAuthMethod.WebServiceAccessKey => "Web service access key",
        NavAuthMethod.OAuthClientCredentials => "Microsoft Entra app (OAuth)",
        _ => m.ToString()
    };

    private static string ProcessAccount() => $"{Environment.UserDomainName}\\{Environment.UserName}";
    private static string Account(NavSetupConnection c) => string.IsNullOrWhiteSpace(c.Domain) ? c.UserName ?? "" : $"{c.Domain}\\{c.UserName}";
    private static string SignedInAs(NavSetupConnection c) => c.AuthMethod switch
    {
        NavAuthMethod.Windows when string.IsNullOrWhiteSpace(c.UserName) => ProcessAccount(),
        NavAuthMethod.Windows => Account(c),
        NavAuthMethod.OAuthClientCredentials => $"app {c.ClientId}",
        _ => c.UserName ?? ""
    };

    private static string Norm(string s) => Regex.Replace(s.Replace("_x0020_", "").ToLowerInvariant(), "[^a-z0-9]", "");

    private static string Label(string ess) => Regex.Replace(ess, "(?<=[a-z])(?=[A-Z])", " ") switch
    {
        "No" => "Employee number", "YTD" => "Year to date", "Group" => "Line type", "Sort" => "Order", "Available" => "Balance", var s => s
    };

    private NavConnectionContext Context(NavSetupConnection c, NavSetupAddresses a, string company, Guid? companyId)
    {
        var settings = new NavIntegrationSettings
        {
            Id = Guid.NewGuid(),
            Name = "setup-assistant",
            Platform = c.Platform,
            SoapBaseUrl = a.Soap,
            ODataBaseUrl = a.OData,
            ApiBaseUrl = a.Api,
            TenantId = c.TenantId?.Trim(),
            EnvironmentName = c.EnvironmentName?.Trim(),
            AuthMethod = c.AuthMethod,
            UserName = string.IsNullOrWhiteSpace(c.UserName) ? null : c.UserName.Trim(),
            Domain = string.IsNullOrWhiteSpace(c.Domain) ? null : c.Domain.Trim(),
            ClientId = c.ClientId?.Trim(),
            TimeoutSeconds = Math.Clamp(c.TimeoutSeconds, 5, 120),
            ValidateServerCertificate = c.ValidateServerCertificate,
            MaxConcurrency = 2,
            CreatedAtUtc = DateTime.UtcNow
        };
        return new NavConnectionContext
        {
            CompanyId = Guid.Empty,
            SettingsId = settings.Id,
            Settings = settings,
            Profile = NavVersionProfile.For(c.Platform),
            NavCompanyName = company,
            NavCompanyGuid = companyId,
            Endpoints = new Dictionary<string, NavEndpointMapping>(StringComparer.OrdinalIgnoreCase),
            Password = c.Password,
            ClientSecret = c.ClientSecret
        };
    }

    private static NavConnectionContext WithEndpoint(NavConnectionContext ctx, string logical, string protocol, string service) => new()
    {
        CompanyId = ctx.CompanyId, SettingsId = ctx.SettingsId, Settings = ctx.Settings, Profile = ctx.Profile, NavCompanyName = ctx.NavCompanyName,
        NavCompanyGuid = ctx.NavCompanyGuid, Password = ctx.Password, ClientSecret = ctx.ClientSecret,
        Endpoints = new Dictionary<string, NavEndpointMapping>(StringComparer.OrdinalIgnoreCase)
        {
            [logical] = new NavEndpointMapping { IntegrationSettingsId = ctx.SettingsId, LogicalName = logical, Protocol = protocol, ServiceName = service }
        }
    };
}
