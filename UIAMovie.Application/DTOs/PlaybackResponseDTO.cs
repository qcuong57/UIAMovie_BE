namespace UIAMovie.Application.DTOs;

/// <summary>
/// Response thống nhất cho hệ thống Playback.
/// Dùng cho cả Movie và TV Show Episode.
/// </summary>
public class PlaybackResponseDTO
{
    // ============================================================
    // CONTENT
    // ============================================================

    /// <summary>
    /// Movie hoặc TvEpisode.
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// ID của content đang phát.
    /// Movie  -> MovieId
    /// TV     -> EpisodeId
    /// </summary>
    public Guid ContentId { get; set; }

    /// <summary>
    /// MovieId hoặc TvShowId.
    /// Với Movie = MovieId.
    /// Với TV Episode = TvShowId.
    /// </summary>
    public Guid ParentId { get; set; }

    public string Title { get; set; } = string.Empty;

    // ============================================================
    // TV METADATA
    // ============================================================

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public string? EpisodeTitle { get; set; }

    // ============================================================
    // ACCESS
    // ============================================================

    public bool IsPremium { get; set; }

    public bool CanWatch { get; set; }

    public bool RequiresPremium { get; set; }

    public string? BlockReason { get; set; }

    // ============================================================
    // VIDEO
    // ============================================================

    public Guid? VideoId { get; set; }

    /// <summary>
    /// HLS hoặc MP4. Null khi bị chặn (CanWatch = false).
    /// </summary>
    public string? StreamingType { get; set; }

    /// <summary>
    /// URL mà React Player/HLS.js sử dụng để phát.
    /// </summary>
    public string? PlaybackUrl { get; set; }

    /// <summary>
    /// Provider hiện tại.
    /// Ví dụ: Cloudinary.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Cloudinary PublicId.
    /// </summary>
    public string? PublicId { get; set; }

    /// <summary>
    /// Quality nếu có.
    /// Ví dụ: 1080p, 720p, 480p.
    /// </summary>
    public string? Quality { get; set; }

    /// <summary>
    /// Duration tính bằng giây.
    /// </summary>
    public int? DurationSeconds { get; set; }

    /// <summary>
    /// true nếu playback là HLS.
    /// </summary>
    public bool IsHls =>
        string.Equals(
            StreamingType,
            "HLS",
            StringComparison.OrdinalIgnoreCase);
}