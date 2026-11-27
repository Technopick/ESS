using Ess.Application.Abstractions;
using Ess.Application.Notifications;
using Ess.Domain.Notifications;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Infrastructure.Notifications;

public sealed class NotificationService : INotificationService
{
    private readonly EssDbContext _db;
    private readonly IClock _clock;

    public NotificationService(EssDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task QueueAsync(NotificationRequest r, CancellationToken ct = default)
    {
        // Company templates may override the default wording ({subject} and {body} placeholders keep the default text).
        var templates = await _db.NotificationTemplates.AsNoTracking()
            .Where(t => t.CompanyId == r.CompanyId && t.EventCode == r.EventCode && t.IsActive).ToListAsync(ct);

        void Add(NotificationChannel channel)
        {
            var t = templates.FirstOrDefault(x => x.Channel == channel);
            _db.Notifications.Add(new Notification
            {
                CompanyId = r.CompanyId,
                RecipientUserId = r.RecipientUserId,
                EventCode = r.EventCode,
                Channel = channel,
                Subject = t?.Subject is null ? r.Subject : Render(t.Subject, r),
                Body = t is null ? r.Body : Render(t.Body, r),
                LinkUrl = r.LinkUrl,
                Status = DeliveryStatus.Pending,
                NextAttemptAtUtc = _clock.UtcNow
            });
        }

        Add(NotificationChannel.InApp);
        if (r.Email) Add(NotificationChannel.Email);
        // SMS is opt-in per event: it is sent only when HR has created an active SMS template for the event.
        if (templates.Any(t => t.Channel == NotificationChannel.Sms)) Add(NotificationChannel.Sms);
    }

    /// <summary>Fills {subject}, {body}, {link} and any extra values. Unknown placeholders are left as they are so mistakes are visible in a preview.</summary>
    public static string Render(string template, NotificationRequest r)
    {
        var s = template.Replace("{subject}", r.Subject).Replace("{body}", r.Body).Replace("{link}", r.LinkUrl ?? "");
        if (r.Values is not null)
            foreach (var (k, v) in r.Values) s = s.Replace("{" + k + "}", v);
        return s;
    }
}
