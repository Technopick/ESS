using System.Security.Cryptography;
using Ess.Domain.Identity;

namespace Ess.Api.Auth;

/// <summary>Ten single-use recovery codes (xxxxx-xxxxx), stored only as SHA-256 hashes.</summary>
public static class MfaRecoveryCodes
{
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    public static IReadOnlyList<string> Generate(User user, int count = 10)
    {
        var codes = Enumerable.Range(0, count).Select(_ => Code()).ToList();
        user.MfaRecoveryCodeHashes = string.Join(';', codes.Select(c => TokenService.Hash(Normalize(c))));
        return codes;
    }

    public static bool TryConsume(User user, string code)
    {
        if (string.IsNullOrEmpty(user.MfaRecoveryCodeHashes) || string.IsNullOrWhiteSpace(code)) return false;
        var hash = TokenService.Hash(Normalize(code));
        var list = user.MfaRecoveryCodeHashes.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!list.Remove(hash)) return false;
        user.MfaRecoveryCodeHashes = string.Join(';', list);
        return true;
    }

    public static int Remaining(User user) => user.MfaRecoveryCodeHashes?.Split(';', StringSplitOptions.RemoveEmptyEntries).Length ?? 0;

    private static string Normalize(string c) => c.Trim().ToLowerInvariant().Replace("-", "").Replace(" ", "");

    private static string Code()
    {
        var chars = Enumerable.Range(0, 10).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray();
        return new string(chars, 0, 5) + "-" + new string(chars, 5, 5);
    }
}
