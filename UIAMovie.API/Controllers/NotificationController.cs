using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;

namespace UIAMovie.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    private Guid GetCurrentUserId()
    {
        var claimValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value
                         ?? User.FindFirst("id")?.Value;

        if (Guid.TryParse(claimValue, out var userId))
        {
            return userId;
        }

        throw new UnauthorizedAccessException("Không xác định được danh tính người dùng.");
    }

    // ==========================================
    // 1. PUBLIC: Dành cho trang Tin tức trên Navbar (Ai cũng xem được)
    // ==========================================
    [HttpGet("public-announcements")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicAnnouncements(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? type = null,
        CancellationToken ct = default)
    {
        var result = await _notificationService.GetPublicAnnouncementsAsync(page, pageSize, type, ct);
        return Ok(result);
    }

    // ==========================================
    // 2. USER: Dành cho Chuông thông báo (Notification Bell)
    // ==========================================
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetUserNotifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? excludeType = "admin_announcement",
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        // Mặc định loại bỏ admin_announcement để chuông chỉ nhận phim mới hoặc tin nhắn riêng
        var result = await _notificationService.GetUserNotificationsAsync(userId, page, pageSize, excludeType, ct);
        return Ok(result);
    }

    [HttpGet("unread-count")]
    [Authorize]
    public async Task<IActionResult> GetUnreadCount(
        [FromQuery] string? excludeType = "admin_announcement",
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var count = await _notificationService.GetUnreadCountAsync(userId, excludeType, ct);
        return Ok(count);
    }

    [HttpPut("{id:guid}/read")]
    [Authorize]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var success = await _notificationService.MarkAsReadAsync(userId, id, ct);
        return Ok(new { success });
    }

    [HttpPut("read-all")]
    [Authorize]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        await _notificationService.MarkAllAsReadAsync(userId, ct);
        return Ok(new { message = "Đã đánh dấu đã đọc tất cả thông báo" });
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteNotification(Guid id, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var success = await _notificationService.DeleteNotificationAsync(userId, id, ct);
        return Ok(new { success });
    }

    // ==========================================
    // 3. ADMIN: Quản lý, Phát thông báo, Sửa & Xóa vĩnh viễn
    // ==========================================
    [HttpPost("broadcast")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> BroadcastNotification(
        [FromBody] CreateAdminBroadcastDTO dto,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        await _notificationService.BroadcastNotificationAsync(
            title: dto.Title,
            message: dto.Message,
            linkUrl: dto.LinkUrl,
            thumbnailUrl: dto.ThumbnailUrl,
            type: string.IsNullOrWhiteSpace(dto.Type) ? "admin_announcement" : dto.Type,
            ct: ct
        );

        return Ok(new { message = "Đã phát thông báo thành công!" });
    }

    [HttpPut("admin/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AdminUpdateNotification(
        Guid id,
        [FromBody] CreateAdminBroadcastDTO dto,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var success = await _notificationService.AdminUpdateNotificationAsync(id, dto, ct);
        if (!success) return NotFound(new { message = "Không tìm thấy thông báo để cập nhật" });

        return Ok(new { message = "Đã cập nhật thông báo thành công!" });
    }

    [HttpDelete("admin/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AdminDeleteNotification(
        Guid id,
        CancellationToken ct = default)
    {
        var success = await _notificationService.AdminDeleteNotificationAsync(id, ct);
        if (!success) return NotFound(new { message = "Không tìm thấy thông báo để xóa" });

        return Ok(new { message = "Đã xóa vĩnh viễn thông báo khỏi hệ thống!" });
    }
}