using Asp.Versioning;
using Ess.Application.Abstractions;
using Ess.Domain.Identity;
using Ess.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Auth;

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

    private readonly EssDbContext _db;
    private readonly TokenService _tokens;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IAuditWriter _audit;

    public AuthController(EssDbContext db, TokenService tokens, IPasswordHasher<User> hasher, IAuditWriter audit)
    {
        _db = db;
        _tokens = tokens;
        _hasher = hasher;
        _audit = audit;
    }

    public sealed record LoginRequest(string UserName, string Password);
    public sealed record TokenResponse(string AccessToken, DateTime ExpiresAtUtc);

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
        var now = DateTime.UtcNow;

        // Same response for unknown user, wrong password and disabled account (no user enumeration).
        IActionResult Fail() => Problem(statusCode: 401, title: "Invalid user name or password.");

        if (user is null || !user.IsActive || user.PasswordHash is null)
        {
            await _audit.WriteAsync("auth.login", "User", null, new { userName = req.UserName }, success: false, ct);
            return Fail();
        }
        if (user.LockoutEndUtc > now)
        {
            await _audit.WriteAsync("auth.login.locked", "User", user.Id.ToString(), null, false, ct);
            return Problem(statusCode: 423, title: "Account temporarily locked after repeated failed sign-ins. Try again later.");
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= MaxFailedAttempts)
            {
                user.LockoutEndUtc = now.Add(LockoutDuration);
                user.AccessFailedCount = 0;
            }
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("auth.login", "User", user.Id.ToString(), null, false, ct);
            return Fail();
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = _hasher.HashPassword(user, req.Password);

        // MFA (TOTP) challenge is added in Phase 2 together with enrolment; accounts with MfaEnabled cannot sign in until then.
        if (user.MfaEnabled) return Problem(statusCode: 401, title: "Multi-factor authentication is required for this account.");

        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = now;
        await _db.SaveChangesAsync(ct);

        var issued = await _tokens.IssueAsync(user, null, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent, ct);
        SetRefreshCookie(issued.RefreshToken, issued.RefreshTokenExpiresUtc);
        await _audit.WriteAsync("auth.login", "User", user.Id.ToString(), null, true, ct);
        return Ok(new TokenResponse(issued.AccessToken, issued.AccessTokenExpiresUtc));
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
        if (user is null) return Unauthorized();

        var issued = await _tokens.IssueAsync(user, token.FamilyId, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent, ct);
        token.RevokedAtUtc = now;
        token.ReplacedByHash = TokenService.Hash(issued.RefreshToken);
        await _db.SaveChangesAsync(ct);
        SetRefreshCookie(issued.RefreshToken, issued.RefreshTokenExpiresUtc);
        return Ok(new TokenResponse(issued.AccessToken, issued.AccessTokenExpiresUtc));
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

    public sealed record MeResponse(string? UserName, IEnumerable<string> Roles, IEnumerable<string> Permissions, string? CompanyId, string? EmployeeId);

    [HttpGet("me")]
    public MeResponse Me() => new(User.FindFirst("name")?.Value,
        User.FindAll("role").Select(c => c.Value), User.FindAll(Infrastructure.HttpCurrentUser.PermissionClaim).Select(c => c.Value),
        User.FindFirst(Infrastructure.HttpCurrentUser.CompanyClaim)?.Value, User.FindFirst(Infrastructure.HttpCurrentUser.EmployeeClaim)?.Value);

    private void SetRefreshCookie(string value, DateTime expires) =>
        Response.Cookies.Append(RefreshCookie, value, new CookieOptions
        {
            HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Strict, Expires = expires, Path = "/api"
        });

    private void ClearRefreshCookie() => Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/api" });
}
