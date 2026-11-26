using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Application.Abstractions;
using Ess.Domain.Notifications;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers;

/// <summary>The signed-in user's in-app notifications. Every user has these; no extra permission is needed.</summary>
[ApiController]
[ApiVersion("1.0")]
[Authorize]
[Route("api/v{version:apiVersion}/notifications")]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly ICurrentUser _user;

    public NotificationsController(EssDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public sealed record NotificationView(Guid Id, string EventCode, string? Subject, string Body, string? LinkUrl, DateTime CreatedAtUtc, bool Read);

    private IQueryable<Notification> Mine => _db.Notifications.Where(n => n.RecipientUserId == _user.UserId && n.Channel == NotificationChannel.InApp);

    [HttpGet]
    public async Task<PagedResult<NotificationView>> List([FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var q = Mine.AsNoTracking().Where(n => !unreadOnly || n.ReadAtUtc == null).OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => new NotificationView(n.Id, n.EventCode, n.Subject, n.Body, n.LinkUrl, n.CreatedAtUtc, n.ReadAtUtc != null));
        return await PagedResult<NotificationView>.CreateAsync(q, page, pageSize, ct);
    }

    [HttpGet("unread-count")]
    public async Task<object> UnreadCount(CancellationToken ct) => new { count = await Mine.CountAsync(n => n.ReadAtUtc == null, ct) };

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await Mine.Where(n => n.Id == id && n.ReadAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, DateTime.UtcNow), ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await Mine.Where(n => n.ReadAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, DateTime.UtcNow), ct);
        return NoContent();
    }
}
