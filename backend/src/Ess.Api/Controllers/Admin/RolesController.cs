using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Domain.Identity;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers.Admin;

/// <summary>Roles and their permissions. System roles can be re-permissioned but not renamed or deleted.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/roles")]
[Route("api/admin/roles")]
[HasPermission(Permissions.UsersManage)]
public sealed class RolesController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly IAuditWriter _audit;

    public RolesController(EssDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public sealed record PermissionView(string Code, string Module, string? Description);
    public sealed record RoleView(Guid Id, string Name, string? Description, bool IsSystem, int UserCount, IReadOnlyList<string> Permissions);

    [HttpGet("/api/admin/permissions")]
    [HttpGet("/api/v{version:apiVersion}/admin/permissions")]
    public async Task<IReadOnlyList<PermissionView>> PermissionsCatalogue(CancellationToken ct) =>
        await _db.Permissions.AsNoTracking().OrderBy(p => p.Module).ThenBy(p => p.Code).Select(p => new PermissionView(p.Code, p.Module, p.Description)).ToListAsync(ct);

    [HttpGet]
    public async Task<IReadOnlyList<RoleView>> List(CancellationToken ct) =>
        await _db.Roles.AsNoTracking().OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)
            .Select(r => new RoleView(r.Id, r.Name, r.Description, r.IsSystem,
                _db.UserRoles.Where(ur => ur.RoleId == r.Id).Select(ur => ur.UserId).Distinct().Count(),
                r.RolePermissions.Select(rp => rp.Permission!.Code).OrderBy(c => c).ToList()))
            .ToListAsync(ct);

    public sealed record SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

    [HttpPost]
    public async Task<IActionResult> Create(SaveRoleRequest req, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 100) return Field("name", "Enter a name of 2 to 100 characters.");
        if (await _db.Roles.AnyAsync(r => r.NormalizedName == name.ToUpperInvariant(), ct)) return Field("name", "A role with this name already exists.");
        var role = new Role { Name = name, NormalizedName = name.ToUpperInvariant(), Description = req.Description };
        _db.Roles.Add(role);
        var err = await SetPermissionsAsync(role, req.Permissions, ct);
        if (err is not null) return Field("permissions", err);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("admin.role.created", "Role", role.Id.ToString(), req, ct: ct);
        return Created($"/api/admin/roles/{role.Id}", new { role.Id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveRoleRequest req, CancellationToken ct)
    {
        var role = await _db.Roles.Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return NotFound();
        if (!role.IsSystem)
        {
            var name = req.Name?.Trim() ?? "";
            if (name.Length is < 2 or > 100) return Field("name", "Enter a name of 2 to 100 characters.");
            if (await _db.Roles.AnyAsync(r => r.Id != id && r.NormalizedName == name.ToUpperInvariant(), ct)) return Field("name", "A role with this name already exists.");
            role.Name = name;
            role.NormalizedName = name.ToUpperInvariant();
        }
        if (role.Name == SystemRoles.SystemAdministrator && !req.Permissions.Contains(Permissions.UsersManage))
            return Field("permissions", "System Administrator must keep users.manage, or nobody could manage access.");
        var before = role.RolePermissions.Select(rp => rp.Permission!.Code).ToList();
        role.Description = req.Description;
        var err = await SetPermissionsAsync(role, req.Permissions, ct);
        if (err is not null) return Field("permissions", err);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("admin.role.updated", "Role", id.ToString(), new { before, after = req.Permissions }, ct: ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return NotFound();
        if (role.IsSystem) return Field("id", "System roles cannot be deleted.");
        if (await _db.UserRoles.AnyAsync(ur => ur.RoleId == id, ct)) return Field("id", "Remove this role from all users first.");
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("admin.role.deleted", "Role", id.ToString(), new { role.Name }, ct: ct);
        return NoContent();
    }

    private async Task<string?> SetPermissionsAsync(Role role, IReadOnlyList<string> codes, CancellationToken ct)
    {
        var wanted = codes.Distinct().ToList();
        var perms = await _db.Permissions.Where(p => wanted.Contains(p.Code)).ToListAsync(ct);
        if (perms.Count != wanted.Count) return "Unknown permission: " + string.Join(", ", wanted.Except(perms.Select(p => p.Code)));
        role.RolePermissions.Clear();
        foreach (var p in perms) role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = p.Id });
        return null;
    }

    private IActionResult Field(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = new[] { message } }));
}
