// UIAMovie.API/Controllers/ViewTrackingController.cs

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Application.Services;
using UIAMovie.Domain.Entities;

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
    private readonly IPlaySessionService _playSessions;

    public ViewTrackingController(IViewTrackingService service, IMemoryCache cache, IPlaySessionService playSessions)
    {
        _service = service;
        _cache = cache;
        _playSessions = playSessions;
    }

    /// <summary>
    /// Heartbeat chỉ được đếm khi gắn với play session thật: đúng người xem, đúng nội dung, đã được cấp link,
    /// và mọi mid-roll đã trôi qua đều đã xem. null = cho qua.
    /// 410 = session hết hạn/không có (client dừng gửi), 428 = còn quảng cáo chưa xem (bỏ beat này).
    /// </summary>
    private async Task<IActionResult?> CheckPlaySessionAsync(
        Guid? userId, string? guestId, AdContentType type, Guid contentId, string? sessionId, int? positionSeconds)
    {
        var r = await _playSessions.ValidateHeartbeatAsync(
            sessionId, PlaySessionService.OwnerKeyOf(userId, guestId), type, contentId, positionSeconds);
        return r switch
        {
            PlaySessionResult.Ok => null,
            PlaySessionResult.NoSession => StatusCode(StatusCodes.Status410Gone),
            PlaySessionResult.AdsPending => StatusCode(428),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
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
        var denied = await CheckPlaySessionAsync(userId, guestId, AdContentType.Movie, dto.MovieId, dto.PlaySessionId, dto.PositionSeconds);
        if (denied != null) return denied;
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
        // Session của tập phim gắn với EpisodeId nên bắt buộc phải có
        if (dto.EpisodeId is null || dto.EpisodeId == Guid.Empty) return BadRequest();

        var (userId, guestId) = ResolveIdentity();
        if (!AllowGuest(userId, guestId)) return StatusCode(StatusCodes.Status429TooManyRequests);
        var denied = await CheckPlaySessionAsync(userId, guestId, AdContentType.Episode, dto.EpisodeId!.Value, dto.PlaySessionId, dto.PositionSeconds);
        if (denied != null) return denied;
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

        var ip = IpKey(HttpContext.Connection.RemoteIpAddress);
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
    /// Khoá theo IP. IPv6: gom theo /64 (một nhà/một thuê bao có cả dải /64) — nếu dùng nguyên địa chỉ thì kẻ gian
    /// xoay địa chỉ trong dải của mình để né giới hạn và làm phình bộ nhớ cache.
    /// </summary>
    private static string IpKey(System.Net.IPAddress? ip)
    {
        if (ip is null) return "anonymous";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return Convert.ToHexString(ip.GetAddressBytes(), 0, 8);
        return ip.ToString();
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