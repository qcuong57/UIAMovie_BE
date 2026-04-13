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

    /// <summary>
    /// Tìm phim có diễn viên tên chứa <paramref name="actorName"/> (case-insensitive).
    /// Include đầy đủ Cast + Person, Director + Person, Images, Videos, Genres.
    /// </summary>
    Task<IEnumerable<Movie>> GetMoviesByActorNameAsync(string actorName);

    /// <summary>
    /// Tính trending score TRÊN DATABASE (không kéo data về RAM).
    /// Trả về top <paramref name="take"/> phim kèm Genres, không include Cast/Images.
    /// 
    /// Score = (views_7_ngày × 3) + (views_30_ngày × 1) + (imdbRating × 10) + recencyBonus
    /// 
    /// Dùng cho GET /movies/trending.
    /// </summary>
    Task<IEnumerable<TrendingMovieProjection>> GetTrendingAsync(
        DateTime cutoff7,
        DateTime cutoff30,
        int take = 20);
}

/// <summary>
/// Projection result từ GetTrendingAsync — chứa Movie kèm thông tin score.
/// Không dùng anonymous type để tránh boxing/unboxing và dễ test.
/// </summary>
public class TrendingMovieProjection
{
    public Movie  Movie      { get; init; } = null!;
    public int    Views7d    { get; init; }  // Lượt xem 7 ngày
    public int    Views30d   { get; init; }  // Lượt xem 30 ngày
    public double Score      { get; init; }  // TrendingScore cuối cùng
}