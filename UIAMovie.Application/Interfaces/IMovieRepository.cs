using UIAMovie.Application.DTOs;
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
    /// Lấy tất cả phim kèm Genres — giữ lại cho các trường hợp cần toàn bộ catalog.
    /// Không include Cast/Images để tránh query nặng.
    ///
    /// LƯU Ý: Chỉ dùng cho các nơi thực sự cần toàn bộ data (ví dụ: actor search).
    /// Với list/filter/paginate → dùng GetPagedAsync thay thế.
    /// </summary>
    Task<IEnumerable<Movie>> GetAllWithGenresAsync();

    /// <summary>
    /// FIX [GetMoviesAsync]: Filter + Sort + Paginate TRÊN DATABASE — không kéo toàn bộ về RAM.
    ///
    /// Thay thế pattern cũ:
    ///   GetAllWithGenresAsync() → filter/sort/paginate trong C# (rất chậm khi catalog lớn)
    ///
    /// Bằng:
    ///   GetPagedAsync(filter) → SQL WHERE + ORDER BY + OFFSET/FETCH
    ///
    /// Hỗ trợ:
    ///   - Filter theo Ids, GenreIds, Search, MinRating, MaxRating,
    ///     FromReleaseDate, ToReleaseDate, OriginCountry
    ///   - Sort: rating | title | releaseDate (asc/desc)
    ///   - Paginate: Page + PageSize
    ///   - Khi Ids != null → giữ thứ tự AI (không sort)
    /// </summary>
    Task<(IEnumerable<Movie> Items, int TotalCount)> GetPagedAsync(FilterMoviesDTO filter);

    /// <summary>
    /// Search phim theo title TRÊN DATABASE — dùng EF .Contains() → SQL LIKE.
    /// Thay thế GetAllWithGenresAsync() + .Where() trong C#.
    /// </summary>
    Task<IEnumerable<Movie>> SearchByTitleAsync(string query);

    /// <summary>Tìm phim theo TmdbId — kiểm tra duplicate khi import.</summary>
    Task<Movie?> GetByTmdbIdAsync(int tmdbId);

    /// <summary>
    /// Tìm phim có diễn viên tên chứa <paramref name="actorName"/> (case-insensitive).
    /// Include đầy đủ Cast + Person, Director + Person, Images, Videos, Genres.
    /// </summary>
    Task<IEnumerable<Movie>> GetMoviesByActorNameAsync(string actorName);

    /// <summary>
    /// Filter phim theo genreId TRÊN DATABASE.
    /// Thay thế GetAllWithGenresAsync() + .Where(genre match) trong C#.
    /// </summary>
    Task<IEnumerable<Movie>> GetByGenreAsync(Guid genreId);

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
        int      take = 20);

    /// <summary>
    /// Lấy danh sách distinct OriginCountry có trong DB.
    /// Thay thế GetMoviesAsync(PageSize=9999) + distinct trong C#.
    /// </summary>
    Task<IEnumerable<string>> GetAvailableCountriesAsync();
}

/// <summary>
/// Projection result từ GetTrendingAsync — chứa Movie kèm thông tin score.
/// </summary>
public class TrendingMovieProjection
{
    public Movie  Movie      { get; init; } = null!;
    public int    Views7d    { get; init; }
    public int    Views30d   { get; init; }
    public double Score      { get; init; }
}