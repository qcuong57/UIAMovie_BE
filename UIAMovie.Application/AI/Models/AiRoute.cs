// UIAMovie.Application/AI/Models/AiRoute.cs
namespace UIAMovie.Application.AI.Models;

public sealed record AiRoute
{
    public string Intent { get; init; } = "movie";
    public double Confidence { get; init; } = 1.0;
    public bool RequiresMovieSearch { get; init; }
    public bool RequiresTvSearch { get; init; }
    public bool RequiresSiteKnowledge { get; init; }
    public bool RequiresUserContext { get; init; }
    public bool RequiresTool { get; init; }
    public IReadOnlyList<string> DetectedEntities { get; init; } = Array.Empty<string>();
}