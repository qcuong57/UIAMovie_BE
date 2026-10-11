// UIAMovie.API/Controllers/RecommendationController.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.AI;
using UIAMovie.Application.AI.Models;
using UIAMovie.Application.AI.Retrieval;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Application.Services;

namespace UIAMovie.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RecommendationController : ControllerBase
{
    private const int MaxLimit = 50;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(12);

    private readonly IMovieRetriever _movieRetriever;
    private readonly IMovieService _movieService;
    private readonly IGroqService _groqService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<RecommendationController> _logger;

    public RecommendationController(
        IMovieRetriever movieRetriever,
        IMovieService movieService,
        IGroqService groqService,
        ICacheService cacheService,
        ILogger<RecommendationController> logger)
    {
        _movieRetriever = movieRetriever;
        _movieService = movieService;
        _groqService = groqService;
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <summary>
    /// GET: /api/recommendation/personalized?limit=15
    /// Gợi ý phim cá nhân hóa dựa trên lịch sử xem và Groq AI.
    /// Khách vãng lai, hoặc bất kỳ lỗi nào trong pipeline cá nhân hóa, đều fallback về Top Trending.
    /// Endpoint này không bao giờ trả 5xx: gợi ý chỉ là tính năng phụ của trang chủ.
    /// </summary>
    [HttpGet("personalized")]
    public async Task<IActionResult> GetPersonalizedRecommendations([FromQuery] int limit = 15)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);

        var hasUser = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                      && userId != Guid.Empty;

        if (hasUser)
        {
            try
            {
                return Ok(await BuildPersonalizedAsync(userId, limit));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Personalized recommendation failed for user {UserId}; falling back to trending", userId);
            }
        }

        return Ok(await GetTrendingSafeAsync(limit));
    }

    private async Task<List<AiCatalogItem>> BuildPersonalizedAsync(Guid userId, int limit)
    {
        var cacheKey = $"recommendations:user:{userId}";
        var cached = await TryGetCacheAsync(cacheKey);
        if (cached is { Count: > 0 })
            return cached.Take(limit).ToList();

        // 1. Lịch sử xem + yêu thích
        var watchHistory = (await _movieService.GetWatchHistoryAsync(userId)).ToList();
        var favorites = (await _movieService.GetFavoritesAsync(userId)).ToList();

        var watchedMovieIds = watchHistory.Select(w => w.MovieId).ToHashSet();
        var watchedTitles = watchHistory.Select(w => w.MovieTitle).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var favoriteTitles = favorites.Select(f => f.MovieTitle).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

        // 2. Ứng viên từ catalog nội bộ
        var (catalogItems, rawMovies, _) = await _movieRetriever.RetrieveMoviesAsync(
            string.Empty,
            new AiQueryHints { MinRating = 6.0 });

        if (catalogItems is null || !catalogItems.Any())
            return await GetTrendingSafeAsync(limit);

        var unwatched = rawMovies.Where(m => !watchedMovieIds.Contains(m.Id)).ToList();
        if (unwatched.Count == 0)
            unwatched = rawMovies.ToList();

        // 3. Thể loại user xem nhiều nhất (tính từ phim ĐÃ XEM, không phải từ ứng viên)
        var tasteSource = rawMovies.Where(m => watchedMovieIds.Contains(m.Id)).ToList();
        if (tasteSource.Count == 0)
            tasteSource = unwatched;

        var favoriteGenres = tasteSource
            .SelectMany(m => m.Genres ?? Enumerable.Empty<string>())
            .GroupBy(g => g)
            .OrderByDescending(grp => grp.Count())
            .Take(3)
            .Select(grp => grp.Key)
            .ToList();

        // 4. Nhờ Groq chọn; lỗi/rate-limit thì dùng thứ tự catalog
        var movieContexts = unwatched.Select(m => new MovieContext(
            m.Id,
            m.Title,
            string.Join(", ", m.Genres ?? Enumerable.Empty<string>()),
            (double)m.Rating,
            m.ReleaseDate?.Year,
            m.Description ?? string.Empty)).ToList();

        var recommended = await TryRecommendWithAiAsync(
            catalogItems,
            movieContexts,
            watchedTitles.Count > 0 ? watchedTitles : favoriteTitles,
            favoriteGenres);

        if (recommended.Count == 0)
        {
            recommended = catalogItems
                .Where(c => !watchedMovieIds.Contains(c.Id))
                .ToList();
        }

        if (recommended.Count == 0)
            recommended = catalogItems.ToList();

        var result = recommended.Take(limit).ToList();
        await TrySetCacheAsync(cacheKey, result);
        return result;
    }

    private async Task<List<AiCatalogItem>> TryRecommendWithAiAsync(
        IEnumerable<AiCatalogItem> catalogItems,
        List<MovieContext> movieContexts,
        List<string> watchedTitles,
        List<string> favoriteGenres)
    {
        try
        {
            var guids = await _groqService.RecommendMoviesAsync(
                watchedTitles: watchedTitles,
                favoriteGenres: favoriteGenres,
                availableMovies: movieContexts);

            if (guids.Count == 0) return new();

            // DistinctBy: ToDictionary sẽ ném lỗi nếu catalog có Id trùng
            var map = catalogItems.DistinctBy(c => c.Id).ToDictionary(c => c.Id);
            return guids.Where(map.ContainsKey).Select(g => map[g]).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Groq recommendation failed; using catalog order");
            return new();
        }
    }

    private async Task<List<AiCatalogItem>> GetTrendingSafeAsync(int limit)
    {
        try
        {
            var trending = await _movieRetriever.GetTrendingCatalogAsync(limit);
            return trending?.ToList() ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Trending fallback failed");
            return new();
        }
    }

    // Cache hỏng/không khả dụng không được làm hỏng request
    private async Task<List<AiCatalogItem>?> TryGetCacheAsync(string key)
    {
        try { return await _cacheService.GetAsync<List<AiCatalogItem>>(key); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recommendation cache read failed: {Key}", key);
            return null;
        }
    }

    private async Task TrySetCacheAsync(string key, List<AiCatalogItem> value)
    {
        try { await _cacheService.SetAsync(key, value, CacheTtl); }
        catch (Exception ex) { _logger.LogWarning(ex, "Recommendation cache write failed: {Key}", key); }
    }
}