using System.Net;
using Ess.Api.Hubs;
using Ess.Application.Abstractions;
using Ess.Domain.Notifications;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Notifications;

/// <summary>
/// Delivers queued notifications after their business transaction commits: in-app ones are pushed over SignalR,
/// email ones are sent with retry and backoff. Rows are claimed with FOR UPDATE SKIP LOCKED so several API nodes can run this.
/// </summary>
public sealed class NotificationDispatcher : BackgroundService
{
    private const int MaxAttempts = 5;
    private readonly IServiceScopeFactory _scopes;
    private readonly IHubContext<NotificationsHub> _hub;
    private readonly ILogger<NotificationDispatcher> _log;
    private readonly IConfiguration _config;

    public NotificationDispatcher(IServiceScopeFactory scopes, IHubContext<NotificationsHub> hub, ILogger<NotificationDispatcher> log, IConfiguration config)
    {
        _scopes = scopes;
        _hub = hub;
        _log = log;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_config.GetValue("Notifications:DispatchIntervalSeconds", 5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await DispatchBatchAsync(stoppingToken) > 0) { }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Notification dispatch failed; retrying");
            }
            try { await Task.Delay(interval, stoppingToken); } catch (OperationCanceledException) { }
        }
    }

    public async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var sms = scope.ServiceProvider.GetRequiredService<ISmsSender>();
        var baseUrl = (_config["App:PublicUrl"] ?? "").TrimEnd('/');
        var now = DateTime.UtcNow;

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var batch = await db.Notifications
                .FromSqlInterpolated($"SELECT * FROM notifications WHERE status = {(int)DeliveryStatus.Pending} AND next_attempt_at_utc <= {now} AND NOT is_deleted ORDER BY created_at_utc LIMIT 50 FOR UPDATE SKIP LOCKED")
                .ToListAsync(ct);
            if (batch.Count == 0) return 0;

            var userIds = batch.Select(n => n.RecipientUserId).Distinct().ToList();
            var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
            var smsUsers = batch.Where(n => n.Channel == NotificationChannel.Sms).Select(n => n.RecipientUserId).Distinct().ToList();
            var phones = smsUsers.Count == 0 ? new Dictionary<Guid, string?>() : await (from m in db.EmployeeMappings.AsNoTracking()
                                                                                       join e in db.Employees on m.EmployeeId equals e.Id
                                                                                       where smsUsers.Contains(m.UserId)
                                                                                       select new { m.UserId, e.MobilePhone }).GroupBy(x => x.UserId)
                .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.MobilePhone).FirstOrDefault(p => p != null), ct);

            foreach (var n in batch)
            {
                n.Attempts++;
                try
                {
                    if (!users.TryGetValue(n.RecipientUserId, out var user) || !user.IsActive)
                    {
                        n.Status = DeliveryStatus.Suppressed;
                        continue;
                    }
                    if (n.Channel == NotificationChannel.InApp)
                    {
                        await _hub.Clients.User(n.RecipientUserId.ToString()).SendAsync("notification",
                            new { n.Id, n.EventCode, n.Subject, n.Body, n.LinkUrl, n.CreatedAtUtc }, ct);
                        n.Status = DeliveryStatus.Delivered;
                    }
                    else if (n.Channel == NotificationChannel.Email)
                    {
                        var link = n.LinkUrl is null || baseUrl.Length == 0 ? "" : $"<p><a href=\"{baseUrl}{WebUtility.HtmlEncode(n.LinkUrl)}\">Open in the portal</a></p>";
                        await email.SendAsync(new EmailMessage(user.Email, n.Subject ?? "Notification",
                            $"<p>{WebUtility.HtmlEncode(n.Body)}</p>{link}", n.Body), ct);
                        n.Status = DeliveryStatus.Sent;
                    }
                    else if (n.Channel == NotificationChannel.Sms)
                    {
                        var phone = PhoneNumbers.ToE164(phones.GetValueOrDefault(n.RecipientUserId));
                        if (!sms.IsConfigured) { n.Status = DeliveryStatus.Suppressed; n.LastError = "No SMS gateway is configured."; }
                        else if (phone is null) { n.Status = DeliveryStatus.Suppressed; n.LastError = "No valid mobile number in NAV."; }
                        else
                        {
                            n.ProviderMessageId = await sms.SendAsync(new SmsMessage(phone, n.Body), ct);
                            n.Status = DeliveryStatus.Sent;
                        }
                    }
                    else n.Status = DeliveryStatus.Suppressed; // WhatsApp Business needs a Meta-approved account; not configured
                    n.SentAtUtc = DateTime.UtcNow;
                    if (n.Status != DeliveryStatus.Suppressed) n.LastError = null;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    n.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                    if (n.Attempts >= MaxAttempts) n.Status = DeliveryStatus.Failed;
                    else n.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(Math.Pow(2, n.Attempts));
                    _log.LogWarning("Notification {Id} ({Channel}) attempt {Attempt} failed: {Error}", n.Id, n.Channel, n.Attempts, n.LastError);
                }
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return batch.Count;
        });
    }
}
