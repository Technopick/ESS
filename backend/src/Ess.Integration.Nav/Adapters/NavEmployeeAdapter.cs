using System.Globalization;
using System.Text.Json;
using Ess.Application.Integration;
using Ess.Integration.Nav.Clients;
using Ess.Integration.Nav.Configuration;
using Microsoft.Extensions.Logging;

namespace Ess.Integration.Nav.Adapters;

/// <summary>
/// Employee adapter. Reads use the "Employee" endpoint (NAV page 5201 "Employee List"/5200 "Employee Card" published
/// over OData, or BC API v2.0 "employees", or a custom ESS API page). Change submissions go through the
/// "Employee.Change" endpoint (custom codeunit / API page) so NAV validation runs; the portal never writes tables.
/// </summary>
public sealed class NavEmployeeAdapter : NavAdapterBase, INavEmployeeService
{
    public const string Endpoint = "Employee";
    public const string ProfileEndpoint = "Employee.Profile";
    public const string ChangeEndpoint = "Employee.Change";
    private const int PageSize = 500;

    // Standard NAV Employee table fields as exposed by OData (spaces and dots become underscores).
    public static readonly IReadOnlyDictionary<string, string> ODataDefaults = new Dictionary<string, string>
    {
        ["No"] = "No", ["FirstName"] = "First_Name", ["MiddleName"] = "Middle_Name", ["LastName"] = "Last_Name",
        ["JobTitle"] = "Job_Title", ["PositionCode"] = "Job_Title",
        // Standard NAV has no department field on Employee; most installs use Global Dimension 1 (or an HR add-on field).
        ["DepartmentCode"] = "Global_Dimension_1_Code", ["LocationCode"] = "Global_Dimension_2_Code",
        ["ManagerNo"] = "Manager_No", ["CompanyEmail"] = "Company_E_Mail", ["MobilePhone"] = "Mobile_Phone_No",
        ["Status"] = "Status", ["EmploymentDate"] = "Employment_Date", ["TerminationDate"] = "Termination_Date",
        // Standard NAV has no contract end date; Kenyan HR add-ons usually add one. Re-map in the field mapping screen.
        ["ContractEndDate"] = "Contract_End_Date",
        ["BirthDate"] = "Birth_Date", ["Gender"] = "Gender", ["LastModified"] = "Last_Date_Modified", ["VersionToken"] = "ETag"
    };

    // Business Central API v2.0 "employees" entity.
    public static readonly IReadOnlyDictionary<string, string> BcApiDefaults = new Dictionary<string, string>
    {
        ["No"] = "number", ["FirstName"] = "givenName", ["MiddleName"] = "middleName", ["LastName"] = "surname",
        ["JobTitle"] = "jobTitle", ["PositionCode"] = "jobTitle", ["DepartmentCode"] = "departmentCode", ["LocationCode"] = "locationCode",
        ["ManagerNo"] = "managerNumber", ["CompanyEmail"] = "email", ["MobilePhone"] = "mobilePhone", ["Status"] = "status",
        ["EmploymentDate"] = "employmentDate", ["TerminationDate"] = "terminationDate", ["ContractEndDate"] = "contractEndDate", ["BirthDate"] = "birthDate",
        ["Gender"] = "gender", ["LastModified"] = "lastModifiedDateTime", ["VersionToken"] = "@odata.etag"
    };

    public NavEmployeeAdapter(NavConnectionManager c, NavODataClient o, NavSoapClient s, ILogger<NavEmployeeAdapter> log) : base(c, o, s, log) { }

    public Task<NavResult<NavEmployeeRecord?>> GetEmployeeAsync(Guid companyId, string employeeNo, CancellationToken ct) =>
        RunAsync<NavEmployeeRecord?>(companyId, "Employee.Get", async ctx =>
        {
            var map = Mapper(ctx, Endpoint, ODataDefaults, BcApiDefaults);
            var page = await OData.GetPageAsync(ctx, Endpoint,
                new NavODataClient.Query(Filter: $"{map.NavField("No")} eq {NavODataClient.Literal(employeeNo)}", Top: 1), null, ct);
            if (!page.Success) return NavResult<NavEmployeeRecord?>.Fail(page.Error!);
            var row = page.Value!.Rows.FirstOrDefault();
            return NavResult<NavEmployeeRecord?>.Ok(row.ValueKind == JsonValueKind.Undefined ? null : ToRecord(map, row));
        }, ct);

    public Task<NavResult<NavPage<NavEmployeeRecord>>> GetEmployeesChangedSinceAsync(Guid companyId, string? watermark, string? continuation, CancellationToken ct) =>
        RunAsync(companyId, "Employee.Sync", async ctx =>
        {
            var ep = ctx.RequireEndpoint(Endpoint);
            var map = Mapper(ctx, Endpoint, ODataDefaults, BcApiDefaults);
            var modifiedField = ep.ModifiedField ?? map.NavField("LastModified");
            string? filter = null;
            if (watermark is not null && DateTime.TryParse(watermark, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var since))
            {
                // NAV "Last Date Modified" is a date (no time); re-read the whole watermark day. Upserts are idempotent.
                var dateOnly = !ctx.Profile.SupportsSystemModifiedAt && !ep.Protocol.Equals("BcApi", StringComparison.OrdinalIgnoreCase);
                filter = DateFilter(ctx, modifiedField, dateOnly ? "ge" : "gt", dateOnly ? since.Date : since, dateOnly);
            }

            var page = await OData.GetPageAsync(ctx, Endpoint, new NavODataClient.Query(Filter: filter, Top: continuation is null ? PageSize : null), continuation, ct);
            if (!page.Success) return NavResult<NavPage<NavEmployeeRecord>>.Fail(page.Error!);

            var records = page.Value!.Rows.Select(r => ToRecord(map, r)).ToList();
            var maxModified = records.Select(r => r.LastModifiedUtc).Where(d => d is not null).DefaultIfEmpty(null).Max();
            var next = maxModified?.ToString("O") ?? watermark;
            return NavResult<NavPage<NavEmployeeRecord>>.Ok(new NavPage<NavEmployeeRecord>(records, next, page.Value.NextLink is not null, page.Value.NextLink));
        }, ct);

    public Task<NavResult<NavEmployeeProfile>> GetProfileAsync(Guid companyId, string employeeNo, CancellationToken ct) =>
        RunAsync(companyId, "Employee.Profile", async ctx =>
        {
            var core = await GetEmployeeAsync(companyId, employeeNo, ct);
            if (!core.Success) return NavResult<NavEmployeeProfile>.Fail(core.Error!);
            if (core.Value is null) return NavResult<NavEmployeeProfile>.Fail(NavErrorCategory.NotFound, $"Employee {employeeNo} not found in NAV.");

            // Extended profile (next of kin, dependants, qualifications, bank) is normally a custom ESS page or codeunit
            // because standard NAV keeps these in add-on tables or not at all. Return the core record when it is not mapped.
            var ext = ctx.FindEndpoint(ProfileEndpoint);
            var empty = new Dictionary<string, string?>();
            var none = Array.Empty<IReadOnlyDictionary<string, string?>>();
            if (ext is null)
                return NavResult<NavEmployeeProfile>.Ok(new NavEmployeeProfile(core.Value, empty, empty, none, none, none, none, null));

            var soap = await Soap.InvokeCodeunitAsync(ctx, ProfileEndpoint, new (string, object?)[] { ("employeeNo", employeeNo) }, false, ct);
            if (!soap.Success) return NavResult<NavEmployeeProfile>.Fail(soap.Error!);
            return NavResult<NavEmployeeProfile>.Ok(ProfileJson.Parse(core.Value, soap.Value!.ReturnValue));
        }, ct);

    public Task<NavResult<NavSubmissionReceipt>> SubmitChangeAsync(Guid companyId, NavEmployeeChangeSubmission change, CancellationToken ct) =>
        RunAsync(companyId, "Employee.Change", async ctx =>
        {
            if (IsSoap(ctx, ChangeEndpoint))
            {
                var r = await Soap.InvokeCodeunitAsync(ctx, ChangeEndpoint, new (string, object?)[]
                {
                    ("externalRef", change.IdempotencyKey), ("portalRef", change.PortalReference), ("employeeNo", change.EmployeeNo),
                    ("section", change.Section), ("changesJson", JsonSerializer.Serialize(change.Changes)), ("approvedBy", change.ApprovedBy)
                }, true, ct);
                return r.Success ? NavResult<NavSubmissionReceipt>.Ok(ReceiptFrom(r.Value!.ReturnValue)) : NavResult<NavSubmissionReceipt>.Fail(r.Error!);
            }
            var p = await OData.PostAsync(ctx, ChangeEndpoint, new
            {
                externalReference = change.IdempotencyKey, portalReference = change.PortalReference, employeeNumber = change.EmployeeNo,
                section = change.Section, changesJson = JsonSerializer.Serialize(change.Changes), approvedBy = change.ApprovedBy
            }, change.IdempotencyKey, ct);
            return p.Success ? NavResult<NavSubmissionReceipt>.Ok(ReceiptFrom(Str(p.Value, "documentNumber"), Str(p.Value, "status"))) : NavResult<NavSubmissionReceipt>.Fail(p.Error!);
        }, ct);

    private static NavEmployeeRecord ToRecord(Mapping.NavFieldMapper m, JsonElement r) => new(
        m.RequireString(r, "No"), m.GetString(r, "FirstName") ?? "", m.GetString(r, "MiddleName"), m.GetString(r, "LastName") ?? "",
        m.GetString(r, "JobTitle"), m.GetString(r, "PositionCode"), m.GetString(r, "DepartmentCode"), m.GetString(r, "LocationCode"),
        m.GetString(r, "ManagerNo"), m.GetString(r, "CompanyEmail"), m.GetString(r, "MobilePhone"), m.GetString(r, "Status") ?? "Active",
        m.GetDate(r, "EmploymentDate"), m.GetDate(r, "ContractEndDate"), m.GetDate(r, "BirthDate"), m.GetString(r, "Gender"),
        m.GetString(r, "VersionToken"), m.GetDateTimeUtc(r, "LastModified"), TerminationDate: m.GetDate(r, "TerminationDate"));
}

/// <summary>Parses the JSON document returned by the ESS profile codeunit (format in docs/nav-customizations.md).</summary>
internal static class ProfileJson
{
    public static NavEmployeeProfile Parse(NavEmployeeRecord core, string? json)
    {
        var empty = new Dictionary<string, string?>();
        if (string.IsNullOrWhiteSpace(json))
            return new NavEmployeeProfile(core, empty, empty, Array.Empty<IReadOnlyDictionary<string, string?>>(),
                Array.Empty<IReadOnlyDictionary<string, string?>>(), Array.Empty<IReadOnlyDictionary<string, string?>>(),
                Array.Empty<IReadOnlyDictionary<string, string?>>(), null);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new NavEmployeeProfile(core, Obj(root, "personal"), Obj(root, "address"), Arr(root, "emergencyContacts"),
            Arr(root, "nextOfKin"), Arr(root, "dependants"), Arr(root, "qualifications"),
            root.TryGetProperty("bank", out _) ? MaskBank(Obj(root, "bank")) : null);
    }

    private static IReadOnlyDictionary<string, string?> Obj(JsonElement root, string name) =>
        root.TryGetProperty(name, out var o) && o.ValueKind == JsonValueKind.Object
            ? o.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.ToString())
            : new Dictionary<string, string?>();

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> Arr(JsonElement root, string name) =>
        root.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(e => (IReadOnlyDictionary<string, string?>)e.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.ToString())).ToList()
            : Array.Empty<IReadOnlyDictionary<string, string?>>();

    private static IReadOnlyDictionary<string, string?> MaskBank(IReadOnlyDictionary<string, string?> bank) =>
        bank.ToDictionary(kv => kv.Key, kv => kv.Key.Contains("account", StringComparison.OrdinalIgnoreCase) && kv.Value is { Length: > 4 } v
            ? new string('•', v.Length - 4) + v[^4..] : kv.Value);
}
