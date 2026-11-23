// UIAMovie.API/Controllers/PlaySessionController.cs

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;
using UIAMovie.Domain.Entities;

namespace UIAMovie.API.Controllers;

[ApiController]
[Route("api/playback/session")]
public class PlaySessionController : ControllerBase
{
    private readonly IPlaySessionService _sessions;

    public PlaySessionController(IPlaySessionService sessions) => _sessions = sessions;

    /// <summary>Bắt đầu 1 lượt xem: chốt lịch quảng cáo + cấp playSessionId. Gọi TRƯỚC khi gọi API playback.</summary>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("playback")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Start([FromBody] StartPlaySessionDTO dto)
    {
        var (userId, guestId) = PlayerIdentity.Resolve(this);
        try { return Ok(await _sessions.StartAsync(userId, guestId, dto)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("{sessionId}/ads/{adRef}/start")]
    [AllowAnonymous]
    [EnableRateLimiting("heartbeat")]
    public async Task<IActionResult> AdStarted(string sessionId, string adRef)
    {
        var (u, g) = PlayerIdentity.Resolve(this);
        return Map(await _sessions.AdStartedAsync(sessionId, PlaySessionService.OwnerKeyOf(u, g), adRef));
    }

    [HttpPost("{sessionId}/ads/{adRef}/complete")]
    [AllowAnonymous]
    [EnableRateLimiting("heartbeat")]
    public async Task<IActionResult> AdCompleted(string sessionId, string adRef)
    {
        var (u, g) = PlayerIdentity.Resolve(this);
        return Map(await _sessions.AdCompletedAsync(sessionId, PlaySessionService.OwnerKeyOf(u, g), adRef));
    }

    private IActionResult Map(PlaySessionResult r) => r switch
    {
        PlaySessionResult.Ok => Ok(new AdProgressResultDTO { Ok = true }),
        PlaySessionResult.NoSession => StatusCode(StatusCodes.Status410Gone, new AdProgressResultDTO { Code = "NO_SESSION" }),
        PlaySessionResult.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new AdProgressResultDTO { Code = "FORBIDDEN" }),
        PlaySessionResult.UnknownAd => BadRequest(new AdProgressResultDTO { Code = "UNKNOWN_AD" }),
        PlaySessionResult.OutOfOrder => Conflict(new AdProgressResultDTO { Code = "OUT_OF_ORDER" }),
        PlaySessionResult.TooEarly => Conflict(new AdProgressResultDTO { Code = "TOO_EARLY" }),
        _ => BadRequest(new AdProgressResultDTO { Code = "ERROR" })
    };
}

/// <summary>
/// Dùng chung cho mọi controller cần danh tính người xem (trích từ ViewTrackingController.ResolveIdentity).
/// Nên đổi ViewTrackingController gọi hàm này để hai nơi không lệch nhau.
/// </summary>
public static class PlayerIdentity
{
    private const string GUEST_COOKIE = "guest_id";
    private const string GUEST_HEADER = "X-Guest-Id";

    public static (Guid? UserId, string? GuestId) Resolve(ControllerBase c)
    {
        var claim = c.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? c.User.FindFirstValue("sub");
        if (Guid.TryParse(claim, out var userId)) return (userId, null);

        if (Guid.TryParse(c.Request.Headers[GUEST_HEADER].FirstOrDefault(), out var h)) return (null, h.ToString());

        if (c.Request.Cookies.TryGetValue(GUEST_COOKIE, out var ck) && Guid.TryParse(ck, out var cg))
            return (null, cg.ToString());

        var guestId = Guid.NewGuid().ToString();
        c.Response.Cookies.Append(GUEST_COOKIE, guestId, new CookieOptions
        {
            HttpOnly = true,
            Secure = c.Request.IsHttps,
            SameSite = c.Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        });
        return (null, guestId);
    }
}