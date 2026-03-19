using UIAMovie.Domain.Entities;
 
namespace UIAMovie.Infrastructure.Data.Repositories;
 
public interface IMovieRepository : IRepository<Movie>
{
    /// <summary>
    /// Lấy phim kèm đầy đủ: Cast + Person, Director + Person,
    /// Images, Videos, Genres + Genre name.
    /// Dùng cho GET /movies/{id}
    /// </summary>
    Task<Movie?> GetByIdWithDetailsAsync(Guid id);
 
    /// <summary>
    /// Lấy tất cả phim kèm Genres — dùng cho list/filter/trending.
    /// Không include Cast/Images để tránh query nặng.
    /// </summary>
    Task<IEnumerable<Movie>> GetAllWithGenresAsync();
 
    /// <summary>Tìm phim theo TmdbId — kiểm tra duplicate khi import.</summary>
    Task<Movie?> GetByTmdbIdAsync(int tmdbId);
}