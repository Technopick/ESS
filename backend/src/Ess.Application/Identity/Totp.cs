using System.Security.Cryptography;
using System.Text;

namespace Ess.Application.Identity;

/// <summary>RFC 6238 TOTP (HMAC-SHA1, 30-second step, 6 digits) compatible with Microsoft/Google Authenticator.</summary>
public static class Totp
{
    public const int StepSeconds = 30;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string NewSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public static long TimeStep(DateTime utcNow) => new DateTimeOffset(utcNow, TimeSpan.Zero).ToUnixTimeSeconds() / StepSeconds;

    public static string Compute(string base32Secret, long timeStep)
    {
        var key = Base32Decode(base32Secret);
        var counter = BitConverter.GetBytes(timeStep);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>Verifies a code allowing one step of clock drift either way. Returns the matched step, or null.</summary>
    public static long? Verify(string base32Secret, string code, DateTime utcNow, long? lastUsedStep)
    {
        code = code.Replace(" ", "");
        if (code.Length != 6 || !code.All(char.IsDigit)) return null;
        var now = TimeStep(utcNow);
        for (var drift = -1; drift <= 1; drift++)
        {
            var step = now + drift;
            if (lastUsedStep is not null && step <= lastUsedStep) continue; // replay protection
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(base32Secret, step)), Encoding.ASCII.GetBytes(code)))
                return step;
        }
        return null;
    }

    public static string OtpAuthUri(string issuer, string account, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits=6&period=30";

    public static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Base32Decode(string s)
    {
        s = s.TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in s)
        {
            var v = Base32Alphabet.IndexOf(c);
            if (v < 0) throw new FormatException("Invalid base32 character.");
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return bytes.ToArray();
    }
}

/// <summary>Password policy shared by reset, invite and admin-set passwords.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;

    public static IReadOnlyList<string> Validate(string password, string? userName = null)
    {
        var errors = new List<string>();
        if (password.Length < MinLength) errors.Add($"Use at least {MinLength} characters.");
        if (password.Length > 128) errors.Add("Use at most 128 characters.");
        var classes = new[] { password.Any(char.IsLower), password.Any(char.IsUpper), password.Any(char.IsDigit), password.Any(c => !char.IsLetterOrDigit(c)) }.Count(x => x);
        if (classes < 3) errors.Add("Use at least three of: lower-case, upper-case, digits, symbols.");
        if (userName is not null && password.Contains(userName, StringComparison.OrdinalIgnoreCase)) errors.Add("Do not include your user name.");
        return errors;
    }
}
