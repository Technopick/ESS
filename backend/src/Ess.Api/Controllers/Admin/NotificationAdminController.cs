using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Application.Abstractions;
using Ess.Application.Notifications;
using Ess.Application.Security;
using Ess.Domain.Notifications;
using Ess.Infrastructure.Notifications;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers.Admin;

/// <summary>Notification templates per event and channel, delivery history and retry (spec §8).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/notifications")]
[Route("api/admin/notifications")]
[HasPermission(Permissions.NotificationsConfigure)]
public sealed class NotificationAdminController : ControllerBase
{
    private static readonly string[] Placeholders = { "{subject}", "{body}", "{link}" };
    private readonly EssDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IAuditWriter _audit;

    public NotificationAdminController(EssDbContext db, ICurrentUser user, IAuditWriter audit)
    {
        _db = db;
        _user = user;
        _audit = audit;
    }

    private Guid CompanyId => _user.CompanyId ?? throw new UnauthorizedAccessException();

    public sealed record TemplateView(Guid Id, NotificationChannel Channel, string? Subject, string Body, bool IsActive, DateTime? ModifiedAtUtc);
    public sealed record EventView(string Code, string Description, IReadOnlyList<TemplateView> Templates);
    public sealed record Overview(IReadOnlyList<EventView> Events, IReadOnlyList<string> Placeholders, bool SmsConfigured, bool EmailConfigured);

    [HttpGet("templates")]
    public async Task<Overview> Templates([FromServices] ISmsSender sms, [FromServices] IConfiguration config, CancellationToken ct)
    {
        var templates = await _db.NotificationTemplates.AsNoTracking().Where(t => t.CompanyId == CompanyId).ToListAsync(ct);
        return new Overview(
            NotificationEvents.Catalog.Select(kv => new EventView(kv.Key, kv.Value, templates.Where(t => t.EventCode == kv.Key).OrderBy(t => t.Channel)
                .Select(t => new TemplateView(t.Id, t.Channel, t.Subject, t.Body, t.IsActive, t.ModifiedAtUtc ?? t.CreatedAtUtc)).ToList())).ToList(),
            Placeholders, sms.IsConfigured, !string.IsNullOrWhiteSpace(config["Smtp:Host"]) && !config.GetValue<bool>("Smtp:LogOnly"));
    }

    public sealed record TemplateInput(string? Subject, string Body, bool IsActive);

    /// <summary>Creates or replaces the template for one event and channel. SMS templates switch SMS on for that event.</summary>
    [HttpPut("templates/{eventCode}/{channel}")]
    public async Task<IActionResult> Save(string eventCode, NotificationChannel channel, TemplateInput input, CancellationToken ct)
    {
        if (!NotificationEvents.Catalog.ContainsKey(eventCode)) return NotFound();
        if (channel == NotificationChannel.WhatsApp) return Problem(statusCode: 409, title: "Not available", detail: "WhatsApp needs an approved WhatsApp Business account and is not set up.");
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Body)) errors["body"] = new[] { "Write the message." };
        else if (input.Body.Length > (channel == NotificationChannel.Sms ? 459 : 4000))
            errors["body"] = new[] { channel == NotificationChannel.Sms ? "SMS templates are limited to 459 characters (three messages)." : "Keep the message under 4,000 characters." };
        if (input.Subject is { Length: > 200 }) errors["subject"] = new[] { "Keep the subject under 200 characters." };
        if (eventCode == NotificationEvents.PayslipAvailable && channel == NotificationChannel.Sms && (input.Body.Contains("KES", StringComparison.OrdinalIgnoreCase) || input.Body.Any(char.IsDigit)))
            errors["body"] = new[] { "Payslip SMS must not contain amounts or numbers." };
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));

        var t = await _db.NotificationTemplates.FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.EventCode == eventCode && x.Channel == channel, ct);
        if (t is null)
        {
            t = new NotificationTemplate { CompanyId = CompanyId, EventCode = eventCode, Channel = channel, Body = input.Body };
            _db.NotificationTemplates.Add(t);
        }
        t.Subject = channel == NotificationChannel.Sms ? null : string.IsNullOrWhiteSpace(input.Subject) ? null : input.Subject.Trim();
        t.Body = input.Body.Trim();
        t.IsActive = input.IsActive;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("notification.template.saved", nameof(NotificationTemplate), t.Id.ToString(), new { eventCode, channel = channel.ToString(), input.IsActive }, ct: ct);
        return NoContent();
    }

    [HttpDelete("templates/{eventCode}/{channel}")]
    public async Task<IActionResult> Reset(string eventCode, NotificationChannel channel, CancellationToken ct)
    {
        var t = await _db.NotificationTemplates.FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.EventCode == eventCode && x.Channel == channel, ct);
        if (t is null) return NoContent();
        t.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("notification.template.reset", nameof(NotificationTemplate), t.Id.ToString(), new { eventCode, channel = channel.ToString() }, ct: ct);
        return NoContent();
    }

    public sealed record PreviewInput(string? Subject, string Body);
    public sealed record PreviewResult(string? Subject, string Body);

    [HttpPost("templates/preview")]
    public PreviewResult Preview(PreviewInput input)
    {
        var sample = new NotificationRequest(CompanyId, Guid.Empty, "preview", "LV-2026-000123 was approved",
            "Your annual leave from 2 Dec 2026 to 6 Dec 2026 (5 days) was approved.", "/leave/123");
        return new PreviewResult(input.Subject is null ? null : NotificationService.Render(input.Subject, sample), NotificationService.Render(input.Body, sample));
    }

    public sealed record DeliveryRow(Guid Id, DateTime CreatedAtUtc, string EventCode, NotificationChannel Channel, string Recipient, string? Subject, DeliveryStatus Status,
        int Attempts, DateTime? SentAtUtc, DateTime? ReadAtUtc, string? LastError);

    /// <summary>Delivery history. Message bodies are not listed here because they can be personal; subjects are enough to trace a delivery.</summary>
    [HttpGet("deliveries")]
    public async Task<PagedResult<DeliveryRow>> Deliveries([FromQuery] DeliveryStatus? status, [FromQuery] NotificationChannel? channel, [FromQuery] string? eventCode,
        [FromQuery] string? recipient, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var q = from n in _db.Notifications.AsNoTracking()
                join u in _db.Users on n.RecipientUserId equals u.Id
                where n.CompanyId == CompanyId
                select new { n, u.UserName };
        if (status is not null) q = q.Where(x => x.n.Status == status);
        if (channel is not null) q = q.Where(x => x.n.Channel == channel);
        if (!string.IsNullOrWhiteSpace(eventCode)) q = q.Where(x => x.n.EventCode == eventCode);
        if (!string.IsNullOrWhiteSpace(recipient)) q = q.Where(x => x.UserName.ToLower().Contains(recipient.Trim().ToLower()));
        return await PagedResult<DeliveryRow>.CreateAsync(q.OrderByDescending(x => x.n.CreatedAtUtc)
            .Select(x => new DeliveryRow(x.n.Id, x.n.CreatedAtUtc, x.n.EventCode, x.n.Channel, x.UserName, x.n.Subject, x.n.Status, x.n.Attempts, x.n.SentAtUtc, x.n.ReadAtUtc, x.n.LastError)),
            page, pageSize, ct);
    }

    public sealed record DeliveryStats(int Pending, int Failed, int SentToday, int SuppressedToday);

    [HttpGet("deliveries/stats")]
    public async Task<DeliveryStats> Stats(CancellationToken ct)
    {
        var since = DateTime.UtcNow.Date;
        var q = _db.Notifications.AsNoTracking().Where(n => n.CompanyId == CompanyId);
        return new DeliveryStats(await q.CountAsync(n => n.Status == DeliveryStatus.Pending, ct), await q.CountAsync(n => n.Status == DeliveryStatus.Failed, ct),
            await q.CountAsync(n => (n.Status == DeliveryStatus.Sent || n.Status == DeliveryStatus.Delivered) && n.SentAtUtc >= since, ct),
            await q.CountAsync(n => n.Status == DeliveryStatus.Suppressed && n.CreatedAtUtc >= since, ct));
    }

    /// <summary>Queues failed deliveries again (one, or all failed ones when no id is given).</summary>
    [HttpPost("deliveries/retry")]
    public async Task<IActionResult> Retry([FromQuery] Guid? id, CancellationToken ct)
    {
        var q = _db.Notifications.Where(n => n.CompanyId == CompanyId && n.Status == DeliveryStatus.Failed);
        if (id is not null) q = q.Where(n => n.Id == id);
        var rows = await q.Take(1000).ToListAsync(ct);
        foreach (var n in rows)
        {
            n.Status = DeliveryStatus.Pending;
            n.Attempts = 0;
            n.NextAttemptAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("notification.retried", "Notification", id?.ToString(), new { count = rows.Count }, ct: ct);
        return Ok(new { queued = rows.Count });
    }
}
