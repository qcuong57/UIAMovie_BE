// UIAMovie.Application/Interfaces/IServices/IBannerService.cs
using UIAMovie.Application.DTOs;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Interfaces.IServices;

public interface IBannerService
{
    // Public
    Task<List<BannerDTO>> GetActiveAsync(BannerPlacement placement, Guid? userId = null);
    Task RecordEventsAsync(IEnumerable<BannerEventInputDTO> events, Guid? userId);

    /// <summary>Trả bannerId nếu banner tồn tại và gắn đúng gói; ngược lại null. Cho phép banner đã tắt/hết hạn.</summary>
    Task<Guid?> ResolveAttributionAsync(Guid? bannerId, string planId);

    // Admin
    Task<AdminBannerDTO?> GetByIdAsync(Guid id);
    Task<(IEnumerable<AdminBannerDTO> Items, int Total)> GetPagedAsync(FilterBannersDTO filter);
    Task<Guid> CreateAsync(CreateBannerDTO dto);
    Task<bool> UpdateAsync(Guid id, UpdateBannerDTO dto);
    Task<bool> SetActiveAsync(Guid id, bool isActive);
    Task<bool> DeleteAsync(Guid id);
    Task<List<BannerStatsDTO>> GetStatsAsync(DateTime? fromUtc, DateTime? toUtcExclusive);
}