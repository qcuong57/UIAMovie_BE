// UIAMovie.Application/Interfaces/IAdService.cs

using UIAMovie.Application.DTOs;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Interfaces;

public interface IAdService
{
    // ── Ad CRUD (Admin) ───────────────────────────────────────────────────────

    Task<AdDTO?> GetAdByIdAsync(Guid id);
    Task<(IEnumerable<AdDTO> Items, int Total)> GetAdsAsync(FilterAdsDTO filter);
    Task<Guid>  CreateAdAsync(CreateAdDTO dto);
    Task<bool>  UpdateAdAsync(Guid id, UpdateAdDTO dto);
    Task<bool>  DeleteAdAsync(Guid id);

    // ── Global Slots (Admin) ──────────────────────────────────────────────────

    /// <summary>
    /// Tạo global slot — gắn ad vào tất cả content hoặc theo loại.
    /// POST /api/ads/{adId}/global-slots
    /// </summary>
    Task<Guid> CreateGlobalSlotAsync(Guid adId, CreateGlobalSlotDTO dto);

    Task<bool> UpdateGlobalSlotAsync(Guid slotId, UpdateGlobalSlotDTO dto);
    Task<bool> DeleteGlobalSlotAsync(Guid slotId);

    // ── Content-specific Override (Admin, optional) ───────────────────────────

    /// <summary>
    /// Tạo override cho 1 content cụ thể.
    /// Khi content có override ở position X, global slots ở position X bị bỏ qua.
    /// POST /api/ads/{adId}/overrides
    /// </summary>
    Task<Guid> CreateOverrideAsync(Guid adId, CreateOverrideDTO dto);
    Task<bool> DeleteOverrideAsync(Guid overrideId);

    // ── Player API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Trả về tất cả ads cần phát cho 1 content, grouped by position.
    /// Player gọi trước khi bắt đầu xem.
    ///
    /// Merge logic:
    ///   1. Lấy global slots khớp contentType (AppliesTo = null hoặc = contentType).
    ///   2. Lấy content-specific overrides cho (contentType, contentId).
    ///   3. Với mỗi Position: nếu có override → bỏ global slots ở position đó,
    ///      dùng override thay thế. Nếu không có override → dùng global slots.
    ///
    /// Kết quả được cache Redis 5 phút per contentType (vì global ads không
    /// thay đổi theo contentId, cache key dùng contentType để share giữa các content).
    /// </summary>
    Task<ContentAdsDTO> GetAdsForContentAsync(
        AdContentType contentType,
        Guid          contentId);
}