using Ess.Domain.Common;

namespace Ess.Domain.Notifications;

public enum NotificationChannel { InApp = 0, Email = 1, Sms = 2, WhatsApp = 3 }
public enum DeliveryStatus { Pending = 0, Sent = 1, Delivered = 2, Failed = 3, Suppressed = 4 }

public class NotificationTemplate : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    /// <summary>Event code, e.g. "leave.approved", "payslip.published".</summary>
    public required string EventCode { get; set; }
    public NotificationChannel Channel { get; set; }
    public string? Subject { get; set; }
    /// <summary>Template body with {{placeholders}}. Payroll amounts must never be placed in SMS/WhatsApp templates.</summary>
    public required string Body { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Notification : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid RecipientUserId { get; set; }
    public required string EventCode { get; set; }
    public NotificationChannel Channel { get; set; }
    public string? Subject { get; set; }
    public required string Body { get; set; }
    public string? LinkUrl { get; set; }
    public DeliveryStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? LastError { get; set; }
}

public class Announcement : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public required string Title { get; set; }
    public required string BodyHtml { get; set; }
    public DateTime PublishAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public Guid? TargetDepartmentId { get; set; }
    public Guid? TargetLocationId { get; set; }
    public bool IsPinned { get; set; }
}
