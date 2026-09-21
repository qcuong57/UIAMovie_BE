// UIAMovie.Application/AI/Retrieval/MovieRetriever.cs
using UIAMovie.Application.AI.Models;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;

namespace UIAMovie.Application.AI.Retrieval;

public class MovieRetriever : IMovieRetriever
{
    private readonly IMovieService _movieService;
    private readonly ITvShowService _tvShowService;

    public MovieRetriever(IMovieService movieService, ITvShowService tvShowService)
    {
        _movieService = movieService;
        _tvShowService = tvShowService;
    }

    public async Task<MovieDTO?> GetMovieDetailAsync(Guid id) 
        => await _movieService.GetMovieByIdAsync(id);

    public async Task<List<AiCatalogItem>> GetTrendingCatalogAsync(int limit = 25)
    {
        var trending = await _movieService.GetTrendingMoviesAsync();
        return trending.Take(limit).Select(AiCatalogItem.From).ToList();
    }

    public async Task<(List<AiCatalogItem> Items, List<MovieDTO> RawMovies, bool FallbackApplied)> RetrieveMoviesAsync(
        string message, 
        AiQueryHints hints)
    {
        var cleanTitle = CleanSearchQuery(hints.SimilarTo ?? message);
        var fallbackApplied = false;

        var filter = new FilterMoviesDTO
        {
            Search = !string.IsNullOrWhiteSpace(cleanTitle) ? cleanTitle : null,
            MinRating = hints.MinRating.HasValue ? (decimal)hints.MinRating.Value : null,
            FromReleaseDate = hints.FromYear.HasValue
                ? new DateTime(hints.FromYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null,
            ToReleaseDate = hints.ToYear.HasValue
                ? new DateTime(hints.ToYear.Value, 12, 31, 23, 59, 59, DateTimeKind.Utc) : null,
            OriginCountry = hints.CountryCode,
            SortBy = hints.Trending ? "views" : (hints.Newest ? "releasedate" : "rating"),
            SortDesc = true,
            PageSize = 25
        };

        var page = await _movieService.GetMoviesAsync(filter);
        var rawMovies = page.Items.ToList();
        var candidates = rawMovies.Select(AiCatalogItem.From).ToList();

        // 1. Fallback nếu kẹp filter gây 0 kết quả nhưng có tên
        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(cleanTitle) && hints.HasFilters)
        {
            var loosePage = await _movieService.GetMoviesAsync(new FilterMoviesDTO
            {
                Search = cleanTitle,
                PageSize = 25
            });

            if (loosePage.Items.Any())
            {
                rawMovies = loosePage.Items.ToList();
                candidates = rawMovies.Select(AiCatalogItem.From).ToList();
                fallbackApplied = true;
            }
        }

        // 2. Fallback cuối cùng: nạp danh sách phim đánh giá cao / thịnh hành
        if (candidates.Count == 0)
        {
            var topPage = await _movieService.GetMoviesAsync(new FilterMoviesDTO
            {
                PageSize = 25,
                SortBy = "rating",
                SortDesc = true
            });
            rawMovies = topPage.Items.ToList();
            candidates = rawMovies.Select(AiCatalogItem.From).ToList();
            fallbackApplied = true;
        }

        return (candidates, rawMovies, fallbackApplied);
    }

    private static string CleanSearchQuery(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var text = input.Trim();

        string[] prefixes =
        [
            "cho tôi thông tin về bộ phim", "cho tôi thông tin về series", "cho tôi thông tin về phim",
            "cho tôi thông tin bộ phim", "cho tôi thông tin series", "cho tôi thông tin phim",
            "cho tôi thông tin về", "cho tôi thông tin", "thông tin về bộ phim", "thông tin về series",
            "thông tin về phim", "thông tin bộ phim", "thông tin series", "thông tin phim", "thông tin về", "thông tin",
            "cho tôi biết về bộ phim", "cho tôi biết về series", "cho tôi biết về phim", "cho tôi biết về",
            "cho tôi xem bộ phim", "cho tôi xem series", "cho tôi xem phim", "cho tôi xem",
            "hãy giới thiệu bộ phim", "hãy giới thiệu series", "hãy giới thiệu phim", "giới thiệu bộ phim",
            "giới thiệu series", "giới thiệu phim",
            "review bộ phim", "review series", "review phim", "đánh giá bộ phim", "đánh giá series", "đánh giá phim",
            "tìm kiếm bộ phim", "tìm kiếm series", "tìm kiếm phim", "tìm bộ phim", "tìm series", "tìm phim",
            "nội dung bộ phim", "nội dung series", "nội dung phim", "cốt truyện bộ phim", "cốt truyện series",
            "cốt truyện phim", "trailer bộ phim", "trailer series", "trailer phim", "trailer",
            "xem bộ phim", "xem series", "xem phim", "bộ phim", "series", "phim", "tác phẩm"
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