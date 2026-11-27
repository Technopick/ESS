namespace Ess.Application.Abstractions;

public sealed record SmsMessage(string To, string Text);

/// <summary>Sends SMS through the configured gateway. Never send payroll figures or other sensitive data by SMS.</summary>
public interface ISmsSender
{
    bool IsConfigured { get; }
    /// <summary>Returns the provider's message id. Throws when the gateway rejects the message so the dispatcher can retry.</summary>
    Task<string?> SendAsync(SmsMessage message, CancellationToken ct = default);
}

public static class PhoneNumbers
{
    /// <summary>Normalises Kenyan numbers to E.164 (07xx / 01xx / 254xx to +254...). Returns null when the number is not usable.</summary>
    public static string? ToE164(string? raw, string defaultCountryCode = "254")
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (raw.TrimStart().StartsWith('+')) return digits.Length is >= 9 and <= 15 ? "+" + digits : null;
        if (digits.StartsWith("00")) digits = digits[2..];
        else if (digits.StartsWith('0')) digits = defaultCountryCode + digits[1..];
        else if (digits.Length == 9) digits = defaultCountryCode + digits;
        return digits.Length is >= 11 and <= 15 ? "+" + digits : null;
    }
}
