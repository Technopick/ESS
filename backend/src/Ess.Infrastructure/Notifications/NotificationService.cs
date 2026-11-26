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
                Subject = t?.Subject?.Replace("{subject}", r.Subject) ?? r.Subject,
                Body = t?.Body.Replace("{body}", r.Body).Replace("{subject}", r.Subject) ?? r.Body,
                LinkUrl = r.LinkUrl,
                Status = DeliveryStatus.Pending,
                NextAttemptAtUtc = _clock.UtcNow
            });
        }

        Add(NotificationChannel.InApp);
        if (r.Email) Add(NotificationChannel.Email);
    }
}
