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
    private readonly IMovieService _movieService;

    public RatingReviewController(
        IRatingReviewService ratingReviewService,
        IMovieService movieService)
    {
        _ratingReviewService = ratingReviewService;
        _movieService        = movieService;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PUBLIC ENDPOINTS — Không cần đăng nhập
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Lấy tất cả reviews (toàn hệ thống, có phân trang) — dùng cho homepage carousel</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponseDTO<AllReviewsResponseDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllReviews(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize   = 50)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var result = await _ratingReviewService.GetAllReviewsAsync(pageNumber, pageSize);

        return Ok(new ApiResponseDTO<AllReviewsResponseDTO>
        {
            Data    = result,
            Message = "Lấy danh sách reviews thành công"
        });
    }

    /// <summary>Lấy tất cả reviews của phim (có phân trang)</summary>
    [HttpGet("movies/{movieId:guid}")]
    [ProducesResponseType(typeof(ApiResponseDTO<MovieReviewsResponseDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMovieReviews(
        [FromRoute] Guid movieId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize   = 20)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var movie = await _movieService.GetMovieByIdAsync(movieId);
        if (movie == null)
            return NotFound(new ApiErrorResponseDTO
            {
                Message    = "Không tìm thấy phim",
                StatusCode = 404
            });

        var reviews = await _ratingReviewService.GetMovieReviewsAsync(movieId, pageNumber, pageSize);

        return Ok(new ApiResponseDTO<MovieReviewsResponseDTO>
        {
            Data = new MovieReviewsResponseDTO
            {
                MovieId    = movieId,
                MovieTitle = movie.Title,
                Reviews    = reviews.ToList()
            }
        });
    }

    /// <summary>Lấy thống kê rating của phim</summary>
    [HttpGet("movies/{movieId:guid}/stats")]
    [ProducesResponseType(typeof(ApiResponseDTO<MovieRatingStatsDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMovieRatingStats([FromRoute] Guid movieId)
    {
        var stats = await _ratingReviewService.GetMovieRatingStatsAsync(movieId);
        if (stats == null)
            return NotFound(new ApiErrorResponseDTO
            {
                Message    = "Không tìm thấy phim",
                StatusCode = 404
            });

        return Ok(new ApiResponseDTO<MovieRatingStatsDTO> { Data = stats });
    }

    /// <summary>Lấy chi tiết một review</summary>
    [HttpGet("{reviewId:guid}")]
    [ProducesResponseType(typeof(ApiResponseDTO<ReviewDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviewById([FromRoute] Guid reviewId)
    {
        var review = await _ratingReviewService.GetReviewByIdAsync(reviewId);
        if (review == null)
            return NotFound(new ApiErrorResponseDTO
            {
                Message    = "Không tìm thấy review",
                StatusCode = 404
            });

        return Ok(new ApiResponseDTO<ReviewDTO> { Data = review });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AUTHENTICATED ENDPOINTS — Cần đăng nhập
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Tạo rating/review cho phim</summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponseDTO<CreateReviewResponseDTO>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponseDTO), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRatingReview([FromBody] RatingReviewDTO dto)
    {
        if (dto == null)
            return BadRequest(new ApiErrorResponseDTO { Message = "Dữ liệu không hợp lệ", StatusCode = 400 });

        if (dto.Rating < 1 || dto.Rating > 10)
            return BadRequest(new ApiErrorResponseDTO { Message = "Đánh giá phải từ 1 đến 10 sao", StatusCode = 400 });

        if (!string.IsNullOrEmpty(dto.ReviewText) && dto.ReviewText.Length > 5000)
            return BadRequest(new ApiErrorResponseDTO { Message = "Review không được vượt quá 5000 ký tự", StatusCode = 400 });

        var userId = GetUserId();

        try
        {
            var reviewId = await _ratingReviewService.CreateRatingReviewAsync(userId, dto);
            var response = new ApiResponseDTO<CreateReviewResponseDTO>
            {
                Data    = new CreateReviewResponseDTO { ReviewId = reviewId },
                Message = "Review đã được tạo thành công"
            };
            return CreatedAtAction(nameof(GetReviewById), new { reviewId }, response);
        }
        catch (InvalidOperationException ex) { return BadRequest(new ApiErrorResponseDTO { Message = ex.Message, StatusCode = 400 }); }
        catch (ArgumentException ex)         { return BadRequest(new ApiErrorResponseDTO { Message = ex.Message, StatusCode = 400 }); }
    }

    /// <summary>Cập nhật review của mình</summary>
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

        var userId = GetUserId();

        try
        {
            var success = await _ratingReviewService.UpdateRatingReviewAsync(reviewId, userId, dto);
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
        var userId = GetUserId();

        try
        {
            var success = await _ratingReviewService.DeleteRatingReviewAsync(reviewId, userId);
            if (!success)
                return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy review", StatusCode = 404 });

            return Ok(new ApiResponseDTO<object> { Data = null, Message = "Xóa review thành công" });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    /// <summary>Lấy tất cả reviews của user hiện tại</summary>
    [HttpGet("my")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponseDTO<UserReviewsResponseDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyReviews()
    {
        var userId  = GetUserId();
        var reviews = await _ratingReviewService.GetUserReviewsAsync(userId);

        return Ok(new ApiResponseDTO<UserReviewsResponseDTO>
        {
            Data = new UserReviewsResponseDTO
            {
                TotalReviews = reviews.Count(),
                Reviews      = reviews.ToList()
            }
        });
    }

    /// <summary>Kiểm tra user đã review phim này chưa</summary>
    [HttpGet("check/{movieId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponseDTO<CheckReviewResponseDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckUserReview([FromRoute] Guid movieId)
    {
        var userId    = GetUserId();
        var hasReview = await _ratingReviewService.CheckUserHasReviewAsync(userId, movieId);
        var review    = hasReview
            ? await _ratingReviewService.GetUserReviewForMovieAsync(userId, movieId)
            : null;

        return Ok(new ApiResponseDTO<CheckReviewResponseDTO>
        {
            Data = new CheckReviewResponseDTO { HasReview = hasReview, Review = review }
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ADMIN ENDPOINTS — Chỉ Admin
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>[Admin] Xóa review vi phạm quy tắc</summary>
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

    // ─────────────────────────────────────────────────────────────────────────
    private Guid GetUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrEmpty(userIdStr) ? Guid.Empty : Guid.Parse(userIdStr);
    }
}