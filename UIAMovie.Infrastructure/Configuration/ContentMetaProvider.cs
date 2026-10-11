// UIAMovie.Infrastructure/Services/ContentMetaProvider.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data;

namespace UIAMovie.Infrastructure.Services;

public class ContentMetaProvider : IContentMetaProvider
{
    // Admin đổi IsPublished / IsPremium / thời lượng → heartbeat thấy chậm tối đa chừng này.
    private static readonly TimeSpan MetaTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan EpisodeTtl = TimeSpan.FromMinutes(10);

    private readonly MovieDbContext _db;
    private readonly IMemoryCache _cache;

    public ContentMetaProvider(MovieDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<ContentMeta?> GetMovieAsync(Guid movieId)
    {
        var key = $"vmeta:movie:{movieId}";
        if (_cache.TryGetValue(key, out ContentMeta? hit) && hit is not null) return hit;

        var meta = await _db.Movies.AsNoTracking()
            .Where(m => m.Id == movieId)
            .Select(m => new ContentMeta(m.IsPublished, m.IsPremium, m.Duration))
            .FirstOrDefaultAsync();

        // Không cache kết quả "không tồn tại" → bot gửi GUID ngẫu nhiên không làm phình RAM
        if (meta is not null) _cache.Set(key, meta, MetaTtl);
        return meta;
    }

    public async Task<ContentMeta?> GetTvShowAsync(Guid tvShowId)
    {
        var key = $"vmeta:tv:{tvShowId}";
        if (_cache.TryGetValue(key, out ContentMeta? hit) && hit is not null) return hit;

        var meta = await _db.TvShows.AsNoTracking()
            .Where(t => t.Id == tvShowId)
            .Select(t => new ContentMeta(t.IsPublished, t.IsPremium, t.EpisodeRuntime))
            .FirstOrDefaultAsync();

        if (meta is not null) _cache.Set(key, meta, MetaTtl);
        return meta;
    }

    public async Task<int?> GetEpisodeRuntimeMinutesAsync(Guid tvShowId, Guid episodeId)
    {
        var key = $"vmeta:ep:{tvShowId}:{episodeId}";
        if (_cache.TryGetValue(key, out int? cached)) return cached;

        var found = await _db.Set<Episode>().AsNoTracking()
            .Where(e => e.Id == episodeId && e.Season.TvShowId == tvShowId)
            .Select(e => new { e.Runtime })
            .FirstOrDefaultAsync();

        if (found is null) return null;           // không tồn tại → không cache
        _cache.Set(key, found.Runtime, EpisodeTtl);
        return found.Runtime;
    }
}