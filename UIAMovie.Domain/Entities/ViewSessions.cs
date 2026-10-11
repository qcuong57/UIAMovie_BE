// UIAMovie.Domain/Entities/ViewSessions.cs
// Hệ thống đếm view chuẩn Netflix — RIÊNG BIỆT với WatchHistory / TvShowWatchHistory (resume).

namespace UIAMovie.Domain.Entities;

/// <summary>
/// Một "session xem" phim = (UserId hoặc GuestId, MovieId, ViewDate).
/// Resume trong cùng ngày cộng dồn vào row cũ; sang ngày khác tạo row mới (rewatch).
/// </summary>
public class MovieViewSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Có giá trị nếu user đã đăng nhập. NULL nếu là guest.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Định danh khách: header X-Guest-Id (ưu tiên) hoặc cookie guest_id. NULL nếu user đã đăng nhập.</summary>
    public string? GuestId { get; set; }

    public Guid MovieId { get; set; }

    /// <summary>Ngày xem theo giờ Việt Nam (IClock.Today / VnClock), dùng để gộp session trong cùng ngày.</summary>
    public DateOnly ViewDate { get; set; }

    /// <summary>Tổng số giây đã xem thực sự, cộng dồn qua từng heartbeat.</summary>
    public int AccumulatedSeconds { get; set; }

    /// <summary>TRUE khi đã đạt ngưỡng view hợp lệ (set 1 lần, không reset).</summary>
    public bool IsCountedAsView { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastHeartbeatAt { get; set; } = DateTime.UtcNow;

    public Movie? Movie { get; set; }
}

/// <summary>
/// Session xem TV Show. Gộp ở level SHOW (không phải từng tập), giống Netflix đếm theo title:
/// (UserId/GuestId, TvShowId, ViewDate). Xem nhiều tập trong ngày vẫn cộng dồn vào 1 session
/// → 1 view/ngày/người cho mỗi show.
/// </summary>
public class TvShowViewSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string? GuestId { get; set; }
    public Guid TvShowId { get; set; }
    public DateOnly ViewDate { get; set; }
    public int AccumulatedSeconds { get; set; }
    public bool IsCountedAsView { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastHeartbeatAt { get; set; } = DateTime.UtcNow;

    public TvShow? TvShow { get; set; }
}

// ── Navigation (thêm vào Movie.cs và TvShow.cs, tùy chọn) ───────────────────
// Movie:  public ICollection<MovieViewSession>  MovieViewSessions  { get; set; } = new List<MovieViewSession>();
// TvShow: public ICollection<TvShowViewSession> TvShowViewSessions { get; set; } = new List<TvShowViewSession>();