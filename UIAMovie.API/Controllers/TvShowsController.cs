// UIAMovie.API/Controllers/TvShowsController.cs

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Services;
using UIAMovie.Domain.Constants;
using UIAMovie.Infrastructure.Configuration;

namespace UIAMovie.Controllers;

[ApiController]
[Route("api/tvshows")]
public class TvShowsController : ControllerBase
{
    private readonly ITvShowService    _tvShowService;
    private readonly ITmdbService      _tmdbService;
    private readonly IGenreService     _genreService;
    private readonly ICloudinaryService _cloudinaryService;

    public TvShowsController(
        ITvShowService      tvShowService,
        ITmdbService        tmdbService,
        IGenreService       genreService,
        ICloudinaryService  cloudinaryService)
    {
        _tvShowService     = tvShowService;
        _tmdbService       = tmdbService;
        _genreService      = genreService;
        _cloudinaryService = cloudinaryService;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PUBLIC — Danh sách & tìm kiếm
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> GetTvShows([FromQuery] FilterTvShowsDTO filter)
    {
        var result = await _tvShowService.GetTvShowsAsync(filter);
        return Ok(new ApiResponseDTO<object> { Data = result, Message = "Thành công" });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var show = await _tvShowService.GetTvShowByIdAsync(id);
        return show == null
            ? NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy TV show", StatusCode = 404 })
            : Ok(new ApiResponseDTO<object> { Data = show, Message = "Thành công" });
    }

    [HttpGet("search/actor")]
    public async Task<IActionResult> SearchByActor([FromQuery] string actorName)
    {
        if (string.IsNullOrWhiteSpace(actorName))
            return BadRequest(new ApiErrorResponseDTO
            {
                Message    = "actorName không được để trống",
                StatusCode = 400
            });

        var shows = await _tvShowService.SearchTvShowsByActorAsync(actorName);
        return Ok(new ApiResponseDTO<object> { Data = shows, Message = "Thành công" });
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new ApiErrorResponseDTO { Message = "Query không được để trống", StatusCode = 400 });

        var shows = await _tvShowService.SearchTvShowsAsync(query);
        return Ok(new ApiResponseDTO<object> { Data = shows, Message = "Thành công" });
    }

    [HttpGet("genre/{genreId:guid}")]
    public async Task<IActionResult> GetByGenre(Guid genreId)
    {
        var shows = await _tvShowService.GetTvShowsByGenreAsync(genreId);
        return Ok(new ApiResponseDTO<object> { Data = shows, Message = "Thành công" });
    }

    [HttpGet("countries")]
    public async Task<IActionResult> GetAvailableCountries()
    {
        var countries = await _tvShowService.GetAvailableCountriesAsync();
        return Ok(new ApiResponseDTO<List<string>> { Data = countries.ToList(), Message = "Thành công" });
    }

    // ═══════════════════════════════════════════════════════════════════
    // SEASON / EPISODE — Load on-demand (lazy)
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet("{id:guid}/seasons/{seasonNumber:int}")]
    public async Task<IActionResult> GetSeason(Guid id, int seasonNumber)
    {
        var season = await _tvShowService.GetSeasonAsync(id, seasonNumber);
        return season == null
            ? NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy season", StatusCode = 404 })
            : Ok(new ApiResponseDTO<object> { Data = season, Message = "Thành công" });
    }

    [HttpGet("{id:guid}/seasons/{seasonNumber:int}/episodes/{episodeNumber:int}")]
    public async Task<IActionResult> GetEpisode(Guid id, int seasonNumber, int episodeNumber)
    {
        var episode = await _tvShowService.GetEpisodeAsync(id, seasonNumber, episodeNumber);
        return episode == null
            ? NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy episode", StatusCode = 404 })
            : Ok(new ApiResponseDTO<object> { Data = episode, Message = "Thành công" });
    }

    // ═══════════════════════════════════════════════════════════════════
    // FAVORITES — Yêu thích TV Show
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet("favorites")]
    [Authorize]
    public async Task<IActionResult> GetFavorites()
    {
        var favorites = await _tvShowService.GetFavoritesAsync(GetUserId());
        return Ok(new ApiResponseDTO<object> { Data = favorites, Message = "Thành công" });
    }

    [HttpPost("favorites")]
    [Authorize]
    public async Task<IActionResult> AddFavorite([FromBody] AddTvShowFavoriteDTO dto)
    {
        var success = await _tvShowService.AddFavoriteAsync(GetUserId(), dto.TvShowId);
        return success
            ? Ok(new ApiResponseDTO<object> { Message = "Đã thêm vào yêu thích" })
            : BadRequest(new ApiErrorResponseDTO { Message = "TV show đã có trong danh sách yêu thích", StatusCode = 400 });
    }

    [HttpDelete("favorites/{tvShowId:guid}")]
    [Authorize]
    public async Task<IActionResult> RemoveFavorite(Guid tvShowId)
    {
        var success = await _tvShowService.RemoveFavoriteAsync(GetUserId(), tvShowId);
        return success
            ? Ok(new ApiResponseDTO<object> { Message = "Đã xóa khỏi yêu thích" })
            : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy trong danh sách yêu thích", StatusCode = 404 });
    }

    // ═══════════════════════════════════════════════════════════════════
    // TMDB — Tìm kiếm & import
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet("tmdb/search")]
    [Authorize]
    public async Task<IActionResult> SearchTmdb([FromQuery] string query, [FromQuery] int page = 1)
    {
        var result = await _tmdbService.SearchTvShowsAsync(query, page);
        return Ok(new ApiResponseDTO<object> { Data = result, Message = "Thành công" });
    }

    [HttpGet("tmdb/trending")]
    public async Task<IActionResult> GetTmdbTrending([FromQuery] string timeWindow = "week")
    {
        var result = await _tmdbService.GetTrendingTvShowsAsync(timeWindow);
        return Ok(new ApiResponseDTO<object> { Data = result, Message = "Thành công" });
    }

    [HttpGet("tmdb/{tmdbId:int}")]
    public async Task<IActionResult> GetTmdbTvShow(int tmdbId)
    {
        var show = await _tmdbService.GetTvShowAsync(tmdbId);
        return show == null
            ? NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy TV show trên TMDB", StatusCode = 404 })
            : Ok(new ApiResponseDTO<object> { Data = show, Message = "Thành công" });
    }

    [HttpPost("tmdb/{tmdbId:int}/import")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> ImportFromTmdb(int tmdbId)
    {
        var existing = await _tvShowService.GetTvShowByTmdbIdAsync(tmdbId);
        if (existing != null)
            return Conflict(new ApiErrorResponseDTO
            {
                Message    = $"TV show này đã được import rồi (ID: {existing.Id})",
                StatusCode = 409
            });

        var full = await _tmdbService.GetFullTvShowAsync(tmdbId);
        if (full == null)
            return NotFound(new ApiErrorResponseDTO
            {
                Message    = "Không tìm thấy TV show trên TMDB",
                StatusCode = 404
            });

        var genreIds = await _genreService.ResolveGenreIdsFromTmdbAsync(
            full.Detail.Genres.Select(g => g.Id));

        var cast = full.Cast
            .Take(10)
            .Select(c =>
            {
                full.PersonImages.TryGetValue(c.Id, out var imgs);
                full.PersonDetails.TryGetValue(c.Id, out var detail);
                return new ImportCastDTO
                {
                    TmdbPersonId  = c.Id,
                    Name          = c.Name,
                    Character     = c.Character,
                    Order         = c.Order,
                    ProfileUrl    = c.ProfileUrl,
                    Biography     = detail?.Biography,
                    Birthday      = detail?.Birthday,
                    PlaceOfBirth  = detail?.PlaceOfBirth,
                    ProfileImages = imgs ?? new()
                };
            }).ToList();

        ImportDirectorDTO? directorDto = null;
        if (full.Director != null)
        {
            full.PersonImages.TryGetValue(full.Director.Id, out var dirImgs);
            full.PersonDetails.TryGetValue(full.Director.Id, out var dirDetail);
            directorDto = new ImportDirectorDTO
            {
                TmdbPersonId  = full.Director.Id,
                Name          = full.Director.Name,
                ProfileUrl    = full.Director.ProfileUrl,
                Biography     = dirDetail?.Biography,
                Birthday      = dirDetail?.Birthday,
                PlaceOfBirth  = dirDetail?.PlaceOfBirth,
                ProfileImages = dirImgs ?? new()
            };
        }

        var images = full.Backdrops
            .Take(10)
            .Select(b => new ImportImageDTO { Url = b.Url ?? "", ImageType = "backdrop" })
            .Concat(full.Posters
                .Take(10)
                .Select(p => new ImportImageDTO { Url = p.Url ?? "", ImageType = "poster" }))
            .Where(i => !string.IsNullOrEmpty(i.Url))
            .ToList();

        var trailers = full.Trailers
            .Select(t => new ImportTrailerDTO { YoutubeUrl = t.YoutubeUrl, Name = t.Name })
            .ToList();

        var seasons = full.SeasonDetails.Values
            .Where(s => s.SeasonNumber > 0)
            .OrderBy(s => s.SeasonNumber)
            .Select(s => new CreateSeasonDTO
            {
                SeasonNumber = s.SeasonNumber,
                Name         = s.Name,
                Overview     = string.IsNullOrEmpty(s.Overview) ? null : s.Overview,
                PosterUrl    = s.PosterUrl,
                AirDate      = DateTime.TryParse(s.AirDate, out var ad) ? ad : null,
                Episodes     = s.Episodes
                    .OrderBy(e => e.EpisodeNumber)
                    .Select(e => new CreateEpisodeDTO
                    {
                        EpisodeNumber = e.EpisodeNumber,
                        Title         = e.Title,
                        Overview      = string.IsNullOrEmpty(e.Overview) ? null : e.Overview,
                        StillUrl      = e.StillUrl,
                        Runtime       = e.Runtime,
                        Rating        = e.VoteAverage > 0 ? (decimal)e.VoteAverage : null,
                        AirDate       = DateTime.TryParse(e.AirDate, out var ea) ? ea : null
                    }).ToList()
            }).ToList();

        var dto = new CreateTvShowDTO
        {
            TmdbId           = full.Detail.Id,
            Title            = full.Detail.Name,
            Description      = full.Detail.Overview,
            FirstAirDate     = DateTime.TryParse(full.Detail.FirstAirDate, out var fad) ? fad : null,
            LastAirDate      = DateTime.TryParse(full.Detail.LastAirDate, out var lad) ? lad : null,
            PosterUrl        = full.Detail.PosterUrl,
            BackdropUrl      = full.Detail.BackdropUrl,
            EpisodeRuntime   = full.Detail.EpisodeRuntime,
            ImdbRating       = full.Detail.VoteAverage > 0 ? (decimal)full.Detail.VoteAverage : null,
            OriginCountry    = full.Detail.OriginCountry.FirstOrDefault(),
            Status           = full.Detail.Status,
            NumberOfSeasons  = full.Detail.NumberOfSeasons,
            NumberOfEpisodes = full.Detail.NumberOfEpisodes,
            GenreIds         = genreIds,
            Cast             = cast,
            Director         = directorDto,
            Images           = images,
            Trailers         = trailers,
            Seasons          = seasons
        };

        var showId = await _tvShowService.CreateTvShowAsync(dto);

        return CreatedAtAction(nameof(GetById), new { id = showId }, new ApiResponseDTO<object>
        {
            Data = new
            {
                showId,
                genreCount       = genreIds.Count,
                castCount        = cast.Count,
                imageCount       = images.Count,
                seasonCount      = seasons.Count,
                episodeCount     = seasons.Sum(s => s.Episodes.Count),
                hasDirector      = directorDto != null,
                personBioCount   = full.PersonDetails.Count(kv => !string.IsNullOrEmpty(kv.Value?.Biography)),
                personImageCount = full.PersonImages.Count(kv => kv.Value.Any())
            },
            Message = "Import TV show thành công"
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // VIDEO — Upload & xóa video
    // ═══════════════════════════════════════════════════════════════════

    [HttpPost("{id:guid}/videos")]
    [Authorize(Roles = Roles.Admin)]
    [RequestSizeLimit(5_368_709_120)]
    [RequestFormLimits(MultipartBodyLengthLimit = 5_368_709_120)]
    public async Task<IActionResult> UploadVideo(Guid id, [FromForm] UploadTvShowVideoDTO dto)
    {
        if (dto.VideoFile == null || dto.VideoFile.Length == 0)
            return BadRequest(new ApiErrorResponseDTO { Message = "File không hợp lệ", StatusCode = 400 });

        var videoUrl = await _cloudinaryService.UploadVideoAsync(
            dto.VideoFile, $"uiamovie/tvshows/{id}");

        var success = await _tvShowService.AddVideoAsync(id, videoUrl, dto.VideoType, dto.Quality);

        return success
            ? Ok(new ApiResponseDTO<object> { Data = new { videoUrl }, Message = "Upload video thành công" })
            : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy TV show", StatusCode = 404 });
    }

    [HttpDelete("videos/{videoId:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DeleteVideo(Guid videoId)
    {
        var success = await _tvShowService.DeleteVideoAsync(videoId);
        return success
            ? Ok(new ApiResponseDTO<object> { Message = "Xóa video thành công" })
            : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy video", StatusCode = 404 });
    }

    // ═══════════════════════════════════════════════════════════════════
    // EPISODE VIDEO — Upload & xóa video từng tập
    // ═══════════════════════════════════════════════════════════════════

    [HttpPost("episodes/{episodeId:guid}/video")]
    [Authorize(Roles = Roles.Admin)]
    [RequestSizeLimit(5_368_709_120)]
    [RequestFormLimits(MultipartBodyLengthLimit = 5_368_709_120)]
    public async Task<IActionResult> UploadEpisodeVideo(Guid episodeId, IFormFile videoFile)
    {
        if (videoFile == null || videoFile.Length == 0)
            return BadRequest(new ApiErrorResponseDTO { Message = "File không hợp lệ", StatusCode = 400 });

        var videoUrl = await _cloudinaryService.UploadVideoAsync(
            videoFile, $"uiamovie/episodes/{episodeId}");

        var (found, oldUrl) = await _tvShowService.SetEpisodeVideoAsync(episodeId, videoUrl);
        if (!found) return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy episode", StatusCode = 404 });

        // Xóa file cũ trên Cloudinary nếu có
        if (!string.IsNullOrEmpty(oldUrl))
        {
            var oldPublicId = ExtractCloudinaryPublicId(oldUrl);
            if (oldPublicId != null)
                try { await _cloudinaryService.DeleteFileAsync(oldPublicId); } catch { }
        }

        return Ok(new ApiResponseDTO<object> { Data = new { videoUrl }, Message = "Upload video tập thành công" });
    }

    [HttpDelete("episodes/{episodeId:guid}/video")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DeleteEpisodeVideo(Guid episodeId)
    {
        var (found, oldUrl) = await _tvShowService.RemoveEpisodeVideoAsync(episodeId);
        if (!found) return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy episode", StatusCode = 404 });

        if (!string.IsNullOrEmpty(oldUrl))
        {
            var publicId = ExtractCloudinaryPublicId(oldUrl);
            if (publicId != null)
                try { await _cloudinaryService.DeleteFileAsync(publicId); } catch { }
        }

        return Ok(new ApiResponseDTO<object> { Message = "Đã xóa video tập" });
    }

    // ═══════════════════════════════════════════════════════════════════
    // ADMIN — CRUD
    // ═══════════════════════════════════════════════════════════════════

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create([FromBody] CreateTvShowDTO dto)
    {
        var showId = await _tvShowService.CreateTvShowAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = showId },
            new ApiResponseDTO<object> { Data = new { showId }, Message = "Tạo TV show thành công" });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTvShowDTO dto)
    {
        var success = await _tvShowService.UpdateTvShowAsync(id, dto);
        return success
            ? Ok(new ApiResponseDTO<object> { Message = "Cập nhật thành công" })
            : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy TV show", StatusCode = 404 });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var success = await _tvShowService.DeleteTvShowAsync(id);
        return success
            ? Ok(new ApiResponseDTO<object> { Message = "Xóa TV show thành công" })
            : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy TV show", StatusCode = 404 });
    }

    [HttpPost("{id:guid}/sync")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SyncNewEpisodes(Guid id)
    {
        var tmdbId = await _tvShowService.GetTmdbIdAsync(id);
        if (tmdbId == null)
            return BadRequest(new ApiErrorResponseDTO { Message = "TV Show không có TmdbId", StatusCode = 400 });

        var fullData = await _tmdbService.GetFullTvShowAsync(tmdbId.Value);
        if (fullData == null)
            return BadRequest(new ApiErrorResponseDTO { Message = "Không lấy được data từ TMDB", StatusCode = 400 });

        var result = await _tvShowService.SyncNewEpisodesAsync(id, fullData);

        return result.Success
            ? Ok(new ApiResponseDTO<object> { Data = result, Message = result.Message })
            : BadRequest(new ApiErrorResponseDTO { Message = result.Message, StatusCode = 400 });
    }

    // ═══════════════════════════════════════════════════════════════════
    // WATCH HISTORY — Lịch sử xem
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet("history")]
    [Authorize]
    public async Task<IActionResult> GetWatchHistory()
    {
        var history = await _tvShowService.GetWatchHistoryAsync(GetUserId());
        return Ok(new ApiResponseDTO<object> { Data = history, Message = "Thành công" });
    }

    [HttpPost("history")]
    [Authorize]
    public async Task<IActionResult> UpdateWatchProgress([FromBody] UpdateTvShowWatchProgressDTO dto)
    {
        await _tvShowService.UpdateWatchProgressAsync(
            GetUserId(), dto.TvShowId, dto.EpisodeId, dto.ProgressSeconds, dto.IsCompleted);

        return Ok(new ApiResponseDTO<object> { Message = "Đã cập nhật tiến trình xem" });
    }

    [HttpDelete("history/{historyId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteWatchHistory(Guid historyId)
    {
        var success = await _tvShowService.DeleteWatchHistoryAsync(GetUserId(), historyId);
        if (!success)
            return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy lịch sử xem", StatusCode = 404 });

        return Ok(new ApiResponseDTO<object> { Message = "Đã xóa lịch sử xem" });
    }

    [HttpDelete("history")]
    [Authorize]
    public async Task<IActionResult> ClearWatchHistory()
    {
        await _tvShowService.ClearWatchHistoryAsync(GetUserId());
        return Ok(new ApiResponseDTO<object> { Message = "Đã xóa toàn bộ lịch sử xem" });
    }

    // ─── Helper ──────────────────────────────────────────────────────────────

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? Guid.Empty.ToString());

    private static string? ExtractCloudinaryPublicId(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.Contains("youtube.com") || url.Contains("youtu.be")) return null;
        if (!url.Contains("cloudinary.com")) return null;

        var match = System.Text.RegularExpressions.Regex.Match(
            url, @"/upload/(?:v\d+/)?(.+?)(?:\.[^./]+)?$");

        return match.Success ? match.Groups[1].Value : null;
    }
}