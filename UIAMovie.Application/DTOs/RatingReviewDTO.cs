// UIAMovie.Application/DTOs/RatingReviewDTOs.cs

namespace UIAMovie.Application.DTOs;

/// <summary>
/// Request DTO — Tạo/Cập nhật rating &amp; review.
/// Chỉ cung cấp đúng 1 trong 3 tổ hợp:
///   • { movieId }              → review phim
///   • { tvShowId }             → review cả show
///   • { tvShowId, episodeId }  → review từng tập
/// </summary>
public class RatingReviewDTO
{
    public Guid? MovieId   { get; set; }
    public Guid? TvShowId  { get; set; }
    /// <summary>Chỉ điền khi muốn review một tập cụ thể (kèm TvShowId).</summary>
    public Guid? EpisodeId { get; set; }

    public int     Rating     { get; set; }   // 1-10
    public string? ReviewText { get; set; }
    public bool    IsSpoiler  { get; set; } = false;
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

public class ReviewDTO
{
    public Guid Id { get; set; }

    public Guid? MovieId   { get; set; }
    public Guid? TvShowId  { get; set; }
    /// <summary>null nếu đây là review cấp show, có giá trị nếu review từng tập.</summary>
    public Guid? EpisodeId { get; set; }
    /// <summary>Tiện hiển thị — số tập (VD: "S2E5"). null khi không phải episode review.</summary>
    public string? EpisodeLabel { get; set; }

    public Guid    UserId     { get; set; }
    public string  UserName   { get; set; } = string.Empty;
    public string? UserAvatar { get; set; }

    public int     Rating     { get; set; }
    public string? ReviewText { get; set; }
    public bool    IsSpoiler  { get; set; }
    public DateTime  CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ReviewSummaryDTO
{
    public Guid    Id         { get; set; }
    public string  UserName   { get; set; } = string.Empty;
    public string? UserAvatar { get; set; }
    public int     Rating     { get; set; }
    public string? ReviewText { get; set; }
    public bool    IsSpoiler  { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ── Stats DTOs ────────────────────────────────────────────────────────────────

public class MovieRatingStatsDTO
{
    public Guid    MovieId    { get; set; }
    public decimal AverageRating { get; set; }
    public int     TotalReviews  { get; set; }
    public Dictionary<int, int> RatingDistribution { get; set; } = new();
}

public class TvShowRatingStatsDTO
{
    public Guid    TvShowId     { get; set; }
    public decimal AverageRating { get; set; }
    public int     TotalReviews  { get; set; }
    public Dictionary<int, int> RatingDistribution { get; set; } = new();
}

public class EpisodeRatingStatsDTO
{
    public Guid    EpisodeId    { get; set; }
    public Guid    TvShowId     { get; set; }
    public decimal AverageRating { get; set; }
    public int     TotalReviews  { get; set; }
    public Dictionary<int, int> RatingDistribution { get; set; } = new();
}

// ── List Response DTOs ────────────────────────────────────────────────────────

public class MovieReviewsResponseDTO
{
    public Guid   MovieId    { get; set; }
    public string MovieTitle { get; set; } = string.Empty;
    public List<ReviewDTO> Reviews { get; set; } = new();
}

public class TvShowReviewsResponseDTO
{
    public Guid   TvShowId    { get; set; }
    public string TvShowTitle { get; set; } = string.Empty;
    public List<ReviewDTO> Reviews { get; set; } = new();
}

public class EpisodeReviewsResponseDTO
{
    public Guid   EpisodeId    { get; set; }
    public Guid   TvShowId     { get; set; }
    public string EpisodeLabel { get; set; } = string.Empty;   // VD: "S1E3 - Tên tập"
    public List<ReviewDTO> Reviews { get; set; } = new();
}

public class UserReviewsResponseDTO
{
    public int TotalReviews { get; set; }
    public List<ReviewDTO> Reviews { get; set; } = new();
}

public class CheckReviewResponseDTO
{
    public bool       HasReview { get; set; }
    public ReviewDTO? Review    { get; set; }
}

public class CreateReviewResponseDTO
{
    public Guid   ReviewId { get; set; }
    public string Message  { get; set; } = "Review đã được tạo thành công";
}

/// <summary>
/// Tất cả reviews toàn hệ thống — phân trang, dùng cho homepage carousel.
/// ReviewDTO chứa MovieId / TvShowId / EpisodeId để client tự join.
/// </summary>
public class AllReviewsResponseDTO
{
    public List<ReviewDTO> Items      { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize   { get; set; }
    public int TotalPages => TotalCount == 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
}

public class PaginatedReviewsDTO
{
    public Guid   MovieId    { get; set; }
    public string MovieTitle { get; set; } = string.Empty;
    public List<ReviewDTO> Reviews { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize   { get; set; }
    public int TotalPages => (TotalCount + PageSize - 1) / PageSize;
}