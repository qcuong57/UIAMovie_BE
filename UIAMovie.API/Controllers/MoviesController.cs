// UIAMovie.API/Controllers/MoviesController.cs
// THÊM:
//   [1] GET /api/movies/{id}          → gắn ContentAccessDTO vào response nếu user đã login
//   [2] GET /api/movies/{id}/watch    → Premium gate — trả video URL chỉ khi user có quyền
//   [3] PATCH /api/movies/{id}/premium → Admin toggle IsPremium của phim

using System.IO;
using System.Linq;
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
    private readonly IMovieService _movieService;
    private readonly ITmdbService _tmdbService;
    private readonly ICloudinaryService _cloudinaryService;
    private readonly IGenreService _genreService;
    private readonly ISubscriptionChecker _subscriptionChecker;

    public MoviesController(
        IMovieService movieService,
        ITmdbService tmdbService,
        ICloudinaryService cloudinaryService,
        IGenreService genreService,
        ISubscriptionChecker subscriptionChecker)
    {
        _movieService = movieService;
        _tmdbService = tmdbService;
        _cloudinaryService = cloudinaryService;
        _genreService = genreService;
        _subscriptionChecker = subscriptionChecker;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PUBLIC — Không cần đăng nhập
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet("country/{country}")]
    public async Task<IActionResult> GetByCountry(string country)
    {
        if (string.IsNullOrWhiteSpace(country))
            return BadRequest(new ApiErrorResponseDTO
                { Message = "Mã quốc gia không được để trống", StatusCode = 400 });

        var filter = new FilterMoviesDTO
        {
            OriginCountry = country.Trim(),
            PageSize = 50,
            SortBy = "rating",
            SortDesc = true
        };
        var result = await _movieService.GetMoviesAsync(filter);
        return Ok(new ApiResponseDTO<object> { Data = result, Message = "Thành công" });
    }

    [HttpGet("countries")]
    public async Task<IActionResult> GetAvailableCountries()
    {
        var countries = await _movieService.GetAvailableCountriesAsync();
        return Ok(new ApiResponseDTO<List<string>> { Data = countries.ToList(), Message = "Thành công" });
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

    /// <summary>
    /// Danh sách thể loại có sẵn trong DB nội bộ (khác /tmdb/genres — cái đó lấy từ TMDB).
    /// FE dùng để hiển thị ô chọn thể loại khi thêm/sửa phim thủ công.
    /// GET /api/movies/genres
    /// </summary>
    [HttpGet("genres")]
    public async Task<IActionResult> GetGenres()
    {
        var genres = await _genreService.GetAllAsync();
        return Ok(new ApiResponseDTO<object> { Data = genres, Message = "Thành công" });
    }

    [HttpGet("genre/{genreId:guid}")]
    public async Task<IActionResult> GetByGenre(Guid genreId)
    {
        var movies = await _movieService.GetMoviesByGenreAsync(genreId);
        return Ok(new ApiResponseDTO<object> { Data = movies, Message = "Thành công" });
    }

    /// <summary>
    /// Lấy chi tiết phim.
    /// Nếu user đã đăng nhập → gắn thêm Access (ContentAccessDTO) vào response
    /// để frontend biết có thể xem không mà không cần gọi thêm API.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var movie = await _movieService.GetMovieByIdAsync(id);
        if (movie == null)
            return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy phim", StatusCode = 404 });

        // Gắn ContentAccessDTO nếu user đã đăng nhập
        var userId = TryGetUserId();
        if (userId.HasValue)
        {
            movie.Access = await BuildContentAccessAsync(movie, userId.Value);
        }
        else if (movie.IsPremium)
        {
            // User chưa đăng nhập nhưng phim là Premium
            movie.Access = new ContentAccessDTO
            {
                CanWatch = false,
                RequiresPremium = true,
                BlockReason = "Đăng nhập và nâng cấp Premium để xem phim này"
            };
        }

        return Ok(new ApiResponseDTO<object> { Data = movie, Message = "Thành công" });
    }

    // ═══════════════════════════════════════════════════════════════════
    // WATCH — Premium gate
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Endpoint để frontend lấy danh sách video URL có thể phát.
    ///
    /// Logic:
    ///   - Phim FREE       → trả về videos cho mọi user (kể cả chưa đăng nhập)
    ///   - Phim PREMIUM    → phải đăng nhập + có Premium hợp lệ
    ///
    /// GET /api/movies/{id}/watch
    /// Response: { videos: [...], canWatch: true }
    ///
    /// Tại sao tách endpoint riêng thay vì ẩn URL trong GetById?
    ///   - GetById vẫn trả về videos (để trailer public xem được).
    ///   - /watch là "playback gate" — chỉ trả về stream URL khi đủ quyền.
    ///   - Dễ add logging (tracking lượt xem) mà không ảnh hưởng GetById.
    /// </summary>
    [HttpGet("{id:guid}/watch")]
    public async Task<IActionResult> WatchMovie(Guid id)
    {
        var movie = await _movieService.GetMovieByIdAsync(id);
        if (movie == null)
            return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy phim", StatusCode = 404 });

        // Phim FREE → ai cũng xem được
        if (!movie.IsPremium)
        {
            return Ok(new ApiResponseDTO<object>
            {
                Data = new
                {
                    canWatch = true,
                    videos = movie.Videos
                },
                Message = "Thành công"
            });
        }

        // Phim PREMIUM → cần đăng nhập
        var userId = TryGetUserId();
        if (!userId.HasValue)
            return Unauthorized(new ApiErrorResponseDTO
            {
                Message = "Vui lòng đăng nhập để xem phim Premium",
                StatusCode = 401
            });

        // Kiểm tra tài khoản active + subscription còn hạn
        var canWatch = await _subscriptionChecker.CanWatchPremiumContentAsync(userId.Value);
        if (!canWatch)
        {
            // Phân biệt bị ban vs hết hạn để frontend hiển thị thông báo đúng
            var isPremium = await _subscriptionChecker.IsPremiumAsync(userId.Value);
            var reason = isPremium
                ? "Tài khoản của bạn đã bị khóa"
                : "Nâng cấp lên Premium để xem phim này";

            return StatusCode(403, new ApiErrorResponseDTO { Message = reason, StatusCode = 403 });
        }

        // Ghi lại lượt xem (async fire-and-forget, không block response)
        _ = _movieService.UpdateWatchProgressAsync(userId.Value, id, 0, false);

        return Ok(new ApiResponseDTO<object>
        {
            Data = new
            {
                canWatch = true,
                videos = movie.Videos
            },
            Message = "Thành công"
        });
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

    /// <summary>
    /// Tìm diễn viên/đạo diễn (Person) có sẵn trong DB theo tên — dùng cho ô autocomplete
    /// khi thêm phim thủ công hoặc chỉnh sửa cast của phim import từ TMDB.
    /// Yêu cầu tối thiểu 2 ký tự.
    /// </summary>
    [HttpGet("persons/search")]
    public async Task<IActionResult> SearchPersons([FromQuery] string query)
    {
        var persons = await _movieService.SearchPersonsAsync(query ?? string.Empty);
        return Ok(new ApiResponseDTO<object> { Data = persons, Message = "Thành công" });
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
            return Conflict(new ApiErrorResponseDTO
                { Message = $"Phim này đã được import rồi (ID: {existing.Id})", StatusCode = 409 });

        var full = await _tmdbService.GetFullMovieAsync(tmdbId);
        if (full == null)
            return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy phim trên TMDB", StatusCode = 404 });

        var genreIds = await _genreService.ResolveGenreIdsFromTmdbAsync(
            full.Detail.Genres.Select(g => g.Id));

        var dto = new CreateMovieDTO
        {
            TmdbId = full.Detail.Id,
            Title = full.Detail.Title,
            Description = string.IsNullOrEmpty(full.Detail.Overview) ? full.Detail.Title : full.Detail.Overview,
            ReleaseDate = DateTime.TryParse(full.Detail.ReleaseDate, out var d)
                ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
                : null,
            PosterUrl = full.Detail.PosterUrl,
            BackdropUrl = full.Detail.BackdropUrl,
            Duration = full.Detail.Runtime,
            ImdbRating = (decimal?)full.Detail.VoteAverage,
            ContentRating = null,
            OriginCountry = full.Detail.OriginCountry.FirstOrDefault(),
            IsPremium = false, // Import mặc định Free — Admin tự set Premium sau
            GenreIds = genreIds,

            Cast = full.Cast.Take(10).Select(c => new ImportCastDTO
            {
                TmdbPersonId = c.Id,
                Name = c.Name,
                Character = c.Character,
                Order = c.Order,
                ProfileUrl = c.ProfileUrl,
                Biography = full.PersonDetails.TryGetValue(c.Id, out var pd) ? pd?.Biography : null,
                Birthday = full.PersonDetails.TryGetValue(c.Id, out var pd2) ? pd2?.Birthday : null,
                PlaceOfBirth = full.PersonDetails.TryGetValue(c.Id, out var pd3) ? pd3?.PlaceOfBirth : null,
                ProfileImages = full.PersonImages.TryGetValue(c.Id, out var imgs) ? imgs.ToList() : new()
            }).ToList(),

            Director = full.Director == null
                ? null
                : new ImportDirectorDTO
                {
                    TmdbPersonId = full.Director.Id,
                    Name = full.Director.Name,
                    ProfileUrl = full.Director.ProfileUrl,
                    Biography = full.PersonDetails.TryGetValue(full.Director.Id, out var dpd) ? dpd?.Biography : null,
                    Birthday = full.PersonDetails.TryGetValue(full.Director.Id, out var dpd2) ? dpd2?.Birthday : null,
                    PlaceOfBirth = full.PersonDetails.TryGetValue(full.Director.Id, out var dpd3)
                        ? dpd3?.PlaceOfBirth
                        : null,
                    ProfileImages = full.PersonImages.TryGetValue(full.Director.Id, out var dimgs)
                        ? dimgs.ToList()
                        : new()
                },

            Images = full.Backdrops.Select(i => new ImportImageDTO { Url = i.Url!, ImageType = "backdrop" })
                .Concat(full.Posters.Select(i => new ImportImageDTO { Url = i.Url!, ImageType = "poster" }))
                .ToList(),

            Trailers = full.Trailers
                .Select(t => new ImportTrailerDTO { YoutubeUrl = t.YoutubeUrl, Name = t.Name })
                .ToList()
        };

        var movieId = await _movieService.CreateMovieAsync(dto);

        return CreatedAtAction(nameof(GetById), new { id = movieId }, new ApiResponseDTO<object>
        {
            Data = new
            {
                movieId,
                genreCount = genreIds.Count,
                castCount = dto.Cast.Count,
                imageCount = dto.Images.Count,
                hasDirector = dto.Director != null,
                personBioCount = full.PersonDetails.Count(kv => !string.IsNullOrEmpty(kv.Value?.Biography)),
                personImageCount = full.PersonImages.Count(kv => kv.Value.Any())
            },
            Message = "Import thành công"
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // ADMIN — Thêm phim thủ công (không qua TMDB)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Upload 1 ảnh (poster / backdrop / ảnh diễn viên-đạo diễn) lên Cloudinary và trả về URL.
    /// Dùng cho luồng "thêm phim thủ công": admin có thể upload file HOẶC dán URL có sẵn
    /// thẳng vào CreateMovieDTO — cả 2 cách đều ra 1 chuỗi URL như nhau, endpoint này chỉ là
    /// bước phụ để lấy URL khi admin không có sẵn link ảnh.
    ///
    /// POST /api/movies/upload-image
    /// Form-data: file (bắt buộc), type ("poster" | "backdrop" | "person", mặc định "poster")
    /// Response: { url }
    /// </summary>
    // FIX: whitelist content-type + extension cho upload-image — trước đây chỉ giới hạn
    // size, không check file có phải ảnh không → có thể upload file bất kỳ lên Cloudinary
    // dưới vỏ bọc "ảnh poster". Đây là check ở tầng ứng dụng (đọc header do client gửi,
    // có thể bị giả mạo) — không phải xác thực magic-byte thật sự của file. Nếu cần chặt
    // hơn (chống spoof Content-Type/extension), nên đọc vài byte đầu file để so signature
    // thật (VD FF D8 FF cho JPEG, 89 50 4E 47 cho PNG) trước khi upload lên Cloudinary.
    private static readonly string[] AllowedImageExtensions   = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private static readonly string[] AllowedImageContentTypes =
        { "image/jpeg", "image/png", "image/webp", "image/gif" };

    [HttpPost("upload-image")]
    [Authorize(Roles = Roles.Admin)]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB — đủ cho ảnh poster/backdrop/avatar
    public async Task<IActionResult> UploadImage(IFormFile file, [FromForm] string type = "poster")
    {
        if (file == null || file.Length == 0)
            return BadRequest(new ApiErrorResponseDTO { Message = "File không hợp lệ", StatusCode = 400 });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var contentType = (file.ContentType ?? "").ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(ext) || !AllowedImageContentTypes.Contains(contentType))
            return BadRequest(new ApiErrorResponseDTO
                { Message = "Chỉ chấp nhận file ảnh (jpg, png, webp, gif)", StatusCode = 400 });

        var allowedTypes = new[] { "poster", "backdrop", "person" };
        var folder = allowedTypes.Contains(type) ? type : "poster";

        var url = await _cloudinaryService.UploadImageAsync(file, $"uiamovie/movies/{folder}");

        return Ok(new ApiResponseDTO<object> { Data = new { url }, Message = "Upload ảnh thành công" });
    }

    // ═══════════════════════════════════════════════════════════════════
    // ADMIN — CRUD phim
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tạo phim — dùng chung cho cả 2 luồng:
    ///   - Thêm thủ công: FE tự nhập Title/Description/PosterUrl(đã upload hoặc dán URL)/Cast(TmdbPersonId=null)...
    ///   - (TMDB import thì đi qua action ImportFromTmdb ở trên, action đó tự build DTO rồi gọi CreateMovieAsync)
    /// TmdbId để null khi tạo thủ công — entity Movie đã hỗ trợ sẵn (TmdbId là int? nullable).
    /// </summary>
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

    /// <summary>
    /// [Admin] Bật/tắt Premium cho phim nhanh mà không cần UpdateMovieDTO đầy đủ.
    /// PATCH /api/movies/{id}/premium
    /// Body: { "isPremium": true }
    /// </summary>
    [HttpPatch("{id:guid}/premium")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SetPremium(Guid id, [FromBody] SetMoviePremiumDTO dto)
    {
        var success = await _movieService.UpdateMovieAsync(id, new UpdateMovieDTO { IsPremium = dto.IsPremium });
        return success
            ? Ok(new ApiResponseDTO<object>
            {
                Message = dto.IsPremium
                    ? "Đã đặt phim thành Premium"
                    : "Đã chuyển phim về Free"
            })
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
    [RequestSizeLimit(500 * 1024 * 1024)] // 500MB
    [RequestFormLimits(MultipartBodyLengthLimit = 500 * 1024 * 1024)]
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
    // TRAILER — 2 nguồn chạy song song: Youtube (VideoType="trailer")
    // và video tự upload (VideoType="trailer_upload"). Set/xóa loại này
    // không ảnh hưởng loại kia vì khác VideoType.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Upload file trailer video lên Cloudinary (song song với trailer Youtube).
    /// POST /api/movies/{id}/trailer/upload  (multipart/form-data, field "trailerFile")
    /// </summary>
    [HttpPost("{id:guid}/trailer/upload")]
    [Authorize(Roles = Roles.Admin)]
    [RequestSizeLimit(200 * 1024 * 1024)] // 200MB
    [RequestFormLimits(MultipartBodyLengthLimit = 200 * 1024 * 1024)]
    public async Task<IActionResult> UploadTrailerVideo(Guid id, IFormFile trailerFile)
    {
        if (trailerFile == null || trailerFile.Length == 0)
            return BadRequest(new ApiErrorResponseDTO { Message = "File không hợp lệ", StatusCode = 400 });

        var url = await _cloudinaryService.UploadVideoAsync(trailerFile, $"uiamovie/movies/{id}/trailer");
        var success = await _movieService.AddVideoAsync(id, url, "trailer_upload", quality: null);

        return success
            ? Ok(new ApiResponseDTO<object> { Data = new { trailerVideoUrl = url }, Message = "Upload trailer thành công" })
            : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy phim", StatusCode = 404 });
    }

    /// <summary>
    /// Set/đổi link trailer Youtube thủ công (không cần import lại từ TMDB).
    /// PUT /api/movies/{id}/trailer/youtube
    /// Body: { "youtubeUrl": "https://www.youtube.com/watch?v=..." }
    /// </summary>
    [HttpPut("{id:guid}/trailer/youtube")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SetTrailerYoutube(Guid id, [FromBody] SetTrailerYoutubeDTO dto)
    {
        if (string.IsNullOrWhiteSpace(dto.YoutubeUrl))
            return BadRequest(new ApiErrorResponseDTO { Message = "URL không được để trống", StatusCode = 400 });

        var success = await _movieService.SetTrailerYoutubeAsync(id, dto.YoutubeUrl);
        return success
            ? Ok(new ApiResponseDTO<object> { Message = "Đã cập nhật trailer Youtube" })
            : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy phim", StatusCode = 404 });
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
            : BadRequest(new ApiErrorResponseDTO
                { Message = "Phim đã có trong danh sách yêu thích", StatusCode = 400 });
    }

    [HttpDelete("favorites/{movieId:guid}")]
    [Authorize]
    public async Task<IActionResult> RemoveFavorite(Guid movieId)
    {
        var success = await _movieService.RemoveFavoriteAsync(GetUserId(), movieId);
        return success
            ? Ok(new ApiResponseDTO<object> { Message = "Đã xóa khỏi yêu thích" })
            : NotFound(new ApiErrorResponseDTO
                { Message = "Không tìm thấy trong danh sách yêu thích", StatusCode = 404 });
    }

    // ═══════════════════════════════════════════════════════════════════
    // SEARCH
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet("search/actor")]
    public async Task<IActionResult> SearchByActor([FromQuery] string actorName)
    {
        if (string.IsNullOrWhiteSpace(actorName))
            return BadRequest(new ApiErrorResponseDTO
                { Message = "Tên diễn viên không được để trống", StatusCode = 400 });

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
        if (!success)
            return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy lịch sử xem", StatusCode = 404 });

        return Ok(new ApiResponseDTO<object> { Message = "Đã xóa lịch sử xem" });
    }

    [HttpDelete("history")]
    [Authorize]
    public async Task<IActionResult> ClearWatchHistory()
    {
        await _movieService.ClearWatchHistoryAsync(GetUserId());
        return Ok(new ApiResponseDTO<object> { Message = "Đã xóa toàn bộ lịch sử xem" });
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Build ContentAccessDTO dựa trên IsPremium của phim và subscription của user.</summary>
    private async Task<ContentAccessDTO> BuildContentAccessAsync(MovieDTO movie, Guid userId)
    {
        if (!movie.IsPremium)
            return new ContentAccessDTO { CanWatch = true, RequiresPremium = false };

        var canWatch = await _subscriptionChecker.CanWatchPremiumContentAsync(userId);
        return new ContentAccessDTO
        {
            CanWatch = canWatch,
            RequiresPremium = true,
            BlockReason = canWatch ? null : "Nâng cấp lên Premium để xem phim này"
        };
    }

    /// <summary>Lấy userId từ JWT — null nếu chưa đăng nhập.</summary>
    private Guid? TryGetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirstValue("sub");

        return Guid.TryParse(claim, out var id) ? id : null;
    }

    /// <summary>Lấy userId từ JWT — throw nếu chưa đăng nhập (dùng cho các endpoint [Authorize]).</summary>
    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? Guid.Empty.ToString());
}

// ─── Helper DTO (nhỏ, đặt cùng file cho tiện) ────────────────────────────────

/// <summary>Body cho PATCH /api/movies/{id}/premium</summary>
public class SetMoviePremiumDTO
{
    public bool IsPremium { get; set; }
}