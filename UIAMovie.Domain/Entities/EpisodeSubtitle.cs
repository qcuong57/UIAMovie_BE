// UIAMovie.Domain/Entities/EpisodeSubtitle.cs

namespace UIAMovie.Domain.Entities;

/// <summary>
/// Một file subtitle gắn với một tập phim (Episode) của TV Show.
/// Hỗ trợ: import thủ công (.srt / .vtt) và AI dịch tự động.
/// Thiết kế đồng nhất với MovieSubtitle.
/// </summary>
public class EpisodeSubtitle
{
    public Guid   Id        { get; set; } = Guid.NewGuid();
    public Guid   EpisodeId { get; set; }

    /// <summary>ISO 639-1: "vi", "en", "ko", "ja", "zh", ...</summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>Tên hiển thị: "Tiếng Việt", "English", "한국어"</summary>
    public string LanguageName { get; set; } = string.Empty;

    /// <summary>
    /// Nội dung file subtitle lưu thẳng vào DB dạng text (WebVTT).
    /// Không dùng file storage để đơn giản hoá — subtitle thường &lt; 500KB.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>"manual" | "ai_translated"</summary>
    public string Source { get; set; } = SubtitleSource.Manual;

    /// <summary>Ngôn ngữ gốc khi AI dịch. Null nếu import thủ công.</summary>
    public string? TranslatedFrom { get; set; }

    /// <summary>Trạng thái khi AI đang xử lý.</summary>
    public SubtitleStatus Status { get; set; } = SubtitleStatus.Ready;

    /// <summary>Lý do lỗi nếu AI dịch thất bại.</summary>
    public string? ErrorMessage { get; set; }

    public bool     IsDefault  { get; set; } = false;
    public Guid?    UploadedBy { get; set; }
    public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt  { get; set; } = DateTime.UtcNow;

    // Navigation
    public Episode? Episode { get; set; }
}