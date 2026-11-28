using Asp.Versioning;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers.Admin;

/// <summary>
/// Small reference lists for forms (companies, departments, locations). Signed-in users see their own company only;
/// user administrators, who link accounts to companies, see all companies.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/lookups")]
[Route("api/lookups")]
public sealed class LookupsController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly ICurrentUser _user;

    public LookupsController(EssDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public sealed record Item(Guid Id, string Code, string Name);

    private bool AllCompanies => _user.HasPermission(Permissions.UsersManage) || _user.HasPermission(Permissions.IntegrationConfigure);
    private bool MayRead(Guid companyId) => AllCompanies || _user.CompanyId == companyId;

    [HttpGet("companies")]
    public async Task<IReadOnlyList<Item>> Companies(CancellationToken ct)
    {
        var q = _db.Companies.AsNoTracking().Where(c => c.IsActive);
        if (!AllCompanies) q = q.Where(c => c.Id == _user.CompanyId);
        return await q.OrderBy(c => c.DisplayName).Select(c => new Item(c.Id, c.Code, c.DisplayName)).ToListAsync(ct);
    }

    [HttpGet("departments")]
    public async Task<IReadOnlyList<Item>> Departments([FromQuery] Guid companyId, CancellationToken ct) =>
        !MayRead(companyId) ? Array.Empty<Item>()
            : await _db.Departments.AsNoTracking().Where(d => d.CompanyId == companyId).OrderBy(d => d.Name).Select(d => new Item(d.Id, d.NavKey, d.Name)).ToListAsync(ct);

    [HttpGet("locations")]
    public async Task<IReadOnlyList<Item>> Locations([FromQuery] Guid companyId, CancellationToken ct) =>
        !MayRead(companyId) ? Array.Empty<Item>()
            : await _db.Locations.AsNoTracking().Where(d => d.CompanyId == companyId).OrderBy(d => d.Name).Select(d => new Item(d.Id, d.NavKey, d.Name)).ToListAsync(ct);
}
