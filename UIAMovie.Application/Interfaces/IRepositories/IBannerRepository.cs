// UIAMovie.Application/Interfaces/IRepositories/IBannerRepository.cs
using UIAMovie.Application.DTOs;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Interfaces.IRepositories;

public interface IBannerRepository
{
    Task<Banner?> GetByIdAsync(Guid id);
    Task<(IEnumerable<Banner> Items, int TotalCount)> GetPagedAsync(FilterBannersDTO filter);

    /// <summary>Banner IsActive theo placement (CHƯA lọc theo thời gian — service lọc StartAt/EndAt để cache không bị cũ).</summary>
    Task<List<Banner>> GetActiveByPlacementAsync(BannerPlacement placement);

    Task<Guid> AddAsync(Banner banner);
    Task UpdateAsync(Banner banner);
    Task DeleteAsync(Guid id);

    // Tracking
    Task<HashSet<Guid>> GetExistingIdsAsync(IEnumerable<Guid> ids);
    Task AddEventsAsync(List<BannerEvent> events);
    /// <summary>Số liệu thô theo banner (chưa tính tỉ lệ) — service tính CTR/conversion.</summary>
    Task<List<BannerStatsRow>> GetStatsAsync(DateTime? fromUtc, DateTime? toUtcExclusive);
}

/// <summary>Số liệu tổng hợp thô của 1 banner trong một khoảng thời gian.</summary>
public record BannerStatsRow(
    Guid BannerId, string Title, int Views, int Clicks,
    int OrdersCreated, int OrdersPaid, long Revenue,
    int Dismisses = 0, int Conversions = 0);