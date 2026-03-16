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
        _movieService = movieService;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PUBLIC ENDPOINTS — Không cần đăng nhập
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Lấy tất cả reviews của phim (có phân trang)</summary>
    /// <param name="movieId">ID của phim</param>
    /// <param name="pageNumber">Trang thứ mấy (default: 1)</param>
    /// <param name="pageSize">Số reviews per trang (default: 20)</param>
    [HttpGet("movies/{movieId:guid}")]
    [ProducesResponseType(typeof(MovieReviewsResponseDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMovieReviews(
        [FromRoute] Guid movieId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        // Validate params
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        // Check movie exists
        var movie = await _movieService.GetMovieByIdAsync(movieId);
        if (movie == null)
            return NotFound(new { message = "Không tìm thấy phim" });

        // Get reviews
        var reviews = await _ratingReviewService.GetMovieReviewsAsync(movieId, pageNumber, pageSize);

        return Ok(new MovieReviewsResponseDTO
        {
            MovieId = movieId,
            MovieTitle = movie.Title,
            Reviews = reviews.ToList()
        });
    }

    /// <summary>Lấy thống kê rating của phim</summary>
    [HttpGet("movies/{movieId:guid}/stats")]
    [ProducesResponseType(typeof(MovieRatingStatsDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMovieRatingStats([FromRoute] Guid movieId)
    {
        var stats = await _ratingReviewService.GetMovieRatingStatsAsync(movieId);
        if (stats == null)
            return NotFound(new { message = "Không tìm thấy phim" });

        return Ok(stats);
    }

    /// <summary>Lấy chi tiết một review</summary>
    [HttpGet("{reviewId:guid}")]
    [ProducesResponseType(typeof(ReviewDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviewById([FromRoute] Guid reviewId)
    {
        var review = await _ratingReviewService.GetReviewByIdAsync(reviewId);
        if (review == null)
            return NotFound(new { message = "Không tìm thấy review" });

        return Ok(review);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AUTHENTICATED ENDPOINTS — Cần đăng nhập
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Tạo rating/review cho phim</summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(CreateReviewResponseDTO), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateRatingReview([FromBody] RatingReviewDTO dto)
    {
        if (dto == null)
            return BadRequest(new { message = "Dữ liệu không hợp lệ" });

        // Validate rating range
        if (dto.Rating < 1 || dto.Rating > 10)
            return BadRequest(new { message = "Đánh giá phải từ 1 đến 10 sao" });

        // Validate review text length (optional)
        if (!string.IsNullOrEmpty(dto.ReviewText) && dto.ReviewText.Length > 5000)
            return BadRequest(new { message = "Review không được vượt quá 5000 ký tự" });

        var userId = GetUserId();

        try
        {
            var reviewId = await _ratingReviewService.CreateRatingReviewAsync(userId, dto);
            return CreatedAtAction(nameof(GetReviewById), new { reviewId },
                new CreateReviewResponseDTO { ReviewId = reviewId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Cập nhật review của mình</summary>
    [HttpPut("{reviewId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRatingReview(
        [FromRoute] Guid reviewId,
        [FromBody] RatingReviewDTO dto)
    {
        if (dto == null)
            return BadRequest(new { message = "Dữ liệu không hợp lệ" });

        // Validate rating
        if (dto.Rating < 1 || dto.Rating > 10)
            return BadRequest(new { message = "Đánh giá phải từ 1 đến 10 sao" });

        // Validate review text
        if (!string.IsNullOrEmpty(dto.ReviewText) && dto.ReviewText.Length > 5000)
            return BadRequest(new { message = "Review không được vượt quá 5000 ký tự" });

        var userId = GetUserId();

        try
        {
            var success = await _ratingReviewService.UpdateRatingReviewAsync(reviewId, userId, dto);
            if (!success)
                return NotFound(new { message = "Không tìm thấy review" });

            return Ok(new { message = "Cập nhật review thành công" });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Xóa review của mình</summary>
    [HttpDelete("{reviewId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRatingReview([FromRoute] Guid reviewId)
    {
        var userId = GetUserId();

        try
        {
            var success = await _ratingReviewService.DeleteRatingReviewAsync(reviewId, userId);
            if (!success)
                return NotFound(new { message = "Không tìm thấy review" });

            return Ok(new { message = "Xóa review thành công" });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>Lấy tất cả reviews của user hiện tại</summary>
    [HttpGet("my")]
    [Authorize]
    [ProducesResponseType(typeof(UserReviewsResponseDTO), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyReviews()
    {
        var userId = GetUserId();
        var reviews = await _ratingReviewService.GetUserReviewsAsync(userId);

        return Ok(new UserReviewsResponseDTO
        {
            TotalReviews = reviews.Count(),
            Reviews = reviews.ToList()
        });
    }

    /// <summary>Kiểm tra user đã review phim này chưa</summary>
    [HttpGet("check/{movieId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(CheckReviewResponseDTO), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckUserReview([FromRoute] Guid movieId)
    {
        var userId = GetUserId();
        var hasReview = await _ratingReviewService.CheckUserHasReviewAsync(userId, movieId);
        var review = hasReview
            ? await _ratingReviewService.GetUserReviewForMovieAsync(userId, movieId)
            : null;

        return Ok(new CheckReviewResponseDTO
        {
            HasReview = hasReview,
            Review = review
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ADMIN ENDPOINTS — Chỉ Admin
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>[Admin] Xóa review vi phạm quy tắc</summary>
    [HttpDelete("admin/{reviewId:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AdminDeleteReview([FromRoute] Guid reviewId)
    {
        var review = await _ratingReviewService.GetReviewByIdAsync(reviewId);
        if (review == null)
            return NotFound(new { message = "Không tìm thấy review" });

        var success = await _ratingReviewService.DeleteRatingReviewAsync(reviewId, review.UserId);
        if (!success)
            return BadRequest(new { message = "Không thể xóa review" });

        return Ok(new { message = "Review đã bị xóa bởi admin" });
    }

    /// <summary>[Admin] Lấy tất cả reviews (không phân trang - tìm vi phạm)</summary>
    [HttpGet("admin/all")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(List<ReviewDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdminGetAllReviews()
    {
        // Giả sử lấy tất cả reviews từ tất cả phim
        var reviews = new List<ReviewDTO>();
        
        // TODO: Implement if needed - get all reviews from all movies
        // For now, return empty list
        
        return Ok(reviews);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper Methods
    // ─────────────────────────────────────────────────────────────────────────

    private Guid GetUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrEmpty(userIdStr) ? Guid.Empty : Guid.Parse(userIdStr);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Response DTOs
// ─────────────────────────────────────────────────────────────────────────────

public class MessageResponse
{
    public string Message { get; set; }
}

public class ErrorResponse
{
    public string Message { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
}