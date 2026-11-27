using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Api.Reporting;
using Ess.Api.Services;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Domain.Organization;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers;

/// <summary>Management dashboard with filters and Excel/PDF export (spec §7).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/management")]
[Route("api/management")]
[HasPermission(Permissions.DashboardManagement)]
public sealed class ManagementController : ControllerBase
{
    private readonly ManagementDashboardService _service;

    public ManagementController(ManagementDashboardService service) => _service = service;

    public sealed record Query(Guid? DepartmentId, Guid? LocationId, EmploymentStatus? Status, Guid? EmployeeId, DateOnly? From, DateOnly? To)
    {
        public DashboardFilter ToFilter() => new(DepartmentId, LocationId, Status, EmployeeId, From, To);
    }

    [HttpGet("dashboard")]
    public async Task<ManagementDashboard> Dashboard([FromQuery] Query q, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return await _service.BuildAsync(q.ToFilter(), ct);
    }

    [HttpGet("dashboard/export")]
    [HasPermission(Permissions.DataExport)]
    public async Task<IActionResult> Export([FromQuery] Query q, [FromServices] ReportRunner runner, [FromServices] EssDbContext db, [FromServices] IAuditWriter audit,
        [FromQuery] ExportFormat format = ExportFormat.Xlsx, CancellationToken ct = default)
    {
        var d = await _service.BuildAsync(q.ToFilter(), ct);
        var filters = new List<KeyValuePair<string, string>>();
        if (q.DepartmentId is not null) filters.Add(new("Department", await db.Departments.Where(x => x.Id == q.DepartmentId).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "?"));
        if (q.LocationId is not null) filters.Add(new("Location", await db.Locations.Where(x => x.Id == q.LocationId).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "?"));
        if (q.Status is not null) filters.Add(new("Employment status", q.Status.Value.ToString()));
        var (content, type, ext) = ReportExporter.ExportMany(ManagementDashboardService.ToTables(d, filters), "Management dashboard", format, await runner.GeneratedByAsync(ct), runner.LocalNow);
        await audit.WriteAsync("dashboard.exported", "ManagementDashboard", null, new { q, format = format.ToString() }, ct: ct);
        Response.Headers.CacheControl = "no-store";
        return File(content, type, $"management-dashboard-{runner.LocalNow:yyyyMMdd-HHmm}.{ext}");
    }
}
