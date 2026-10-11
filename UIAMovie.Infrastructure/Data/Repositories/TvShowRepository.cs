// UIAMovie.Infrastructure/Data/Repositories/TvShowRepository.cs

using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data;

namespace UIAMovie.Infrastructure.Data.Repositories;

public class TvShowRepository : Repository<TvShow>, ITvShowRepository
{
    private readonly MovieDbContext _context;

    public TvShowRepository(MovieDbContext context) : base(context)
    {
        _context = context;
    }

    /// <summary>
    /// Load TvShow kèm Cast, Director, Images, Videos, Genres, Seasons metadata.
    /// KHÔNG include Episodes để tránh cartesian explosion với show nhiều tập (One Piece...).
    /// Episodes load riêng qua GetSeasonWithEpisodesAsync khi user click vào season.
    /// </summary>
    public async Task<TvShow?> GetByIdWithDetailsAsync(Guid id)
    {
        return await _context.TvShows
            .AsNoTracking()
            // Cast (giới hạn 20) + Person + PersonImages
            .Include(t => t.TvShowCasts.OrderBy(c => c.Order).Take(20))
                .ThenInclude(c => c.Person)
                    .ThenInclude(p => p.Images)
            // Directors + Person + PersonImages
            .Include(t => t.TvShowDirectors)
                .ThenInclude(d => d.Person)
                    .ThenInclude(p => p.Images)
            // Images
            .Include(t => t.TvShowImages)
            // Videos (trailers)
            .Include(t => t.TvShowVideos)
            // Genres
            .Include(t => t.TvShowGenres)
                .ThenInclude(g => g.Genre)
            // Seasons metadata ONLY — KHÔNG .ThenInclude(s => s.Episodes)
            .Include(t => t.Seasons)
            // AsSplitQuery: tách thành nhiều SELECT riêng, tránh cartesian product
            .AsSplitQuery()
            // Filter id 1 lần duy nhất ở đây, không filter 2 lần
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    /// <summary>
    /// Load 1 season + toàn bộ episodes của season đó, dùng cho endpoint
    /// GET /api/tvshows/{id}/seasons/{seasonNumber}.
    /// Tách riêng để không bao giờ load tất cả episodes khi GET /tvshows/{id}.
    /// </summary>
    public async Task<Season?> GetSeasonWithEpisodesAsync(Guid tvShowId, int seasonNumber)
    {
        return await _context.Seasons
            .AsNoTracking()
            .Where(s => s.TvShowId == tvShowId && s.SeasonNumber == seasonNumber)
            .Include(s => s.Episodes.OrderBy(e => e.EpisodeNumber))
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Filter + Sort + Paginate trực tiếp trên DB — không load toàn bộ về RAM.
    ///
    /// Khi có Ids (AI recommend): trả về đúng các show được chỉ định, giữ thứ tự AI.
    /// Khi không có Ids: áp dụng filter chuẩn + ORDER BY + OFFSET/FETCH trên SQL.
    /// </summary>
    public async Task<(IEnumerable<TvShow> Items, int TotalCount)> GetPagedAsync(FilterTvShowsDTO filter)
    {
        var query = _context.TvShows
            .AsNoTracking()
            .Include(t => t.TvShowGenres)
                .ThenInclude(g => g.Genre)
            .Include(t => t.TvShowVideos)
            .AsQueryable();

        // ── 1. Filter theo Ids (AI mode) ────────────────────────────────────────
        if (filter.Ids is { Count: > 0 })
        {
            query = query.Where(t => filter.Ids.Contains(t.Id));
            var idItems = await query.ToListAsync();

            var ordered = idItems
                .OrderBy(t => filter.Ids.IndexOf(t.Id))
                .ToList();

            return (ordered, ordered.Count);
        }

        // ── 2. Tìm kiếm tên series ────────────────────────────────────────────
        var hasSearch = !string.IsNullOrWhiteSpace(filter.Search);
        if (hasSearch)
        {
            var searchPattern = $"%{filter.Search.Trim()}%";
            query = query.Where(t => EF.Functions.ILike(t.Title, searchPattern));
        }

        if (filter.GenreIds is { Count: > 0 })
            query = query.Where(t => t.TvShowGenres.Any(g => filter.GenreIds.Contains(g.GenreId)));

        if (filter.MinRating.HasValue)
            query = query.Where(t => t.ImdbRating >= filter.MinRating);

        if (filter.MaxRating.HasValue)
            query = query.Where(t => t.ImdbRating <= filter.MaxRating);

        if (filter.FromFirstAirDate.HasValue)
        {
            var from = DateTime.SpecifyKind(filter.FromFirstAirDate.Value, DateTimeKind.Utc);
            query = query.Where(t => t.FirstAirDate >= from);
        }

        if (filter.ToFirstAirDate.HasValue)
        {
            var to = DateTime.SpecifyKind(filter.ToFirstAirDate.Value, DateTimeKind.Utc);
            query = query.Where(t => t.FirstAirDate <= to);
        }

        // ── 3. FIX: Sắp lên sóng / Đã lên sóng ───────────────────────────────
        // NẾU người dùng đang tìm kiếm theo từ khóa tên -> KHÔNG ĐƯỢC CHẶN SHOW SẮP LÊN SÓNG
        var now = DateTime.UtcNow;
        if (!hasSearch)
        {
            query = filter.IsUpcoming == true
                ? query.Where(t => t.FirstAirDate.HasValue && t.FirstAirDate.Value > now)
                : query.Where(t => !t.FirstAirDate.HasValue || t.FirstAirDate.Value <= now);
        }

        if (!string.IsNullOrWhiteSpace(filter.OriginCountry))
            query = query.Where(t => t.OriginCountry != null &&
                                     t.OriginCountry.ToLower() == filter.OriginCountry.Trim().ToLower());

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(t => t.Status != null &&
                                     t.Status.ToLower() == filter.Status.Trim().ToLower());

        // ── 4. Sort ─────────────────────────────────────────────────────────────
        query = filter.SortBy?.ToLower() switch
        {
            "title"        => filter.SortDesc
                                  ? query.OrderByDescending(t => t.Title)
                                  : query.OrderBy(t => t.Title),
            "firstairdate" => filter.SortDesc
                                  ? query.OrderByDescending(t => t.FirstAirDate)
                                  : query.OrderBy(t => t.FirstAirDate),
            _              => filter.SortDesc
                                  ? query.OrderByDescending(t => t.ImdbRating)
                                  : query.OrderBy(t => t.ImdbRating)
        };

        // ── Count + Paginate ─────────────────────────────────────────────────
        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<IEnumerable<TvShow>> SearchByTitleAsync(string query)
    {
        var pattern = $"%{query.Trim()}%";
        return await _context.TvShows
            .AsNoTracking()
            .Include(t => t.TvShowGenres)
                .ThenInclude(g => g.Genre)
            .Where(t => EF.Functions.ILike(t.Title, pattern))
            .OrderByDescending(t => t.ImdbRating)
            .Take(50)
            .ToListAsync();
    }

    public async Task<TvShow?> GetByTmdbIdAsync(int tmdbId)
    {
        return await _context.TvShows
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TmdbId == tmdbId);
    }

    public async Task<IEnumerable<TvShow>> GetByGenreAsync(Guid genreId)
    {
        return await _context.TvShows
            .AsNoTracking()
            .Include(t => t.TvShowGenres)
                .ThenInclude(g => g.Genre)
            .Where(t => t.TvShowGenres.Any(g => g.GenreId == genreId))
            .OrderByDescending(t => t.ImdbRating)
            .ToListAsync();
    }

    public async Task<IEnumerable<string>> GetAvailableCountriesAsync()
    {
        return await _context.TvShows
            .AsNoTracking()
            .Where(t => t.OriginCountry != null && t.OriginCountry != "")
            .Select(t => t.OriginCountry!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }
    
    public async Task<IEnumerable<TvShow>> SearchByActorNameAsync(string actorName)
    {
        return await _context.TvShows
            .AsNoTracking()
            // Cast (giới hạn 20) + Person + PersonImages — khớp với GetByIdWithDetailsAsync
            .Include(t => t.TvShowCasts.OrderBy(c => c.Order).Take(20))
                .ThenInclude(c => c.Person)
                    .ThenInclude(p => p.Images)
            // Genres
            .Include(t => t.TvShowGenres)
                .ThenInclude(g => g.Genre)
            // Videos (để MapToDTO lấy TrailerKey)
            .Include(t => t.TvShowVideos)
            // Directors
            .Include(t => t.TvShowDirectors)
                .ThenInclude(d => d.Person)
                    .ThenInclude(p => p.Images)
            // Images
            .Include(t => t.TvShowImages)
            // Seasons metadata only — KHÔNG include Episodes
            .Include(t => t.Seasons)
            .Where(t => t.TvShowCasts
                .Any(c => c.Person != null &&
                          c.Person.Name.ToLower().Contains(actorName.ToLower())))
            .OrderByDescending(t => t.ImdbRating)
            .Take(50)
            // AsSplitQuery: tránh cartesian product khi có nhiều Include
            .AsSplitQuery()
            .ToListAsync();
    }

    /// <summary>
    /// Trending TV show = tổng view hợp lệ có time-decay (half-life 3 ngày) trong 30 ngày gần nhất,
    /// nguồn TvShowViewSessions (1 view/người/ngày/show). Show có view luôn xếp trên show "bù chỗ trống".
    /// </summary>
    public async Task<IEnumerable<TrendingTvShowProjection>> GetTrendingAsync(DateOnly today, int take = 20)
    {
        const double HALF_LIFE_DAYS = 3.0;
        var from30 = today.AddDays(-29);
        var from7  = today.AddDays(-6);

        var daily = await _context.TvShowViewSessions
            .AsNoTracking()
            .Where(s => s.IsCountedAsView && s.ViewDate >= from30 && s.ViewDate <= today)
            .GroupBy(s => new { s.TvShowId, s.ViewDate })
            .Select(g => new { g.Key.TvShowId, g.Key.ViewDate, Views = g.Count() })
            .ToListAsync();

        var stats = daily
            .GroupBy(d => d.TvShowId)
            .ToDictionary(g => g.Key, g => new
            {
                V7    = g.Where(d => d.ViewDate >= from7).Sum(d => d.Views),
                V30   = g.Sum(d => d.Views),
                Score = g.Sum(d => d.Views * Math.Pow(0.5, (today.DayNumber - d.ViewDate.DayNumber) / HALF_LIFE_DAYS))
            });

        var now = DateTime.UtcNow;
        var ids = stats.Keys.ToList();

        // Show chưa publish / chưa lên sóng không được lọt trending
        var aired = _context.TvShows
            .AsNoTracking()
            .Where(t => t.IsPublished && (!t.FirstAirDate.HasValue || t.FirstAirDate <= now));

        var eligibleIds = await aired
            .Where(t => ids.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync();

        var topIds = eligibleIds
            .OrderByDescending(id => stats[id].Score)
            .ThenByDescending(id => stats[id].V30)
            .Take(take)
            .ToList();

        var loaded = await aired
            .Where(t => topIds.Contains(t.Id))
            .Include(t => t.TvShowGenres).ThenInclude(g => g.Genre)
            .Include(t => t.TvShowVideos)
            .ToDictionaryAsync(t => t.Id);

        var result = topIds
            .Where(loaded.ContainsKey)
            .Select(id => new TrendingTvShowProjection
            {
                TvShow   = loaded[id],
                Views7d  = stats[id].V7,
                Views30d = stats[id].V30,
                Score    = stats[id].Score
            })
            .ToList();

        if (result.Count < take)
        {
            var have = result.Select(r => r.TvShow.Id).ToList();
            var fillers = await aired
                .Where(t => !have.Contains(t.Id))
                .OrderByDescending(t => t.FirstAirDate)
                .ThenByDescending(t => t.ImdbRating)
                .Include(t => t.TvShowGenres).ThenInclude(g => g.Genre)
                .Include(t => t.TvShowVideos)
                .Take(take - result.Count)
                .ToListAsync();

            result.AddRange(fillers.Select(t => new TrendingTvShowProjection
            {
                TvShow = t, Views7d = 0, Views30d = 0, Score = 0
            }));
        }

        return result;
    }
}