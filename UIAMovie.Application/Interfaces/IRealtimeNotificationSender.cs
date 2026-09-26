namespace UIAMovie.Application.Interfaces;

public interface IRealtimeNotificationSender
{
    Task SendToUserAsync(Guid userId, object notification);
    Task BroadcastAsync(object notification);
}