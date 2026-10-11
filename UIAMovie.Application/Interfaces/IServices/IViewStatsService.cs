// UIAMovie.Application/Interfaces/IServices/IViewStatsService.cs

using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Interfaces.IServices;

public interface IViewStatsService
{
    Task<ViewStatsOverviewDTO> GetOverviewAsync(string? granularity, string? contentType, DateOnly? from, DateOnly? to, Guid? contentId);
    Task<List<TopContentDTO>> GetTopAsync(string? contentType, DateOnly? from, DateOnly? to, int take);
    Task<List<GenreStatDTO>> GetByGenreAsync(string? contentType, DateOnly? from, DateOnly? to);
}