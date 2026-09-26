using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Interfaces;

public interface INotificationService
{
    // Gửi thông báo riêng cho 1 user
    Task SendNotificationAsync(
        Guid userId,
        string title,
        string message,
        string? linkUrl = null,
        string? thumbnailUrl = null,
        string type = "general",
        CancellationToken ct = default);

    // Phát thông báo toàn hệ thống (phim mới hoặc tin tức bảo trì)
    Task BroadcastNotificationAsync(
        string title,
        string message,
        string? linkUrl = null,
        string? thumbnailUrl = null,
        string type = "movie_release",
        CancellationToken ct = default);

    // Lấy thông báo cho chuông người dùng (mặc định loại bỏ admin_announcement)
    Task<PaginatedDTO<NotificationDTO>> GetUserNotificationsAsync(
        Guid userId,
        int page = 1,
        int pageSize = 20,
        string? excludeType = "admin_announcement",
        CancellationToken ct = default);

    // Đếm số chưa đọc (loại bỏ admin_announcement cho chuông)
    Task<int> GetUnreadCountAsync(
        Guid userId,
        string? excludeType = "admin_announcement",
        CancellationToken ct = default);

    // Lấy danh sách tin tức/thông báo hệ thống công khai (cho trang Tin tức trên Navbar)
    Task<PaginatedDTO<NotificationDTO>> GetPublicAnnouncementsAsync(
        int page = 1,
        int pageSize = 10,
        string? type = null,
        CancellationToken ct = default);

    Task<bool> MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);

    Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default);

    Task<bool> DeleteNotificationAsync(Guid userId, Guid notificationId, CancellationToken ct = default);

    // Admin chỉnh sửa thông báo
    Task<bool> AdminUpdateNotificationAsync(Guid id, CreateAdminBroadcastDTO dto, CancellationToken ct = default);

    // Admin xóa vĩnh viễn thông báo
    Task<bool> AdminDeleteNotificationAsync(Guid id, CancellationToken ct = default);

    Task<int> DeleteOldNotificationsAsync(int olderThanDays = 90, CancellationToken ct = default);
}