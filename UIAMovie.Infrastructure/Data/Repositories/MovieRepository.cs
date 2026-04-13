// UIAMovie.Infrastructure/Data/Repositories/MovieRepository.cs

using Microsoft.EntityFrameworkCore;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data;

namespace UIAMovie.Infrastructure.Data.Repositories;

public class MovieRepository : Repository<Movie>, IMovieRepository
{
    private readonly MovieDbContext _context;

    public MovieRepository(MovieDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<Movie?> GetByIdWithDetailsAsync(Guid id)
    {
        return await _context.Movies
            .AsNoTracking()
            .Include(m => m.MovieCasts.OrderBy(c => c.Order))
                .ThenInclude(c => c.Person)
                    .ThenInclude(p => p.Images)
            .Include(m => m.MovieDirectors)
                .ThenInclude(d => d.Person)
                    .ThenInclude(p => p.Images)
            .Include(m => m.MovieImages)
            .Include(m => m.MovieVideos)
            .Include(m => m.MovieGenres)
                .ThenInclude(g => g.Genre)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<IEnumerable<Movie>> GetAllWithGenresAsync()
    {
        return await _context.Movies
            .AsNoTracking()
            .Include(m => m.MovieGenres)
                .ThenInclude(g => g.Genre)
            .ToListAsync();
    }

    public async Task<Movie?> GetByTmdbIdAsync(int tmdbId)
    {
        return await _context.Movies
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TmdbId == tmdbId);
    }

    public async Task<IEnumerable<Movie>> GetMoviesByActorNameAsync(string actorName)
    {
        return await _context.Movies
            .AsNoTracking()
            .Include(m => m.MovieGenres).ThenInclude(g => g.Genre)
            .Include(m => m.MovieCasts)
                .ThenInclude(c => c.Person)
                    .ThenInclude(p => p.Images)
            .Include(m => m.MovieDirectors)
                .ThenInclude(d => d.Person)
                    .ThenInclude(p => p.Images)
            .Include(m => m.MovieVideos)
            .Include(m => m.MovieImages)
            .Where(m => m.MovieCasts
                .Any(c => c.Person != null &&
                          c.Person.Name.Contains(actorName)))
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TrendingMovieProjection>> GetTrendingAsync(
        DateTime cutoff7,
        DateTime cutoff30,
        int take = 20)
    {
        // ── Bước 1: Tính views_7d và views_30d TRÊN DB ──────────────────────
        // GroupBy chạy thành SQL GROUP BY — không kéo data về RAM
        var viewStats = await _context.WatchHistories
            .AsNoTracking()
            .Where(wh => wh.WatchedAt >= cutoff30)           // chỉ lọc 30 ngày
            .GroupBy(wh => wh.MovieId)
            .Select(g => new
            {
                MovieId  = g.Key,
                Views30d = g.Count(),
                Views7d  = g.Count(wh => wh.WatchedAt >= cutoff7)
            })
            .ToDictionaryAsync(x => x.MovieId, x => x);     // Dict tra cứu O(1)

        // ── Bước 2: Lấy danh sách phim published kèm Genres (không include Cast/Images) ──
        var movies = await _context.Movies
            .AsNoTracking()
            .Where(m => m.IsPublished)
            .Include(m => m.MovieGenres)
                .ThenInclude(g => g.Genre)
            .ToListAsync();

        // ── Bước 3: Tính score trong C# (phép tính float không nên làm trên DB) ──
        var now = DateTime.UtcNow;
        var result = movies
            .Select(m =>
            {
                viewStats.TryGetValue(m.Id, out var stat);
                var v7d  = stat?.Views7d  ?? 0;
                var v30d = stat?.Views30d ?? 0;

                // Recency bonus — ưu tiên phim mới ra rạp / mới được import
                double recencyBonus = 0;
                if (m.ReleaseDate.HasValue)
                {
                    var daysOld = (now - m.ReleaseDate.Value).TotalDays;
                    recencyBonus = daysOld switch
                    {
                        <= 7   => 120,   // Vừa ra rạp tuần này — boost mạnh nhất
                        <= 30  => 80,
                        <= 90  => 50,
                        <= 180 => 25,
                        <= 365 => 10,
                        _      => 0
                    };
                }

                // TrendingScore:
                //   views 7 ngày  × 3.0  — tín hiệu "đang hot" quan trọng nhất
                //   views 30 ngày × 1.0  — momentum tháng
                //   imdbRating    × 10.0 — chất lượng nội tại (0–100 điểm)
                //   recencyBonus         — ưu tiên phim mới
                var score = (v7d  * 3.0)
                          + (v30d * 1.0)
                          + ((double)(m.ImdbRating ?? 0) * 10.0)
                          + recencyBonus;

                return new TrendingMovieProjection
                {
                    Movie    = m,
                    Views7d  = v7d,
                    Views30d = v30d,
                    Score    = score
                };
            })
            .OrderByDescending(x => x.Score)
            .Take(take)
            .ToList();

        return result;
    }
}