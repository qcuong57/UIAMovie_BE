// UIAMovie.Application/DTOs/ReviewReplyDTOs.cs

namespace UIAMovie.Application.DTOs;

/// <summary>Request DTO — tạo hoặc cập nhật reply cho một review.</summary>
public class ReviewReplyDTO
{
    public string ReplyText { get; set; } = string.Empty;

    /// <summary>
    /// Id của reply mà user đang trả lời. null = trả lời thẳng review.
    /// Chỉ dùng khi tạo mới (bỏ qua khi cập nhật). Reply chỉ lồng tối đa 1 cấp:
    /// nếu trỏ vào một reply con, server tự gắn vào reply gốc và đánh dấu @người được trả lời.
    /// </summary>
    public Guid? ParentReplyId { get; set; }
}

/// <summary>Response DTO — thông tin 1 reply.</summary>
public class ReplyDTO
{
    public Guid Id { get; set; }
    public Guid RatingReviewId { get; set; }

    public Guid    UserId     { get; set; }
    public string  UserName   { get; set; } = string.Empty;
    public string? UserAvatar { get; set; }

    public string ReplyText { get; set; } = string.Empty;

    /// <summary>null = reply cấp 1 (trả lời review). Có giá trị = reply con của reply gốc này.</summary>
    public Guid? ParentReplyId { get; set; }

    /// <summary>Chỉ có khi reply con trả lời một reply con khác — FE hiển thị "@tên".</summary>
    public Guid?   ReplyToUserId   { get; set; }
    public string? ReplyToUserName { get; set; }

    public DateTime  CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Response DTO — danh sách reply của 1 review.</summary>
public class ReviewRepliesResponseDTO
{
    public Guid   RatingReviewId { get; set; }
    /// <summary>Tổng số reply (cả gốc lẫn con) — dùng cho nhãn "N trả lời".</summary>
    public int    TotalReplies   { get; set; }
    /// <summary>Tổng số reply gốc — dùng để phân trang ("Xem thêm").</summary>
    public int    TotalRootReplies { get; set; }
    public List<ReplyDTO> Replies { get; set; } = new();
}

/// <summary>Response DTO — sau khi tạo reply thành công.</summary>
public class CreateReplyResponseDTO
{
    public Guid   ReplyId { get; set; }
    public string Message { get; set; } = "Trả lời đã được tạo thành công";
}