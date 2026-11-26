using Ess.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Ess.Infrastructure.Email;

public sealed class SmtpOptions
{
    public const string Section = "Smtp";
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@example.invalid";
    public string FromName { get; set; } = "Employee Self-Service";
    /// <summary>Development: write emails to the log instead of sending (never enable in Production).</summary>
    public bool LogOnly { get; set; }
}

/// <summary>SMTP sender (MailKit). The full notification service with templates, retries and SMS arrives in Phase 5.</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _o;
    private readonly ILogger<SmtpEmailSender> _log;

    public SmtpEmailSender(IOptions<SmtpOptions> o, ILogger<SmtpEmailSender> log)
    {
        _o = o.Value;
        _log = log;
    }

    public async Task SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        if (_o.LogOnly || string.IsNullOrWhiteSpace(_o.Host))
        {
            _log.LogWarning("SMTP not configured; email to {To} with subject '{Subject}' was logged, not sent. Body: {Body}", m.To, m.Subject, m.TextBody ?? m.HtmlBody);
            return;
        }
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_o.FromName, _o.FromAddress));
        msg.To.Add(MailboxAddress.Parse(m.To));
        msg.Subject = m.Subject;
        msg.Body = new BodyBuilder { HtmlBody = m.HtmlBody, TextBody = m.TextBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_o.Host, _o.Port, _o.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, ct);
        if (!string.IsNullOrEmpty(_o.UserName)) await client.AuthenticateAsync(_o.UserName, _o.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }
}
