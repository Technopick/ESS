namespace Ess.Application.Notifications;

/// <summary>Event codes for notifications (templates can override wording per company).</summary>
public static class NotificationEvents
{
    public const string ApprovalRequired = "approval.required";
    public const string ApprovalReminder = "approval.reminder";
    public const string ApprovalEscalated = "approval.escalated";
    public const string RequestApproved = "request.approved";
    public const string RequestRejected = "request.rejected";
    public const string RequestReturned = "request.returned";
    public const string NavPosted = "nav.posted";
    public const string NavFailed = "nav.failed";
    public const string PayslipAvailable = "payslip.available";
    public const string DocumentExpiring = "document.expiring";
}

public sealed record NotificationRequest(
    Guid CompanyId,
    Guid RecipientUserId,
    string EventCode,
    string Subject,
    string Body,
    string? LinkUrl,
    bool Email = true);

/// <summary>
/// Queues in-app (and optionally email) notifications in the caller's unit of work; a background dispatcher
/// pushes them over SignalR and sends email after commit. Never put payroll figures or other sensitive data in a notification.
/// </summary>
public interface INotificationService
{
    Task QueueAsync(NotificationRequest request, CancellationToken ct = default);
}
