namespace UIAMovie.Domain.Entities;

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// null biểu thị thông báo toàn hệ thống (Broadcast/System Notification)
    /// </summary>
    public Guid? UserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }

    /// <summary>
    /// Ảnh poster phim / TV Show hoặc thumbnail do Admin đính kèm
    /// </summary>
    public string? ThumbnailUrl { get; set; }

    public string Type { get; set; } = "general";

    /// <summary>
    /// Áp dụng cho thông báo cá nhân (UserId != null)
    /// Broadcast notification (UserId == null) dùng bảng NotificationRead để track trạng thái
    /// </summary>
    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class NotificationRead
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ReadAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User đã "xóa" broadcast notification này khỏi danh sách của mình.
    /// Không xóa thật vì notification là chung cho tất cả user.
    /// </summary>
    public bool IsDismissed { get; set; } = false;
}