// UIAMovie.Application/DTOs/SubtitleDTOs.cs

using Microsoft.AspNetCore.Http;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.DTOs;

// ── Response DTOs ─────────────────────────────────────────────────────────────

/// <summary>
/// Trả về khi list subtitle của phim (không kèm content — tránh response nặng).
/// Frontend dùng để render dropdown chọn ngôn ngữ.
/// </summary>
public class SubtitleInfoDTO
{
    public Guid           Id             { get; set; }
    public string         LanguageCode   { get; set; } = string.Empty;
    public string         LanguageName   { get; set; } = string.Empty;
    public string         Source         { get; set; } = string.Empty;
    public SubtitleStatus Status         { get; set; }
    public string?        ErrorMessage   { get; set; }
    public bool           IsDefault      { get; set; }
    public DateTime       CreatedAt      { get; set; }
}

/// <summary>
/// Trả về khi load subtitle để phát — kèm content WebVTT.
/// </summary>
public class SubtitleContentDTO
{
    public Guid   Id           { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string LanguageName { get; set; } = string.Empty;
    public string Content      { get; set; } = string.Empty;  // WebVTT text
    public string Format       { get; set; } = "vtt";
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

/// <summary>Upload subtitle thủ công — nhận file .srt hoặc .vtt.</summary>
public class UploadSubtitleDTO
{
    /// <summary>File .srt hoặc .vtt — bắt buộc.</summary>
    public IFormFile File { get; set; } = null!;

    /// <summary>ISO 639-1: "vi", "en", "ko", ...</summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>Tên hiển thị trong dropdown. Nếu bỏ trống, tự động lấy từ LanguageCode.</summary>
    public string? LanguageName { get; set; }

    /// <summary>Đặt làm subtitle mặc định khi phim load không?</summary>
    public bool IsDefault { get; set; } = false;
}

/// <summary>Yêu cầu AI dịch subtitle sang ngôn ngữ khác.</summary>
public class TranslateSubtitleDTO
{
    /// <summary>Id của subtitle gốc cần dịch.</summary>
    public Guid   SourceSubtitleId { get; set; }

    /// <summary>ISO 639-1 ngôn ngữ đích: "vi", "en", "ja", ...</summary>
    public string TargetLanguageCode { get; set; } = string.Empty;

    /// <summary>Tên hiển thị ngôn ngữ đích. Tự điền nếu để trống.</summary>
    public string? TargetLanguageName { get; set; }
}

/// <summary>
/// Yêu cầu AI dịch toàn bộ nội dung SRT/VTT sang ngôn ngữ khác.
/// Dùng khi muốn tạo subtitle từ đầu bằng AI (không cần subtitle gốc).
/// </summary>
public class AiGenerateSubtitleDTO
{
    /// <summary>Id phim cần tạo subtitle.</summary>
    public Guid   MovieId { get; set; }

    /// <summary>Nội dung SRT/VTT gốc (tiếng Anh hoặc bất kỳ).</summary>
    public string SourceContent { get; set; } = string.Empty;

    /// <summary>ISO 639-1 ngôn ngữ gốc: "en", "ko", ...</summary>
    public string SourceLanguageCode { get; set; } = "en";

    /// <summary>ISO 639-1 ngôn ngữ đích.</summary>
    public string TargetLanguageCode { get; set; } = "vi";

    /// <summary>Tên ngôn ngữ đích hiển thị.</summary>
    public string? TargetLanguageName { get; set; }
}