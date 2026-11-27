using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Api.Services;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Domain.Notifications;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers;

/// <summary>Company announcements: staff read the ones aimed at them; HR creates, schedules, targets and retires them (spec §6, §8).</summary>
[ApiController]
[ApiVersion("1.0")]
[Authorize]
[Route("api/v{version:apiVersion}/announcements")]
[Route("api/announcements")]
public sealed class AnnouncementsController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly ICurrentUser _user;
    private readonly EmployeeContext _me;

    public AnnouncementsController(EssDbContext db, ICurrentUser user, EmployeeContext me)
    {
        _db = db;
        _user = user;
        _me = me;
    }

    private Guid CompanyId => _user.CompanyId ?? throw new UnauthorizedAccessException();

    public sealed record AnnouncementView(Guid Id, string Title, string BodyHtml, DateTime PublishAtUtc, DateTime? ExpiresAtUtc, bool IsPinned);

    /// <summary>Live announcements for the caller's department and location.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<AnnouncementView>> Feed(CancellationToken ct)
    {
        var e = await _me.GetAsync(ct);
        var now = DateTime.UtcNow;
        return await _db.Announcements.AsNoTracking()
            .Where(a => a.CompanyId == CompanyId && a.PublishAtUtc <= now && (a.ExpiresAtUtc == null || a.ExpiresAtUtc > now)
                        && (a.TargetDepartmentId == null || (e != null && a.TargetDepartmentId == e.DepartmentId))
                        && (a.TargetLocationId == null || (e != null && a.TargetLocationId == e.LocationId)))
            .OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.PublishAtUtc).Take(50)
            .Select(a => new AnnouncementView(a.Id, a.Title, a.BodyHtml, a.PublishAtUtc, a.ExpiresAtUtc, a.IsPinned)).ToListAsync(ct);
    }

    public sealed record AdminView(Guid Id, string Title, string BodyHtml, DateTime PublishAtUtc, DateTime? ExpiresAtUtc, bool IsPinned, Guid? TargetDepartmentId,
        string? TargetDepartment, Guid? TargetLocationId, string? TargetLocation, bool NotifyOnPublish, bool NotifyByEmail, DateTime? NotifiedAtUtc, string State);

    [HttpGet("manage")]
    [HasPermission(Permissions.AnnouncementsManage)]
    public async Task<IReadOnlyList<AdminView>> All(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var rows = await (from a in _db.Announcements.AsNoTracking()
                          join d in _db.Departments on a.TargetDepartmentId equals d.Id into dj from d in dj.DefaultIfEmpty()
                          join l in _db.Locations on a.TargetLocationId equals l.Id into lj from l in lj.DefaultIfEmpty()
                          where a.CompanyId == CompanyId
                          orderby a.PublishAtUtc descending
                          select new { a, Dept = d != null ? d.Name : null, Loc = l != null ? l.Name : null }).Take(500).ToListAsync(ct);
        return rows.Select(x => new AdminView(x.a.Id, x.a.Title, x.a.BodyHtml, x.a.PublishAtUtc, x.a.ExpiresAtUtc, x.a.IsPinned, x.a.TargetDepartmentId, x.Dept,
            x.a.TargetLocationId, x.Loc, x.a.NotifyOnPublish, x.a.NotifyByEmail, x.a.NotifiedAtUtc,
            x.a.PublishAtUtc > now ? "Scheduled" : x.a.ExpiresAtUtc <= now ? "Expired" : "Live")).ToList();
    }

    public sealed record AnnouncementInput(string Title, string Body, DateTime? PublishAtUtc, DateTime? ExpiresAtUtc, bool IsPinned, Guid? TargetDepartmentId,
        Guid? TargetLocationId, bool NotifyOnPublish, bool NotifyByEmail);

    [HttpPost]
    [HasPermission(Permissions.AnnouncementsManage)]
    public async Task<IActionResult> Create(AnnouncementInput input, [FromServices] AnnouncementService service, [FromServices] IAuditWriter audit, CancellationToken ct)
    {
        if (await ValidateAsync(input, ct) is { } bad) return bad;
        var a = new Announcement { CompanyId = CompanyId, Title = input.Title.Trim(), BodyHtml = AnnouncementService.Sanitize(input.Body) };
        Apply(a, input);
        _db.Announcements.Add(a);
        await _db.SaveChangesAsync(ct);
        await audit.WriteAsync("announcement.created", nameof(Announcement), a.Id.ToString(), new { a.Title, a.PublishAtUtc, a.TargetDepartmentId, a.TargetLocationId }, ct: ct);
        if (a.NotifyOnPublish && a.PublishAtUtc <= DateTime.UtcNow) await service.NotifyDueAsync(ct);
        return Created($"/api/announcements/{a.Id}", new { a.Id });
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.AnnouncementsManage)]
    public async Task<IActionResult> Update(Guid id, AnnouncementInput input, [FromServices] AnnouncementService service, [FromServices] IAuditWriter audit, CancellationToken ct)
    {
        var a = await _db.Announcements.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId, ct);
        if (a is null) return NotFound();
        if (await ValidateAsync(input, ct) is { } bad) return bad;
        a.Title = input.Title.Trim();
        a.BodyHtml = AnnouncementService.Sanitize(input.Body);
        Apply(a, input);
        await _db.SaveChangesAsync(ct);
        await audit.WriteAsync("announcement.updated", nameof(Announcement), a.Id.ToString(), new { a.Title }, ct: ct);
        if (a.NotifyOnPublish && a.NotifiedAtUtc is null && a.PublishAtUtc <= DateTime.UtcNow) await service.NotifyDueAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.AnnouncementsManage)]
    public async Task<IActionResult> Delete(Guid id, [FromServices] IAuditWriter audit, CancellationToken ct)
    {
        var a = await _db.Announcements.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId, ct);
        if (a is null) return NotFound();
        a.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
        await audit.WriteAsync("announcement.deleted", nameof(Announcement), a.Id.ToString(), new { a.Title }, ct: ct);
        return NoContent();
    }

    private static void Apply(Announcement a, AnnouncementInput i)
    {
        a.PublishAtUtc = i.PublishAtUtc?.ToUniversalTime() ?? (a.PublishAtUtc == default ? DateTime.UtcNow : a.PublishAtUtc);
        a.ExpiresAtUtc = i.ExpiresAtUtc?.ToUniversalTime();
        a.IsPinned = i.IsPinned;
        a.TargetDepartmentId = i.TargetDepartmentId;
        a.TargetLocationId = i.TargetLocationId;
        a.NotifyOnPublish = i.NotifyOnPublish;
        a.NotifyByEmail = i.NotifyOnPublish && i.NotifyByEmail;
    }

    private async Task<IActionResult?> ValidateAsync(AnnouncementInput i, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(i.Title) || i.Title.Length > 200) errors["title"] = new[] { "Give a title of up to 200 characters." };
        if (string.IsNullOrWhiteSpace(i.Body) || AnnouncementService.Sanitize(i.Body).Length == 0) errors["body"] = new[] { "Write the announcement." };
        else if (i.Body.Length > 20_000) errors["body"] = new[] { "Keep the announcement under 20,000 characters." };
        if (i.ExpiresAtUtc is not null && i.ExpiresAtUtc <= (i.PublishAtUtc ?? DateTime.UtcNow)) errors["expiresAtUtc"] = new[] { "The end date must be after the publish date." };
        if (i.TargetDepartmentId is not null && !await _db.Departments.AnyAsync(d => d.Id == i.TargetDepartmentId && d.CompanyId == CompanyId, ct)) errors["targetDepartmentId"] = new[] { "Unknown department." };
        if (i.TargetLocationId is not null && !await _db.Locations.AnyAsync(d => d.Id == i.TargetLocationId && d.CompanyId == CompanyId, ct)) errors["targetLocationId"] = new[] { "Unknown location." };
        return errors.Count == 0 ? null : ValidationProblem(new ValidationProblemDetails(errors));
    }
}
