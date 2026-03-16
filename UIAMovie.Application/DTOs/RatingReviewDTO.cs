// UIAMovie.Application/DTOs/RatingReviewDTOs.cs

namespace UIAMovie.Application.DTOs;

/// <summary>Request DTO - Tạo/Cập nhật rating & review</summary>
public class RatingReviewDTO
{
    public Guid MovieId { get; set; }
    public int Rating { get; set; }            // 1-10
    public string? ReviewText { get; set; }
    public bool IsSpoiler { get; set; } = false;
}

/// <summary>Response DTO - Chi tiết review (full info)</summary>
public class ReviewDTO
{
    public Guid Id { get; set; }
    public Guid MovieId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; }
    public string? UserAvatar { get; set; }
    public int Rating { get; set; }
    public string? ReviewText { get; set; }
    public bool IsSpoiler { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Response DTO - Tóm tắt review cho list view</summary>
public class ReviewSummaryDTO
{
    public Guid Id { get; set; }
    public string UserName { get; set; }
    public string? UserAvatar { get; set; }
    public int Rating { get; set; }
    public string? ReviewText { get; set; }
    public bool IsSpoiler { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Response DTO - Thống kê rating của phim</summary>
public class MovieRatingStatsDTO
{
    public Guid MovieId { get; set; }
    public decimal AverageRating { get; set; }      // 0.00 - 10.00
    public int TotalReviews { get; set; }
    /// <summary>Dictionary<Rating (1-10), Count></summary>
    public Dictionary<int, int> RatingDistribution { get; set; } = new();
}

/// <summary>Response DTO - Tạo review thành công</summary>
public class CreateReviewResponseDTO
{
    public Guid ReviewId { get; set; }
    public string Message { get; set; } = "Review đã được tạo thành công";
}

/// <summary>Response DTO - Danh sách reviews của phim</summary>
public class MovieReviewsResponseDTO
{
    public Guid MovieId { get; set; }
    public string MovieTitle { get; set; }
    public List<ReviewDTO> Reviews { get; set; } = new();
}

/// <summary>Response DTO - User reviews</summary>
public class UserReviewsResponseDTO
{
    public int TotalReviews { get; set; }
    public List<ReviewDTO> Reviews { get; set; } = new();
}

/// <summary>Response DTO - Check user reviewed</summary>
public class CheckReviewResponseDTO
{
    public bool HasReview { get; set; }
    public ReviewDTO? Review { get; set; }
}

/// <summary>Response DTO - Danh sách reviews với phân trang</summary>
public class PaginatedReviewsDTO
{
    public Guid MovieId { get; set; }
    public string MovieTitle { get; set; }
    public List<ReviewDTO> Reviews { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (TotalCount + PageSize - 1) / PageSize;
}