using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Controllers;

[ApiController]
[Route("api/playback")]
public class PlaybackController : ControllerBase
{
    private readonly IPlaybackService _playbackService;
    private readonly IRepository<Season> _seasonRepository;
    private readonly IRepository<Episode> _episodeRepository;

    public PlaybackController(
        IPlaybackService playbackService,
        IRepository<Season> seasonRepository,
        IRepository<Episode> episodeRepository)
    {
        _playbackService = playbackService;
        _seasonRepository = seasonRepository;
        _episodeRepository = episodeRepository;
    }

    // ============================================================
    // MOVIE
    // ============================================================

    /// <summary>
    /// GET /api/playback/movie/{movieId}
    /// </summary>
    [HttpGet("movie/{movieId:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting("playback")]
    public async Task<IActionResult> GetMoviePlayback(Guid movieId)
    {
        var userId = GetCurrentUserId();
        var result = await _playbackService.GetMoviePlaybackAsync(movieId, userId);

        if (result == null)
        {
            return NotFound(new { message = "Movie hoặc video không tồn tại." });
        }

        if (!result.CanWatch && result.BlockReason == "LOGIN_REQUIRED")
        {
            return Unauthorized(new
            {
                message = "Vui lòng đăng nhập để xem nội dung Premium.",
                code = result.BlockReason,
                data = result
            });
        }

        if (!result.CanWatch && result.BlockReason == "PREMIUM_REQUIRED")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Nội dung này yêu cầu gói Premium.",
                code = result.BlockReason,
                data = result
            });
        }

        return Ok(result);
    }

    // ============================================================
    // TV SHOW EPISODE (By GUID)
    // ============================================================

    /// <summary>
    /// GET /api/playback/tv/{tvShowId}/episode/{episodeId}
    /// </summary>
    [HttpGet("tv/{tvShowId:guid}/episode/{episodeId:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting("playback")]
    public async Task<IActionResult> GetEpisodePlayback(Guid tvShowId, Guid episodeId)
    {
        var userId = GetCurrentUserId();
        var result = await _playbackService.GetEpisodePlaybackAsync(tvShowId, episodeId, userId);

        if (result == null)
        {
            return NotFound(new { message = "TV Show, Episode hoặc video không tồn tại." });
        }

        if (!result.CanWatch && result.BlockReason == "LOGIN_REQUIRED")
        {
            return Unauthorized(new
            {
                message = "Vui lòng đăng nhập để xem nội dung Premium.",
                code = result.BlockReason,
                data = result
            });
        }

        if (!result.CanWatch && result.BlockReason == "PREMIUM_REQUIRED")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Nội dung này yêu cầu gói Premium.",
                code = result.BlockReason,
                data = result
            });
        }

        return Ok(result);
    }

    // ============================================================
    // TV SHOW EPISODE (By Season & Episode Number)
    // ============================================================

    /// <summary>
    /// GET /api/playback/tv/{tvShowId}/season/{seasonNumber}/episode/{episodeNumber}
    /// Hỗ trợ frontend gọi theo số mùa và số tập trực tiếp
    /// </summary>
    [HttpGet("tv/{tvShowId:guid}/season/{seasonNumber:int}/episode/{episodeNumber:int}")]
    [AllowAnonymous]
    [EnableRateLimiting("playback")]
    public async Task<IActionResult> GetEpisodePlaybackByNumber(Guid tvShowId, int seasonNumber, int episodeNumber)
    {
        var season = await _seasonRepository.FindOneAsync(s => s.TvShowId == tvShowId && s.SeasonNumber == seasonNumber);
        if (season == null)
        {
            return NotFound(new { message = "Season không tồn tại." });
        }

        var episode = await _episodeRepository.FindOneAsync(e => e.SeasonId == season.Id && e.EpisodeNumber == episodeNumber);
        if (episode == null)
        {
            return NotFound(new { message = "Episode không tồn tại." });
        }

        return await GetEpisodePlayback(tvShowId, episode.Id);
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(claim, out var userId))
            return userId;

        return null;
    }
}