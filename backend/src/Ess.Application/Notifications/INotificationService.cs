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
    public const string AnnouncementPublished = "announcement.published";
    public const string ContractExpiring = "contract.expiring";

    /// <summary>Every event with a plain description, for the template editor.</summary>
    public static readonly IReadOnlyDictionary<string, string> Catalog = new Dictionary<string, string>
    {
        [ApprovalRequired] = "A request is waiting for the recipient's approval",
        [ApprovalReminder] = "Reminder about an approval that is still waiting",
        [ApprovalEscalated] = "An overdue approval was escalated to the recipient",
        [RequestApproved] = "The recipient's request was approved or completed",
        [RequestRejected] = "The recipient's request was rejected",
        [RequestReturned] = "The recipient's request was returned for correction",
        [NavPosted] = "The recipient's request was recorded in NAV",
        [NavFailed] = "NAV did not accept the recipient's request",
        [PayslipAvailable] = "A new payslip is available (never contains amounts)",
        [DocumentExpiring] = "One of the recipient's documents is about to expire",
        [AnnouncementPublished] = "HR published an announcement for the recipient",
        [ContractExpiring] = "A staff contract the recipient manages is about to end",
    };
}

public sealed record NotificationRequest(
    Guid CompanyId,
    Guid RecipientUserId,
    string EventCode,
    string Subject,
    string Body,
    string? LinkUrl,
    bool Email = true,
    /// <summary>Extra values for template placeholders such as {reference} or {employee}.</summary>
    IReadOnlyDictionary<string, string>? Values = null);

/// <summary>
/// Queues in-app (and optionally email) notifications in the caller's unit of work; a background dispatcher
/// pushes them over SignalR and sends email after commit. Never put payroll figures or other sensitive data in a notification.
/// </summary>
public interface INotificationService
{
    Task QueueAsync(NotificationRequest request, CancellationToken ct = default);
}
