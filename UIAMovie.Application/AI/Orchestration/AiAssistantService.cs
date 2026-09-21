using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UIAMovie.Application.AI.Intent;
using UIAMovie.Application.AI.Models;
using UIAMovie.Application.AI.Retrieval;
using UIAMovie.Application.AI.Security;
using UIAMovie.Application.AI.Tools;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.DTOs.AI;
using UIAMovie.Application.Interfaces;
using UIAMovie.Infrastructure.AI.Providers;

namespace UIAMovie.Application.AI.Orchestration;

public class AiAssistantService : IAiAssistantService
{
    private readonly IAiRouter _router;
    private readonly IMovieRetriever _movieRetriever;
    private readonly ITvShowRetriever _tvShowRetriever;
    private readonly ISiteKnowledgeRetriever _siteRetriever;
    private readonly IUserContextRetriever _userContextRetriever;
    private readonly IAiProvider _provider;
    private readonly MovieCompareTool _compareTool;
    private readonly ReviewSummaryTool _reviewTool;
    private readonly ILogger<AiAssistantService> _logger;

    private const int MaxMovieCards = 6;

    public AiAssistantService(
        IAiRouter router,
        IMovieRetriever movieRetriever,
        ITvShowRetriever tvShowRetriever,
        ISiteKnowledgeRetriever siteRetriever,
        IUserContextRetriever userContextRetriever,
        IAiProvider provider,
        MovieCompareTool compareTool,
        ReviewSummaryTool reviewTool,
        ILogger<AiAssistantService> logger)
    {
        _router = router;
        _movieRetriever = movieRetriever;
        _tvShowRetriever = tvShowRetriever;
        _siteRetriever = siteRetriever;
        _userContextRetriever = userContextRetriever;
        _provider = provider;
        _compareTool = compareTool;
        _reviewTool = reviewTool;
        _logger = logger;
    }

    public async Task<AiChatResponseDto> ProcessChatAsync(
        AiChatRequestDto request,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var sanitizedMessage = AiInputSanitizer.SanitizeUserMessage(request.Message);
        var recentHistory = request.History
            .Where(h => h.Role == "user")
            .TakeLast(3)
            .Select(h => h.Content)
            .ToList();

        var route = await _router.RouteAsync(sanitizedMessage, recentHistory);
        var userContext = await _userContextRetriever.GetUserContextAsync(userId);

        AiChatResponseDto response = route.Intent switch
        {
            "site" => await HandleSiteChatAsync(request, sanitizedMessage, userContext),
            "mood" => await HandleMoodChatAsync(request, sanitizedMessage),
            "compare" => await HandleCompareChatAsync(request, sanitizedMessage),
            "review" => await HandleReviewChatAsync(request, sanitizedMessage),
            "tvshow" => await HandleTvShowChatAsync(request, sanitizedMessage, userContext),
            _ => await HandleMovieChatAsync(request, sanitizedMessage, userContext),
        };

        sw.Stop();
        response.Meta.ProcessingTimeMs = (int)sw.ElapsedMilliseconds;
        return response;
    }

    private async Task<AiChatResponseDto> HandleSiteChatAsync(
        AiChatRequestDto request, string message, AiUserContext userContext)
    {
        var siteKnowledge = await _siteRetriever.GetSiteKnowledgeAsync(message);
        var systemPrompt = MoviePrompts.SiteGuideSystem + "\n\n" + siteKnowledge;

        var messages = BuildProviderMessages(systemPrompt, message, request.History, userContext);
        var reply = await _provider.GenerateChatResponseAsync(messages);

        if (string.IsNullOrWhiteSpace(reply))
        {
            reply = "Xin lỗi bạn, hệ thống hướng dẫn đang quá tải hoặc tạm thời gián đoạn. Bạn vui lòng thử lại sau ít phút hoặc liên hệ support@uiamovie.vn nhé!";
        }

        var chips = MoviePrompts.GenerateSuggestedChips("site");

        return new AiChatResponseDto
        {
            Message = reply,
            Intent = "site",
            SuggestedActions = chips,
            Actions = chips.Select(c => new AiActionDto { Type = "chip", Label = c, Value = c }).ToList(),
            Meta = new AiResponseMetaDto { Provider = "groq" }
        };
    }

    private async Task<AiChatResponseDto> HandleMovieChatAsync(
        AiChatRequestDto request, string message, AiUserContext userContext)
    {
        var hints = AiQueryParser.Parse(message);
        var (candidates, rawMovies, fallbackApplied) = await _movieRetriever.RetrieveMoviesAsync(message, hints);

        List<TvShowSummaryDTO> fallbackShows = new();
        var lowerMsg = message.ToLowerInvariant();
        var userExplicitlyWantsOnlyMovies = lowerMsg.Contains("phim lẻ") || lowerMsg.Contains("chiếu rạp");

        if (!userExplicitlyWantsOnlyMovies)
        {
            var tvResult = await _tvShowRetriever.RetrieveTvShowsAsync(message, hints);
            if (tvResult.RawShows.Count > 0)
            {
                fallbackShows = tvResult.RawShows;
                candidates.AddRange(tvResult.Items.Take(10));
            }
        }

        string? detailBlock = null;
        if (candidates.Count == 1)
        {
            var first = candidates[0];
            if (first.Kind == "movie")
            {
                var full = await _movieRetriever.GetMovieDetailAsync(first.Id);
                if (full != null)
                {
                    candidates[0] = AiCatalogItem.From(full);
                    detailBlock = AiCatalogCsvBuilder.BuildDetail(candidates[0]);
                }
            }
            else
            {
                var fullTv = await _tvShowRetriever.GetTvShowDetailAsync(first.Id);
                if (fullTv != null)
                {
                    candidates[0] = AiCatalogItem.From(fullTv);
                    detailBlock = AiCatalogCsvBuilder.BuildDetail(candidates[0]);
                }
            }
        }

        var catalogCsv = AiCatalogCsvBuilder.Build(candidates);
        var userPrompt = MoviePrompts.BuildChatUser(
            message,
            catalogCsv: catalogCsv,
            detailBlock: detailBlock,
            filterBlock: hints.Describe(),
            user: userContext);

        var messages = BuildProviderMessages(MoviePrompts.ChatSystem, userPrompt, request.History);
        var reply = await _provider.GenerateChatResponseAsync(messages);

        if (string.IsNullOrWhiteSpace(reply))
        {
            reply = candidates.Count > 0
                ? "Dưới đây là các tác phẩm phù hợp nhất trong hệ thống mà tôi tìm thấy cho bạn:"
                : "Xin lỗi bạn, hiện tại tôi chưa tìm thấy tác phẩm nào hoàn toàn phù hợp với yêu cầu này.";
        }

        var mentionedMovies = ExtractMentionedMovies(reply, rawMovies);
        if (mentionedMovies.Count == 0 && rawMovies.Count > 0)
        {
            mentionedMovies = rawMovies.Take(MaxMovieCards).ToList();
        }

        var mentionedShows = fallbackShows.Count > 0 ? ExtractMentionedTvShows(reply, fallbackShows) : new();
        if (mentionedShows.Count == 0 && fallbackShows.Count > 0)
        {
            mentionedShows = fallbackShows.Take(MaxMovieCards).ToList();
        }

        var chips = MoviePrompts.GenerateSuggestedChips("movie", candidates.FirstOrDefault());

        return new AiChatResponseDto
        {
            Message = reply,
            Intent = fallbackShows.Count > 0 && mentionedMovies.Count == 0 ? "tvshow" : "movie",
            Movies = mentionedMovies,
            TvShows = mentionedShows,
            Items = mentionedMovies.Select(m => new AiRecommendationItemDto
            {
                Id = m.Id,
                Kind = "movie",
                Title = m.Title,
                PosterUrl = m.PosterUrl,
                Rating = m.Rating,
                Year = m.ReleaseDate?.Year,
                IsPremium = m.IsPremium,
                Genres = m.Genres
            }).Concat(mentionedShows.Select(s => new AiRecommendationItemDto
            {
                Id = s.Id,
                Kind = "tv",
                Title = s.Title,
                PosterUrl = s.PosterUrl,
                Rating = s.Rating,
                IsPremium = s.IsPremium,
                Genres = s.Genres
            })).ToList(),
            SuggestedActions = chips,
            Actions = chips.Select(c => new AiActionDto { Type = "chip", Label = c, Value = c }).ToList(),
            Meta = new AiResponseMetaDto { Provider = "groq", FallbackApplied = fallbackApplied }
        };
    }

    private async Task<AiChatResponseDto> HandleTvShowChatAsync(
        AiChatRequestDto request, string message, AiUserContext userContext)
    {
        var hints = AiQueryParser.Parse(message);
        var (candidates, rawShows, fallbackApplied) = await _tvShowRetriever.RetrieveTvShowsAsync(message, hints);

        List<MovieDTO> fallbackMovies = new();
        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(hints.SimilarTo ?? message))
        {
            var movieResult = await _movieRetriever.RetrieveMoviesAsync(message, hints);
            if (movieResult.Items.Count > 0)
            {
                candidates = movieResult.Items;
                fallbackMovies = movieResult.RawMovies;
            }
        }

        string? detailBlock = null;
        if (candidates.Count == 1)
        {
            var first = candidates[0];
            if (first.Kind == "tv")
            {
                var full = await _tvShowRetriever.GetTvShowDetailAsync(first.Id);
                if (full != null)
                {
                    candidates[0] = AiCatalogItem.From(full);
                    detailBlock = AiCatalogCsvBuilder.BuildDetail(candidates[0]);
                }
            }
            else
            {
                var fullMovie = await _movieRetriever.GetMovieDetailAsync(first.Id);
                if (fullMovie != null)
                {
                    candidates[0] = AiCatalogItem.From(fullMovie);
                    detailBlock = AiCatalogCsvBuilder.BuildDetail(candidates[0]);
                }
            }
        }

        var catalogCsv = AiCatalogCsvBuilder.Build(candidates);
        var userPrompt = MoviePrompts.BuildChatUser(
            message,
            catalogCsv: catalogCsv,
            detailBlock: detailBlock,
            filterBlock: hints.Describe(),
            user: userContext);

        var messages = BuildProviderMessages(MoviePrompts.ChatSystem, userPrompt, request.History);
        var reply = await _provider.GenerateChatResponseAsync(messages);

        if (string.IsNullOrWhiteSpace(reply))
        {
            reply = candidates.Count > 0
                ? "Dưới đây là các series/phim bộ phù hợp nhất trong hệ thống mà tôi tìm thấy cho bạn:"
                : "Xin lỗi bạn, hiện tại tôi chưa tìm thấy series nào phù hợp với yêu cầu này.";
        }

        var mentionedShows = ExtractMentionedTvShows(reply, rawShows);
        if (mentionedShows.Count == 0 && rawShows.Count > 0)
        {
            mentionedShows = rawShows.Take(MaxMovieCards).ToList();
        }

        var mentionedMovies = fallbackMovies.Count > 0 ? ExtractMentionedMovies(reply, fallbackMovies) : new();
        if (mentionedMovies.Count == 0 && fallbackMovies.Count > 0)
        {
            mentionedMovies = fallbackMovies.Take(MaxMovieCards).ToList();
        }

        var chips = MoviePrompts.GenerateSuggestedChips("tvshow", candidates.FirstOrDefault());

        return new AiChatResponseDto
        {
            Message = reply,
            Intent = fallbackMovies.Count > 0 && mentionedShows.Count == 0 ? "movie" : "tvshow",
            TvShows = mentionedShows,
            Movies = mentionedMovies,
            Items = mentionedShows.Select(s => new AiRecommendationItemDto
            {
                Id = s.Id,
                Kind = "tv",
                Title = s.Title,
                PosterUrl = s.PosterUrl,
                Rating = s.Rating,
                IsPremium = s.IsPremium,
                Genres = s.Genres
            }).Concat(mentionedMovies.Select(m => new AiRecommendationItemDto
            {
                Id = m.Id,
                Kind = "movie",
                Title = m.Title,
                PosterUrl = m.PosterUrl,
                Rating = m.Rating,
                Year = m.ReleaseDate?.Year,
                IsPremium = m.IsPremium,
                Genres = m.Genres
            })).ToList(),
            SuggestedActions = chips,
            Actions = chips.Select(c => new AiActionDto { Type = "chip", Label = c, Value = c }).ToList(),
            Meta = new AiResponseMetaDto { Provider = "groq", FallbackApplied = fallbackApplied }
        };
    }

    private async Task<AiChatResponseDto> HandleCompareChatAsync(AiChatRequestDto request, string message)
    {
        var trending = await _movieRetriever.GetTrendingCatalogAsync(100);
        var titleIndex = new AiTitleIndex(trending);
        var mentioned = titleIndex.FindMentioned(message, max: 2);

        MovieDTO? movieA = null;
        MovieDTO? movieB = null;

        if (mentioned.Count >= 2)
        {
            movieA = await _movieRetriever.GetMovieDetailAsync(mentioned[0].Id);
            movieB = await _movieRetriever.GetMovieDetailAsync(mentioned[1].Id);
        }

        // TỰ ĐỘNG CHỌN 2 PHIM CÙNG THỂ LOẠI/GU ĐIỆN ẢNH ĐỂ ĐỐI ĐẦU XỨNG TẦM
        if (movieA is null || movieB is null)
        {
            if (trending.Count >= 2)
            {
                var anchor = trending[0];
                var suggestions = AiCompareSuggester.SuggestFor(anchor, trending, take: 1);
                var pair = suggestions.FirstOrDefault() ?? trending[1];

                movieA = await _movieRetriever.GetMovieDetailAsync(anchor.Id);
                movieB = await _movieRetriever.GetMovieDetailAsync(pair.Id);
            }
        }

        if (movieA is null || movieB is null)
            return await HandleMovieChatAsync(request, message, AiUserContext.Guest);

        var toolResult = await _compareTool.ExecuteAsync(message, new Dictionary<string, object>
        {
            ["MovieA"] = movieA,
            ["MovieB"] = movieB
        });

        var jsonComparison = toolResult.Data?.ToString() ?? string.Empty;

        return new AiChatResponseDto
        {
            Message = $"Dưới đây là góc nhìn phân tích và so sánh chi tiết giữa **{movieA.Title}** và **{movieB.Title}**:",
            Intent = "compare",
            Movies = new List<MovieDTO> { movieA, movieB },
            CompareTable = jsonComparison,
            SuggestedActions = new[] { $"Xem trailer {movieA.Title}", $"Xem trailer {movieB.Title}", "So sánh 2 phim khác" },
            Actions = new List<AiActionDto>
            {
                new() { Type = "chip", Label = $"Xem trailer {movieA.Title}", Value = $"Xem trailer {movieA.Title}" },
                new() { Type = "chip", Label = "So sánh 2 phim khác", Value = "So sánh 2 phim khác" }
            },
            Meta = new AiResponseMetaDto { Provider = "groq" }
        };
    }

    private async Task<AiChatResponseDto> HandleMoodChatAsync(AiChatRequestDto request, string message)
    {
        var lower = message.ToLowerInvariant();
        var targetMood = MoviePrompts.MoodGenreMap.Keys.FirstOrDefault(k => lower.Contains(k)) ?? "vui";
        var targetGenres = MoviePrompts.MoodGenreMap[targetMood];

        var candidates = await _movieRetriever.GetTrendingCatalogAsync(30);
        var subset = candidates
            .Where(m => m.Genres.Any(g => targetGenres.Any(tg => g.Contains(tg, StringComparison.OrdinalIgnoreCase))))
            .Take(15)
            .ToList();

        var catalogCsv = AiCatalogCsvBuilder.Build(subset);
        var userPrompt = MoviePrompts.BuildMoodPickUser(targetMood, string.Join(", ", targetGenres), catalogCsv);

        var messages = BuildProviderMessages(MoviePrompts.ChatSystem, userPrompt, request.History);
        var reply = await _provider.GenerateChatResponseAsync(messages);

        if (string.IsNullOrWhiteSpace(reply))
        {
            reply = $"Dưới đây là các phim phù hợp với tâm trạng '{targetMood}' dành cho bạn:";
        }

        var movieDtos = subset.Take(MaxMovieCards).Select(s => new MovieDTO
        {
            Id = s.Id,
            Title = s.Title,
            PosterUrl = s.TrailerUrl,
            Rating = (decimal?)(s.Rating ?? 0),
            Genres = s.Genres.ToList()
        }).ToList();

        return new AiChatResponseDto
        {
            Message = reply,
            Intent = "mood",
            Movies = movieDtos,
            Items = movieDtos.Select(m => new AiRecommendationItemDto
            {
                Id = m.Id,
                Kind = "movie",
                Title = m.Title,
                Rating = m.Rating,
                Genres = m.Genres
            }).ToList(),
            SuggestedActions = MoviePrompts.GenerateSuggestedChips("mood"),
            Actions = MoviePrompts.GenerateSuggestedChips("mood")
                .Select(c => new AiActionDto { Type = "chip", Label = c, Value = c }).ToList(),
            Meta = new AiResponseMetaDto { Provider = "groq" }
        };
    }

    private async Task<AiChatResponseDto> HandleReviewChatAsync(AiChatRequestDto request, string message)
    {
        var trending = await _movieRetriever.GetTrendingCatalogAsync(100);
        var titleIndex = new AiTitleIndex(trending);
        var mentioned = titleIndex.FindMentioned(message, max: 1).FirstOrDefault();

        if (mentioned is null)
            return await HandleMovieChatAsync(request, message, AiUserContext.Guest);

        var toolResult = await _reviewTool.ExecuteAsync(message, new Dictionary<string, object>
        {
            ["MovieId"] = mentioned.Id,
            ["Title"] = mentioned.Title
        });

        var reply = toolResult.FormattedContext ?? $"Chưa có tóm tắt đánh giá cho **{mentioned.Title}**.";

        return new AiChatResponseDto
        {
            Message = reply.StartsWith("Phim") ? reply : $"Tóm tắt đánh giá về phim **{mentioned.Title}**:\n\n{reply}",
            Intent = "review",
            SuggestedActions = new[] { $"Xem trailer {mentioned.Title}", $"Phim tương tự {mentioned.Title}" },
            Meta = new AiResponseMetaDto { Provider = "groq" }
        };
    }

    private static List<AiProviderMessage> BuildProviderMessages(
        string systemPrompt,
        string userMessage,
        List<AiChatMessageDto>? history,
        AiUserContext? user = null)
    {
        var list = new List<AiProviderMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };

        if (history is { Count: > 0 })
        {
            var trimmed = history
                .Where(h => (h.Role is "user" or "assistant") && !string.IsNullOrWhiteSpace(h.Content))
                .TakeLast(8)
                .ToList();

            if (trimmed.Count > 0 &&
                trimmed[^1].Role == "user" &&
                string.Equals(trimmed[^1].Content.Trim(), userMessage.Trim(), StringComparison.Ordinal))
            {
                trimmed.RemoveAt(trimmed.Count - 1);
            }

            foreach (var h in trimmed)
                list.Add(new AiProviderMessage { Role = h.Role, Content = h.Content });
        }

        list.Add(new AiProviderMessage { Role = "user", Content = userMessage });
        return list;
    }

    private static List<MovieDTO> ExtractMentionedMovies(string reply, List<MovieDTO> pool)
    {
        if (string.IsNullOrWhiteSpace(reply)) return new();
        var lower = reply.ToLowerInvariant();

        return pool
            .Where(m => lower.Contains(m.Title.ToLowerInvariant()))
            .Take(MaxMovieCards)
            .ToList();
    }

    private static List<TvShowSummaryDTO> ExtractMentionedTvShows(string reply, List<TvShowSummaryDTO> pool)
    {
        if (string.IsNullOrWhiteSpace(reply)) return new();
        var lower = reply.ToLowerInvariant();

        return pool
            .Where(s => lower.Contains(s.Title.ToLowerInvariant()))
            .Take(MaxMovieCards)
            .ToList();
    }
}