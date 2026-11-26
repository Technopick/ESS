using Ess.Application.Security;
using Hangfire.Dashboard;

namespace Ess.Api.Infrastructure;

/// <summary>Only integration/system administrators can open the background job dashboard.</summary>
public sealed class HangfireDashboardAuthorization : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var user = context.GetHttpContext().User;
        return user.Identity?.IsAuthenticated == true && user.HasClaim(HttpCurrentUser.PermissionClaim, Permissions.IntegrationOperate);
    }
}
