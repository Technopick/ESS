using Ess.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace Ess.Api.Infrastructure;

/// <summary>[HasPermission("leave.apply")] maps to a policy that requires the permission claim in the access token.</summary>
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission) : base("perm:" + permission) { }
}

public static class PermissionPolicies
{
    public static void AddPermissionPolicies(this AuthorizationOptions options)
    {
        foreach (var p in Permissions.All())
            options.AddPolicy("perm:" + p, b => b.RequireAuthenticatedUser().RequireClaim(HttpCurrentUser.PermissionClaim, p));
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(); // deny by default
    }
}
