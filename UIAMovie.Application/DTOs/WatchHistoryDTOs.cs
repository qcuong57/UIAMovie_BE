namespace UIAMovie.Application.DTOs;

public class WatchHistoryDTO
{
    public Guid Id { get; set; }
    public Guid MovieId { get; set; }
    public string MovieTitle { get; set; }
    public string? PosterUrl { get; set; }
    public DateTime WatchedAt { get; set; }
    public int ProgressMinutes { get; set; }

    /// <summary>Vị trí xem chính xác (giây) — FE dùng để resume.</summary>
    public int ProgressSeconds { get; set; }

    /// <summary>Tổng thời lượng (giây). NULL nếu chưa biết.</summary>
    public int? Duration { get; set; }

    /// <summary>0–100, dùng vẽ thanh tiến độ "Continue Watching".</summary>
    public double ProgressPercentage { get; set; }

    public bool IsCompleted { get; set; }
}

public class UpdateWatchProgressDTO
{
    public Guid MovieId { get; set; }

    /// <summary>Giữ cho client cũ. Bị bỏ qua nếu ProgressSeconds có giá trị.</summary>
    public int ProgressMinutes { get; set; }

    /// <summary>Vị trí hiện tại (giây) — player gửi video.currentTime. NULL = client cũ, dùng ProgressMinutes.</summary>
    public int? ProgressSeconds { get; set; }

    /// <summary>Tổng thời lượng video (giây).</summary>
    public int? DurationSeconds { get; set; }

    public bool IsCompleted { get; set; }
}

public class TvShowWatchHistoryDTO
{
    public Guid      Id              { get; set; }
    public Guid      TvShowId        { get; set; }
    public string    TvShowTitle     { get; set; } = "";
    public string?   PosterUrl       { get; set; }
    public Guid?     EpisodeId       { get; set; }

    // Episode metadata — null nếu track ở level show (không có episodeId)
    public int?      SeasonNumber    { get; set; }
    public int?      EpisodeNumber   { get; set; }
    public string?   EpisodeName     { get; set; }
    public int?      EpisodeRuntime  { get; set; } // phút

    public DateTime  WatchedAt       { get; set; }
    public int       ProgressSeconds { get; set; }

    /// <summary>Tổng thời lượng (giây): ưu tiên duration thật từ player, thiếu thì EpisodeRuntime * 60.</summary>
    public int?      DurationSeconds { get; set; }

    public bool      IsCompleted     { get; set; }
}