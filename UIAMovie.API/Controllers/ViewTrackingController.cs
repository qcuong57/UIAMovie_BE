// UIAMovie.API/Controllers/ViewTrackingController.cs

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces.IServices;

namespace UIAMovie.API.Controllers;

[ApiController]
[Route("api/playback")]
public class ViewTrackingController : ControllerBase
{
    private const string GUEST_COOKIE = "guest_id";
    private const string GUEST_HEADER = "X-Guest-Id";

    /// <summary>
    /// Số guest id khác nhau tối đa mà 1 IP được dùng trong 24h. Guest id do client tự sinh nên có thể
    /// tạo hàng loạt để bơm session/view; giới hạn này chặn việc đó. Đặt đủ rộng cho NAT (ký túc xá, quán cà phê).
    /// Lưu trong bộ nhớ từng instance (nhiều instance thì limit nhân lên).
    /// </summary>
    private const int MAX_GUEST_IDS_PER_IP = 100;

    private readonly IViewTrackingService _service;
    private readonly IMemoryCache _cache;

    public ViewTrackingController(IViewTrackingService service, IMemoryCache cache)
    {
        _service = service;
        _cache = cache;
    }

    /// <summary>Heartbeat đếm view phim. Cho phép guest; có JWT thì dùng UserId.</summary>
    [HttpPost("heartbeat")]
    [AllowAnonymous]
    [EnableRateLimiting("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatDTO dto)
    {
        if (dto.MovieId == Guid.Empty) return BadRequest();

        var (userId, guestId) = ResolveIdentity();
        if (!AllowGuest(userId, guestId)) return StatusCode(StatusCodes.Status429TooManyRequests);
        try { return Ok(await _service.RecordMovieHeartbeatAsync(userId, guestId, dto)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return StatusCode(StatusCodes.Status403Forbidden); }
    }

    [HttpPost("heartbeat/tvshow")]
    [AllowAnonymous]
    [EnableRateLimiting("heartbeat")]
    public async Task<IActionResult> TvShowHeartbeat([FromBody] TvShowHeartbeatDTO dto)
    {
        if (dto.TvShowId == Guid.Empty) return BadRequest();

        var (userId, guestId) = ResolveIdentity();
        if (!AllowGuest(userId, guestId)) return StatusCode(StatusCodes.Status429TooManyRequests);
        try { return Ok(await _service.RecordTvShowHeartbeatAsync(userId, guestId, dto)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return StatusCode(StatusCodes.Status403Forbidden); }
    }

    /// <summary>
    /// User đăng nhập: luôn cho qua. Guest: mỗi IP chỉ được dùng tối đa MAX_GUEST_IDS_PER_IP guest id / 24h.
    /// </summary>
    private bool AllowGuest(Guid? userId, string? guestId)
    {
        if (userId.HasValue || guestId is null) return true;

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        var ids = _cache.GetOrCreate("guest-ids:" + ip, e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
            return new HashSet<string>();
        })!;

        lock (ids)
        {
            if (ids.Contains(guestId)) return true;
            if (ids.Count >= MAX_GUEST_IDS_PER_IP) return false;
            ids.Add(guestId);
            return true;
        }
    }

    /// <summary>
    /// User login → UserId từ claim, guestId = null.
    /// Guest → ưu tiên header X-Guest-Id (FE tự sinh, lưu localStorage — bền khi FE/API khác domain,
    /// không bị chặn third-party cookie); không có thì đọc cookie guest_id; chưa có thì tạo mới + set cookie.
    /// </summary>
    private (Guid? UserId, string? GuestId) ResolveIdentity()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(claim, out var userId))
            return (userId, null);

        if (Guid.TryParse(Request.Headers[GUEST_HEADER].FirstOrDefault(), out var headerGuest))
            return (null, headerGuest.ToString());

        if (Request.Cookies.TryGetValue(GUEST_COOKIE, out var cookieGuest) && Guid.TryParse(cookieGuest, out var cg))
            return (null, cg.ToString());

        var guestId = Guid.NewGuid().ToString();
        Response.Cookies.Append(GUEST_COOKIE, guestId, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            // Cross-site (FE vercel.app → API khác domain) bắt buộc SameSite=None + Secure
            SameSite = Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        });
        return (null, guestId);
    }
}