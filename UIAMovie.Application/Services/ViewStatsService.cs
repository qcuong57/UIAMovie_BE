// UIAMovie.Application/Services/ViewStatsService.cs

using System.Globalization;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Application.Interfaces.IServices;

namespace UIAMovie.Application.Services;

public class ViewStatsService : IViewStatsService
{
    private const int MaxRangeDays = 366 * 5;
    private readonly IViewStatsRepository _repo;
    private readonly IClock _clock;

    public ViewStatsService(IViewStatsRepository repo, IClock clock)
    {
        _repo = repo;
        _clock = clock;
    }

    public async Task<ViewStatsOverviewDTO> GetOverviewAsync(
        string? granularity, string? contentType, DateOnly? from, DateOnly? to, Guid? contentId)
    {
        var g = NormGranularity(granularity);
        var ct = NormContentType(contentType);
        var (f, t) = ResolveRange(g, from, to);
        f = ClampToMaxBuckets(g, f, t);

        var daily = await _repo.GetDailyAsync(f, t, ct, contentId);
        var dailyBy = daily.GroupBy(d => Bucket(d.Date, g).Key).ToDictionary(x => x.Key, x => x.ToList());

        var points = new List<ViewStatPointDTO>();
        // Duyệt từng kỳ liên tục để biểu đồ không bị đứt quãng (kỳ không có view → 0)
        var cursor = f;
        while (cursor <= t)
        {
            var b = Bucket(cursor, g);
            dailyBy.TryGetValue(b.Key, out var rows);
            rows ??= new();

            var from_ = b.Start < f ? f : b.Start;
            var to_ = b.End > t ? t : b.End;

            int uniqueUsers;
            if (rows.Count == 0) uniqueUsers = 0;                          // kỳ trống → không cần query
            else if (g == "day") uniqueUsers = rows.Sum(r => r.UniqueUsers); // 1 ngày = 1 bucket → dùng luôn số đã đếm trong SQL
            else uniqueUsers = await _repo.CountUniqueUsersAsync(from_, to_, ct, contentId); // 1 COUNT DISTINCT / kỳ, trả về 1 số

            var secs = rows.Sum(r => r.WatchSeconds);
            points.Add(new ViewStatPointDTO
            {
                Period = b.Key,
                From = from_,
                To = to_,
                Views = rows.Sum(r => r.Views),
                WatchSeconds = secs,
                WatchHours = Math.Round(secs / 3600.0, 2),
                Sessions = rows.Sum(r => r.Sessions),
                GuestSessions = rows.Sum(r => r.GuestSessions),
                UniqueUsers = uniqueUsers
            });
            cursor = b.End.AddDays(1);
        }

        return new ViewStatsOverviewDTO
        {
            Granularity = g,
            ContentType = ct,
            From = f,
            To = t,
            TotalViews = points.Sum(p => p.Views),
            TotalWatchHours = Math.Round(points.Sum(p => p.WatchSeconds) / 3600.0, 2),
            // distinct toàn kỳ (không cộng dồn theo bucket) — 1 COUNT DISTINCT trong SQL
            TotalUniqueUsers = await _repo.CountUniqueUsersAsync(f, t, ct, contentId),
            Points = points
        };
    }

    public Task<List<TopContentDTO>> GetTopAsync(string? contentType, DateOnly? from, DateOnly? to, int take)
    {
        var (f, t) = ResolveRange("day", from, to);
        return _repo.GetTopAsync(f, t, NormContentType(contentType), Math.Clamp(take, 1, 50));
    }

    public Task<List<GenreStatDTO>> GetByGenreAsync(string? contentType, DateOnly? from, DateOnly? to)
    {
        var (f, t) = ResolveRange("day", from, to);
        return _repo.GetByGenreAsync(f, t, NormContentType(contentType));
    }

    // ── helpers ───────────────────────────────────────────────────────────
    private static string NormGranularity(string? g) =>
        g?.ToLowerInvariant() is "day" or "week" or "month" or "quarter" or "year" ? g.ToLowerInvariant() : "day";

    private static string NormContentType(string? c) =>
        c?.ToLowerInvariant() is "movie" or "tvshow" ? c.ToLowerInvariant() : "all";

    /// <summary>
    /// Giới hạn số kỳ trả về (chart không cần hàng nghìn điểm, và mỗi kỳ coarse tốn 1 COUNT DISTINCT).
    /// Vượt quá thì giữ lại các kỳ gần nhất.
    /// </summary>
    private static DateOnly ClampToMaxBuckets(string g, DateOnly f, DateOnly t)
    {
        var maxBuckets = g == "day" ? 366 : 120;
        var starts = new List<DateOnly>();
        var cursor = f;
        while (cursor <= t)
        {
            var b = Bucket(cursor, g);
            starts.Add(b.Start);
            cursor = b.End.AddDays(1);
        }
        if (starts.Count <= maxBuckets) return f;

        var firstKept = starts[^maxBuckets];
        return firstKept > f ? firstKept : f;
    }

    private (DateOnly From, DateOnly To) ResolveRange(string g, DateOnly? from, DateOnly? to)
    {
        // Cùng nguồn "hôm nay" (giờ VN) với lúc ghi ViewDate
        var today = _clock.Today;
        var t = to ?? today;
        var f = from ?? g switch
        {
            "day"     => t.AddDays(-29),
            "week"    => t.AddDays(-7 * 12 + 1),
            "month"   => t.AddMonths(-11),
            "quarter" => t.AddMonths(-3 * 8 + 1),
            "year"    => t.AddYears(-4),
            _         => t.AddDays(-29)
        };
        if (f > t) (f, t) = (t, f);
        if (t.DayNumber - f.DayNumber > MaxRangeDays) f = t.AddDays(-MaxRangeDays);
        return (f, t);
    }

    private static (string Key, DateOnly Start, DateOnly End) Bucket(DateOnly d, string g)
    {
        switch (g)
        {
            case "week":
            {
                var dt = d.ToDateTime(TimeOnly.MinValue);
                var y = ISOWeek.GetYear(dt); var w = ISOWeek.GetWeekOfYear(dt);
                var mon = DateOnly.FromDateTime(ISOWeek.ToDateTime(y, w, DayOfWeek.Monday));
                return ($"{y}-W{w:00}", mon, mon.AddDays(6));
            }
            case "month":
            {
                var s = new DateOnly(d.Year, d.Month, 1);
                return ($"{d.Year}-{d.Month:00}", s, s.AddMonths(1).AddDays(-1));
            }
            case "quarter":
            {
                var q = (d.Month - 1) / 3 + 1;
                var s = new DateOnly(d.Year, (q - 1) * 3 + 1, 1);
                return ($"{d.Year}-Q{q}", s, s.AddMonths(3).AddDays(-1));
            }
            case "year":
                return ($"{d.Year}", new DateOnly(d.Year, 1, 1), new DateOnly(d.Year, 12, 31));
            default:
                return (d.ToString("yyyy-MM-dd"), d, d);
        }
    }
}