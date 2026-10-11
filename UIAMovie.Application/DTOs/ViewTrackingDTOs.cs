// UIAMovie.Application/DTOs/ViewTrackingDTOs.cs

namespace UIAMovie.Application.DTOs;

/// <summary>Body cho POST /api/playback/heartbeat (phim).</summary>
public class HeartbeatDTO
{
    public Guid MovieId { get; set; }

    /// <summary>Số giây xem thực sự kể từ heartbeat trước (FE gửi 15). Server chặn ngoài 0–30.</summary>
    public int SecondsSinceLastBeat { get; set; }
}

/// <summary>Body cho POST /api/playback/heartbeat/tvshow.</summary>
public class TvShowHeartbeatDTO
{
    public Guid TvShowId { get; set; }

    /// <summary>Tùy chọn: dùng để lấy Runtime của tập tính ngưỡng. Thiếu thì dùng TvShow.EpisodeRuntime.</summary>
    public Guid? EpisodeId { get; set; }

    public int SecondsSinceLastBeat { get; set; }
}

public class HeartbeatResultDTO
{
    public int AccumulatedSeconds { get; set; }
    public bool IsCountedAsView { get; set; }
}