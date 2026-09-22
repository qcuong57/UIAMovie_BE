// UIAMovie.API/Controllers/RecommendationController.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.AI;
using UIAMovie.Application.AI.Models;
using UIAMovie.Application.AI.Retrieval;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Services;

namespace UIAMovie.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RecommendationController : ControllerBase
{
    private readonly IMovieRetriever _movieRetriever;
    private readonly IMovieService _movieService;
    private readonly IGroqService _groqService;
    private readonly ICacheService _cacheService;

    public RecommendationController(
        IMovieRetriever movieRetriever,
        IMovieService movieService,
        IGroqService groqService,
        ICacheService cacheService)
    {
        _movieRetriever = movieRetriever;
        _movieService = movieService;
        _groqService = groqService;
        _cacheService = cacheService;
    }

    /// <summary>
    /// GET: /api/recommendation/personalized?limit=15
    /// Gợi ý phim cá nhân hóa dựa trên lịch sử xem và Groq AI.
    /// Khách vãng lai chưa đăng nhập sẽ tự động fallback về Top Trending.
    /// </summary>
    [HttpGet("personalized")]
    public async Task<IActionResult> GetPersonalizedRecommendations([FromQuery] int limit = 15)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(userIdStr, out var userId);

        // 1. Nếu chưa đăng nhập: Trả về danh sách Trending Catalog
        if (userId == Guid.Empty)
        {
            var fallbackTrending = await _movieRetriever.GetTrendingCatalogAsync(limit);
            return Ok(fallbackTrending);
        }

        var cacheKey = $"recommendations:user:{userId}";
        var cached = await _cacheService.GetAsync<List<AiCatalogItem>>(cacheKey);
        if (cached != null) 
        {
            return Ok(cached);
        }

        // 2. Lấy Watch History và Favorites của User
        var watchHistory = (await _movieService.GetWatchHistoryAsync(userId)).ToList();
        var favorites = (await _movieService.GetFavoritesAsync(userId)).ToList();

        var watchedMovieIds = watchHistory.Select(w => w.MovieId).ToHashSet();
        var watchedTitles = watchHistory.Select(w => w.MovieTitle).ToList();
        var favoriteTitles = favorites.Select(f => f.MovieTitle).ToList();

        // 3. Lấy ứng viên phim từ catalog nội bộ
        var (catalogItems, rawMovies, _) = await _movieRetriever.RetrieveMoviesAsync(
            string.Empty, 
            new AiQueryHints { MinRating = 6.0 }
        );

        // Lọc bỏ những phim user đã xem
        var unWatchedMovies = rawMovies
            .Where(m => !watchedMovieIds.Contains(m.Id))
            .ToList();

        if (unWatchedMovies.Count == 0)
        {
            unWatchedMovies = rawMovies;
        }

        // 4. Gom các thể loại user xem nhiều nhất để gửi cho AI
        var favoriteGenres = unWatchedMovies
            .SelectMany(m => m.Genres)
            .GroupBy(g => g)
            .OrderByDescending(grp => grp.Count())
            .Take(3)
            .Select(grp => grp.Key)
            .ToList();

        // 5. Chuẩn hóa sang List<MovieContext> khớp chữ ký IGroqService
        var movieContexts = unWatchedMovies.Select(m => new MovieContext(
            m.Id,
            m.Title,
            string.Join(", ", m.Genres),
            (double)m.Rating,
            m.ReleaseDate?.Year,
            m.Description ?? string.Empty
        )).ToList();

        List<AiCatalogItem> recommendedItems = new();

        try
        {
            // Gọi phương thức RecommendMoviesAsync có sẵn trong IGroqService
            var recommendedGuids = await _groqService.RecommendMoviesAsync(
                watchedTitles: watchedTitles.Count > 0 ? watchedTitles : favoriteTitles,
                favoriteGenres: favoriteGenres,
                availableMovies: movieContexts
            );

            if (recommendedGuids.Count > 0)
            {
                var candidateMap = catalogItems.ToDictionary(c => c.Id);
                foreach (var guid in recommendedGuids)
                {
                    if (candidateMap.TryGetValue(guid, out var item))
                    {
                        recommendedItems.Add(item);
                    }
                }
            }
        }
        catch
        {
            // Fallback an toàn nếu Groq AI chạm rate-limit hoặc lỗi kết nối
            recommendedItems = catalogItems
                .Where(c => !watchedMovieIds.Contains(c.Id))
                .Take(limit)
                .ToList();
        }

        // Đảm bảo luôn có dữ liệu trả về cho Frontend
        if (recommendedItems.Count == 0)
        {
            recommendedItems = catalogItems.Take(limit).ToList();
        }

        // Giới hạn số lượng theo query param
        var finalResult = recommendedItems.Take(limit).ToList();

        // 6. Cache kết quả trong 12 tiếng để tránh lặp request AI
        await _cacheService.SetAsync(cacheKey, finalResult, TimeSpan.FromHours(12));

        return Ok(finalResult);
    }
}