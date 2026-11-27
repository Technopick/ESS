using Ess.Application.Notifications;
using Ess.Domain.Notifications;
using Ess.Domain.Organization;
using Ess.Infrastructure.Persistence;
using Ganss.Xss;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Services;

/// <summary>Company announcements: HTML is sanitised on save, and staff in the target group are notified when an announcement goes live.</summary>
public sealed class AnnouncementService
{
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();
    private readonly EssDbContext _db;
    private readonly INotificationService _notify;

    public AnnouncementService(EssDbContext db, INotificationService notify)
    {
        _db = db;
        _notify = notify;
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var t in new[] { "p", "br", "strong", "b", "em", "i", "u", "ul", "ol", "li", "a", "h3", "h4", "blockquote" }) s.AllowedTags.Add(t);
        s.AllowedAttributes.Clear();
        s.AllowedAttributes.Add("href");
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("https");
        s.AllowedSchemes.Add("mailto");
        s.AllowedCssProperties.Clear();
        s.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Html.Dom.IHtmlAnchorElement a) { a.SetAttribute("rel", "noopener noreferrer"); a.SetAttribute("target", "_blank"); }
        };
        s.AllowedAttributes.Add("rel");
        s.AllowedAttributes.Add("target");
        return s;
    }

    /// <summary>Plain text becomes paragraphs; HTML keeps only basic formatting and https/mailto links.</summary>
    public static string Sanitize(string input)
    {
        var looksHtml = System.Text.RegularExpressions.Regex.IsMatch(input, @"<\s*/?\s*(p|br|strong|b|em|i|u|ul|ol|li|a|h[1-6]|blockquote|div|span)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var html = looksHtml ? input : string.Join("", input.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => "<p>" + System.Net.WebUtility.HtmlEncode(p.Trim()).Replace("\n", "<br>") + "</p>"));
        return Sanitizer.Sanitize(html).Trim();
    }

    /// <summary>Notifies the target group of every live announcement that asked for it and has not been sent yet. Safe to run repeatedly.</summary>
    public async Task<int> NotifyDueAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var due = await _db.Announcements.Where(a => a.NotifyOnPublish && a.NotifiedAtUtc == null && a.PublishAtUtc <= now && (a.ExpiresAtUtc == null || a.ExpiresAtUtc > now)).ToListAsync(ct);
        var sent = 0;
        foreach (var a in due)
        {
            var recipients = await (from m in _db.EmployeeMappings
                                    join e in _db.Employees on m.EmployeeId equals e.Id
                                    join u in _db.Users on m.UserId equals u.Id
                                    where m.CompanyId == a.CompanyId && u.IsActive && e.Status != EmploymentStatus.Terminated
                                          && (a.TargetDepartmentId == null || e.DepartmentId == a.TargetDepartmentId)
                                          && (a.TargetLocationId == null || e.LocationId == a.TargetLocationId)
                                    select m.UserId).Distinct().ToListAsync(ct);
            foreach (var userId in recipients)
                await _notify.QueueAsync(new NotificationRequest(a.CompanyId, userId, NotificationEvents.AnnouncementPublished, a.Title,
                    $"New announcement from HR: {a.Title}", "/", Email: a.NotifyByEmail), ct);
            a.NotifiedAtUtc = now;
            sent += recipients.Count;
            await _db.SaveChangesAsync(ct);
        }
        return sent;
    }
}
