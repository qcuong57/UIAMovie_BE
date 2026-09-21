// UIAMovie.Application/AI/Retrieval/ITvShowRetriever.cs
using UIAMovie.Application.AI.Models;
using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.AI.Retrieval;

public interface ITvShowRetriever
{
    Task<(List<AiCatalogItem> Items, List<TvShowSummaryDTO> RawShows, bool FallbackApplied)> RetrieveTvShowsAsync(
        string message, 
        AiQueryHints hints);

    Task<TvShowDTO?> GetTvShowDetailAsync(Guid id);
}