using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Domain.Organization;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers.Hr;

/// <summary>
/// Departments, job titles and locations with headcounts (spec §6). Names and codes are mastered in NAV and synced; the portal only
/// keeps the department head and parent department, which NAV dimensions do not hold and which approval routing needs.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/hr/organization")]
[Route("api/hr/organization")]
[HasPermission(Permissions.EmployeesViewAll)]
public sealed class HrOrganizationController : ControllerBase
{
    private static readonly EmploymentStatus[] Current = { EmploymentStatus.Active, EmploymentStatus.OnProbation, EmploymentStatus.Suspended };
    private readonly EssDbContext _db;
    private readonly ICurrentUser _user;

    public HrOrganizationController(EssDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    private Guid CompanyId => _user.CompanyId ?? throw new UnauthorizedAccessException();

    public sealed record DepartmentRow(Guid Id, string Code, string Name, Guid? ParentId, string? Parent, Guid? HeadEmployeeId, string? Head, int Headcount, DateTime LastSyncedAtUtc);
    public sealed record SimpleRow(Guid Id, string Code, string Name, string? Department, int Headcount, DateTime LastSyncedAtUtc);

    [HttpGet("departments")]
    public async Task<IReadOnlyList<DepartmentRow>> Departments(CancellationToken ct) =>
        await (from d in _db.Departments.AsNoTracking()
               join p in _db.Departments on d.ParentDepartmentId equals p.Id into pj from p in pj.DefaultIfEmpty()
               join h in _db.Employees on d.HeadEmployeeId equals h.Id into hj from h in hj.DefaultIfEmpty()
               where d.CompanyId == CompanyId
               orderby d.Name
               select new DepartmentRow(d.Id, d.NavKey, d.Name, d.ParentDepartmentId, p != null ? p.Name : null, d.HeadEmployeeId, h != null ? h.FirstName + " " + h.LastName : null,
                   _db.Employees.Count(e => e.DepartmentId == d.Id && Current.Contains(e.Status)), d.LastSyncedAtUtc)).ToListAsync(ct);

    [HttpGet("positions")]
    public async Task<IReadOnlyList<SimpleRow>> Positions(CancellationToken ct) =>
        await (from p in _db.Positions.AsNoTracking()
               join d in _db.Departments on p.DepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
               where p.CompanyId == CompanyId
               orderby p.Title
               select new SimpleRow(p.Id, p.NavKey, p.Title, d != null ? d.Name : null, _db.Employees.Count(e => e.PositionId == p.Id && Current.Contains(e.Status)), p.LastSyncedAtUtc)).ToListAsync(ct);

    [HttpGet("locations")]
    public async Task<IReadOnlyList<SimpleRow>> Locations(CancellationToken ct) =>
        await _db.Locations.AsNoTracking().Where(l => l.CompanyId == CompanyId).OrderBy(l => l.Name)
            .Select(l => new SimpleRow(l.Id, l.NavKey, l.Name, null, _db.Employees.Count(e => e.LocationId == l.Id && Current.Contains(e.Status)), l.LastSyncedAtUtc)).ToListAsync(ct);

    public sealed record DepartmentRouting(Guid? HeadEmployeeId, Guid? ParentDepartmentId);

    /// <summary>Sets the department head and parent used by approval routes. Requires permission to configure workflows.</summary>
    [HttpPut("departments/{id:guid}")]
    [HasPermission(Permissions.WorkflowConfigure)]
    public async Task<IActionResult> SetRouting(Guid id, DepartmentRouting input, [FromServices] IAuditWriter audit, CancellationToken ct)
    {
        var d = await _db.Departments.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId, ct);
        if (d is null) return NotFound();
        var errors = new Dictionary<string, string[]>();
        if (input.HeadEmployeeId is { } head && !await _db.Employees.AnyAsync(e => e.Id == head && e.CompanyId == CompanyId && Current.Contains(e.Status), ct))
            errors["headEmployeeId"] = new[] { "Choose a current employee." };
        if (input.ParentDepartmentId is { } parent)
        {
            var parents = await _db.Departments.AsNoTracking().Where(x => x.CompanyId == CompanyId).ToDictionaryAsync(x => x.Id, x => x.ParentDepartmentId, ct);
            if (!parents.ContainsKey(parent)) errors["parentDepartmentId"] = new[] { "Unknown department." };
            else
            {
                // Walk up from the proposed parent; reaching this department would create a loop.
                for (Guid? p = parent; p is not null; p = parents.GetValueOrDefault(p.Value))
                    if (p == id) { errors["parentDepartmentId"] = new[] { "A department cannot sit under itself or one of its own sub-departments." }; break; }
            }
        }
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));
        var before = new { d.HeadEmployeeId, d.ParentDepartmentId };
        d.HeadEmployeeId = input.HeadEmployeeId;
        d.ParentDepartmentId = input.ParentDepartmentId;
        await _db.SaveChangesAsync(ct);
        await audit.WriteAsync("department.routing.changed", nameof(Department), id.ToString(), new { before, after = input }, ct: ct);
        return NoContent();
    }
}
