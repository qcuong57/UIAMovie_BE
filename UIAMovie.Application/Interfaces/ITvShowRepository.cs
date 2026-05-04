// UIAMovie.Application/Interfaces/ITvShowRepository.cs

using UIAMovie.Application.DTOs;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Interfaces;

public interface ITvShowRepository : IRepository<TvShow>
{
    Task<(IEnumerable<TvShow> Items, int TotalCount)> GetPagedAsync(FilterTvShowsDTO filter);

    /// <summary>
    /// Load TvShow kèm Cast, Director, Images, Videos, Genres, Seasons metadata.
    /// KHÔNG include Episodes — tránh cartesian explosion với show nhiều tập.
    /// </summary>
    Task<TvShow?> GetByIdWithDetailsAsync(Guid id);

    /// <summary>
    /// Load 1 Season kèm Episodes, dùng cho GET /tvshows/{id}/seasons/{n}.
    /// Tách riêng để không bao giờ load tất cả episodes khi GET /tvshows/{id}.
    /// </summary>
    Task<Season?> GetSeasonWithEpisodesAsync(Guid tvShowId, int seasonNumber);

    Task<TvShow?> GetByTmdbIdAsync(int tmdbId);
    Task<IEnumerable<TvShow>> SearchByTitleAsync(string query);
    Task<IEnumerable<TvShow>> SearchByActorNameAsync(string actorName);
    Task<IEnumerable<TvShow>> GetByGenreAsync(Guid genreId);
    Task<IEnumerable<string>> GetAvailableCountriesAsync();
}