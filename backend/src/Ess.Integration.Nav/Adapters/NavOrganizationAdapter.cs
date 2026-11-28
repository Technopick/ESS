using Ess.Application.Integration;
using Ess.Integration.Nav.Clients;
using Ess.Integration.Nav.Configuration;
using Microsoft.Extensions.Logging;

namespace Ess.Integration.Nav.Adapters;

/// <summary>Departments, positions, locations and the base calendar (public holidays).</summary>
public sealed class NavOrganizationAdapter : NavAdapterBase, INavOrganizationService
{
    // Departments default to Dimension Values of Global Dimension 1; positions to an HR add-on table; locations to Dimension 2.
    internal static readonly IReadOnlyDictionary<string, string> RefOData = new Dictionary<string, string>
    { ["Code"] = "Code", ["Name"] = "Name", ["ParentCode"] = "Parent_Code", ["LastModified"] = "Last_Modified_Date_Time" };
    internal static readonly IReadOnlyDictionary<string, string> RefBc = new Dictionary<string, string>
    { ["Code"] = "code", ["Name"] = "displayName", ["ParentCode"] = "parentCode", ["LastModified"] = "lastModifiedDateTime" };
    internal static readonly IReadOnlyDictionary<string, string> CalOData = new Dictionary<string, string>
    { ["Date"] = "Date", ["Nonworking"] = "Nonworking", ["Description"] = "Description" };
    internal static readonly IReadOnlyDictionary<string, string> CalBc = new Dictionary<string, string>
    { ["Date"] = "date", ["Nonworking"] = "nonworking", ["Description"] = "description" };

    public NavOrganizationAdapter(NavConnectionManager c, NavODataClient o, NavSoapClient s, ILogger<NavOrganizationAdapter> log) : base(c, o, s, log) { }

    public Task<NavResult<IReadOnlyList<NavReferenceRecord>>> GetDepartmentsAsync(Guid companyId, CancellationToken ct) => Ref(companyId, "Department", ct);
    public Task<NavResult<IReadOnlyList<NavReferenceRecord>>> GetPositionsAsync(Guid companyId, CancellationToken ct) => Ref(companyId, "Position", ct);
    public Task<NavResult<IReadOnlyList<NavReferenceRecord>>> GetLocationsAsync(Guid companyId, CancellationToken ct) => Ref(companyId, "Location", ct);

    public Task<NavResult<IReadOnlyList<DateOnly>>> GetNonWorkingDatesAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken ct) =>
        RunAsync(companyId, "BaseCalendar", async ctx =>
        {
            if (ctx.FindEndpoint("BaseCalendar") is null) return NavResult<IReadOnlyList<DateOnly>>.Ok(Array.Empty<DateOnly>());
            var map = Mapper(ctx, "BaseCalendar", CalOData, CalBc);
            var rows = await OData.GetAllAsync(ctx, "BaseCalendar", new NavODataClient.Query(), ct);
            return rows.Map<IReadOnlyList<DateOnly>>(list => list
                .Where(r => map.GetBool(r, "Nonworking") ?? true)
                .Select(r => map.GetDate(r, "Date"))
                .Where(d => d is not null && d >= from && d <= to).Select(d => d!.Value).Distinct().ToList());
        }, ct);

    private Task<NavResult<IReadOnlyList<NavReferenceRecord>>> Ref(Guid companyId, string logical, CancellationToken ct) =>
        RunAsync(companyId, logical + ".List", async ctx =>
        {
            var map = Mapper(ctx, logical, RefOData, RefBc);
            var rows = await OData.GetAllAsync(ctx, logical, new NavODataClient.Query(), ct);
            return rows.Map<IReadOnlyList<NavReferenceRecord>>(list => list.Select(r => new NavReferenceRecord(
                map.RequireString(r, "Code"), map.GetString(r, "Name") ?? map.RequireString(r, "Code"), map.GetString(r, "ParentCode"),
                Str(r, "@odata.etag"), map.GetDateTimeUtc(r, "LastModified"))).ToList());
        }, ct);
}
