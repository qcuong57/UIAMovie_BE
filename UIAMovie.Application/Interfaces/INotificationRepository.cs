using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Interfaces;

public interface INotificationRepository : IRepository<Notification>
{
    // Lấy thông báo người dùng (có hỗ trợ loại trừ loại thông báo như admin_announcement cho chuông)
    Task<(List<(Notification Notif, bool IsRead)> Items, int TotalCount)> GetUserNotificationsPagedAsync(
        Guid userId, int page, int pageSize, string? excludeType = null, CancellationToken ct = default);

    // Đếm số thông báo chưa đọc (loại trừ admin_announcement cho chuông)
    Task<int> CountUnreadAsync(Guid userId, string? excludeType = null, CancellationToken ct = default);

    // Lấy danh sách tin tức/thông báo hệ thống công khai cho trang Tin tức (không cần đăng nhập)
    Task<(List<Notification> Items, int TotalCount)> GetPublicAnnouncementsPagedAsync(
        int page, int pageSize, string? type = null, CancellationToken ct = default);

    // Đánh dấu đã đọc
    Task<bool> MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);

    Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default);

    // User xóa / ẩn thông báo khỏi danh sách của mình
    Task<bool> DeleteAsync(Guid userId, Guid notificationId, CancellationToken ct = default);

    // Admin cập nhật nội dung thông báo hệ thống
    Task<bool> AdminUpdateAsync(
        Guid id, string title, string message, string? linkUrl, string? thumbnailUrl, string type, CancellationToken ct = default);

    // Admin xóa vĩnh viễn thông báo khỏi cơ sở dữ liệu
    Task<bool> AdminHardDeleteAsync(Guid id, CancellationToken ct = default);

    // Tự động dọn dẹp thông báo cũ
    Task<int> DeleteOlderThanAsync(DateTime threshold, CancellationToken ct = default);
}