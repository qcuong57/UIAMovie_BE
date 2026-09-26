using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Services;

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IRealtimeNotificationSender _realtimeSender;

    public NotificationService(
        INotificationRepository notificationRepository,
        IRealtimeNotificationSender realtimeSender)
    {
        _notificationRepository = notificationRepository;
        _realtimeSender = realtimeSender;
    }

    public async Task SendNotificationAsync(
        Guid userId,
        string title,
        string message,
        string? linkUrl = null,
        string? thumbnailUrl = null,
        string type = "general",
        CancellationToken ct = default)
    {
        var notif = new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            LinkUrl = linkUrl,
            ThumbnailUrl = thumbnailUrl,
            Type = type,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        await _notificationRepository.AddAsync(notif);
        await _notificationRepository.SaveChangesAsync();

        var dto = new NotificationDTO
        {
            Id = notif.Id,
            Title = title,
            Message = message,
            LinkUrl = linkUrl,
            ThumbnailUrl = thumbnailUrl,
            Type = type,
            IsRead = false,
            CreatedAt = notif.CreatedAt
        };
        await _realtimeSender.SendToUserAsync(userId, dto);
    }

    public async Task BroadcastNotificationAsync(
        string title,
        string message,
        string? linkUrl = null,
        string? thumbnailUrl = null,
        string type = "movie_release",
        CancellationToken ct = default)
    {
        var notif = new Notification
        {
            UserId = null,
            Title = title,
            Message = message,
            LinkUrl = linkUrl,
            ThumbnailUrl = thumbnailUrl,
            Type = type,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        await _notificationRepository.AddAsync(notif);
        await _notificationRepository.SaveChangesAsync();

        // Gửi realtime qua SignalR Hub
        await _realtimeSender.BroadcastAsync(new NotificationDTO
        {
            Id = notif.Id,
            Title = title,
            Message = message,
            LinkUrl = linkUrl,
            ThumbnailUrl = thumbnailUrl,
            Type = type,
            IsRead = false,
            CreatedAt = notif.CreatedAt
        });
    }

    public async Task<PaginatedDTO<NotificationDTO>> GetUserNotificationsAsync(
        Guid userId, int page = 1, int pageSize = 20, string? excludeType = "admin_announcement", CancellationToken ct = default)
    {
        var (items, totalCount) =
            await _notificationRepository.GetUserNotificationsPagedAsync(userId, page, pageSize, excludeType, ct);

        var dtos = items.Select(x => new NotificationDTO
        {
            Id = x.Notif.Id,
            Title = x.Notif.Title,
            Message = x.Notif.Message,
            LinkUrl = x.Notif.LinkUrl,
            ThumbnailUrl = x.Notif.ThumbnailUrl,
            Type = x.Notif.Type,
            IsRead = x.IsRead,
            CreatedAt = x.Notif.CreatedAt
        }).ToList();

        return new PaginatedDTO<NotificationDTO>
        {
            Items = dtos,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };
    }

    public Task<int> GetUnreadCountAsync(Guid userId, string? excludeType = "admin_announcement", CancellationToken ct = default)
        => _notificationRepository.CountUnreadAsync(userId, excludeType, ct);

    public async Task<PaginatedDTO<NotificationDTO>> GetPublicAnnouncementsAsync(
        int page = 1, int pageSize = 10, string? type = null, CancellationToken ct = default)
    {
        var (items, totalCount) =
            await _notificationRepository.GetPublicAnnouncementsPagedAsync(page, pageSize, type, ct);

        var dtos = items.Select(x => new NotificationDTO
        {
            Id = x.Id,
            Title = x.Title,
            Message = x.Message,
            LinkUrl = x.LinkUrl,
            ThumbnailUrl = x.ThumbnailUrl,
            Type = x.Type,
            IsRead = true, // Tin tức công khai không đánh dấu đỏ cá nhân
            CreatedAt = x.CreatedAt
        }).ToList();

        return new PaginatedDTO<NotificationDTO>
        {
            Items = dtos,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };
    }

    public Task<bool> MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
        => _notificationRepository.MarkAsReadAsync(userId, notificationId, ct);

    public Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default)
        => _notificationRepository.MarkAllAsReadAsync(userId, ct);

    public Task<bool> DeleteNotificationAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
        => _notificationRepository.DeleteAsync(userId, notificationId, ct);

    public Task<bool> AdminUpdateNotificationAsync(Guid id, CreateAdminBroadcastDTO dto, CancellationToken ct = default)
        => _notificationRepository.AdminUpdateAsync(id, dto.Title, dto.Message, dto.LinkUrl, dto.ThumbnailUrl, dto.Type, ct);

    public Task<bool> AdminDeleteNotificationAsync(Guid id, CancellationToken ct = default)
        => _notificationRepository.AdminHardDeleteAsync(id, ct);

    public Task<int> DeleteOldNotificationsAsync(int olderThanDays = 90, CancellationToken ct = default)
        => _notificationRepository.DeleteOlderThanAsync(DateTime.UtcNow.AddDays(-olderThanDays), ct);
}