using UIAMovie.Domain.Entities;

namespace UIAMovie.Infrastructure.Data.Repositories;

public interface ISubtitleRepository : IRepository<MovieSubtitle>
{
    /// <summary>Lấy danh sách subtitle (không kèm content) của một phim.</summary>
    Task<IEnumerable<MovieSubtitle>> GetByMovieIdAsync(Guid movieId);

    /// <summary>Lấy một subtitle kèm content để stream ra player.</summary>
    Task<MovieSubtitle?> GetByIdAsync(Guid id);

    /// <summary>Kiểm tra phim đã có subtitle ngôn ngữ này chưa.</summary>
    Task<MovieSubtitle?> GetByMovieAndLanguageAsync(Guid movieId, string languageCode);

    /// <summary>Unset IsDefault cho toàn bộ subtitle của phim (trước khi set default mới).</summary>
    Task ClearDefaultAsync(Guid movieId);
}