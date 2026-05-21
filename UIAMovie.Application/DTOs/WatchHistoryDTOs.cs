namespace UIAMovie.Application.DTOs;

public class WatchHistoryDTO
{
    public Guid Id { get; set; }
    public Guid MovieId { get; set; }
    public string MovieTitle { get; set; }
    public string? PosterUrl { get; set; }
    public DateTime WatchedAt { get; set; }
    public int ProgressMinutes { get; set; }
    public bool IsCompleted { get; set; }
}

public class UpdateWatchProgressDTO
{
    public Guid MovieId { get; set; }
    public int ProgressMinutes { get; set; }
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
    public bool      IsCompleted     { get; set; }
}