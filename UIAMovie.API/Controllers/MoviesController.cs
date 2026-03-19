// UIAMovie.API/Controllers/MoviesController.cs

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;
using UIAMovie.Domain.Constants;
using UIAMovie.Infrastructure.Configuration;

namespace UIAMovie.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MoviesController : ControllerBase
{
    private readonly IMovieService      _movieService;
    private readonly ITmdbService       _tmdbService;
    private readonly ICloudinaryService _cloudinaryService;

    public MoviesController(
        IMovieService      movieService,
        ITmdbService       tmdbService,
        ICloudinaryService cloudinaryService)
    {
        _movieService      = movieService;
        _tmdbService       = tmdbService;
        _cloudinaryService = cloudinaryService;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PUBLIC — Không cần đăng nhập
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Lấy danh sách phim — lọc, tìm kiếm, phân trang</summary>
    [HttpGet]
    public async Task<IActionResult> GetMovies([FromQuery] FilterMoviesDTO filter)
    {
        var result = await _movieService.GetMoviesAsync(filter);
        return Ok(result);
    }

    /// <summary>Top 20 phim trending (theo rating)</summary>
    [HttpGet("trending")]
    public async Task<IActionResult> GetTrending()
    {
        var movies = await _movieService.GetTrendingMoviesAsync();
        return Ok(movies);
    }

    /// <summary>Tìm kiếm phim theo tên</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new { message = "Query không được để trống" });

        var movies = await _movieService.SearchMoviesAsync(query);
        return Ok(movies);
    }

    /// <summary>Lấy phim theo genre</summary>
    [HttpGet("genre/{genreId:guid}")]
    public async Task<IActionResult> GetByGenre(Guid genreId)
    {
        var movies = await _movieService.GetMoviesByGenreAsync(genreId);
        return Ok(movies);
    }

    /// <summary>Lấy chi tiết phim theo ID</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var movie = await _movieService.GetMovieByIdAsync(id);
        return movie == null ? NotFound(new { message = "Không tìm thấy phim" }) : Ok(movie);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TMDB — Tìm kiếm & import từ TMDB
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Tìm phim trên TMDB</summary>
    [HttpGet("tmdb/search")]
    [Authorize]
    public async Task<IActionResult> SearchTmdb([FromQuery] string query, [FromQuery] int page = 1)
    {
        var result = await _tmdbService.SearchMoviesAsync(query, page);
        return Ok(result);
    }

    /// <summary>Lấy phim trending từ TMDB</summary>
    [HttpGet("tmdb/trending")]
    public async Task<IActionResult> GetTmdbTrending([FromQuery] string timeWindow = "week")
    {
        var result = await _tmdbService.GetTrendingMoviesAsync(timeWindow);
        return Ok(result);
    }

    /// <summary>Lấy chi tiết phim từ TMDB theo tmdbId</summary>
    [HttpGet("tmdb/{tmdbId:int}")]
    public async Task<IActionResult> GetTmdbMovie(int tmdbId)
    {
        var movie = await _tmdbService.GetMovieAsync(tmdbId);
        return movie == null ? NotFound() : Ok(movie);
    }

    /// <summary>Lấy trailer phim từ TMDB</summary>
    [HttpGet("tmdb/{tmdbId:int}/trailers")]
    public async Task<IActionResult> GetTrailers(int tmdbId)
    {
        var trailers = await _tmdbService.GetMovieTrailersAsync(tmdbId);
        return Ok(trailers);
    }

    /// <summary>Lấy danh sách genre từ TMDB</summary>
    [HttpGet("tmdb/genres")]
    public async Task<IActionResult> GetTmdbGenres()
    {
        var genres = await _tmdbService.GetGenresAsync();
        return Ok(genres);
    }

    /// <summary>
    /// [Admin] Import phim từ TMDB vào database.
    /// Tự động kéo về: detail + top 10 cast + đạo diễn + ảnh + trailers.
    /// </summary>
    [HttpPost("tmdb/{tmdbId:int}/import")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> ImportFromTmdb(int tmdbId)
    {
        // Kiểm tra duplicate
        var existing = await _movieService.GetMovieByTmdbIdAsync(tmdbId);
        if (existing != null)
            return Conflict(new { message = "Phim này đã được import rồi", movieId = existing.Id });

        // Gọi song song 4 TMDB APIs (detail + credits + images + trailers)
        var full = await _tmdbService.GetFullMovieAsync(tmdbId);
        if (full == null)
            return NotFound(new { message = "Không tìm thấy phim trên TMDB" });

        var dto = new CreateMovieDTO
        {
            TmdbId      = full.Detail.Id,
            Title       = full.Detail.Title,
            Description = string.IsNullOrEmpty(full.Detail.Overview)
                            ? full.Detail.Title
                            : full.Detail.Overview,
            ReleaseDate = DateTime.TryParse(full.Detail.ReleaseDate, out var d)
                            ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
                            : null,
            PosterUrl   = full.Detail.PosterUrl,
            BackdropUrl = full.Detail.BackdropUrl,
            Duration    = full.Detail.Runtime,
            ImdbRating  = (decimal)full.Detail.VoteAverage,
            GenreIds    = new List<Guid>(),

            // ← Cast (top 10)
            Cast = full.Cast.Select(c => new ImportCastDTO
            {
                TmdbPersonId = c.Id,
                Name         = c.Name,
                Character    = c.Character,
                Order        = c.Order,
                ProfileUrl   = c.ProfileUrl
            }).ToList(),

            // ← Đạo diễn
            Director = full.Director == null ? null : new ImportDirectorDTO
            {
                TmdbPersonId = full.Director.Id,
                Name         = full.Director.Name,
                ProfileUrl   = full.Director.ProfileUrl
            },

            // ← Hình ảnh (5 backdrops + 3 posters)
            Images = full.Backdrops.Select(i => new ImportImageDTO
            {
                Url       = i.Url!,
                ImageType = "backdrop"
            })
            .Concat(full.Posters.Select(i => new ImportImageDTO
            {
                Url       = i.Url!,
                ImageType = "poster"
            }))
            .ToList(),

            // ← Trailers (YouTube URL)
            Trailers = full.Trailers.Select(t => new ImportTrailerDTO
            {
                YoutubeUrl = t.YoutubeUrl,
                Name       = t.Name
            }).ToList()
        };

        var movieId = await _movieService.CreateMovieAsync(dto);

        return CreatedAtAction(nameof(GetById), new { id = movieId }, new
        {
            message    = "Import thành công",
            movieId,
            castCount  = dto.Cast.Count,
            imageCount = dto.Images.Count,
            hasDirector = dto.Director != null
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // ADMIN — CRUD phim
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>[Admin] Tạo phim mới</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create([FromBody] CreateMovieDTO dto)
    {
        var movieId = await _movieService.CreateMovieAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = movieId },
            new { message = "Tạo phim thành công", movieId });
    }

    /// <summary>[Admin] Cập nhật phim</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMovieDTO dto)
    {
        var success = await _movieService.UpdateMovieAsync(id, dto);
        return success
            ? Ok(new { message = "Cập nhật thành công" })
            : NotFound(new { message = "Không tìm thấy phim" });
    }

    /// <summary>[Admin] Xóa phim</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var success = await _movieService.DeleteMovieAsync(id);
        return success
            ? Ok(new { message = "Xóa phim thành công" })
            : NotFound(new { message = "Không tìm thấy phim" });
    }

    // ═══════════════════════════════════════════════════════════════════
    // VIDEO — Upload & xóa video
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>[Admin] Upload video lên Cloudinary và gắn vào phim</summary>
    [HttpPost("{id:guid}/videos")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> UploadVideo(Guid id, [FromForm] UploadMovieVideoDTO dto)
    {
        if (dto.VideoFile == null || dto.VideoFile.Length == 0)
            return BadRequest(new { message = "File không hợp lệ" });

        var videoUrl = await _cloudinaryService.UploadVideoAsync(
            dto.VideoFile, $"uiamovie/movies/{id}");

        var success = await _movieService.AddVideoAsync(id, videoUrl, dto.VideoType, dto.Quality);

        return success
            ? Ok(new { message = "Upload video thành công", videoUrl })
            : NotFound(new { message = "Không tìm thấy phim" });
    }

    /// <summary>[Admin] Xóa video</summary>
    [HttpDelete("videos/{videoId:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DeleteVideo(Guid videoId)
    {
        var success = await _movieService.DeleteVideoAsync(videoId);
        return success
            ? Ok(new { message = "Xóa video thành công" })
            : NotFound(new { message = "Không tìm thấy video" });
    }

    // ═══════════════════════════════════════════════════════════════════
    // FAVORITES — Yêu thích
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet("favorites")]
    [Authorize]
    public async Task<IActionResult> GetFavorites()
    {
        var favorites = await _movieService.GetFavoritesAsync(GetUserId());
        return Ok(favorites);
    }

    [HttpPost("favorites")]
    [Authorize]
    public async Task<IActionResult> AddFavorite([FromBody] AddFavoriteDTO dto)
    {
        var success = await _movieService.AddFavoriteAsync(GetUserId(), dto.MovieId);
        return success
            ? Ok(new { message = "Đã thêm vào yêu thích" })
            : BadRequest(new { message = "Phim đã có trong danh sách yêu thích" });
    }

    [HttpDelete("favorites/{movieId:guid}")]
    [Authorize]
    public async Task<IActionResult> RemoveFavorite(Guid movieId)
    {
        var success = await _movieService.RemoveFavoriteAsync(GetUserId(), movieId);
        return success
            ? Ok(new { message = "Đã xóa khỏi yêu thích" })
            : NotFound(new { message = "Không tìm thấy trong danh sách yêu thích" });
    }

    // ═══════════════════════════════════════════════════════════════════
    // WATCH HISTORY — Lịch sử xem
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet("history")]
    [Authorize]
    public async Task<IActionResult> GetWatchHistory()
    {
        var history = await _movieService.GetWatchHistoryAsync(GetUserId());
        return Ok(history);
    }

    [HttpPost("history")]
    [Authorize]
    public async Task<IActionResult> UpdateWatchProgress([FromBody] UpdateWatchProgressDTO dto)
    {
        await _movieService.UpdateWatchProgressAsync(
            GetUserId(), dto.MovieId, dto.ProgressMinutes, dto.IsCompleted);

        return Ok(new { message = "Đã cập nhật tiến trình xem" });
    }

    // ─── Helper ──────────────────────────────────────────────────────────────

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? Guid.Empty.ToString());
}