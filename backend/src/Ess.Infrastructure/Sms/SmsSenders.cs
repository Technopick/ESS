using System.Net.Http.Json;
using System.Text.Json;
using Ess.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ess.Infrastructure.Sms;

public sealed class SmsOptions
{
    public const string Section = "Sms";
    /// <summary>None (default), Log (testing: write to the log only), MobileSasa or AfricasTalking.</summary>
    public string Provider { get; set; } = "None";
    /// <summary>Africa's Talking only: the account user name.</summary>
    public string? Username { get; set; }
    /// <summary>Mobile Sasa API token (starts with mbs_) or Africa's Talking API key. From a secret store or environment variable only.</summary>
    public string? ApiKey { get; set; }
    /// <summary>The approved sender name (alphanumeric sender ID). Required by Mobile Sasa.</summary>
    public string? SenderId { get; set; }
    /// <summary>Gateway URL. Empty uses the provider's default.</summary>
    public string? BaseUrl { get; set; }
    public string DefaultCountryCode { get; set; } = "254";
    /// <summary>Mobile Sasa only: International sends 2547XXXXXXXX, Local sends 07XXXXXXXX.</summary>
    public string PhoneFormat { get; set; } = "International";

    public const string MobileSasaDefaultUrl = "https://api.mobilesasa.com/v1/send/message";
    public const string AfricasTalkingDefaultUrl = "https://api.africastalking.com/version1/messaging";
}

/// <summary>
/// SMS gateway adapter. Mobile Sasa is the customer's gateway; Africa's Talking is kept as an alternative. Another gateway is
/// added by implementing <see cref="ISmsSender"/> and selecting it with Sms:Provider. Keys come from a secret store only.
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

    private bool Is(string provider) => _o.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase);

    public bool IsConfigured => Is("Log")
        || (Is("MobileSasa") && !string.IsNullOrWhiteSpace(_o.ApiKey) && !string.IsNullOrWhiteSpace(_o.SenderId))
        || (Is("AfricasTalking") && !string.IsNullOrWhiteSpace(_o.ApiKey) && !string.IsNullOrWhiteSpace(_o.Username));

    public async Task<string?> SendAsync(SmsMessage m, CancellationToken ct = default)
    {
        var to = PhoneNumbers.ToE164(m.To, _o.DefaultCountryCode) ?? throw new ArgumentException("The mobile number is not valid.");
        var text = m.Text.Length > 459 ? m.Text[..456] + "..." : m.Text; // three SMS parts at most
        if (Is("Log"))
        {
            _log.LogWarning("SMS provider is 'Log'; SMS to {To} was logged, not sent: {Text}", Mask(to), text);
            return null;
        }
        if (!IsConfigured) throw new InvalidOperationException("No SMS gateway is configured.");
        return Is("MobileSasa") ? await SendMobileSasaAsync(to, text, ct) : await SendAfricasTalkingAsync(to, text, ct);
    }

    /// <summary>Mobile Sasa: POST JSON {senderID, phone, message} with a Bearer token.</summary>
    private async Task<string?> SendMobileSasaAsync(string e164, string text, CancellationToken ct)
    {
        var phone = _o.PhoneFormat.Equals("Local", StringComparison.OrdinalIgnoreCase) && e164.StartsWith("+" + _o.DefaultCountryCode)
            ? "0" + e164[(1 + _o.DefaultCountryCode.Length)..]
            : e164.TrimStart('+');
        using var req = new HttpRequestMessage(HttpMethod.Post, string.IsNullOrWhiteSpace(_o.BaseUrl) ? SmsOptions.MobileSasaDefaultUrl : _o.BaseUrl);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _o.ApiKey);
        req.Headers.Accept.ParseAdd("application/json");
        req.Content = JsonContent.Create(new { senderID = _o.SenderId, phone, message = text });
        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        return ParseMobileSasaResponse((int)res.StatusCode, body);
    }

    /// <summary>
    /// Reads a Mobile Sasa reply. A non-success HTTP status, or a body with "status": false, is a rejection (thrown so the
    /// dispatcher records it and retries). The gateway's message id is returned when present.
    /// </summary>
    public static string? ParseMobileSasaResponse(int httpStatus, string body)
    {
        JsonElement root = default;
        var hasJson = false;
        try { root = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body).RootElement.Clone(); hasJson = root.ValueKind == JsonValueKind.Object; }
        catch (JsonException) { }
        string? Text(params string[] names)
        {
            if (!hasJson) return null;
            foreach (var n in names)
                if (root.TryGetProperty(n, out var v)) return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
            return null;
        }
        var detail = Text("message", "responseDescription", "error") ?? "no details";
        if (httpStatus is < 200 or > 299) throw new HttpRequestException($"SMS gateway returned {httpStatus}: {detail}");
        if (hasJson && root.TryGetProperty("status", out var st) && (st.ValueKind == JsonValueKind.False || (st.ValueKind == JsonValueKind.String && st.GetString() is "false" or "failed" or "error")))
            throw new InvalidOperationException($"SMS gateway refused the message: {detail}");
        return Text("messageId", "messageID", "message_id", "bulkId");
    }

    private async Task<string?> SendAfricasTalkingAsync(string to, string text, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, string.IsNullOrWhiteSpace(_o.BaseUrl) ? SmsOptions.AfricasTalkingDefaultUrl : _o.BaseUrl);
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
