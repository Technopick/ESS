using System.Net.Http.Json;
using System.Text.Json;
using Ess.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ess.Infrastructure.Sms;

public sealed class SmsOptions
{
    public const string Section = "Sms";
    /// <summary>None (default), Log (Development only: write to the log) or AfricasTalking.</summary>
    public string Provider { get; set; } = "None";
    public string? Username { get; set; }
    public string? ApiKey { get; set; }
    public string? SenderId { get; set; }
    /// <summary>Gateway URL; use https://api.sandbox.africastalking.com/version1/messaging for the sandbox.</summary>
    public string BaseUrl { get; set; } = "https://api.africastalking.com/version1/messaging";
    public string DefaultCountryCode { get; set; } = "254";
}

/// <summary>
/// SMS gateway adapter. Africa's Talking is built in because it serves Kenya; another gateway is added by implementing
/// <see cref="ISmsSender"/> and selecting it with Sms:Provider. The API key comes from configuration or a secret store only.
/// </summary>
public sealed class ConfigurableSmsSender : ISmsSender
{
    private readonly HttpClient _http;
    private readonly SmsOptions _o;
    private readonly ILogger<ConfigurableSmsSender> _log;

    public ConfigurableSmsSender(HttpClient http, IOptions<SmsOptions> o, ILogger<ConfigurableSmsSender> log)
    {
        _http = http;
        _o = o.Value;
        _log = log;
    }

    public bool IsConfigured => _o.Provider.Equals("Log", StringComparison.OrdinalIgnoreCase)
        || (_o.Provider.Equals("AfricasTalking", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_o.ApiKey) && !string.IsNullOrWhiteSpace(_o.Username));

    public async Task<string?> SendAsync(SmsMessage m, CancellationToken ct = default)
    {
        var to = PhoneNumbers.ToE164(m.To, _o.DefaultCountryCode) ?? throw new ArgumentException("The mobile number is not valid.");
        var text = m.Text.Length > 459 ? m.Text[..456] + "..." : m.Text; // three SMS parts at most
        if (_o.Provider.Equals("Log", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogWarning("SMS provider is 'Log'; SMS to {To} was logged, not sent: {Text}", Mask(to), text);
            return null;
        }
        if (!IsConfigured) throw new InvalidOperationException("No SMS gateway is configured.");

        using var req = new HttpRequestMessage(HttpMethod.Post, _o.BaseUrl);
        req.Headers.Add("apiKey", _o.ApiKey);
        req.Headers.Accept.ParseAdd("application/json");
        var form = new List<KeyValuePair<string, string>> { new("username", _o.Username!), new("to", to), new("message", text) };
        if (!string.IsNullOrWhiteSpace(_o.SenderId)) form.Add(new("from", _o.SenderId));
        req.Content = new FormUrlEncodedContent(form);
        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"SMS gateway returned {(int)res.StatusCode}.");

        using var doc = JsonDocument.Parse(body);
        var recipient = doc.RootElement.GetProperty("SMSMessageData").GetProperty("Recipients").EnumerateArray().FirstOrDefault();
        if (recipient.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("SMS gateway accepted no recipients: " + doc.RootElement.GetProperty("SMSMessageData").GetProperty("Message").GetString());
        var status = recipient.GetProperty("status").GetString();
        if (!string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"SMS gateway status: {status}");
        return recipient.TryGetProperty("messageId", out var id) ? id.GetString() : null;
    }

    private static string Mask(string phone) => phone.Length <= 4 ? "****" : new string('*', phone.Length - 4) + phone[^4..];
}
