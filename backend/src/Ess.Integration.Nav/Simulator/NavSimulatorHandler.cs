using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Ess.Integration.Nav.Simulator;

/// <summary>
/// DEVELOPMENT/TEST ONLY. An in-process stand-in for a NAV 2018 service tier that answers OData v4 and SOAP calls
/// with clearly fictitious data, so the full integration engine (URLs, auth headers, paging, SOAP envelopes, faults,
/// idempotency, reconciliation) can be exercised without a NAV server. It is refused in Production and every record
/// it returns is prefixed "SIM-". It is never a substitute for testing against the customer's NAV sandbox.
/// </summary>
public sealed class NavSimulatorHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, string> _leaveByRef = new();
    private readonly ConcurrentDictionary<string, string> _claimsByRef = new();
    private int _docCounter;

    /// <summary>Test hook: fail the next N SOAP write calls with a timeout to exercise retry and reconciliation.</summary>
    public int FailNextWrites;
    /// <summary>Test hook: when true the next write is applied in "NAV" but the response is lost (simulates a timeout after commit).</summary>
    public bool LoseNextWriteResponse;

    private static readonly object[] Employees =
    {
        Emp("SIM-001", "Amani", "Wanjiru", "HR Manager", "HR", "NBI", null, "Female"),
        Emp("SIM-002", "Baraka", "Otieno", "Accountant", "FIN", "NBI", "SIM-004", "Male"),
        Emp("SIM-003", "Chebet", "Kiprono", "HR Officer", "HR", "MSA", "SIM-001", "Female"),
        Emp("SIM-004", "Daudi", "Mwangi", "Finance Manager", "FIN", "NBI", null, "Male"),
    };

    private static object Emp(string no, string first, string last, string title, string dept, string loc, string? mgr, string gender) => new Dictionary<string, object?>
    {
        ["No"] = no, ["First_Name"] = first, ["Middle_Name"] = "", ["Last_Name"] = last, ["Job_Title"] = title,
        ["Global_Dimension_1_Code"] = dept, ["Global_Dimension_2_Code"] = loc, ["Manager_No"] = mgr ?? "",
        ["Company_E_Mail"] = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}@example.invalid", ["Mobile_Phone_No"] = "",
        ["Status"] = "Active", ["Employment_Date"] = "2020-01-06", ["Termination_Date"] = "0001-01-01", ["Birth_Date"] = "0001-01-01",
        ["Gender"] = gender, ["Last_Date_Modified"] = "2026-01-01"
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        var query = Uri.UnescapeDataString(request.RequestUri.Query);
        var service = path.Split('/').Last();

        if (request.Method == HttpMethod.Get && path.Contains("/Codeunit/"))
            return Text("<definitions name=\"" + service + "\"/>", "text/xml"); // WSDL probe

        if (request.Method == HttpMethod.Post && path.Contains("/Codeunit/"))
            return await SoapAsync(request, service, ct);

        if (request.Method == HttpMethod.Get)
        {
            IEnumerable<object> rows = service switch
            {
                "ESSEmployees" => Employees,
                "ESSDepartments" => new object[] { Ref("HR", "Human Resources"), Ref("FIN", "Finance") },
                "ESSLocations" => new object[] { Ref("NBI", "Nairobi"), Ref("MSA", "Mombasa") },
                "ESSPositions" => new object[] { Ref("HR Manager", "HR Manager"), Ref("HR Officer", "HR Officer"), Ref("Accountant", "Accountant"), Ref("Finance Manager", "Finance Manager") },
                "ESSLeaveTypes" => new object[] { LeaveType("ANNUAL", "Annual Leave", 21), LeaveType("SICK", "Sick Leave", 14), LeaveType("MATERNITY", "Maternity Leave", 90), LeaveType("PATERNITY", "Paternity Leave", 14) },
                "ESSLeaveBalances" => Employees.Cast<Dictionary<string, object?>>().SelectMany(e => new object[]
                {
                    Bal((string)e["No"]!, "ANNUAL", 21, 4, 6, 0), Bal((string)e["No"]!, "SICK", 14, 0, 1, 0)
                }),
                "ESSPayrollPeriods" => new object[]
                {
                    new Dictionary<string, object?> { ["Code"] = "2026-10", ["Name"] = "October 2026", ["Starting_Date"] = "2026-10-01", ["Ending_Date"] = "2026-10-31", ["Pay_Date"] = "2026-10-28", ["Closed"] = true },
                    new Dictionary<string, object?> { ["Code"] = "2026-11", ["Name"] = "November 2026", ["Starting_Date"] = "2026-11-01", ["Ending_Date"] = "2026-11-30", ["Pay_Date"] = "2026-11-27", ["Closed"] = false }
                },
                "ESSPayslipHeaders" => SimulatedPayroll.Headers(Employees.Cast<Dictionary<string, object?>>().Select(e => (string)e["No"]!)),
                "ESSPayslipLines" => SimulatedPayroll.Lines(Employees.Cast<Dictionary<string, object?>>().Select(e => (string)e["No"]!)),
                _ => throw new InvalidOperationException($"Simulator has no entity set '{service}'.")
            };
            rows = ApplyEqFilters(rows, query);
            return Json(new Dictionary<string, object?> { ["@odata.context"] = "simulator", ["value"] = rows.ToList() });
        }
        return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
    }

    private async Task<HttpResponseMessage> SoapAsync(HttpRequestMessage request, string service, CancellationToken ct)
    {
        var xml = XDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        var call = xml.Descendants().First(e => e.Parent?.Name.LocalName == "Body");
        var method = call.Name.LocalName;
        string P(string n) => call.Elements().FirstOrDefault(e => e.Name.LocalName == n)?.Value ?? "";
        var isWrite = method.StartsWith("Submit") || method.StartsWith("Cancel");

        if (isWrite && Interlocked.Decrement(ref FailNextWrites) >= 0)
            throw new HttpRequestException("Simulated connection refused.");
        Interlocked.Exchange(ref FailNextWrites, Math.Max(FailNextWrites, 0));

        string? result;
        switch (method)
        {
            case "SubmitLeaveApplication":
                if (P("employeeNo") == "") return Fault("Employee No. must have a value.");
                if (decimal.Parse(P("days"), System.Globalization.CultureInfo.InvariantCulture) > 21) return Fault("Leave days exceed the available balance.");
                result = _leaveByRef.GetOrAdd(P("externalRef"), _ => $"SIM-LV{Interlocked.Increment(ref _docCounter):D5}");
                break;
            case "SubmitExpenseClaim":
                result = _claimsByRef.GetOrAdd(P("externalRef"), _ => $"SIM-EC{Interlocked.Increment(ref _docCounter):D5}");
                break;
            case "FindLeaveByExternalRef":
                result = _leaveByRef.TryGetValue(P("externalRef"), out var d1) ? d1 : "";
                break;
            case "FindClaimByExternalRef":
                result = _claimsByRef.TryGetValue(P("externalRef"), out var d2) ? d2 : "";
                break;
            case "CancelLeaveApplication":
                result = P("documentNo");
                break;
            case "GetLeaveStatus":
                result = "Open";
                break;
            case "GetEmployeeProfile":
                if (!Employees.Cast<Dictionary<string, object?>>().Any(e => (string)e["No"]! == P("employeeNo"))) return Fault($"Employee {P("employeeNo")} does not exist.");
                result = JsonSerializer.Serialize(new
                {
                    personal = new { maritalStatus = "Single", nationalId = "SIM-ID-" + P("employeeNo"), kraPin = "SIM-PIN" },
                    address = new { address = "Simulated Road 1", city = "Nairobi", county = "Nairobi", postCode = "00100" },
                    emergencyContacts = new[] { new { name = "Simulated Contact", relationship = "Sibling", phone = "+254700000000" } },
                    nextOfKin = new[] { new { name = "Simulated Kin", relationship = "Parent", phone = "+254700000001", percentage = "100" } },
                    dependants = Array.Empty<object>(),
                    qualifications = new[] { new { code = "BCOM", description = "Bachelor of Commerce", institution = "Simulated University", from = "2012-09-01", to = "2016-07-31", expires = "" } },
                    bank = new { bankName = "Simulated Bank", branch = "CBD", accountNo = "0123456789012" }
                });
                break;
            default:
                return Fault($"The method {method} does not exist on codeunit {service}.");
        }

        if (isWrite && LoseNextWriteResponse)
        {
            LoseNextWriteResponse = false;
            throw new TaskCanceledException("Simulated timeout after NAV committed the transaction.");
        }

        XNamespace ns = $"urn:microsoft-dynamics-schemas/codeunit/{service}";
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        var resp = new XDocument(new XElement(soap + "Envelope", new XElement(soap + "Body",
            new XElement(ns + (method + "_Result"), new XElement(ns + "return_value", result)))));
        return Text(resp.ToString(SaveOptions.DisableFormatting), "text/xml");
    }

    private static IEnumerable<object> ApplyEqFilters(IEnumerable<object> rows, string query)
    {
        // Supports "$filter=Field eq 'value' and Field2 eq 'value2'" which is all the adapters use for lookups.
        var m = System.Text.RegularExpressions.Regex.Matches(query, @"(\w+) eq '((?:[^']|'')*)'");
        foreach (System.Text.RegularExpressions.Match f in m)
        {
            var field = f.Groups[1].Value;
            var value = f.Groups[2].Value.Replace("''", "'");
            rows = rows.Where(r => r is Dictionary<string, object?> d && d.TryGetValue(field, out var v) && string.Equals(v?.ToString(), value, StringComparison.OrdinalIgnoreCase));
        }
        return rows;
    }

    private static object Ref(string code, string name) => new Dictionary<string, object?> { ["Code"] = code, ["Name"] = name };
    private static object LeaveType(string code, string desc, int ent) => new Dictionary<string, object?> { ["Code"] = code, ["Description"] = desc, ["Total_Absence_Base"] = ent };
    private static object Bal(string emp, string type, decimal ent, decimal bf, decimal taken, decimal pending) => new Dictionary<string, object?>
    {
        ["Employee_No"] = emp, ["Leave_Type"] = type, ["Leave_Period"] = "2026", ["Entitlement"] = ent, ["Brought_Forward"] = bf,
        ["Days_Taken"] = taken, ["Days_Pending"] = pending, ["Balance"] = ent + bf - taken - pending
    };

    private static HttpResponseMessage Json(object body) => Text(JsonSerializer.Serialize(body), "application/json");

    private static HttpResponseMessage Text(string body, string type) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, type) };

    private static HttpResponseMessage Fault(string message) => new(HttpStatusCode.InternalServerError)
    {
        Content = new StringContent(
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>a:Microsoft.Dynamics.Nav.Types.Exceptions.NavNCLDialogException</faultcode>" +
            $"<faultstring xml:lang=\"en-US\">{System.Security.SecurityElement.Escape(message)}</faultstring></s:Fault></s:Body></s:Envelope>", Encoding.UTF8, "text/xml")
    };
}
