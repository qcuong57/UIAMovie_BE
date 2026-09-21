// UIAMovie.Infrastructure/Data/Repositories/MovieRepository.cs

using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.DTOs;
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

    public async Task<IEnumerable<TrendingMovieProjection>> GetTrendingAsync(
        DateTime cutoff7,
        DateTime cutoff30,
        int take = 20)
    {
        var viewStats = await _context.WatchHistories
            .AsNoTracking()
            .Where(wh => wh.WatchedAt >= cutoff30)
            .GroupBy(wh => wh.MovieId)
            .Select(g => new
            {
                MovieId  = g.Key,
                Views30d = g.Count(),
                Views7d  = g.Count(wh => wh.WatchedAt >= cutoff7)
            })
            .ToDictionaryAsync(x => x.MovieId, x => x);

        var movies = await _context.Movies
            .AsNoTracking()
            .Where(m => m.IsPublished)
            .Include(m => m.MovieGenres)
                .ThenInclude(g => g.Genre)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var result = movies
            .Select(m =>
            {
                viewStats.TryGetValue(m.Id, out var stat);
                var v7d  = stat?.Views7d  ?? 0;
                var v30d = stat?.Views30d ?? 0;

                double recencyBonus = 0;
                if (m.ReleaseDate.HasValue)
                {
                    var daysOld = (now - m.ReleaseDate.Value).TotalDays;
                    recencyBonus = daysOld switch
                    {
                        <= 7   => 120,
                        <= 30  => 80,
                        <= 90  => 50,
                        <= 180 => 25,
                        <= 365 => 10,
                        _      => 0
                    };
                }

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