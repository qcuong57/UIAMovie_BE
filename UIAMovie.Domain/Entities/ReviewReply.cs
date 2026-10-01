namespace UIAMovie.Domain.Entities;

/// <summary>
/// Reply (bình luận trả lời) cho một RatingReview.
/// Mỗi review có thể có nhiều reply, mỗi reply thuộc 1 user.
/// </summary>
public class ReviewReply
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Review mà reply này thuộc về.</summary>
    public Guid RatingReviewId { get; set; }

    /// <summary>User viết reply.</summary>
    public Guid UserId { get; set; }

    public string ReplyText { get; set; } = string.Empty;

    /// <summary>
    /// null = reply cấp 1 (trả lời thẳng review).
    /// Có giá trị = reply con; LUÔN trỏ tới reply cấp 1 (tối đa 1 cấp lồng, như Facebook/YouTube).
    /// </summary>
    public Guid? ParentReplyId { get; set; }

    /// <summary>Khi trả lời một reply con: user của reply con đó (để hiển thị @tên).</summary>
    public Guid? ReplyToUserId { get; set; }

    public bool IsPublished { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────
    public ReviewReply?  ParentReply  { get; set; }
    public ICollection<ReviewReply> Children { get; set; } = new List<ReviewReply>();
    public RatingReview? RatingReview { get; set; }
    public User?         User         { get; set; }
}