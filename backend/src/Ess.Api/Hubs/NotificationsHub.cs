using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ess.Api.Hubs;

/// <summary>Real-time in-app notifications. Each user is addressed through their user id (Clients.User).</summary>
[Authorize]
public sealed class NotificationsHub : Hub
{
}

/// <summary>Tokens keep the JWT "sub" claim (inbound claim mapping is off), so address SignalR users by it.</summary>
public sealed class SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst("sub")?.Value ?? connection.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
}
