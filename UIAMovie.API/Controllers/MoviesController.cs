// UIAMovie.API/Controllers/MoviesController.cs

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
[Route("api/[controller]")]
public class MoviesController : ControllerBase
{
    private readonly IMovieService      _movieService;
    private readonly ITmdbService       _tmdbService;
    private readonly ICloudinaryService _cloudinaryService;
    private readonly IGenreService      _genreService;

    public MoviesController(
        IMovieService      movieService,
        ITmdbService       tmdbService,
        ICloudinaryService cloudinaryService,
        IGenreService      genreService)
    {
        _movieService      = movieService;
        _tmdbService       = tmdbService;
        _cloudinaryService = cloudinaryService;
        _genreService      = genreService;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PUBLIC — Không cần đăng nhập
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet("country/{country}")]
    public async Task<IActionResult> GetByCountry(string country)
    {
        if (string.IsNullOrWhiteSpace(country))
            return BadRequest(new ApiErrorResponseDTO { Message = "Mã quốc gia không được để trống", StatusCode = 400 });

        var filter = new FilterMoviesDTO
        {
            OriginCountry = country.Trim().ToUpper(),
            PageSize      = 200,
            SortBy        = "rating",
            SortDesc      = true
        };
        var result = await _movieService.GetMoviesAsync(filter);
        return Ok(new ApiResponseDTO<object> { Data = result, Message = "Thành công" });
    }

    [HttpGet("countries")]
    public async Task<IActionResult> GetAvailableCountries()
    {
        var all = await _movieService.GetMoviesAsync(new FilterMoviesDTO { PageSize = 9999 });
        var countries = all.Items
            .Where(m => !string.IsNullOrEmpty(m.OriginCountry))
            .Select(m => m.OriginCountry!)
            .Distinct()
            .OrderBy(c => c)
            .ToList();
        return Ok(new ApiResponseDTO<List<string>> { Data = countries, Message = "Thành công" });
    }

    [HttpGet]
    public async Task<IActionResult> GetMovies([FromQuery] FilterMoviesDTO filter)
    {
        var result = await _movieService.GetMoviesAsync(filter);
        return Ok(new ApiResponseDTO<object> { Data = result, Message = "Thành công" });
    }

    [HttpGet("trending")]
    public async Task<IActionResult> GetTrending()
    {
        var movies = await _movieService.GetTrendingMoviesAsync();
        return Ok(new ApiResponseDTO<object> { Data = movies, Message = "Thành công" });
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new ApiErrorResponseDTO { Message = "Query không được để trống", StatusCode = 400 });

        var movies = await _movieService.SearchMoviesAsync(query);
        return Ok(new ApiResponseDTO<object> { Data = movies, Message = "Thành công" });
    }

    [HttpGet("genre/{genreId:guid}")]
    public async Task<IActionResult> GetByGenre(Guid genreId)
    {
        var movies = await _movieService.GetMoviesByGenreAsync(genreId);
        return Ok(new ApiResponseDTO<object> { Data = movies, Message = "Thành công" });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var movie = await _movieService.GetMovieByIdAsync(id);
        return movie == null 
            ? NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy phim", StatusCode = 404 }) 
            : Ok(new ApiResponseDTO<object> { Data = movie, Message = "Thành công" });
    }

    // ═══════════════════════════════════════════════════════════════════
    // TMDB — Tìm kiếm & import từ TMDB
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet("tmdb/search")]
    [Authorize]
    public async Task<IActionResult> SearchTmdb([FromQuery] string query, [FromQuery] int page = 1)
    {
        var result = await _tmdbService.SearchMoviesAsync(query, page);
        return Ok(new ApiResponseDTO<object> { Data = result, Message = "Thành công" });
    }

    [HttpGet("tmdb/trending")]
    public async Task<IActionResult> GetTmdbTrending([FromQuery] string timeWindow = "week")
    {
        var result = await _tmdbService.GetTrendingMoviesAsync(timeWindow);
        return Ok(new ApiResponseDTO<object> { Data = result, Message = "Thành công" });
    }

    [HttpGet("tmdb/{tmdbId:int}")]
    public async Task<IActionResult> GetTmdbMovie(int tmdbId)
    {
        var movie = await _tmdbService.GetMovieAsync(tmdbId);
        return movie == null 
            ? NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy phim trên TMDB", StatusCode = 404 }) 
            : Ok(new ApiResponseDTO<object> { Data = movie, Message = "Thành công" });
    }

    [HttpGet("tmdb/{tmdbId:int}/trailers")]
    public async Task<IActionResult> GetTrailers(int tmdbId)
    {
        var trailers = await _tmdbService.GetMovieTrailersAsync(tmdbId);
        return Ok(new ApiResponseDTO<object> { Data = trailers, Message = "Thành công" });
    }

    [HttpGet("tmdb/genres")]
    public async Task<IActionResult> GetTmdbGenres()
    {
        var genres = await _tmdbService.GetGenresAsync();
        return Ok(new ApiResponseDTO<object> { Data = genres, Message = "Thành công" });
    }

    [HttpGet("tmdb/person/{tmdbPersonId:int}")]
    public async Task<IActionResult> GetTmdbPerson(int tmdbPersonId)
    {
        var person = await _tmdbService.GetPersonDetailAsync(tmdbPersonId);
        return person == null 
            ? NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy thông tin người này", StatusCode = 404 }) 
            : Ok(new ApiResponseDTO<object> { Data = person, Message = "Thành công" });
    }

    [HttpGet("tmdb/person/{tmdbPersonId:int}/images")]
    public async Task<IActionResult> GetTmdbPersonImages(int tmdbPersonId)
    {
        var images = await _tmdbService.GetPersonImagesAsync(tmdbPersonId);
        return Ok(new ApiResponseDTO<object> { Data = images, Message = "Thành công" });
    }

    [HttpPost("tmdb/{tmdbId:int}/import")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> ImportFromTmdb(int tmdbId)
    {
        var existing = await _movieService.GetMovieByTmdbIdAsync(tmdbId);
        if (existing != null)
            return Conflict(new ApiErrorResponseDTO { Message = $"Phim này đã được import rồi (ID: {existing.Id})", StatusCode = 409 });

        var full = await _tmdbService.GetFullMovieAsync(tmdbId);
        if (full == null)
            return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy phim trên TMDB", StatusCode = 404 });

        var genreIds = await _genreService.ResolveGenreIdsFromTmdbAsync(
            full.Detail.Genres.Select(g => g.Id));

        var dto = new CreateMovieDTO
        {
            TmdbId      = full.Detail.Id,
            Title       = full.Detail.Title,
            Description = string.IsNullOrEmpty(full.Detail.Overview) ? full.Detail.Title : full.Detail.Overview,
            ReleaseDate = DateTime.TryParse(full.Detail.ReleaseDate, out var d) ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null,
            PosterUrl   = full.Detail.PosterUrl,
            BackdropUrl = full.Detail.BackdropUrl,
            Duration    = full.Detail.Runtime,
            ImdbRating  = (decimal)full.Detail.VoteAverage,
            OriginCountry = full.Detail.OriginCountry.FirstOrDefault(),
            GenreIds    = genreIds,

            Cast = full.Cast.Select(c =>
            {
                var bio    = full.PersonDetails.GetValueOrDefault(c.Id);
                var images = full.PersonImages.GetValueOrDefault(c.Id) ?? new();
                return new ImportCastDTO
                {
                    TmdbPersonId  = c.Id, Name = c.Name, Character = c.Character, Order = c.Order,
                    ProfileUrl    = c.ProfileUrl, Biography = bio?.Biography, Birthday = bio?.Birthday,
                    PlaceOfBirth  = bio?.PlaceOfBirth, ProfileImages = images
                };
            }).ToList(),

            Director = full.Director == null ? null : new ImportDirectorDTO
            {
                TmdbPersonId  = full.Director.Id, Name = full.Director.Name, ProfileUrl = full.Director.ProfileUrl,
                Biography     = full.PersonDetails.GetValueOrDefault(full.Director.Id)?.Biography,
                Birthday      = full.PersonDetails.GetValueOrDefault(full.Director.Id)?.Birthday,
                PlaceOfBirth  = full.PersonDetails.GetValueOrDefault(full.Director.Id)?.PlaceOfBirth,
                ProfileImages = full.PersonImages.GetValueOrDefault(full.Director.Id) ?? new()
            },

            Images = full.Backdrops.Select(i => new ImportImageDTO { Url = i.Url!, ImageType = "backdrop" })
                .Concat(full.Posters.Select(i => new ImportImageDTO { Url = i.Url!, ImageType = "poster" })).ToList(),

            Trailers = full.Trailers.Select(t => new ImportTrailerDTO { YoutubeUrl = t.YoutubeUrl, Name = t.Name }).ToList()
        };

        var movieId = await _movieService.CreateMovieAsync(dto);

        return CreatedAtAction(nameof(GetById), new { id = movieId }, new ApiResponseDTO<object>
        {
            Data = new
            {
                movieId, genreCount = genreIds.Count, castCount = dto.Cast.Count,
                imageCount = dto.Images.Count, hasDirector = dto.Director != null,
                personBioCount = full.PersonDetails.Count(kv => !string.IsNullOrEmpty(kv.Value?.Biography)),
                personImageCount = full.PersonImages.Count(kv => kv.Value.Any())
            },
            Message = "Import thành công"
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // ADMIN — CRUD phim
    // ═══════════════════════════════════════════════════════════════════

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create([FromBody] CreateMovieDTO dto)
    {
        var movieId = await _movieService.CreateMovieAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = movieId },
            new ApiResponseDTO<object> { Data = new { movieId }, Message = "Tạo phim thành công" });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMovieDTO dto)
    {
        var success = await _movieService.UpdateMovieAsync(id, dto);
        return success
            ? Ok(new ApiResponseDTO<object> { Message = "Cập nhật thành công" })
            : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy phim", StatusCode = 404 });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var success = await _movieService.DeleteMovieAsync(id);
        return success
            ? Ok(new ApiResponseDTO<object> { Message = "Xóa phim thành công" })
            : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy phim", StatusCode = 404 });
    }

    // ═══════════════════════════════════════════════════════════════════
    // VIDEO — Upload & xóa video
    // ═══════════════════════════════════════════════════════════════════

    [HttpPost("{id:guid}/videos")]
    [Authorize(Roles = Roles.Admin)]
    [RequestSizeLimit(5_368_709_120)]
    [RequestFormLimits(MultipartBodyLengthLimit = 5_368_709_120)]
    public async Task<IActionResult> UploadVideo(Guid id, [FromForm] UploadMovieVideoDTO dto)
    {
        if (dto.VideoFile == null || dto.VideoFile.Length == 0)
            return BadRequest(new ApiErrorResponseDTO { Message = "File không hợp lệ", StatusCode = 400 });

        var videoUrl = await _cloudinaryService.UploadVideoAsync(
            dto.VideoFile, $"uiamovie/movies/{id}");

        var success = await _movieService.AddVideoAsync(id, videoUrl, dto.VideoType, dto.Quality);

        return success
            ? Ok(new ApiResponseDTO<object> { Data = new { videoUrl }, Message = "Upload video thành công" })
            : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy phim", StatusCode = 404 });
    }

    [HttpDelete("videos/{videoId:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DeleteVideo(Guid videoId)
    {
        var success = await _movieService.DeleteVideoAsync(videoId);
        return success
            ? Ok(new ApiResponseDTO<object> { Message = "Xóa video thành công" })
            : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy video", StatusCode = 404 });
    }

    // ═══════════════════════════════════════════════════════════════════
    // FAVORITES — Yêu thích
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet("favorites")]
    [Authorize]
    public async Task<IActionResult> GetFavorites()
    {
        var favorites = await _movieService.GetFavoritesAsync(GetUserId());
        return Ok(new ApiResponseDTO<object> { Data = favorites, Message = "Thành công" });
    }

    [HttpPost("favorites")]
    [Authorize]
    public async Task<IActionResult> AddFavorite([FromBody] AddFavoriteDTO dto)
    {
        var success = await _movieService.AddFavoriteAsync(GetUserId(), dto.MovieId);
        return success
            ? Ok(new ApiResponseDTO<object> { Message = "Đã thêm vào yêu thích" })
            : BadRequest(new ApiErrorResponseDTO { Message = "Phim đã có trong danh sách yêu thích", StatusCode = 400 });
    }

    [HttpDelete("favorites/{movieId:guid}")]
    [Authorize]
    public async Task<IActionResult> RemoveFavorite(Guid movieId)
    {
        var success = await _movieService.RemoveFavoriteAsync(GetUserId(), movieId);
        return success
            ? Ok(new ApiResponseDTO<object> { Message = "Đã xóa khỏi yêu thích" })
            : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy trong danh sách yêu thích", StatusCode = 404 });
    }

    // ═══════════════════════════════════════════════════════════════════
    // SEARCH
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet("search/actor")]
    public async Task<IActionResult> SearchByActor([FromQuery] string actorName)
    {
        if (string.IsNullOrWhiteSpace(actorName))
            return BadRequest(new ApiErrorResponseDTO { Message = "Tên diễn viên không được để trống", StatusCode = 400 });

        var movies = await _movieService.SearchMoviesByActorAsync(actorName);
        return Ok(new ApiResponseDTO<object> { Data = movies, Message = "Thành công" });
    }

    [HttpGet("person/{personId:guid}/images")]
    public async Task<IActionResult> GetPersonImagesFromDb(Guid personId)
    {
        var images = await _movieService.GetPersonImagesAsync(personId);
        return Ok(new ApiResponseDTO<object> { Data = images, Message = "Thành công" });
    }

    // ═══════════════════════════════════════════════════════════════════
    // WATCH HISTORY — Lịch sử xem
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet("history")]
    [Authorize]
    public async Task<IActionResult> GetWatchHistory()
    {
        var history = await _movieService.GetWatchHistoryAsync(GetUserId());
        return Ok(new ApiResponseDTO<object> { Data = history, Message = "Thành công" });
    }

    [HttpPost("history")]
    [Authorize]
    public async Task<IActionResult> UpdateWatchProgress([FromBody] UpdateWatchProgressDTO dto)
    {
        await _movieService.UpdateWatchProgressAsync(
            GetUserId(), dto.MovieId, dto.ProgressMinutes, dto.IsCompleted);

        return Ok(new ApiResponseDTO<object> { Message = "Đã cập nhật tiến trình xem" });
    }

    [HttpDelete("history/{historyId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteWatchHistory(Guid historyId)
    {
        var success = await _movieService.DeleteWatchHistoryAsync(GetUserId(), historyId);
        if (!success) return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy lịch sử xem", StatusCode = 404 });
        return Ok(new ApiResponseDTO<object> { Message = "Đã xóa lịch sử xem" });
    }

    [HttpDelete("history")]
    [Authorize]
    public async Task<IActionResult> ClearWatchHistory()
    {
        await _movieService.ClearWatchHistoryAsync(GetUserId());
        return Ok(new ApiResponseDTO<object> { Message = "Đã xóa toàn bộ lịch sử xem" });
    }

    // ─── Helper ──────────────────────────────────────────────────────────────

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? Guid.Empty.ToString());
}