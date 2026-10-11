// UIAMovie.Application/Interfaces/IRepositories/IViewStatsRepository.cs

using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Interfaces.IRepositories;

public interface IViewStatsRepository
{
    /// <param name="contentType">"all" | "movie" | "tvshow"</param>
    Task<List<DailyViewRow>> GetDailyAsync(DateOnly from, DateOnly to, string contentType, Guid? contentId);

    /// <summary>
    /// Số user đăng nhập distinct trong [from, to] — đếm ngay trong SQL (COUNT DISTINCT), không kéo danh sách user về RAM.
    /// </summary>
    Task<int> CountUniqueUsersAsync(DateOnly from, DateOnly to, string contentType, Guid? contentId);

    Task<List<TopContentDTO>> GetTopAsync(DateOnly from, DateOnly to, string contentType, int take);

    Task<List<GenreStatDTO>> GetByGenreAsync(DateOnly from, DateOnly to, string contentType);
}