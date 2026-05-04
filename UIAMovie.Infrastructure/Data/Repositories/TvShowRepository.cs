// UIAMovie.Infrastructure/Data/Repositories/TvShowRepository.cs

using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
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
            .AsQueryable();

        // ── Filter theo Ids (AI mode) ────────────────────────────────────────
        if (filter.Ids is { Count: > 0 })
        {
            query = query.Where(t => filter.Ids.Contains(t.Id));
            var idItems = await query.ToListAsync();

            var ordered = idItems
                .OrderBy(t => filter.Ids.IndexOf(t.Id))
                .ToList();

            return (ordered, ordered.Count);
        }

        // ── Các filter thông thường ──────────────────────────────────────────

        if (!string.IsNullOrWhiteSpace(filter.Search))
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

        if (!string.IsNullOrWhiteSpace(filter.OriginCountry))
            query = query.Where(t => t.OriginCountry != null &&
                                     t.OriginCountry.ToLower() == filter.OriginCountry.Trim().ToLower());

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(t => t.Status != null &&
                                     t.Status.ToLower() == filter.Status.Trim().ToLower());

        // ── Sort ─────────────────────────────────────────────────────────────
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
}