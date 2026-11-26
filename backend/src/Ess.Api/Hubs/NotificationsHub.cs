using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ess.Api.Hubs;

/// <summary>Real-time in-app notifications. Each user is addressed through their user id (Clients.User).</summary>
[Authorize]
public sealed class NotificationsHub : Hub
{
}
