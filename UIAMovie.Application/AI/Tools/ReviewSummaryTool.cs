// UIAMovie.Application/AI/Tools/ReviewSummaryTool.cs
using UIAMovie.Application.AI.Models;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Services;
using UIAMovie.Infrastructure.AI.Providers;

namespace UIAMovie.Application.AI.Tools;

public class ReviewSummaryTool : IAiTool
{
    public string Name => "ReviewSummaryTool";
    private readonly IRatingReviewService _reviewService;
    private readonly IAiProvider _provider;

    public ReviewSummaryTool(IRatingReviewService reviewService, IAiProvider provider)
    {
        _reviewService = reviewService;
        _provider = provider;
    }

    public async Task<AiToolResult> ExecuteAsync(string input, IDictionary<string, object>? parameters = null)
    {
        if (parameters == null || !parameters.TryGetValue("MovieId", out var idObj) || idObj is not Guid movieId)
            return new AiToolResult { Success = false, ErrorMessage = "MovieId không hợp lệ." };

        var title = parameters.TryGetValue("Title", out var t) ? t?.ToString() ?? "Phim" : "Phim";

        var reviews = await _reviewService.GetMovieReviewsAsync(movieId);
        var reviewTexts = reviews
            .Where(r => !string.IsNullOrWhiteSpace(r.ReviewText))
            .Select(r => r.ReviewText!)
            .ToList();

        if (reviewTexts.Count == 0)
        {
            return new AiToolResult
            {
                Success = true,
                ToolName = Name,
                Data = null,
                FormattedContext = $"Phim **{title}** hiện tại chưa có đánh giá nào từ cộng đồng."
            };
        }

        var prompt = MoviePrompts.BuildReviewUser(title, reviewTexts);
        var summary = await _provider.GenerateChatResponseAsync(
            new List<AiProviderMessage>
            {
                new() { Role = "system", Content = MoviePrompts.ReviewSystem },
                new() { Role = "user", Content = prompt }
            },
            new AiProviderOptions { Temperature = 0.2, MaxTokens = 200 });

        return new AiToolResult
        {
            Success = true,
            ToolName = Name,
            Data = summary,
            FormattedContext = summary
        };
    }
}