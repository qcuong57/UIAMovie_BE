// UIAMovie.Application/AI/Retrieval/IMovieRetriever.cs
using UIAMovie.Application.AI.Models;
using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.AI.Retrieval;

public interface IMovieRetriever
{
    Task<(List<AiCatalogItem> Items, List<MovieDTO> RawMovies, bool FallbackApplied)> RetrieveMoviesAsync(
        string message, 
        AiQueryHints hints);
    
    Task<MovieDTO?> GetMovieDetailAsync(Guid id);
    Task<List<AiCatalogItem>> GetTrendingCatalogAsync(int limit = 25);
}