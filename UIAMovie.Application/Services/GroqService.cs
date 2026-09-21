// UIAMovie.Infrastructure/Services/GroqService.cs
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using UIAMovie.Application.AI;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Infrastructure.AI.Parsing;
using UIAMovie.Infrastructure.AI.Providers;

namespace UIAMovie.Infrastructure.Services;

public sealed class GroqService : IGroqService
{
    private readonly IAiProvider _provider;
    private readonly ICacheService _cache;
    private readonly ILogger<GroqService> _logger;

    private const int RecommendMovieLimit = 20;
    private const int SearchMovieLimit = 25;
    private const int MaxOutputTokens = 500;

    public GroqService(
        IAiProvider provider,
        ICacheService cache,
        ILogger<GroqService> logger)
    {
        _provider = provider;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> ChatAsync(
        string userMessage,
        string? systemContext = null,
        List<ChatMessageDTO>? history = null)
    {
        var system = systemContext ?? MoviePrompts.ChatSystem;
        var messages = new List<AiProviderMessage>
        {
            new() { Role = "system", Content = system }
        };

        if (history is { Count: > 0 })
        {
            var trimmed = history
                .Where(h => h.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(h.Content))
                .TakeLast(8);

            foreach (var h in trimmed)
                messages.Add(new AiProviderMessage { Role = h.Role, Content = h.Content });
        }

        messages.Add(new AiProviderMessage { Role = "user", Content = userMessage });

        var reply = await _provider.GenerateChatResponseAsync(
            messages, new AiProviderOptions { MaxTokens = MaxOutputTokens });

        return string.IsNullOrWhiteSpace(reply)
            ? "Xin lỗi, hệ thống AI đang quá tải. Bạn vui lòng thử lại sau ít phút nhé!"
            : reply;
    }

    public async Task<List<Guid>> RecommendMoviesAsync(
        List<string> watchedTitles,
        List<string> favoriteGenres,
        List<MovieContext> availableMovies)
    {
        var cacheKey = BuildCacheKeyHash("rec", watchedTitles, favoriteGenres);
        var cached = await _cache.GetAsync<List<Guid>>(cacheKey);
        if (cached != null) return cached;

        var subset = SelectMoviesForRecommend(availableMovies, favoriteGenres, RecommendMovieLimit);
        var movieCsv = AiMovieCsvBuilder.Build(subset);
        var userMessage = MoviePrompts.BuildRecommendUser(
            watched: string.Join(", ", watchedTitles.Take(15)),
            genres: string.Join(", ", favoriteGenres),
            movieCsv: movieCsv);

        var result = await ParseJsonGuidArrayAsync(MoviePrompts.RecommendSystem, userMessage);
        if (result.Count > 0)
            await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(30));

        return result;
    }

    public async Task<List<Guid>> MoodRecommendAsync(string mood, string targetGenres, string movieCsv)
    {
        var userMessage = MoviePrompts.BuildMoodUser(mood, targetGenres, movieCsv);
        return await ParseJsonGuidArrayAsync(MoviePrompts.MoodSystem, userMessage);
    }

    public async Task<List<Guid>> SmartSearchAsync(string query, List<MovieContext> availableMovies)
    {
        var cacheKey = $"ai:search:{query.ToLowerInvariant().Trim()}";
        var cached = await _cache.GetAsync<List<Guid>>(cacheKey);
        if (cached != null) return cached;

        var subset = SelectMoviesForSearch(availableMovies, query, SearchMovieLimit);
        var movieCsv = AiMovieCsvBuilder.Build(subset);
        var userMessage = MoviePrompts.BuildSearchUser(query, movieCsv);

        var result = await ParseJsonGuidArrayAsync(MoviePrompts.SearchSystem, userMessage);
        if (result.Count > 0)
            await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(15));

        return result;
    }

    public async Task<List<Guid>> RecommendTvShowsAsync(
        List<string> watchedTitles,
        List<string> favoriteGenres,
        List<TvShowContext> availableShows)
    {
        var cacheKey = BuildCacheKeyHash("tv:rec", watchedTitles, favoriteGenres);
        var cached = await _cache.GetAsync<List<Guid>>(cacheKey);
        if (cached != null) return cached;

        var subset = SelectTvShowsForRecommend(availableShows, favoriteGenres, RecommendMovieLimit);
        var showCsv = AiTvShowCsvBuilder.Build(subset);
        var userMessage = MoviePrompts.BuildTvShowRecommendUser(
            watched: string.Join(", ", watchedTitles.Take(15)),
            genres: string.Join(", ", favoriteGenres),
            showCsv: showCsv);

        var result = await ParseJsonGuidArrayAsync(MoviePrompts.TvShowRecommendSystem, userMessage);
        if (result.Count > 0)
            await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(30));

        return result;
    }

    public async Task<List<Guid>> SmartSearchTvShowsAsync(string query, List<TvShowContext> availableShows)
    {
        var cacheKey = $"ai:tv:search:{query.ToLowerInvariant().Trim()}";
        var cached = await _cache.GetAsync<List<Guid>>(cacheKey);
        if (cached != null) return cached;

        var subset = SelectTvShowsForSearch(availableShows, query, SearchMovieLimit);
        var showCsv = AiTvShowCsvBuilder.Build(subset);
        var userMessage = MoviePrompts.BuildTvShowSearchUser(query, showCsv);

        var result = await ParseJsonGuidArrayAsync(MoviePrompts.TvShowSearchSystem, userMessage);
        if (result.Count > 0)
            await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(15));

        return result;
    }

    private async Task<List<Guid>> ParseJsonGuidArrayAsync(string systemPrompt, string userMessage)
    {
        var raw = await _provider.GenerateChatResponseAsync(
            new List<AiProviderMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userMessage }
            },
            new AiProviderOptions { MaxTokens = MaxOutputTokens, EnforceJsonObject = false });

        return AiJsonParser.ParseGuidArray(raw, _logger);
    }

    private static List<MovieContext> SelectMoviesForRecommend(List<MovieContext> all, List<string> genres, int limit)
    {
        var genreSet = genres.Select(g => g.ToLowerInvariant()).ToHashSet();
        return all
            .Where(m => !string.IsNullOrWhiteSpace(m.Description))
            .OrderByDescending(m => m.Genres.Split(',').Any(g => genreSet.Contains(g.Trim().ToLowerInvariant())) ? 1 : 0)
            .ThenByDescending(m => m.Rating)
            .Take(limit)
            .ToList();
    }

    private static List<MovieContext> SelectMoviesForSearch(List<MovieContext> all, string query, int limit)
    {
        var tokens = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        return all
            .OrderByDescending(m => (tokens.Any(t => m.Title.ToLowerInvariant().Contains(t)) ? 3 : 0) + m.Rating * 0.1)
            .Take(limit)
            .ToList();
    }

    private static List<TvShowContext> SelectTvShowsForRecommend(List<TvShowContext> all, List<string> genres, int limit)
    {
        var genreSet = genres.Select(g => g.ToLowerInvariant()).ToHashSet();
        return all
            .Where(s => !string.IsNullOrWhiteSpace(s.Description))
            .OrderByDescending(s => s.Genres.Split(',').Any(g => genreSet.Contains(g.Trim().ToLowerInvariant())) ? 1 : 0)
            .ThenByDescending(s => s.Rating)
            .Take(limit)
            .ToList();
    }

    private static List<TvShowContext> SelectTvShowsForSearch(List<TvShowContext> all, string query, int limit)
    {
        var tokens = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        return all
            .OrderByDescending(s => (tokens.Any(t => s.Title.ToLowerInvariant().Contains(t)) ? 3 : 0) + s.Rating * 0.1)
            .Take(limit)
            .ToList();
    }

    private static string BuildCacheKeyHash(string prefix, List<string> a, List<string> b)
    {
        var raw = string.Join(",", a.Take(10).OrderBy(x => x)) + "|" + string.Join(",", b.OrderBy(x => x));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16];
        return $"ai:{prefix}:{hash}";
    }
}