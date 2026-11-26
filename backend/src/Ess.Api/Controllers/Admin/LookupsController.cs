using Asp.Versioning;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers.Admin;

/// <summary>Small reference lists for forms (companies, departments). Any signed-in user may read them.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/lookups")]
[Route("api/lookups")]
public sealed class LookupsController : ControllerBase
{
    private readonly EssDbContext _db;
    public LookupsController(EssDbContext db) => _db = db;

    public sealed record Item(Guid Id, string Code, string Name);

    [HttpGet("companies")]
    public async Task<IReadOnlyList<Item>> Companies(CancellationToken ct) =>
        await _db.Companies.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.DisplayName).Select(c => new Item(c.Id, c.Code, c.DisplayName)).ToListAsync(ct);

    [HttpGet("departments")]
    public async Task<IReadOnlyList<Item>> Departments([FromQuery] Guid companyId, CancellationToken ct) =>
        await _db.Departments.AsNoTracking().Where(d => d.CompanyId == companyId).OrderBy(d => d.Name).Select(d => new Item(d.Id, d.NavKey, d.Name)).ToListAsync(ct);

    [HttpGet("locations")]
    public async Task<IReadOnlyList<Item>> Locations([FromQuery] Guid companyId, CancellationToken ct) =>
        await _db.Locations.AsNoTracking().Where(d => d.CompanyId == companyId).OrderBy(d => d.Name).Select(d => new Item(d.Id, d.NavKey, d.Name)).ToListAsync(ct);
}
