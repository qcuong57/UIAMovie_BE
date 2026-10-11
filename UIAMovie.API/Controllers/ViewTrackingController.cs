// UIAMovie.API/Controllers/ViewTrackingController.cs

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces.IServices;

namespace UIAMovie.API.Controllers;

[ApiController]
[Route("api/playback")]
public class ViewTrackingController : ControllerBase
{
    private const string GUEST_COOKIE = "guest_id";
    private const string GUEST_HEADER = "X-Guest-Id";
    private readonly IViewTrackingService _service;

    public ViewTrackingController(IViewTrackingService service)
    {
        _service = service;
    }

    /// <summary>Heartbeat đếm view phim. Cho phép guest; có JWT thì dùng UserId.</summary>
    [HttpPost("heartbeat")]
    [AllowAnonymous]
    [EnableRateLimiting("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatDTO dto)
    {
        if (dto.MovieId == Guid.Empty) return BadRequest();

        var (userId, guestId) = ResolveIdentity();
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
        try { return Ok(await _service.RecordTvShowHeartbeatAsync(userId, guestId, dto)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return StatusCode(StatusCodes.Status403Forbidden); }
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