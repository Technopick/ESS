using Asp.Versioning;
using Ess.Api.Auth;
using Ess.Api.Infrastructure;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Domain.Identity;
using Ess.Domain.Organization;
using Ess.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers.Admin;

/// <summary>User administration: accounts, role assignments, NAV employee links, invitations and provisioning.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/users")]
[Route("api/admin/users")]
[HasPermission(Permissions.UsersManage)]
public sealed class UsersController : ControllerBase
{
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromHours(72);
    private readonly EssDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly ICurrentUser _me;
    private readonly PasswordResetService _resets;

    public UsersController(EssDbContext db, IAuditWriter audit, ICurrentUser me, PasswordResetService resets)
    {
        _db = db;
        _audit = audit;
        _me = me;
        _resets = resets;
    }

    public sealed record RoleAssignmentView(Guid RoleId, string Role, Guid? CompanyId, Guid? ScopeDepartmentId, string? ScopeDepartment);
    public sealed record EmployeeLinkView(Guid CompanyId, string Company, Guid EmployeeId, string EmployeeNo, string EmployeeName);
    public sealed record UserView(Guid Id, string UserName, string Email, bool IsActive, bool MfaEnabled, bool IsLocked, bool HasPassword,
        DateTime? LastLoginAtUtc, IReadOnlyList<RoleAssignmentView> Roles, IReadOnlyList<EmployeeLinkView> Employees, Guid? DefaultCompanyId);

    [HttpGet]
    public async Task<PagedResult<UserView>> List([FromQuery] string? search, [FromQuery] Guid? roleId, [FromQuery] bool? active,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var q = _db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToUpperInvariant();
            var linked = _db.EmployeeMappings.Join(_db.Employees, m => m.EmployeeId, e => e.Id, (m, e) => new { m.UserId, e.NavKey, e.FirstName, e.LastName })
                .Where(x => x.NavKey.ToUpper().Contains(s) || (x.FirstName + " " + x.LastName).ToUpper().Contains(s)).Select(x => x.UserId);
            q = q.Where(u => u.NormalizedUserName.Contains(s) || u.NormalizedEmail.Contains(s) || linked.Contains(u.Id));
        }
        if (roleId is not null) q = q.Where(u => u.UserRoles.Any(r => r.RoleId == roleId && !r.IsDeleted));
        if (active is not null) q = q.Where(u => u.IsActive == active);

        var total = await q.CountAsync(ct);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, PagedResult<UserView>.MaxPageSize);
        var users = await q.OrderBy(u => u.NormalizedUserName).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<UserView>(await ProjectAsync(users, ct), page, pageSize, total);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserView>> Get(Guid id, CancellationToken ct)
    {
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return u is null ? NotFound() : (await ProjectAsync(new List<User> { u }, ct))[0];
    }

    public sealed record RoleAssignmentInput(Guid RoleId, Guid? CompanyId = null, Guid? ScopeDepartmentId = null);
    public sealed record CreateUserRequest(string UserName, string Email, IReadOnlyList<RoleAssignmentInput> Roles, Guid? CompanyId, string? EmployeeNo,
        bool SendInvite = true);

    public sealed class CreateUserValidator : AbstractValidator<CreateUserRequest>
    {
        public CreateUserValidator()
        {
            RuleFor(x => x.UserName).NotEmpty().MaximumLength(256).Matches(@"^[A-Za-z0-9._@\-]+$").WithMessage("Use letters, digits and . _ @ - only.");
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
            RuleFor(x => x.Roles).NotNull();
            RuleFor(x => x.CompanyId).NotNull().When(x => !string.IsNullOrWhiteSpace(x.EmployeeNo)).WithMessage("Choose the company of the employee.");
        }
    }

    [HttpPost]
    public async Task<ActionResult<UserView>> Create(CreateUserRequest req, CancellationToken ct)
    {
        await new CreateUserValidator().ValidateAndThrowAsync(req, ct);
        var nu = req.UserName.Trim().ToUpperInvariant();
        var ne = req.Email.Trim().ToUpperInvariant();
        if (await _db.Users.AnyAsync(u => u.NormalizedUserName == nu || u.NormalizedEmail == ne, ct))
            return FieldOf<UserView>("userName", "A user with this user name or email already exists.");

        var user = new User
        {
            UserName = req.UserName.Trim(), NormalizedUserName = nu, Email = req.Email.Trim(), NormalizedEmail = ne,
            DefaultCompanyId = req.CompanyId ?? await _db.Companies.OrderBy(c => c.CreatedAtUtc).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct)
        };
        _db.Users.Add(user);
        var roleError = await ApplyRolesAsync(user.Id, req.Roles, ct);
        if (roleError is not null) return FieldOf<UserView>("roles", roleError);
        if (!string.IsNullOrWhiteSpace(req.EmployeeNo))
        {
            var linkError = await LinkAsync(user.Id, req.CompanyId!.Value, req.EmployeeNo.Trim(), ct);
            if (linkError is not null) return FieldOf<UserView>("employeeNo", linkError);
        }
        await _db.SaveChangesAsync(ct);
        if (req.SendInvite) await _resets.SendAsync(user, "invite", InviteLifetime, null, ct);
        await _audit.WriteAsync("admin.user.created", "User", user.Id.ToString(), new { user.UserName, roles = req.Roles.Select(r => r.RoleId), req.EmployeeNo }, ct: ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, (await ProjectAsync(new List<User> { user }, ct))[0]);
    }

    public sealed record SetRolesRequest(IReadOnlyList<RoleAssignmentInput> Roles);

    [HttpPut("{id:guid}/roles")]
    public async Task<IActionResult> SetRoles(Guid id, SetRolesRequest req, CancellationToken ct)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == id, ct)) return NotFound();
        if (id == _me.UserId)
        {
            var adminRoleId = await _db.Roles.Where(r => r.Name == SystemRoles.SystemAdministrator).Select(r => r.Id).FirstAsync(ct);
            if (req.Roles.All(r => r.RoleId != adminRoleId)) return Field("roles", "You cannot remove your own System Administrator role.");
        }
        var current = await _db.UserRoles.Where(r => r.UserId == id).ToListAsync(ct);
        _db.UserRoles.RemoveRange(current); // soft delete
        await _db.SaveChangesAsync(ct);
        var error = await ApplyRolesAsync(id, req.Roles, ct);
        if (error is not null) return Field("roles", error);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("admin.user.roles_changed", "User", id.ToString(), new
        {
            before = current.Select(r => new { r.RoleId, r.CompanyId, r.ScopeDepartmentId }),
            after = req.Roles
        }, ct: ct);
        return NoContent();
    }

    public sealed record LinkEmployeeRequest(Guid CompanyId, string EmployeeNo);

    [HttpPost("{id:guid}/employee-links")]
    public async Task<IActionResult> LinkEmployee(Guid id, LinkEmployeeRequest req, CancellationToken ct)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == id, ct)) return NotFound();
        var error = await LinkAsync(id, req.CompanyId, req.EmployeeNo.Trim(), ct);
        if (error is not null) return Field("employeeNo", error);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("admin.user.employee_linked", "User", id.ToString(), req, ct: ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/employee-links/{companyId:guid}")]
    public async Task<IActionResult> UnlinkEmployee(Guid id, Guid companyId, CancellationToken ct)
    {
        var m = await _db.EmployeeMappings.FirstOrDefaultAsync(x => x.UserId == id && x.CompanyId == companyId, ct);
        if (m is null) return NotFound();
        _db.EmployeeMappings.Remove(m);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("admin.user.employee_unlinked", "User", id.ToString(), new { companyId, m.NavEmployeeNo }, ct: ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/disable")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        if (id == _me.UserId) return Field("id", "You cannot disable your own account.");
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return NotFound();
        u.IsActive = false;
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync(ct);
        await RevokeSessionsAsync(id, ct);
        await _audit.WriteAsync("admin.user.disabled", "User", id.ToString(), ct: ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/enable")]
    public Task<IActionResult> Enable(Guid id, CancellationToken ct) => Mutate(id, "admin.user.enabled", u => u.IsActive = true, ct);

    [HttpPost("{id:guid}/unlock")]
    public Task<IActionResult> Unlock(Guid id, CancellationToken ct) => Mutate(id, "admin.user.unlocked", u =>
    {
        u.LockoutEndUtc = null;
        u.AccessFailedCount = 0;
    }, ct);

    /// <summary>For a user who lost their phone and recovery codes. They must enrol again at next sign-in if their role requires MFA.</summary>
    [HttpPost("{id:guid}/reset-mfa")]
    public async Task<IActionResult> ResetMfa(Guid id, CancellationToken ct)
    {
        var r = await Mutate(id, "admin.user.mfa_reset", u =>
        {
            u.MfaEnabled = false;
            u.MfaSecretEncrypted = null;
            u.MfaRecoveryCodeHashes = null;
            u.MfaLastTimeStep = null;
            u.SecurityStamp = Guid.NewGuid().ToString("N");
        }, ct);
        await RevokeSessionsAsync(id, ct);
        return r;
    }

    /// <summary>Emails a single-use link to set a new password. Administrators never see or choose user passwords.</summary>
    [HttpPost("{id:guid}/send-password-link")]
    public async Task<IActionResult> SendPasswordLink(Guid id, CancellationToken ct)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (u is null) return NotFound();
        await _resets.SendAsync(u, u.PasswordHash is null ? "invite" : "reset", u.PasswordHash is null ? InviteLifetime : TimeSpan.FromHours(24), null, ct);
        await _audit.WriteAsync("admin.user.password_link_sent", "User", id.ToString(), ct: ct);
        return Accepted();
    }

    public sealed record ProvisionRequest(Guid CompanyId, bool SendInvites = true, bool DryRun = false);
    public sealed record ProvisionResult(int Eligible, int Created, int LinkedExisting, int SkippedNoEmail, int SkippedConflict, IReadOnlyList<string> Conflicts);

    /// <summary>
    /// Creates portal accounts (role Employee) for active NAV employees that have a work email and no account yet,
    /// or links an existing account with the same email. Supervisors and other roles are assigned afterwards.
    /// </summary>
    [HttpPost("provision")]
    public async Task<ActionResult<ProvisionResult>> Provision(ProvisionRequest req, CancellationToken ct)
    {
        if (!await _db.Companies.AnyAsync(c => c.Id == req.CompanyId, ct)) return NotFound();
        var employeeRoleId = await _db.Roles.Where(r => r.Name == SystemRoles.Employee).Select(r => r.Id).FirstAsync(ct);
        var mapped = await _db.EmployeeMappings.Where(m => m.CompanyId == req.CompanyId).Select(m => m.EmployeeId).ToListAsync(ct);
        var candidates = await _db.Employees.Where(e => e.CompanyId == req.CompanyId && e.Status == EmploymentStatus.Active && !mapped.Contains(e.Id))
            .OrderBy(e => e.NavKey).ToListAsync(ct);

        int created = 0, linked = 0, noEmail = 0, conflict = 0;
        var conflicts = new List<string>();
        var invites = new List<User>();
        var seenEmails = new HashSet<string>();
        foreach (var e in candidates)
        {
            if (string.IsNullOrWhiteSpace(e.WorkEmail)) { noEmail++; continue; }
            var ne = e.WorkEmail.Trim().ToUpperInvariant();
            if (!seenEmails.Add(ne)) { conflict++; conflicts.Add($"{e.NavKey}: email {e.WorkEmail} is shared with another employee"); continue; }
            var existing = await _db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == ne || u.NormalizedUserName == ne, ct);
            if (existing is not null)
            {
                if (await _db.EmployeeMappings.AnyAsync(m => m.UserId == existing.Id && m.CompanyId == req.CompanyId, ct))
                {
                    conflict++;
                    conflicts.Add($"{e.NavKey}: {e.WorkEmail} is already linked to another employee");
                    continue;
                }
                if (!req.DryRun) _db.EmployeeMappings.Add(new EmployeeMapping { CompanyId = req.CompanyId, UserId = existing.Id, EmployeeId = e.Id, NavEmployeeNo = e.NavKey, VerifiedAtUtc = DateTime.UtcNow });
                linked++;
                continue;
            }
            if (!req.DryRun)
            {
                var u = new User { UserName = e.WorkEmail.Trim(), NormalizedUserName = ne, Email = e.WorkEmail.Trim(), NormalizedEmail = ne, DefaultCompanyId = req.CompanyId };
                _db.Users.Add(u);
                _db.UserRoles.Add(new UserRole { UserId = u.Id, RoleId = employeeRoleId, CompanyId = req.CompanyId });
                _db.EmployeeMappings.Add(new EmployeeMapping { CompanyId = req.CompanyId, UserId = u.Id, EmployeeId = e.Id, NavEmployeeNo = e.NavKey, VerifiedAtUtc = DateTime.UtcNow });
                invites.Add(u);
            }
            created++;
        }
        if (!req.DryRun)
        {
            await _db.SaveChangesAsync(ct);
            if (req.SendInvites)
                foreach (var u in invites) await _resets.SendAsync(u, "invite", InviteLifetime, null, ct);
            await _audit.WriteAsync("admin.users.provisioned", "Company", req.CompanyId.ToString(), new { created, linked, noEmail, conflict }, ct: ct);
        }
        return new ProvisionResult(candidates.Count, created, linked, noEmail, conflict, conflicts.Take(100).ToList());
    }

    private async Task<string?> ApplyRolesAsync(Guid userId, IReadOnlyList<RoleAssignmentInput> roles, CancellationToken ct)
    {
        var ids = roles.Select(r => r.RoleId).Distinct().ToList();
        var known = await _db.Roles.Where(r => ids.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct);
        if (known.Count != ids.Count) return "One of the roles does not exist.";
        foreach (var r in roles.DistinctBy(r => (r.RoleId, r.CompanyId, r.ScopeDepartmentId)))
        {
            if (r.ScopeDepartmentId is not null && !await _db.Departments.AnyAsync(d => d.Id == r.ScopeDepartmentId && (r.CompanyId == null || d.CompanyId == r.CompanyId), ct))
                return "The department scope does not belong to the chosen company.";
            _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = r.RoleId, CompanyId = r.CompanyId, ScopeDepartmentId = r.ScopeDepartmentId });
        }
        return null;
    }

    private async Task<string?> LinkAsync(Guid userId, Guid companyId, string employeeNo, CancellationToken ct)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.CompanyId == companyId && e.NavKey == employeeNo, ct);
        if (emp is null) return $"Employee {employeeNo} was not found in this company. Run a NAV sync first if it is new.";
        if (await _db.EmployeeMappings.AnyAsync(m => m.CompanyId == companyId && m.NavEmployeeNo == employeeNo, ct))
            return $"Employee {employeeNo} is already linked to another account.";
        if (await _db.EmployeeMappings.AnyAsync(m => m.CompanyId == companyId && m.UserId == userId, ct))
            return "This account is already linked to an employee in this company.";
        _db.EmployeeMappings.Add(new EmployeeMapping { CompanyId = companyId, UserId = userId, EmployeeId = emp.Id, NavEmployeeNo = employeeNo, VerifiedAtUtc = DateTime.UtcNow });
        return null;
    }

    private async Task<IReadOnlyList<UserView>> ProjectAsync(List<User> users, CancellationToken ct)
    {
        var ids = users.Select(u => u.Id).ToList();
        var roles = await _db.UserRoles.AsNoTracking().Where(r => ids.Contains(r.UserId))
            .Select(r => new { r.UserId, r.RoleId, Role = r.Role!.Name, r.CompanyId, r.ScopeDepartmentId,
                Dept = _db.Departments.Where(d => d.Id == r.ScopeDepartmentId).Select(d => d.Name).FirstOrDefault() })
            .ToListAsync(ct);
        var links = await (from m in _db.EmployeeMappings.AsNoTracking()
                           join e in _db.Employees.AsNoTracking() on m.EmployeeId equals e.Id
                           join c in _db.Companies.AsNoTracking() on m.CompanyId equals c.Id
                           where ids.Contains(m.UserId)
                           select new { m.UserId, m.CompanyId, Company = c.DisplayName, m.EmployeeId, e.NavKey, e.FirstName, e.LastName }).ToListAsync(ct);
        var now = DateTime.UtcNow;
        return users.Select(u => new UserView(u.Id, u.UserName, u.Email, u.IsActive, u.MfaEnabled, u.LockoutEndUtc > now, u.PasswordHash is not null,
            u.LastLoginAtUtc,
            roles.Where(r => r.UserId == u.Id).Select(r => new RoleAssignmentView(r.RoleId, r.Role, r.CompanyId, r.ScopeDepartmentId, r.Dept)).ToList(),
            links.Where(l => l.UserId == u.Id).Select(l => new EmployeeLinkView(l.CompanyId, l.Company, l.EmployeeId, l.NavKey, $"{l.FirstName} {l.LastName}")).ToList(),
            u.DefaultCompanyId)).ToList();
    }

    private async Task<IActionResult> Mutate(Guid id, string action, Action<User> change, CancellationToken ct)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return NotFound();
        change(u);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(action, "User", id.ToString(), ct: ct);
        return NoContent();
    }

    private Task RevokeSessionsAsync(Guid id, CancellationToken ct) =>
        _db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow), ct);

    private IActionResult Field(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = new[] { message } }));

    private ActionResult<T> FieldOf<T>(string field, string message) =>
        new ObjectResult(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = new[] { message } }) { Status = 400 }) { StatusCode = 400 };
}
