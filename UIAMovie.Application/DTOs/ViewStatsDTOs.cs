// UIAMovie.Application/DTOs/ViewStatsDTOs.cs

namespace UIAMovie.Application.DTOs;

/// <summary>Số liệu 1 ngày (trả từ repository).</summary>
public class DailyViewRow
{
    public DateOnly Date { get; set; }
    public int Views { get; set; }
    public long WatchSeconds { get; set; }
    public int Sessions { get; set; }
    public int GuestSessions { get; set; }
    /// <summary>User đăng nhập distinct trong ngày (đếm trong SQL).</summary>
    public int UniqueUsers { get; set; }
}

/// <summary>Số liệu 1 kỳ (ngày/tuần/tháng/quý/năm).</summary>
public class ViewStatPointDTO
{
    public string Period { get; set; } = string.Empty;   // "2026-10-08", "2026-W41", "2026-10", "2026-Q4", "2026"
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int Views { get; set; }
    public long WatchSeconds { get; set; }
    public double WatchHours { get; set; }
    public int Sessions { get; set; }
    public int UniqueUsers { get; set; }       // user đăng nhập, distinct chính xác trong kỳ
    public int GuestSessions { get; set; }     // guest không distinct được → đếm session
}

public class ViewStatsOverviewDTO
{
    public string Granularity { get; set; } = "day";
    public string ContentType { get; set; } = "all";
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int TotalViews { get; set; }
    public double TotalWatchHours { get; set; }
    public int TotalUniqueUsers { get; set; }
    public List<ViewStatPointDTO> Points { get; set; } = new();
}

public class TopContentDTO
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty; // "movie" | "tvshow"
    public string? PosterUrl { get; set; }
    public int Views { get; set; }
    public long WatchSeconds { get; set; }
    public double WatchHours { get; set; }
    public int UniqueUsers { get; set; }
    /// <summary>Kiểu Netflix: watch hours ÷ runtime. NULL nếu không biết runtime.</summary>
    public double? EstimatedViews { get; set; }
}

public class GenreStatDTO
{
    public Guid GenreId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Views { get; set; }
    public double WatchHours { get; set; }
    /// <summary>% lượt xem hợp lệ so với tổng (không đếm trùng phim nhiều thể loại). Tổng các Share có thể > 100.</summary>
    public double Share { get; set; }
}