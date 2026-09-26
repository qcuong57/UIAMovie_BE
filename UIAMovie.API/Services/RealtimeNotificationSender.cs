using Microsoft.AspNetCore.SignalR;
using UIAMovie.API.Hubs;
using UIAMovie.Application.Interfaces;

namespace UIAMovie.API.Services;

public class RealtimeNotificationSender : IRealtimeNotificationSender
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public RealtimeNotificationSender(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendToUserAsync(Guid userId, object notification)
    {
        await _hubContext.Clients
            .Group($"user_{userId}")
            .SendAsync("ReceiveNotification", notification);
    }

    public async Task BroadcastAsync(object notification)
    {
        await _hubContext.Clients.All.SendAsync("ReceiveNotification", notification);
    }
}