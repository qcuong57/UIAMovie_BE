// UIAMovie.Infrastructure/Repositories/ViewStatsRepository.cs

using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Infrastructure.Data;

namespace UIAMovie.Infrastructure.Repositories;

public class ViewStatsRepository : IViewStatsRepository
{
    private readonly MovieDbContext _db;

    public ViewStatsRepository(MovieDbContext db) => _db = db;

    /// <summary>Hình dạng chung của 2 loại session để UNION.</summary>
    private class SessionRow
    {
        public DateOnly ViewDate { get; set; }
        public Guid ContentId { get; set; }
        public bool IsMovie { get; set; }
        public Guid? UserId { get; set; }
        public bool IsCounted { get; set; }
        public int Seconds { get; set; }
    }

    private IQueryable<SessionRow> Sessions(DateOnly fromDate, DateOnly toDate, string contentType, Guid? contentId)
    {
        var movies = _db.MovieViewSessions.AsNoTracking()
            .Where(s => s.ViewDate >= fromDate && s.ViewDate <= toDate)
            .Select(s => new SessionRow
            {
                ViewDate = s.ViewDate, ContentId = s.MovieId, IsMovie = true,
                UserId = s.UserId, IsCounted = s.IsCountedAsView, Seconds = s.AccumulatedSeconds
            });

        var shows = _db.TvShowViewSessions.AsNoTracking()
            .Where(s => s.ViewDate >= fromDate && s.ViewDate <= toDate)
            .Select(s => new SessionRow
            {
                ViewDate = s.ViewDate, ContentId = s.TvShowId, IsMovie = false,
                UserId = s.UserId, IsCounted = s.IsCountedAsView, Seconds = s.AccumulatedSeconds
            });

        var q = contentType switch
        {
            "movie"  => movies,
            "tvshow" => shows,
            _        => movies.Concat(shows)
        };

        return contentId.HasValue ? q.Where(s => s.ContentId == contentId.Value) : q;
    }

    public async Task<List<DailyViewRow>> GetDailyAsync(DateOnly fromDate, DateOnly toDate, string contentType, Guid? contentId)
    {
        return await Sessions(fromDate, toDate, contentType, contentId)
            .GroupBy(s => s.ViewDate)
            .Select(g => new DailyViewRow
            {
                Date = g.Key,
                Views = g.Count(s => s.IsCounted),
                WatchSeconds = g.Sum(s => (long)s.Seconds),
                Sessions = g.Count(),
                GuestSessions = g.Count(s => s.UserId == null),
                UniqueUsers = g.Where(s => s.UserId != null).Select(s => s.UserId).Distinct().Count()
            })
            .OrderBy(x => x.Date)
            .ToListAsync();
    }

    public Task<int> CountUniqueUsersAsync(DateOnly fromDate, DateOnly toDate, string contentType, Guid? contentId) =>
        Sessions(fromDate, toDate, contentType, contentId)
            .Where(s => s.UserId != null)
            .Select(s => s.UserId)
            .Distinct()
            .CountAsync();

    public async Task<List<TopContentDTO>> GetTopAsync(DateOnly fromDate, DateOnly toDate, string contentType, int take)
    {
        var top = await Sessions(fromDate, toDate, contentType, null)
            .GroupBy(s => new { s.ContentId, s.IsMovie })
            .Select(g => new
            {
                g.Key.ContentId,
                g.Key.IsMovie,
                Views = g.Count(s => s.IsCounted),
                Seconds = g.Sum(s => (long)s.Seconds),
                Users = g.Where(s => s.UserId != null).Select(s => s.UserId).Distinct().Count()
            })
            .OrderByDescending(x => x.Views).ThenByDescending(x => x.Seconds)
            .Take(take)
            .ToListAsync();

        var movieIds = top.Where(x => x.IsMovie).Select(x => x.ContentId).ToList();
        var showIds = top.Where(x => !x.IsMovie).Select(x => x.ContentId).ToList();

        var movieInfo = await _db.Movies.AsNoTracking()
            .Where(m => movieIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Title, m.PosterUrl, Runtime = m.Duration })
            .ToDictionaryAsync(m => m.Id);

        var showInfo = await _db.TvShows.AsNoTracking()
            .Where(t => showIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Title, t.PosterUrl, Runtime = (t.EpisodeRuntime ?? 0) * (t.NumberOfEpisodes ?? 0) }) // tổng phút của cả show (chuẩn Netflix: hours ÷ tổng runtime)
            .ToDictionaryAsync(t => t.Id);

        return top.Select(x =>
        {
            string title = "(đã xoá)"; string? poster = null; int? runtime = null;
            if (x.IsMovie && movieInfo.TryGetValue(x.ContentId, out var m))
            { title = m.Title; poster = m.PosterUrl; runtime = m.Runtime; }
            else if (!x.IsMovie && showInfo.TryGetValue(x.ContentId, out var t))
            { title = t.Title; poster = t.PosterUrl; runtime = t.Runtime; }

            return new TopContentDTO
            {
                Id = x.ContentId,
                Title = title,
                PosterUrl = poster,
                ContentType = x.IsMovie ? "movie" : "tvshow",
                Views = x.Views,
                WatchSeconds = x.Seconds,
                WatchHours = Math.Round(x.Seconds / 3600.0, 2),
                UniqueUsers = x.Users,
                EstimatedViews = runtime is > 0 ? Math.Round(x.Seconds / (runtime.Value * 60.0), 2) : null
            };
        }).ToList();
    }

    public async Task<List<GenreStatDTO>> GetByGenreAsync(DateOnly fromDate, DateOnly toDate, string contentType)
    {
        var result = new Dictionary<Guid, (int Views, long Seconds)>();

        void Merge(IEnumerable<(Guid GenreId, int Views, long Seconds)> rows)
        {
            foreach (var r in rows)
            {
                result.TryGetValue(r.GenreId, out var cur);
                result[r.GenreId] = (cur.Views + r.Views, cur.Seconds + r.Seconds);
            }
        }

        if (contentType is "all" or "movie")
        {
            var rows = await (
                from s in _db.MovieViewSessions.AsNoTracking()
                where s.ViewDate >= fromDate && s.ViewDate <= toDate
                join mg in _db.MovieGenres on s.MovieId equals mg.MovieId
                group s by mg.GenreId into g
                select new { GenreId = g.Key, Views = g.Count(x => x.IsCountedAsView), Seconds = g.Sum(x => (long)x.AccumulatedSeconds) }
            ).ToListAsync();
            Merge(rows.Select(r => (r.GenreId, r.Views, r.Seconds)));
        }

        if (contentType is "all" or "tvshow")
        {
            var rows = await (
                from s in _db.TvShowViewSessions.AsNoTracking()
                where s.ViewDate >= fromDate && s.ViewDate <= toDate
                join tg in _db.TvShowGenres on s.TvShowId equals tg.TvShowId
                group s by tg.GenreId into g
                select new { GenreId = g.Key, Views = g.Count(x => x.IsCountedAsView), Seconds = g.Sum(x => (long)x.AccumulatedSeconds) }
            ).ToListAsync();
            Merge(rows.Select(r => (r.GenreId, r.Views, r.Seconds)));
        }

        // Tổng lượt xem hợp lệ KHÔNG đếm trùng (phim N thể loại chỉ tính 1)
        long totalViews = 0;
        if (contentType is "all" or "movie")
            totalViews += await _db.MovieViewSessions.AsNoTracking()
                .CountAsync(s => s.IsCountedAsView && s.ViewDate >= fromDate && s.ViewDate <= toDate);
        if (contentType is "all" or "tvshow")
            totalViews += await _db.TvShowViewSessions.AsNoTracking()
                .CountAsync(s => s.IsCountedAsView && s.ViewDate >= fromDate && s.ViewDate <= toDate);

        var genreIds = result.Keys.ToList();
        var names = await _db.Genres.AsNoTracking()
            .Where(g => genreIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name);

        return result
            .Select(kv => new GenreStatDTO
            {
                GenreId = kv.Key,
                Name = names.GetValueOrDefault(kv.Key, "?"),
                Views = kv.Value.Views,
                WatchHours = Math.Round(kv.Value.Seconds / 3600.0, 2),
                Share = totalViews > 0 ? Math.Round(kv.Value.Views * 100.0 / totalViews, 1) : 0
            })
            .OrderByDescending(x => x.Views)
            .ToList();
    }
}