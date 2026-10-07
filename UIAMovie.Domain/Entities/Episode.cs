namespace UIAMovie.Domain.Entities;

public class Episode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SeasonId { get; set; }

    public int EpisodeNumber { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Overview { get; set; }

    public string? StillUrl { get; set; }

    public int? Runtime { get; set; }

    public decimal? Rating { get; set; }

    public DateTime? AirDate { get; set; }

    public int? TmdbId { get; set; }

    // ============================================================
    // LEGACY / FALLBACK VIDEO
    // ============================================================

    /// <summary>
    /// Legacy MP4 URL.
    /// Giữ lại để backward-compatible.
    /// </summary>
    public string? VideoUrl { get; set; }

    // ============================================================
    // HLS PLAYBACK
    // ============================================================

    /// <summary>
    /// HLS hoặc MP4.
    /// Null = legacy MP4.
    /// </summary>
    public string? StreamingType { get; set; }

    /// <summary>
    /// URL playback chính.
    /// Với HLS thường là .m3u8.
    /// </summary>
    public string? PlaybackUrl { get; set; }

    /// <summary>
    /// Provider video.
    /// Ví dụ: Cloudinary.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Cloudinary PublicId.
    /// </summary>
    public string? PublicId { get; set; }

    /// <summary>
    /// Chất lượng video.
    /// Ví dụ: 1080p, 720p, 480p.
    /// </summary>
    public string? Quality { get; set; }

    public Season Season { get; set; } = null!;
}