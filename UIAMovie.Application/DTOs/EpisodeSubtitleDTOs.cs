// UIAMovie.Application/DTOs/EpisodeSubtitleDTOs.cs

using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.DTOs;

// ── Response DTOs ─────────────────────────────────────────────────────────────

public class EpisodeSubtitleDTO
{
    public Guid           Id             { get; set; }
    public Guid           EpisodeId      { get; set; }
    public string         LanguageCode   { get; set; } = string.Empty;
    public string         LanguageName   { get; set; } = string.Empty;
    public string         Source         { get; set; } = string.Empty;
    public string?        TranslatedFrom { get; set; }
    public SubtitleStatus Status         { get; set; }
    public string?        ErrorMessage   { get; set; }
    public bool           IsDefault      { get; set; }
    public DateTime       CreatedAt      { get; set; }
    public DateTime       UpdatedAt      { get; set; }
}

public class EpisodeSubtitleContentDTO
{
    public Guid   Id           { get; set; }
    public Guid   EpisodeId    { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string LanguageName { get; set; } = string.Empty;
    public string Content      { get; set; } = string.Empty; // WebVTT
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

/// <summary>
/// POST /api/episodes/{episodeId}/subtitles/ai-generate
/// Tách riêng khỏi AiGenerateSubtitleDTO của Movie để dùng EpisodeId.
/// </summary>
public class AiGenerateEpisodeSubtitleDTO
{
    /// <summary>Được gán tự động từ route parameter, không cần client gửi.</summary>
    public Guid   EpisodeId          { get; set; }
    public string SourceContent      { get; set; } = string.Empty;
    public string SourceLanguageCode { get; set; } = "en";
    public string TargetLanguageCode { get; set; } = string.Empty;
    public string? TargetLanguageName { get; set; }
}