// UIAMovie.API/Controllers/RatingReviewController.cs

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;
using UIAMovie.Domain.Constants;

namespace UIAMovie.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RatingReviewController : ControllerBase
{
    private readonly IRatingReviewService _ratingReviewService;
    private readonly IMovieService        _movieService;
    private readonly ITvShowService       _tvShowService;

    public RatingReviewController(
        IRatingReviewService ratingReviewService,
        IMovieService        movieService,
        ITvShowService       tvShowService)
    {
        _ratingReviewService = ratingReviewService;
        _movieService        = movieService;
        _tvShowService       = tvShowService;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PUBLIC — Toàn hệ thống
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Lấy tất cả reviews (Movie + TvShow + Episode, phân trang) — homepage carousel</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponseDTO<AllReviewsResponseDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllReviews(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize   = 50)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize   < 1 || pageSize > 200) pageSize = 50;

        var result = await _ratingReviewService.GetAllReviewsAsync(pageNumber, pageSize);
        return Ok(new ApiResponseDTO<AllReviewsResponseDTO> { Data = result, Message = "Lấy danh sách reviews thành công" });
    }

    /// <summary>Lấy chi tiết một review</summary>
    [HttpGet("{reviewId:guid}")]
    [ProducesResponseType(typeof(ApiResponseDTO<ReviewDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviewById([FromRoute] Guid reviewId)
    {
        var review = await _ratingReviewService.GetReviewByIdAsync(reviewId);
        if (review == null)
            return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy review", StatusCode = 404 });

        return Ok(new ApiResponseDTO<ReviewDTO> { Data = review });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PUBLIC — Movie reviews
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Danh sách reviews của phim (phân trang)</summary>
    [HttpGet("movies/{movieId:guid}")]
    [ProducesResponseType(typeof(ApiResponseDTO<MovieReviewsResponseDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMovieReviews(
        [FromRoute] Guid movieId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize   = 20)
    {
        Clamp(ref pageNumber, ref pageSize, 100);

        var movie = await _movieService.GetMovieByIdAsync(movieId);
        if (movie == null)
            return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy phim", StatusCode = 404 });

        var reviews = await _ratingReviewService.GetMovieReviewsAsync(movieId, pageNumber, pageSize);
        return Ok(new ApiResponseDTO<MovieReviewsResponseDTO>
        {
            Data = new MovieReviewsResponseDTO { MovieId = movieId, MovieTitle = movie.Title, Reviews = reviews.ToList() }
        });
    }

    /// <summary>Thống kê rating của phim</summary>
    [HttpGet("movies/{movieId:guid}/stats")]
    [ProducesResponseType(typeof(ApiResponseDTO<MovieRatingStatsDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMovieRatingStats([FromRoute] Guid movieId)
    {
        var stats = await _ratingReviewService.GetMovieRatingStatsAsync(movieId);
        if (stats == null)
            return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy phim", StatusCode = 404 });

        return Ok(new ApiResponseDTO<MovieRatingStatsDTO> { Data = stats });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PUBLIC — TvShow reviews (cấp show)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Danh sách reviews của TV show (cấp show, không kèm episode reviews)</summary>
    [HttpGet("tvshows/{tvShowId:guid}")]
    [ProducesResponseType(typeof(ApiResponseDTO<TvShowReviewsResponseDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTvShowReviews(
        [FromRoute] Guid tvShowId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize   = 20)
    {
        Clamp(ref pageNumber, ref pageSize, 100);

        var tvShow = await _tvShowService.GetTvShowByIdAsync(tvShowId);
        if (tvShow == null)
            return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy TV show", StatusCode = 404 });

        var reviews = await _ratingReviewService.GetTvShowReviewsAsync(tvShowId, pageNumber, pageSize);
        return Ok(new ApiResponseDTO<TvShowReviewsResponseDTO>
        {
            Data = new TvShowReviewsResponseDTO { TvShowId = tvShowId, TvShowTitle = tvShow.Title, Reviews = reviews.ToList() }
        });
    }

    /// <summary>Thống kê rating của TV show (cấp show)</summary>
    [HttpGet("tvshows/{tvShowId:guid}/stats")]
    [ProducesResponseType(typeof(ApiResponseDTO<TvShowRatingStatsDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTvShowRatingStats([FromRoute] Guid tvShowId)
    {
        var stats = await _ratingReviewService.GetTvShowRatingStatsAsync(tvShowId);
        if (stats == null)
            return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy TV show", StatusCode = 404 });

        return Ok(new ApiResponseDTO<TvShowRatingStatsDTO> { Data = stats });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PUBLIC — Episode reviews
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Danh sách reviews của một tập phim (phân trang)</summary>
    [HttpGet("episodes/{episodeId:guid}")]
    [ProducesResponseType(typeof(ApiResponseDTO<EpisodeReviewsResponseDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEpisodeReviews(
        [FromRoute] Guid episodeId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize   = 20)
    {
        Clamp(ref pageNumber, ref pageSize, 100);

        var stats = await _ratingReviewService.GetEpisodeRatingStatsAsync(episodeId);
        if (stats == null)
            return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy tập phim", StatusCode = 404 });

        var reviews = await _ratingReviewService.GetEpisodeReviewsAsync(episodeId, pageNumber, pageSize);
        return Ok(new ApiResponseDTO<EpisodeReviewsResponseDTO>
        {
            Data = new EpisodeReviewsResponseDTO
            {
                EpisodeId    = episodeId,
                TvShowId     = stats.TvShowId,
                EpisodeLabel = $"Episode {episodeId}",   // caller biết số tập từ TvShow data
                Reviews      = reviews.ToList()
            }
        });
    }

    /// <summary>Thống kê rating của một tập phim</summary>
    [HttpGet("episodes/{episodeId:guid}/stats")]
    [ProducesResponseType(typeof(ApiResponseDTO<EpisodeRatingStatsDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEpisodeRatingStats([FromRoute] Guid episodeId)
    {
        var stats = await _ratingReviewService.GetEpisodeRatingStatsAsync(episodeId);
        if (stats == null)
            return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy tập phim", StatusCode = 404 });

        return Ok(new ApiResponseDTO<EpisodeRatingStatsDTO> { Data = stats });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AUTHENTICATED — Tạo / Sửa / Xóa (dùng chung Movie, TvShow, Episode)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tạo rating/review.
    /// Body (chỉ 1 trong 3 tổ hợp):
    ///   { "movieId": "..." }                         → review phim
    ///   { "tvShowId": "..." }                        → review cả show
    ///   { "tvShowId": "...", "episodeId": "..." }    → review từng tập
    /// </summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponseDTO<CreateReviewResponseDTO>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRatingReview([FromBody] RatingReviewDTO dto)
    {
        if (dto == null)
            return BadRequest(new ApiErrorResponseDTO { Message = "Dữ liệu không hợp lệ", StatusCode = 400 });

        var validation = ValidateDto(dto);
        if (validation != null) return validation;

        try
        {
            var reviewId = await _ratingReviewService.CreateRatingReviewAsync(GetUserId(), dto);
            return CreatedAtAction(nameof(GetReviewById), new { reviewId },
                new ApiResponseDTO<CreateReviewResponseDTO>
                {
                    Data    = new CreateReviewResponseDTO { ReviewId = reviewId },
                    Message = "Review đã được tạo thành công"
                });
        }
        catch (InvalidOperationException ex) { return BadRequest(new ApiErrorResponseDTO { Message = ex.Message, StatusCode = 400 }); }
        catch (ArgumentException ex)         { return BadRequest(new ApiErrorResponseDTO { Message = ex.Message, StatusCode = 400 }); }
    }

    /// <summary>Cập nhật review của mình (chỉ cần rating/reviewText/isSpoiler)</summary>
    [HttpPut("{reviewId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponseDTO<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateRatingReview(
        [FromRoute] Guid reviewId,
        [FromBody]  RatingReviewDTO dto)
    {
        if (dto == null)
            return BadRequest(new ApiErrorResponseDTO { Message = "Dữ liệu không hợp lệ", StatusCode = 400 });

        if (dto.Rating < 1 || dto.Rating > 10)
            return BadRequest(new ApiErrorResponseDTO { Message = "Đánh giá phải từ 1 đến 10 sao", StatusCode = 400 });

        if (!string.IsNullOrEmpty(dto.ReviewText) && dto.ReviewText.Length > 5000)
            return BadRequest(new ApiErrorResponseDTO { Message = "Review không được vượt quá 5000 ký tự", StatusCode = 400 });

        try
        {
            var success = await _ratingReviewService.UpdateRatingReviewAsync(reviewId, GetUserId(), dto);
            if (!success)
                return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy review", StatusCode = 404 });

            return Ok(new ApiResponseDTO<object> { Data = null, Message = "Cập nhật review thành công" });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException ex)        { return BadRequest(new ApiErrorResponseDTO { Message = ex.Message, StatusCode = 400 }); }
    }

    /// <summary>Xóa review của mình</summary>
    [HttpDelete("{reviewId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponseDTO<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteRatingReview([FromRoute] Guid reviewId)
    {
        try
        {
            var success = await _ratingReviewService.DeleteRatingReviewAsync(reviewId, GetUserId());
            if (!success)
                return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy review", StatusCode = 404 });

            return Ok(new ApiResponseDTO<object> { Data = null, Message = "Xóa review thành công" });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AUTHENTICATED — User's own reviews & check
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Lấy tất cả reviews của user hiện tại (Movie + TvShow + Episode)</summary>
    [HttpGet("my")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponseDTO<UserReviewsResponseDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyReviews()
    {
        var reviews = await _ratingReviewService.GetUserReviewsAsync(GetUserId());
        return Ok(new ApiResponseDTO<UserReviewsResponseDTO>
        {
            Data = new UserReviewsResponseDTO { TotalReviews = reviews.Count(), Reviews = reviews.ToList() }
        });
    }

    /// <summary>Kiểm tra user đã review phim chưa</summary>
    [HttpGet("check/movies/{movieId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponseDTO<CheckReviewResponseDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckUserMovieReview([FromRoute] Guid movieId)
        => await CheckAndReturn(
            await _ratingReviewService.CheckUserHasReviewAsync(GetUserId(), movieId),
            () => _ratingReviewService.GetUserReviewForMovieAsync(GetUserId(), movieId));

    /// <summary>Kiểm tra user đã review TV show (cấp show) chưa</summary>
    [HttpGet("check/tvshows/{tvShowId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponseDTO<CheckReviewResponseDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckUserTvShowReview([FromRoute] Guid tvShowId)
        => await CheckAndReturn(
            await _ratingReviewService.CheckUserHasReviewForTvShowAsync(GetUserId(), tvShowId),
            () => _ratingReviewService.GetUserReviewForTvShowAsync(GetUserId(), tvShowId));

    /// <summary>Kiểm tra user đã review tập phim này chưa</summary>
    [HttpGet("check/episodes/{episodeId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponseDTO<CheckReviewResponseDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckUserEpisodeReview([FromRoute] Guid episodeId)
        => await CheckAndReturn(
            await _ratingReviewService.CheckUserHasReviewForEpisodeAsync(GetUserId(), episodeId),
            () => _ratingReviewService.GetUserReviewForEpisodeAsync(GetUserId(), episodeId));

    // ═══════════════════════════════════════════════════════════════════════════
    // ADMIN
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>[Admin] Xóa review vi phạm</summary>
    [HttpDelete("admin/{reviewId:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(ApiResponseDTO<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AdminDeleteReview([FromRoute] Guid reviewId)
    {
        var review = await _ratingReviewService.GetReviewByIdAsync(reviewId);
        if (review == null)
            return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy review", StatusCode = 404 });

        var success = await _ratingReviewService.DeleteRatingReviewAsync(reviewId, review.UserId);
        if (!success)
            return BadRequest(new ApiErrorResponseDTO { Message = "Không thể xóa review", StatusCode = 400 });

        return Ok(new ApiResponseDTO<object> { Data = null, Message = "Review đã bị xóa bởi admin" });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private Guid GetUserId()
    {
        var v = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrEmpty(v) ? Guid.Empty : Guid.Parse(v);
    }

    private static void Clamp(ref int pageNumber, ref int pageSize, int maxSize)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1 || pageSize > maxSize) pageSize = 20;
    }

    private IActionResult? ValidateDto(RatingReviewDTO dto)
    {
        if (dto.MovieId == null && dto.TvShowId == null)
            return BadRequest(new ApiErrorResponseDTO { Message = "Phải cung cấp movieId hoặc tvShowId", StatusCode = 400 });

        if (dto.MovieId != null && dto.TvShowId != null)
            return BadRequest(new ApiErrorResponseDTO { Message = "Chỉ được review cho 1 đối tượng", StatusCode = 400 });

        if (dto.EpisodeId != null && dto.TvShowId == null)
            return BadRequest(new ApiErrorResponseDTO { Message = "Phải cung cấp tvShowId khi review theo tập", StatusCode = 400 });

        if (dto.Rating < 1 || dto.Rating > 10)
            return BadRequest(new ApiErrorResponseDTO { Message = "Đánh giá phải từ 1 đến 10 sao", StatusCode = 400 });

        if (!string.IsNullOrEmpty(dto.ReviewText) && dto.ReviewText.Length > 5000)
            return BadRequest(new ApiErrorResponseDTO { Message = "Review không được vượt quá 5000 ký tự", StatusCode = 400 });

        return null;
    }

    private async Task<IActionResult> CheckAndReturn(bool hasReview, Func<Task<ReviewDTO?>> getReview)
    {
        var review = hasReview ? await getReview() : null;
        return Ok(new ApiResponseDTO<CheckReviewResponseDTO>
        {
            Data = new CheckReviewResponseDTO { HasReview = hasReview, Review = review }
        });
    }
}