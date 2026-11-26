using Microsoft.AspNetCore.Mvc;

namespace Ess.Api.Auth;

/// <summary>
/// Until a user sets up required MFA or replaces an administrator-issued temporary password, every API call
/// except /api/auth and /api/account is refused with a problem the frontend turns into the right set-up screen.
/// </summary>
public sealed class AccountGateMiddleware
{
    private readonly RequestDelegate _next;
    public AccountGateMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var u = ctx.User;
        if (u.Identity?.IsAuthenticated == true && ctx.Request.Path.StartsWithSegments("/api")
            && !ctx.Request.Path.StartsWithSegments("/api/auth") && !ctx.Request.Path.StartsWithSegments("/api/account")
            && !ctx.Request.Path.StartsWithSegments("/api/v1/auth") && !ctx.Request.Path.StartsWithSegments("/api/v1/account"))
        {
            string? code = u.HasClaim(TokenService.PasswordChangeClaim, "required") ? "password_change_required"
                : u.HasClaim(TokenService.MfaEnrolClaim, "required") ? "mfa_enrolment_required" : null;
            if (code is not null)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                var pd = new ProblemDetails { Status = 403, Title = code == "password_change_required"
                    ? "Change your temporary password to continue." : "Set up multi-factor authentication to continue." };
                pd.Extensions["code"] = code;
                await ctx.Response.WriteAsJsonAsync(pd, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
                return;
            }
        }
        await _next(ctx);
    }
}
