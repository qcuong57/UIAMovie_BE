// UIAMovie.Application/AI/Retrieval/TvShowRetriever.cs
using UIAMovie.Application.AI.Models;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;

namespace UIAMovie.Application.AI.Retrieval;

public class TvShowRetriever : ITvShowRetriever
{
    private readonly ITvShowService _tvShowService;

    public TvShowRetriever(ITvShowService tvShowService)
    {
        _tvShowService = tvShowService;
    }

    public async Task<TvShowDTO?> GetTvShowDetailAsync(Guid id)
        => await _tvShowService.GetTvShowByIdAsync(id);

    public async Task<(List<AiCatalogItem> Items, List<TvShowSummaryDTO> RawShows, bool FallbackApplied)> RetrieveTvShowsAsync(
        string message, 
        AiQueryHints hints)
    {
        var cleanTitle = CleanSearchQuery(hints.SimilarTo ?? message);
        var fallbackApplied = false;

        var filter = new FilterTvShowsDTO
        {
            Search = !string.IsNullOrWhiteSpace(cleanTitle) ? cleanTitle : null,
            MinRating = hints.MinRating.HasValue ? (decimal)hints.MinRating.Value : null,
            FromFirstAirDate = hints.FromYear.HasValue
                ? new DateTime(hints.FromYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null,
            ToFirstAirDate = hints.ToYear.HasValue
                ? new DateTime(hints.ToYear.Value, 12, 31, 23, 59, 59, DateTimeKind.Utc) : null,
            OriginCountry = hints.CountryCode,
            SortBy = "rating",
            SortDesc = true,
            PageSize = 25
        };

        var page = await _tvShowService.GetTvShowsAsync(filter);
        var rawShows = page.Items.ToList();
        var candidates = rawShows.Select(AiCatalogItem.From).ToList();

        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(cleanTitle) && hints.HasFilters)
        {
            var loosePage = await _tvShowService.GetTvShowsAsync(new FilterTvShowsDTO
            {
                Search = cleanTitle,
                PageSize = 25
            });

            if (loosePage.Items.Any())
            {
                rawShows = loosePage.Items.ToList();
                candidates = rawShows.Select(AiCatalogItem.From).ToList();
                fallbackApplied = true;
            }
        }

        if (candidates.Count == 0)
        {
            var fallbackPage = await _tvShowService.GetTvShowsAsync(new FilterTvShowsDTO
            {
                PageSize = 25,
                SortBy = "rating",
                SortDesc = true
            });
            rawShows = fallbackPage.Items.ToList();
            candidates = rawShows.Select(AiCatalogItem.From).ToList();
            fallbackApplied = true;
        }

        return (candidates, rawShows, fallbackApplied);
    }

    private static string CleanSearchQuery(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var text = input.Trim();

        string[] prefixes =
        [
            "cho tôi thông tin về series", "cho tôi thông tin series", "thông tin về series",
            "thông tin series", "xem series", "series", "phim bộ", "tìm phim bộ", "gợi ý phim bộ"
        ];

        foreach (var prefix in prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..].Trim();
                break;
            }
        }

        text = text.Trim('"', '\'', '“', '”', '`', '«', '»', '(', ')', '[', ']', '{', '}', ':', ';', ',', '.', '?', '!', ' ');
        return text.Length >= 2 ? text : input.Trim();
    }
}