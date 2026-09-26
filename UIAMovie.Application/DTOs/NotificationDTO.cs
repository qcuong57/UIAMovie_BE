namespace UIAMovie.Application.DTOs;

public class NotificationDTO
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string Type { get; set; } = "general";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateAdminBroadcastDTO
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? LinkUrl { get; set; }
    public string Type { get; set; } = "admin_announcement";
}