using System.Security.Cryptography;
using Asp.Versioning;
using Ess.Application.Abstractions;
using Ess.Application.Identity;
using Ess.Domain.Identity;
using Ess.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Auth;

/// <summary>Sign-in (password + optional TOTP), token refresh, sign-out and self-service password reset (spec §9, §14).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController : ControllerBase
{
    public const string RefreshCookie = "ess_rt";
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MfaChallengeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(30);

    private readonly EssDbContext _db;
    private readonly TokenService _tokens;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IAuditWriter _audit;
    private readonly ISecretProtector _secrets;
    private readonly ITimeLimitedDataProtector _mfaChallenge;

    public AuthController(EssDbContext db, TokenService tokens, IPasswordHasher<User> hasher, IAuditWriter audit, ISecretProtector secrets,
        IDataProtectionProvider dp)
    {
        _db = db;
        _tokens = tokens;
        _hasher = hasher;
        _audit = audit;
        _secrets = secrets;
        _mfaChallenge = dp.CreateProtector("Ess.MfaChallenge.v1").ToTimeLimitedDataProtector();
    }

    public sealed record LoginRequest(string UserName, string Password);
    /// <summary>Either tokens, or MfaRequired with a short-lived challenge to pass to /auth/mfa.</summary>
    public sealed record LoginResponse(string? AccessToken, DateTime? ExpiresAtUtc, bool MfaRequired = false, string? MfaChallenge = null,
        bool MustChangePassword = false);
    public sealed record MfaRequest(string MfaChallenge, string Code);
    public sealed record ForgotPasswordRequest(string Email);
    public sealed record ResetPasswordRequest(string Token, string NewPassword);

    public sealed class LoginValidator : AbstractValidator<LoginRequest>
    {
        public LoginValidator()
        {
            RuleFor(x => x.UserName).NotEmpty().MaximumLength(256);
            RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct)
    {
        await new LoginValidator().ValidateAndThrowAsync(req, ct);
        var norm = req.UserName.Trim().ToUpperInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == norm || u.NormalizedEmail == norm, ct);

        if (user is null || !user.IsActive || user.PasswordHash is null)
        {
            // Hash anyway so response time does not reveal whether the account exists.
            _hasher.HashPassword(new User { UserName = "x", NormalizedUserName = "X", Email = "x", NormalizedEmail = "X" }, req.Password);
            await _audit.WriteAsync("auth.login", "User", null, new { userName = req.UserName }, success: false, ct);
            return InvalidCredentials();
        }
        if (IsLocked(user)) return Locked(user, ct);

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            await RegisterFailureAsync(user, "auth.login", ct);
            return InvalidCredentials();
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = _hasher.HashPassword(user, req.Password);

        if (user.MfaEnabled)
        {
            await _db.SaveChangesAsync(ct);
            var challenge = _mfaChallenge.Protect($"{user.Id}|{user.SecurityStamp}", MfaChallengeLifetime);
            return Ok(new LoginResponse(null, null, MfaRequired: true, MfaChallenge: challenge));
        }
        return await CompleteSignInAsync(user, "password", ct);
    }

    /// <summary>Second step: a 6-digit authenticator code or a one-time recovery code.</summary>
    [HttpPost("mfa")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyMfa(MfaRequest req, CancellationToken ct)
    {
        string payload;
        try { payload = _mfaChallenge.Unprotect(req.MfaChallenge); }
        catch (CryptographicException) { return Problem(statusCode: 401, title: "The sign-in session expired. Sign in again."); }

        var parts = payload.Split('|');
        var userId = Guid.Parse(parts[0]);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        if (user is null || user.SecurityStamp != parts[1] || !user.MfaEnabled || user.MfaSecretEncrypted is null)
            return Problem(statusCode: 401, title: "The sign-in session expired. Sign in again.");
        if (IsLocked(user)) return Locked(user, ct);

        var code = (req.Code ?? "").Trim();
        var secret = _secrets.Unprotect(user.MfaSecretEncrypted);
        var step = Totp.Verify(secret, code, DateTime.UtcNow, user.MfaLastTimeStep);
        if (step is not null)
        {
            user.MfaLastTimeStep = step;
            return await CompleteSignInAsync(user, "totp", ct);
        }
        if (MfaRecoveryCodes.TryConsume(user, code))
        {
            await _audit.WriteAsync("auth.mfa.recovery_code_used", "User", user.Id.ToString(), null, true, ct);
            return await CompleteSignInAsync(user, "recovery-code", ct);
        }
        await RegisterFailureAsync(user, "auth.mfa", ct);
        return Problem(statusCode: 401, title: "That code is not valid. Check your authenticator app and try again.");
    }

    /// <summary>Rotates the refresh token (sent as an HttpOnly, SameSite=Strict cookie, never readable by scripts).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var raw = Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(raw)) return Unauthorized();
        var hash = TokenService.Hash(raw);
        var now = DateTime.UtcNow;
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null) return Unauthorized();

        if (token.RevokedAtUtc is not null)
        {
            // A rotated token was presented again: assume theft and revoke the whole family.
            await _db.RefreshTokens.Where(t => t.FamilyId == token.FamilyId && t.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), ct);
            await _audit.WriteAsync("auth.refresh.reuse", "User", token.UserId.ToString(), null, false, ct);
            ClearRefreshCookie();
            return Unauthorized();
        }
        if (!token.IsActive(now)) return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId && u.IsActive, ct);
        if (user is null || IsLocked(user)) return Unauthorized();

        var issued = await _tokens.IssueAsync(user, token.FamilyId, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent, ct);
        token.RevokedAtUtc = now;
        token.ReplacedByHash = TokenService.Hash(issued.RefreshToken);
        await _db.SaveChangesAsync(ct);
        SetRefreshCookie(issued.RefreshToken, issued.RefreshTokenExpiresUtc);
        return Ok(new LoginResponse(issued.AccessToken, issued.AccessTokenExpiresUtc, MustChangePassword: user.MustChangePassword));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var raw = Request.Cookies[RefreshCookie];
        if (!string.IsNullOrEmpty(raw))
        {
            var hash = TokenService.Hash(raw);
            var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (token is not null)
            {
                await _db.RefreshTokens.Where(t => t.FamilyId == token.FamilyId && t.RevokedAtUtc == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow), ct);
                await _audit.WriteAsync("auth.logout", "User", token.UserId.ToString(), null, true, ct);
            }
        }
        ClearRefreshCookie();
        return NoContent();
    }

    /// <summary>Always answers 202 so the endpoint cannot be used to discover which emails have accounts.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest req, [FromServices] PasswordResetService resets, CancellationToken ct)
    {
        var norm = (req.Email ?? "").Trim().ToUpperInvariant();
        var user = norm.Length == 0 ? null : await _db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == norm && u.IsActive, ct);
        if (user is not null)
        {
            await resets.SendAsync(user, "reset", ResetLifetime, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            await _audit.WriteAsync("auth.password.reset_requested", "User", user.Id.ToString(), null, true, ct);
        }
        return Accepted(new { message = "If an account exists for that email, a reset link has been sent." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req, [FromServices] PasswordResetService resets, CancellationToken ct)
    {
        var (ok, error, user) = await resets.ConsumeAsync(req.Token, req.NewPassword, ct);
        if (!ok) return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["newPassword"] = new[] { error! } }));
        await _audit.WriteAsync("auth.password.reset", "User", user!.Id.ToString(), null, true, ct);
        return NoContent();
    }

    public sealed record MeResponse(string? UserName, IEnumerable<string> Roles, IEnumerable<string> Permissions, string? CompanyId, string? EmployeeId);

    [HttpGet("me")]
    public MeResponse Me() => new(User.FindFirst("name")?.Value,
        User.FindAll("role").Select(c => c.Value), User.FindAll(Infrastructure.HttpCurrentUser.PermissionClaim).Select(c => c.Value),
        User.FindFirst(Infrastructure.HttpCurrentUser.CompanyClaim)?.Value, User.FindFirst(Infrastructure.HttpCurrentUser.EmployeeClaim)?.Value);

    private async Task<IActionResult> CompleteSignInAsync(User user, string method, CancellationToken ct)
    {
        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        var issued = await _tokens.IssueAsync(user, null, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent, ct);
        SetRefreshCookie(issued.RefreshToken, issued.RefreshTokenExpiresUtc);
        await _audit.WriteAsync("auth.login", "User", user.Id.ToString(), new { method }, true, ct);
        return Ok(new LoginResponse(issued.AccessToken, issued.AccessTokenExpiresUtc, MustChangePassword: user.MustChangePassword));
    }

    private async Task RegisterFailureAsync(User user, string action, CancellationToken ct)
    {
        user.AccessFailedCount++;
        if (user.AccessFailedCount >= MaxFailedAttempts)
        {
            user.LockoutEndUtc = DateTime.UtcNow.Add(LockoutDuration);
            user.AccessFailedCount = 0;
            await _audit.WriteAsync("auth.lockout", "User", user.Id.ToString(), null, false, ct);
        }
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(action, "User", user.Id.ToString(), null, false, ct);
    }

    private static bool IsLocked(User user) => user.LockoutEndUtc > DateTime.UtcNow;

    private IActionResult Locked(User user, CancellationToken ct)
    {
        _ = ct;
        return Problem(statusCode: 423, title: "Account temporarily locked after repeated failed sign-ins. Try again later or reset your password.");
    }

    private IActionResult InvalidCredentials() => Problem(statusCode: 401, title: "Invalid user name or password.");

    private void SetRefreshCookie(string value, DateTime expires) =>
        Response.Cookies.Append(RefreshCookie, value, new CookieOptions
        {
            HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Strict, Expires = expires, Path = "/api"
        });

    private void ClearRefreshCookie() => Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/api" });
}
