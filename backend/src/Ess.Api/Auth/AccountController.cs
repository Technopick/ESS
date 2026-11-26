using Asp.Versioning;
using Ess.Application.Abstractions;
using Ess.Application.Identity;
using Ess.Domain.Identity;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Auth;

/// <summary>The signed-in user's own security settings: password change and authenticator-app MFA.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/account")]
[Route("api/account")]
public sealed class AccountController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly ICurrentUser _me;
    private readonly ISecretProtector _secrets;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IAuditWriter _audit;
    private readonly IConfiguration _config;

    public AccountController(EssDbContext db, ICurrentUser me, ISecretProtector secrets, IPasswordHasher<User> hasher, IAuditWriter audit, IConfiguration config)
    {
        _db = db;
        _me = me;
        _secrets = secrets;
        _hasher = hasher;
        _audit = audit;
        _config = config;
    }

    public sealed record SecurityStatus(string UserName, string Email, bool MfaEnabled, int RecoveryCodesRemaining, bool MfaRequiredByPolicy,
        bool MustChangePassword, DateTime? PasswordChangedAtUtc, DateTime? LastLoginAtUtc);

    [HttpGet]
    public async Task<SecurityStatus> Get(CancellationToken ct)
    {
        var u = await CurrentAsync(ct);
        return new SecurityStatus(u.UserName, u.Email, u.MfaEnabled, MfaRecoveryCodes.Remaining(u), User.HasClaim(TokenService.MfaEnrolClaim, "required"),
            u.MustChangePassword, u.PasswordChangedAtUtc, u.LastLoginAtUtc);
    }

    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req, CancellationToken ct)
    {
        var u = await CurrentAsync(ct);
        if (u.PasswordHash is null || _hasher.VerifyHashedPassword(u, u.PasswordHash, req.CurrentPassword) == PasswordVerificationResult.Failed)
            return Field("currentPassword", "Your current password is not correct.");
        var errors = PasswordPolicy.Validate(req.NewPassword, u.UserName);
        if (errors.Count > 0) return Field("newPassword", string.Join(" ", errors));
        if (_hasher.VerifyHashedPassword(u, u.PasswordHash, req.NewPassword) != PasswordVerificationResult.Failed)
            return Field("newPassword", "Choose a password different from the current one.");

        u.PasswordHash = _hasher.HashPassword(u, req.NewPassword);
        u.PasswordChangedAtUtc = DateTime.UtcNow;
        u.MustChangePassword = false;
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync(ct);
        // Sign out other devices; this device gets a new refresh token on its next refresh call only if it still holds a valid one,
        // so the client signs in again after a password change.
        await _db.RefreshTokens.Where(t => t.UserId == u.Id && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow), ct);
        await _audit.WriteAsync("account.password.changed", "User", u.Id.ToString(), null, true, ct);
        return NoContent();
    }

    public sealed record MfaSetupResponse(string Secret, string OtpAuthUri);

    /// <summary>Starts enrolment: creates a new secret (not active until confirmed with a valid code).</summary>
    [HttpPost("mfa/setup")]
    public async Task<IActionResult> SetupMfa(CancellationToken ct)
    {
        var u = await CurrentAsync(ct);
        if (u.MfaEnabled) return Conflict(new { message = "Multi-factor authentication is already on. Turn it off first to re-enrol." });
        var secret = Totp.NewSecret();
        u.MfaSecretEncrypted = _secrets.Protect(secret);
        u.MfaLastTimeStep = null;
        await _db.SaveChangesAsync(ct);
        var issuer = _config["App:Name"] ?? "ESS Portal";
        return Ok(new MfaSetupResponse(secret, Totp.OtpAuthUri(issuer, u.Email, secret)));
    }

    public sealed record CodeRequest(string Code);
    public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

    [HttpPost("mfa/confirm")]
    public async Task<IActionResult> ConfirmMfa(CodeRequest req, CancellationToken ct)
    {
        var u = await CurrentAsync(ct);
        if (u.MfaEnabled) return Conflict(new { message = "Multi-factor authentication is already on." });
        if (u.MfaSecretEncrypted is null) return Field("code", "Start set-up first.");
        var step = Totp.Verify(_secrets.Unprotect(u.MfaSecretEncrypted), req.Code ?? "", DateTime.UtcNow, null);
        if (step is null) return Field("code", "That code is not valid. Check the time on your phone and try again.");

        u.MfaEnabled = true;
        u.MfaLastTimeStep = step;
        var codes = MfaRecoveryCodes.Generate(u);
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("account.mfa.enabled", "User", u.Id.ToString(), null, true, ct);
        return Ok(new RecoveryCodesResponse(codes));
    }

    [HttpPost("mfa/recovery-codes")]
    public async Task<IActionResult> RegenerateRecoveryCodes(CodeRequest req, CancellationToken ct)
    {
        var u = await CurrentAsync(ct);
        if (!u.MfaEnabled || !VerifyCode(u, req.Code)) return Field("code", "That code is not valid.");
        var codes = MfaRecoveryCodes.Generate(u);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("account.mfa.recovery_codes_regenerated", "User", u.Id.ToString(), null, true, ct);
        return Ok(new RecoveryCodesResponse(codes));
    }

    public sealed record DisableMfaRequest(string Password, string Code);

    [HttpPost("mfa/disable")]
    public async Task<IActionResult> DisableMfa(DisableMfaRequest req, CancellationToken ct)
    {
        var u = await CurrentAsync(ct);
        if (!u.MfaEnabled) return NoContent();
        if (User.HasClaim(TokenService.MfaRequiredRoleClaim, "true"))
            return Problem(statusCode: 403, title: "Your role requires multi-factor authentication, so it cannot be turned off.");
        if (u.PasswordHash is null || _hasher.VerifyHashedPassword(u, u.PasswordHash, req.Password) == PasswordVerificationResult.Failed)
            return Field("password", "Your password is not correct.");
        if (!VerifyCode(u, req.Code)) return Field("code", "That code is not valid.");

        u.MfaEnabled = false;
        u.MfaSecretEncrypted = null;
        u.MfaRecoveryCodeHashes = null;
        u.MfaLastTimeStep = null;
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("account.mfa.disabled", "User", u.Id.ToString(), null, true, ct);
        return NoContent();
    }

    private bool VerifyCode(User u, string? code)
    {
        if (u.MfaSecretEncrypted is null) return false;
        var step = Totp.Verify(_secrets.Unprotect(u.MfaSecretEncrypted), code ?? "", DateTime.UtcNow, u.MfaLastTimeStep);
        if (step is null) return MfaRecoveryCodes.TryConsume(u, code ?? "");
        u.MfaLastTimeStep = step;
        return true;
    }

    private async Task<User> CurrentAsync(CancellationToken ct) =>
        await _db.Users.FirstOrDefaultAsync(x => x.Id == _me.UserId, ct) ?? throw new UnauthorizedAccessException();

    private IActionResult Field(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = new[] { message } }));
}
