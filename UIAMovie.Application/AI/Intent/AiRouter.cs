// UIAMovie.Application/AI/Intent/AiRouter.cs
using UIAMovie.Application.AI.Models;
using UIAMovie.Application.AI.Retrieval;

namespace UIAMovie.Application.AI.Intent;

public class AiRouter : IAiRouter
{
    private readonly IMovieRetriever _movieRetriever;

    public AiRouter(IMovieRetriever movieRetriever)
    {
        _movieRetriever = movieRetriever;
    }

    public async Task<AiRoute> RouteAsync(string message, IEnumerable<string>? recentHistory = null)
    {
        // Sử dụng danh sách top movies để build TitleIndex nhận diện thực thể chính xác
        var trending = await _movieRetriever.GetTrendingCatalogAsync(50);
        var titleIndex = new AiTitleIndex(trending);

        var intent = MoviePrompts.DetectIntent(message, recentHistory, titleIndex);
        var mentioned = titleIndex.FindMentioned(message, max: 2);

        return new AiRoute
        {
            Intent = intent,
            Confidence = 1.0,
            RequiresMovieSearch = intent is "movie" or "compare" or "review" or "mood",
            RequiresTvSearch = intent is "tvshow",
            RequiresSiteKnowledge = intent is "site",
            RequiresUserContext = true,
            RequiresTool = intent is "compare" or "review",
            DetectedEntities = mentioned.Select(m => m.Title).ToList()
        };
    }
}