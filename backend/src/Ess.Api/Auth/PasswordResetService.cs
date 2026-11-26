using System.Net;
using System.Security.Cryptography;
using Ess.Application.Abstractions;
using Ess.Application.Identity;
using Ess.Domain.Identity;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Auth;

/// <summary>Issues and consumes single-use reset/invitation links. Successful use revokes every session of the user.</summary>
public sealed class PasswordResetService
{
    private readonly EssDbContext _db;
    private readonly IEmailSender _email;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IConfiguration _config;

    public PasswordResetService(EssDbContext db, IEmailSender email, IPasswordHasher<User> hasher, IConfiguration config)
    {
        _db = db;
        _email = email;
        _hasher = hasher;
        _config = config;
    }

    public async Task SendAsync(User user, string purpose, TimeSpan lifetime, string? ip, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // Invalidate earlier unused links of the same purpose.
        await _db.PasswordResetTokens.Where(t => t.UserId == user.Id && t.Purpose == purpose && t.UsedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAtUtc, now), ct);

        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id, TokenHash = TokenService.Hash(raw), CreatedAtUtc = now, ExpiresAtUtc = now.Add(lifetime), Purpose = purpose, RequestedFromIp = ip
        });
        await _db.SaveChangesAsync(ct);

        var baseUrl = (_config["App:PublicUrl"] ?? "http://localhost:5173").TrimEnd('/');
        var link = $"{baseUrl}/reset-password?token={raw}";
        var name = WebUtility.HtmlEncode(user.UserName);
        var (subject, intro) = purpose == "invite"
            ? ("Your Employee Self-Service account", "An account has been created for you. Choose a password to activate it.")
            : ("Reset your Employee Self-Service password", "We received a request to reset your password. If it wasn't you, ignore this email.");
        var hours = lifetime.TotalHours >= 1 ? $"{lifetime.TotalHours:0} hours" : $"{lifetime.TotalMinutes:0} minutes";
        await _email.SendAsync(new EmailMessage(user.Email, subject,
            $"<p>Hello {name},</p><p>{intro}</p><p><a href=\"{link}\">Set your password</a></p><p>The link works once and expires in {hours}.</p>",
            $"Hello {user.UserName},\n\n{intro}\n\n{link}\n\nThe link works once and expires in {hours}."), ct);
    }

    public async Task<(bool Ok, string? Error, User? User)> ConsumeAsync(string rawToken, string newPassword, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var hash = TokenService.Hash(rawToken ?? "");
        var token = await _db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.UsedAtUtc is not null || token.ExpiresAtUtc < now)
            return (false, "This link is invalid or has expired. Request a new one.", null);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId && u.IsActive, ct);
        if (user is null) return (false, "This link is invalid or has expired. Request a new one.", null);

        var errors = PasswordPolicy.Validate(newPassword ?? "", user.UserName);
        if (errors.Count > 0) return (false, string.Join(" ", errors), null);

        user.PasswordHash = _hasher.HashPassword(user, newPassword!);
        user.PasswordChangedAtUtc = now;
        user.MustChangePassword = false;
        user.EmailConfirmed = true;
        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        token.UsedAtUtc = now;
        await _db.SaveChangesAsync(ct);
        await _db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), ct);
        return (true, null, user);
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
