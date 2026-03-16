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