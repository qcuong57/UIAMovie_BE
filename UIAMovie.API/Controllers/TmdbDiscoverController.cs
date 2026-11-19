// UIAMovie.API/Controllers/TmdbDiscoverController.cs
// Lọc + tìm kiếm kết hợp khi import từ TMDB — dùng chung cho phim ("movie") và TV show ("tv").
//   GET  /api/admin/tmdb/{kind}/discover   — danh sách phim/show theo từ khoá + bộ lọc (xem TmdbDiscoverFilterDTO)
//   GET  /api/admin/tmdb/{kind}/genres     — thể loại TMDB (id + tên tiếng Việt) để dựng bộ lọc
//   POST /api/admin/tmdb/{kind}/exists     — trong danh sách tmdbId gửi lên, id nào đã có trong DB

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Configuration;

namespace UIAMovie.API.Controllers;

[ApiController]
[Route("api/admin/tmdb")]
[Authorize(Roles = "Admin")]
public class TmdbDiscoverController : ControllerBase
{
    private const int MaxExistsIds = 100;
    private static readonly TimeSpan GenreCacheTtl = TimeSpan.FromHours(12);

    // Thể loại TMDB gần như không đổi → cache trong process, khỏi gọi TMDB mỗi lần mở panel
    private static readonly ConcurrentDictionary<string, (DateTime At, List<TmdbGenreDTO> Items)> GenreCache = new();

    private readonly ITmdbService _tmdb;
    private readonly IRepository<Movie> _movieRepository;
    private readonly IRepository<TvShow> _tvShowRepository;
    private readonly ILogger<TmdbDiscoverController> _logger;

    public TmdbDiscoverController(
        ITmdbService tmdb,
        IRepository<Movie> movieRepository,
        IRepository<TvShow> tvShowRepository,
        ILogger<TmdbDiscoverController> logger)
    {
        _tmdb = tmdb;
        _movieRepository = movieRepository;
        _tvShowRepository = tvShowRepository;
        _logger = logger;
    }

    [HttpGet("{kind}/discover")]
    public async Task<IActionResult> Discover(string kind, [FromQuery] TmdbDiscoverFilterDTO filter)
    {
        if (!TryParseKind(kind, out var isTv)) return InvalidKind();

        try
        {
            var result = isTv
                ? await _tmdb.DiscoverTvShowsAsync(filter)
                : await _tmdb.DiscoverMoviesAsync(filter);
            return Ok(result);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "TMDB discover thất bại (kind={Kind})", kind);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "Không kết nối được TMDB, thử lại sau ít phút." });
        }
    }

    [HttpGet("{kind}/genres")]
    public async Task<IActionResult> Genres(string kind)
    {
        if (!TryParseKind(kind, out var isTv)) return InvalidKind();

        var key = isTv ? "tv" : "movie";
        if (GenreCache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < GenreCacheTtl)
            return Ok(cached.Items);

        try
        {
            var genres = isTv ? await _tmdb.GetTvGenresAsync() : await _tmdb.GetGenresAsync();
            var items = genres.OrderBy(g => g.Name).ToList();
            GenreCache[key] = (DateTime.UtcNow, items);
            return Ok(items);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "TMDB genres thất bại (kind={Kind})", kind);
            if (cached.Items != null) return Ok(cached.Items); // còn bản cũ thì dùng tạm
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "Không tải được danh sách thể loại từ TMDB." });
        }
    }

    [HttpPost("{kind}/exists")]
    public async Task<IActionResult> Exists(string kind, [FromBody] TmdbExistsRequestDTO request)
    {
        if (!TryParseKind(kind, out var isTv)) return InvalidKind();

        var ids = (request?.Ids ?? new List<int>())
            .Where(id => id > 0)
            .Distinct()
            .Take(MaxExistsIds)
            .ToList();

        if (ids.Count == 0) return Ok(new { existing = Array.Empty<int>() });

        List<int> existing;
        if (isTv)
        {
            var shows = await _tvShowRepository.FindAsync(s => s.TmdbId.HasValue && ids.Contains(s.TmdbId.Value));
            existing = shows.Select(s => s.TmdbId!.Value).Distinct().ToList();
        }
        else
        {
            var movies = await _movieRepository.FindAsync(m => m.TmdbId.HasValue && ids.Contains(m.TmdbId.Value));
            existing = movies.Select(m => m.TmdbId!.Value).Distinct().ToList();
        }

        return Ok(new { existing });
    }

    private static bool TryParseKind(string kind, out bool isTv)
    {
        isTv = false;
        if (string.Equals(kind, "movie", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(kind, "tv", StringComparison.OrdinalIgnoreCase)) { isTv = true; return true; }
        return false;
    }

    private IActionResult InvalidKind() =>
        BadRequest(new { message = "kind phải là 'movie' hoặc 'tv'." });
}

/// <summary>Body của POST /api/admin/tmdb/{kind}/exists.</summary>
public class TmdbExistsRequestDTO
{
    public List<int> Ids { get; set; } = new();
}