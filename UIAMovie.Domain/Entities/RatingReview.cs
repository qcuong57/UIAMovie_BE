namespace UIAMovie.Domain.Entities;

/// <summary>
/// Rating &amp; Review — hỗ trợ 3 cấp độ:
///   • Movie:   MovieId có giá trị, TvShowId = null, EpisodeId = null
///   • TvShow:  TvShowId có giá trị, MovieId = null, EpisodeId = null
///   • Episode: TvShowId + EpisodeId đều có giá trị, MovieId = null
/// </summary>
public class RatingReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    // ── Target ────────────────────────────────────────────────────────────────
    /// <summary>null nếu review cho TvShow hoặc Episode.</summary>
    public Guid? MovieId { get; set; }

    /// <summary>null nếu review cho Movie. Có giá trị khi review TvShow hoặc Episode.</summary>
    public Guid? TvShowId { get; set; }

    /// <summary>
    /// Có giá trị khi review từng tập cụ thể.
    /// Khi EpisodeId != null thì TvShowId cũng phải != null (denormalized để query nhanh).
    /// </summary>
    public Guid? EpisodeId { get; set; }

    // ── Content ───────────────────────────────────────────────────────────────
    public int Rating { get; set; }          // 1-10
    public string? ReviewText { get; set; }
    public bool IsSpoiler { get; set; } = false;
    public bool IsPublished { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────
    public User?    User    { get; set; }
    public Movie?   Movie   { get; set; }
    public TvShow?  TvShow  { get; set; }
    public Episode? Episode { get; set; }
}