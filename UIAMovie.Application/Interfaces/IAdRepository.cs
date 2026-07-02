// UIAMovie.Application/Interfaces/IAdRepository.cs

using UIAMovie.Application.DTOs;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Interfaces;

public interface IAdRepository
{
    // ── Advertisement CRUD ────────────────────────────────────────────────────

    Task<Advertisement?> GetByIdAsync(Guid id);
    /// <summary>Lấy ad kèm GlobalSlots và Overrides.</summary>
    Task<Advertisement?> GetByIdWithSlotsAsync(Guid id);

    Task<(IEnumerable<Advertisement> Items, int TotalCount)> GetPagedAsync(FilterAdsDTO filter);
    Task<Guid> AddAsync(Advertisement advertisement);
    Task UpdateAsync(Advertisement advertisement);
    Task DeleteAsync(Guid id);

    // ── GlobalAdSlot ──────────────────────────────────────────────────────────

    Task<GlobalAdSlot?> GetSlotByIdAsync(Guid slotId);

    /// <summary>
    /// Lấy tất cả active global slots khớp với contentType.
    /// Trả về slots có AppliesTo = null (tất cả) HOẶC AppliesTo = contentType.
    /// Kèm Advertisement navigation.
    /// </summary>
    Task<IEnumerable<GlobalAdSlot>> GetActiveGlobalSlotsAsync(AdContentType contentType);

    /// <summary>Lấy tất cả global slots của 1 ad (admin view).</summary>
    Task<IEnumerable<GlobalAdSlot>> GetSlotsByAdAsync(Guid adId);

    Task<Guid> AddSlotAsync(GlobalAdSlot slot);
    Task UpdateSlotAsync(GlobalAdSlot slot);
    Task DeleteSlotAsync(Guid slotId);

    // ── AdContentOverride ─────────────────────────────────────────────────────

    Task<AdContentOverride?> GetOverrideByIdAsync(Guid overrideId);

    /// <summary>
    /// Lấy overrides active của 1 content cụ thể, kèm Advertisement.
    /// Dùng để kiểm tra content có override không trước khi áp global slots.
    /// </summary>
    Task<IEnumerable<AdContentOverride>> GetOverridesByContentAsync(
        AdContentType contentType,
        Guid          contentId);

    Task<Guid> AddOverrideAsync(AdContentOverride contentOverride);
    Task UpdateOverrideAsync(AdContentOverride contentOverride);
    Task DeleteOverrideAsync(Guid overrideId);

    /// <summary>Xóa tất cả overrides của 1 content (dùng khi xóa movie/episode).</summary>
    Task DeleteOverridesByContentAsync(AdContentType contentType, Guid contentId);
}