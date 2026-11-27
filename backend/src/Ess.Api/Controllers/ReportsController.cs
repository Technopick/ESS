using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Api.Reporting;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ess.Api.Controllers;

/// <summary>HR, finance and payroll reports with CSV, Excel and PDF export (spec §13). Every view and export is audited.</summary>
[ApiController]
[ApiVersion("1.0")]
[Authorize]
[Route("api/v{version:apiVersion}/reports")]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly ReportRunner _runner;
    private readonly ICurrentUser _user;
    private readonly IAuditWriter _audit;

    public ReportsController(ReportRunner runner, ICurrentUser user, IAuditWriter audit)
    {
        _runner = runner;
        _user = user;
        _audit = audit;
    }

    public sealed record ReportInfo(string Code, string Name, string Category, string Description, ReportFilterSupport Filters);
    public sealed record CatalogResponse(IReadOnlyList<ReportInfo> Reports, bool CanExport);

    [HttpGet]
    public CatalogResponse List() => new(
        ReportCatalog.All.Where(_runner.CanRun).Select(r => new ReportInfo(r.Code, r.Name, r.Category, r.Description, r.Filters)).ToList(),
        _user.HasPermission(Permissions.DataExport));

    public sealed record FilterQuery(DateOnly? From, DateOnly? To, Guid? DepartmentId, Guid? LocationId, Guid? EmployeeId, string? Status, string? PeriodCode)
    {
        public ReportFilter ToFilter() => new(From, To, DepartmentId, LocationId, EmployeeId, string.IsNullOrWhiteSpace(Status) ? null : Status, string.IsNullOrWhiteSpace(PeriodCode) ? null : PeriodCode);
    }

    [HttpGet("{code}")]
    public async Task<IActionResult> Run(string code, [FromQuery] FilterQuery q, CancellationToken ct)
    {
        var def = ReportCatalog.Find(code);
        if (def is null || !_runner.CanRun(def)) return NotFound();
        try
        {
            var table = await _runner.RunAsync(def, q.ToFilter(), ct);
            await _audit.WriteAsync("report.viewed", "Report", def.Code, new { q, rows = table.Rows.Count }, ct: ct);
            Response.Headers.CacheControl = "no-store";
            return Ok(table);
        }
        catch (ReportUnavailableException ex)
        {
            return Problem(statusCode: 409, title: "Report not available", detail: ex.Message);
        }
    }

    [HttpGet("{code}/export")]
    [HasPermission(Permissions.DataExport)]
    public async Task<IActionResult> Export(string code, [FromQuery] FilterQuery q, [FromQuery] ExportFormat format = ExportFormat.Xlsx, CancellationToken ct = default)
    {
        var def = ReportCatalog.Find(code);
        if (def is null || !_runner.CanRun(def)) return NotFound();
        ReportTable table;
        try { table = await _runner.RunAsync(def, q.ToFilter(), ct); }
        catch (ReportUnavailableException ex) { return Problem(statusCode: 409, title: "Report not available", detail: ex.Message); }
        var (content, type, ext) = ReportExporter.Export(table, format, await _runner.GeneratedByAsync(ct), _runner.LocalNow);
        await _audit.WriteAsync("report.exported", "Report", def.Code, new { q, format = format.ToString(), rows = table.Rows.Count }, ct: ct);
        Response.Headers.CacheControl = "no-store";
        return File(content, type, $"{def.Code}-{_runner.LocalNow:yyyyMMdd-HHmm}.{ext}");
    }
}
