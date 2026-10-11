// UIAMovie.Infrastructure/Data/Repositories/MovieRepository.cs

using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IRepositories;
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

    public async Task<(IEnumerable<Movie> Items, int TotalCount)> GetPagedAsync(FilterMoviesDTO filter)
    {
        var query = _context.Movies
            .AsNoTracking()
            .Include(m => m.MovieGenres)
                .ThenInclude(g => g.Genre)
            .Include(m => m.MovieVideos)
            .AsQueryable();

        // ── 1. Filter theo Ids (AI mode) ────────────────────────────────────────
        if (filter.Ids is { Count: > 0 })
        {
            query = query.Where(m => filter.Ids.Contains(m.Id));
            var idItems = await query.ToListAsync();

            var ordered = idItems
                .OrderBy(m => filter.Ids.IndexOf(m.Id))
                .ToList();

            return (ordered, ordered.Count);
        }

        // ── 2. Tìm kiếm tên phim ──────────────────────────────────────────────
        var hasSearch = !string.IsNullOrWhiteSpace(filter.Search);
        if (hasSearch)
        {
            var searchPattern = $"%{filter.Search.Trim()}%";
            query = query.Where(m => EF.Functions.ILike(m.Title, searchPattern));
        }

        if (filter.GenreIds is { Count: > 0 })
            query = query.Where(m => m.MovieGenres.Any(g => filter.GenreIds.Contains(g.GenreId)));

        if (filter.MinRating.HasValue)
            query = query.Where(m => m.ImdbRating >= filter.MinRating);

        if (filter.MaxRating.HasValue)
            query = query.Where(m => m.ImdbRating <= filter.MaxRating);

        if (filter.FromReleaseDate.HasValue)
        {
            var from = DateTime.SpecifyKind(filter.FromReleaseDate.Value, DateTimeKind.Utc);
            query = query.Where(m => m.ReleaseDate >= from);
        }

        if (filter.ToReleaseDate.HasValue)
        {
            var to = DateTime.SpecifyKind(filter.ToReleaseDate.Value, DateTimeKind.Utc);
            query = query.Where(m => m.ReleaseDate <= to);
        }

        // ── 3. FIX: Xử lý phim sắp chiếu vs đã phát hành ──────────────────────
        // NẾU người dùng đang tìm kiếm đích danh từ khóa tên phim -> KHÔNG ĐƯỢC CHẶN PHIM SẮP CHIẾU
        var now = DateTime.UtcNow;
        if (!hasSearch)
        {
            query = filter.IsUpcoming == true
                ? query.Where(m => m.ReleaseDate.HasValue && m.ReleaseDate.Value > now)
                : query.Where(m => !m.ReleaseDate.HasValue || m.ReleaseDate.Value <= now);
        }

        if (!string.IsNullOrWhiteSpace(filter.OriginCountry))
            query = query.Where(m => m.OriginCountry != null &&
                                     m.OriginCountry.ToLower() == filter.OriginCountry.Trim().ToLower());

        // ── 4. Sort ───────────────────────────────────────────────────────────
        query = filter.SortBy?.ToLower() switch
        {
            "title"       => filter.SortDesc
                                 ? query.OrderByDescending(m => m.Title)
                                 : query.OrderBy(m => m.Title),
            "releasedate" => filter.SortDesc
                                 ? query.OrderByDescending(m => m.ReleaseDate)
                                 : query.OrderBy(m => m.ReleaseDate),
            _             => filter.SortDesc
                                 ? query.OrderByDescending(m => m.ImdbRating)
                                 : query.OrderBy(m => m.ImdbRating)
        };

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<IEnumerable<Movie>> SearchByTitleAsync(string query)
    {
        var pattern = $"%{query.Trim()}%";
        return await _context.Movies
            .AsNoTracking()
            .Include(m => m.MovieGenres)
                .ThenInclude(g => g.Genre)
            .Where(m => EF.Functions.ILike(m.Title, pattern))
            .OrderByDescending(m => m.ImdbRating)
            .Take(50)
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

    public async Task<IEnumerable<Movie>> GetByGenreAsync(Guid genreId)
    {
        return await _context.Movies
            .AsNoTracking()
            .Include(m => m.MovieGenres)
                .ThenInclude(g => g.Genre)
            .Where(m => m.MovieGenres.Any(g => g.GenreId == genreId))
            .OrderByDescending(m => m.ImdbRating)
            .ToListAsync();
    }

    public async Task<IEnumerable<string>> GetAvailableCountriesAsync()
    {
        return await _context.Movies
            .AsNoTracking()
            .Where(m => m.OriginCountry != null && m.OriginCountry != "")
            .Select(m => m.OriginCountry!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }

    /// <summary>
    /// Trending = tổng view hợp lệ có time-decay (half-life 3 ngày) trong 30 ngày gần nhất.
    /// Nguồn: MovieViewSessions (IsCountedAsView, theo ViewDate giờ VN). Phim có view luôn xếp trên phim "bù chỗ trống"
    /// (Views7d = Views30d = 0, Score = 0 → FE không nên hiện "🔥 0 lượt xem").
    /// </summary>
    public async Task<IEnumerable<TrendingMovieProjection>> GetTrendingAsync(DateOnly today, int take = 20)
    {
        const double HALF_LIFE_DAYS = 3.0;
        var from30 = today.AddDays(-29);
        var from7  = today.AddDays(-6);

        var daily = await _context.MovieViewSessions
            .AsNoTracking()
            .Where(s => s.IsCountedAsView && s.ViewDate >= from30 && s.ViewDate <= today)
            .GroupBy(s => new { s.MovieId, s.ViewDate })
            .Select(g => new { g.Key.MovieId, g.Key.ViewDate, Views = g.Count() })
            .ToListAsync();

        var stats = daily
            .GroupBy(d => d.MovieId)
            .ToDictionary(g => g.Key, g => new
            {
                V7    = g.Where(d => d.ViewDate >= from7).Sum(d => d.Views),
                V30   = g.Sum(d => d.Views),
                Score = g.Sum(d => d.Views * Math.Pow(0.5, (today.DayNumber - d.ViewDate.DayNumber) / HALF_LIFE_DAYS))
            });

        var now = DateTime.UtcNow;
        var ids = stats.Keys.ToList();

        // Phim chưa publish / chưa chiếu không được lọt trending
        var released = _context.Movies
            .AsNoTracking()
            .Where(m => m.IsPublished && (!m.ReleaseDate.HasValue || m.ReleaseDate <= now));

        // Lọc published/đã chiếu TRƯỚC, rồi chọn top N id, chỉ load entity cho N phim đó
        var eligibleIds = await released
            .Where(m => ids.Contains(m.Id))
            .Select(m => m.Id)
            .ToListAsync();

        var topIds = eligibleIds
            .OrderByDescending(id => stats[id].Score)
            .ThenByDescending(id => stats[id].V30)
            .Take(take)
            .ToList();

        var loaded = await released
            .Where(m => topIds.Contains(m.Id))
            .Include(m => m.MovieGenres).ThenInclude(g => g.Genre)
            .ToDictionaryAsync(m => m.Id);

        var result = topIds
            .Where(loaded.ContainsKey)
            .Select(id => new TrendingMovieProjection
            {
                Movie    = loaded[id],
                Views7d  = stats[id].V7,
                Views30d = stats[id].V30,
                Score    = stats[id].Score
            })
            .ToList();

        // Site ít traffic: bù bằng phim mới nhất / rating cao để trang chủ không trống
        if (result.Count < take)
        {
            var have = result.Select(r => r.Movie.Id).ToList();
            var fillers = await released
                .Where(m => !have.Contains(m.Id))
                .OrderByDescending(m => m.ReleaseDate)
                .ThenByDescending(m => m.ImdbRating)
                .Include(m => m.MovieGenres).ThenInclude(g => g.Genre)
                .Take(take - result.Count)
                .ToListAsync();

            result.AddRange(fillers.Select(m => new TrendingMovieProjection
            {
                Movie = m, Views7d = 0, Views30d = 0, Score = 0
            }));
        }

        return result;
    }
}