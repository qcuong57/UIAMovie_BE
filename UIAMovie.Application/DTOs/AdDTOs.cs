// UIAMovie.Application/DTOs/AdDTOs.cs
//
// DTO map (Global Ads redesign):
//   AdDTO                → response khi list/get ad (admin panel) — kèm global slots.
//   AdPlaybackDTO        → response cho frontend player — chỉ chứa đủ để play.
//   ContentAdsDTO        → wrapper trả về ads sẽ phát cho 1 content (grouped by position).
//   CreateAdDTO          → body khi tạo ad mới.
//   UpdateAdDTO          → body khi cập nhật ad.
//   CreateGlobalSlotDTO  → body khi tạo global slot (gắn ad vào "tất cả" hoặc "theo loại").
//   UpdateGlobalSlotDTO  → body khi sửa global slot.
//   GlobalSlotDTO        → response DTO cho 1 global slot (admin view).
//   CreateOverrideDTO    → body khi tạo content-specific override (nếu cần).
//   FilterAdsDTO         → query params cho GET /api/ads.

using Microsoft.AspNetCore.Http;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.DTOs;

// ── Response DTOs ─────────────────────────────────────────────────────────────

/// <summary>
/// Full ad info — dùng cho Admin panel.
/// Kèm danh sách global slots ad này đang được gắn vào.
/// </summary>
public class AdDTO
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? VideoUrl { get; set; }

    /// <summary>URL ảnh logo/nhãn hiệu của ad.</summary>
    public string? BrandImageUrl { get; set; }

    public int DurationSeconds { get; set; }
    public int? SkipAfterSeconds { get; set; }
    public string? ClickThroughUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Số global slots ad đang được gắn vào. Dùng cho list view (không cần load full slots).</summary>
    public int GlobalSlotCount { get; set; }

    /// <summary>Global slots ad này đang được gắn vào. Chỉ populate khi get by id.</summary>
    public List<GlobalSlotDTO> GlobalSlots { get; set; } = new();
}

/// <summary>
/// Minimal DTO cho frontend player — chỉ chứa những gì cần để phát ad.
/// </summary>
public class AdPlaybackDTO
{
    public Guid AdId { get; set; }
    public string VideoUrl { get; set; } = string.Empty;

    /// <summary>Ảnh logo/nhãn hiệu — player overlay khi phát ad.</summary>
    public string? BrandImageUrl { get; set; }

    public int DurationSeconds { get; set; }
    public int? SkipAfterSeconds { get; set; }
    public string? ClickThroughUrl { get; set; }

    // Slot info (player cần biết để xử lý mid-roll)
    public Guid SlotId { get; set; }
    public AdPosition Position { get; set; }
    public int? MidRollOffsetSeconds { get; set; }
    public int DisplayOrder { get; set; }
}

/// <summary>
/// Response cho GET /api/ads/content/{type}/{id} — ads grouped by position.
/// Player gọi trước khi phát video.
/// </summary>
public class ContentAdsDTO
{
    public AdContentType ContentType { get; set; }
    public Guid ContentId { get; set; }

    /// <summary>Ads phát trước content. Sorted by DisplayOrder.</summary>
    public List<AdPlaybackDTO> PreRoll { get; set; } = new();

    /// <summary>
    /// Ads mid-roll. Sorted by MidRollOffsetSeconds (asc), rồi DisplayOrder.
    /// Frontend trigger ad khi currentTime >= OffsetSeconds.
    /// </summary>
    public List<AdPlaybackDTO> MidRoll { get; set; } = new();

    /// <summary>Ads phát sau content.</summary>
    public List<AdPlaybackDTO> PostRoll { get; set; } = new();
}

/// <summary>Global slot info (admin view).</summary>
public class GlobalSlotDTO
{
    public Guid SlotId { get; set; }

    /// <summary>NULL = áp dụng tất cả content.</summary>
    public AdContentType? AppliesTo { get; set; }

    public AdPosition Position { get; set; }
    public int? MidRollOffsetSeconds { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

/// <summary>Tạo ad mới. Một trong VideoFile hoặc VideoUrl phải có giá trị.</summary>
public class CreateAdDTO
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Upload video lên Cloudinary. Ưu tiên hơn VideoUrl.</summary>
    public IFormFile? VideoFile { get; set; }

    /// <summary>URL video ngoài. Dùng khi không upload file.</summary>
    public string? VideoUrl { get; set; }

    /// <summary>Upload ảnh nhãn hiệu lên Cloudinary. Ưu tiên hơn BrandImageUrl. Bắt buộc: 1 trong 2 phải có giá trị.</summary>
    public IFormFile? BrandImageFile { get; set; }

    /// <summary>URL ảnh nhãn hiệu ngoài. Dùng khi không upload file.</summary>
    public string? BrandImageUrl { get; set; }

    public int DurationSeconds { get; set; }

    /// <summary>NULL = không thể skip. 0 = skip ngay. >0 = skip sau N giây.</summary>
    public int? SkipAfterSeconds { get; set; }

    public string? ClickThroughUrl { get; set; }
}

/// <summary>Cập nhật ad. Tất cả fields là optional (PATCH semantics).</summary>
public class UpdateAdDTO
{
    public string? Title { get; set; }
    public string? VideoUrl { get; set; }
    public IFormFile? VideoFile { get; set; }

    /// <summary>Thay ảnh nhãn hiệu (nếu có upload mới).</summary>
    public IFormFile? BrandImageFile { get; set; }
    /// <summary>Thay ảnh nhãn hiệu bằng URL ngoài (nếu không upload file).</summary>
    public string? BrandImageUrl { get; set; }

    public int? DurationSeconds { get; set; }
    public int? SkipAfterSeconds { get; set; }
    public string? ClickThroughUrl { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>
/// Tạo global slot — gắn ad vào "tất cả content" hoặc "theo loại content".
/// Body cho POST /api/ads/{adId}/global-slots.
/// </summary>
public class CreateGlobalSlotDTO
{
    /// <summary>
    /// NULL = áp dụng toàn bộ content (Movie + TvShow/Episode).
    /// Có giá trị = chỉ áp cho loại content đó.
    /// </summary>
    public AdContentType? AppliesTo { get; set; }

    public AdPosition Position { get; set; }

    /// <summary>Bắt buộc khi Position = MidRoll.</summary>
    public int? MidRollOffsetSeconds { get; set; }

    public int DisplayOrder { get; set; } = 0;
}

/// <summary>Cập nhật global slot. Dùng PATCH /api/ads/global-slots/{slotId}.</summary>
public class UpdateGlobalSlotDTO
{
    public AdContentType? AppliesTo { get; set; }
    public AdPosition? Position { get; set; }
    public int? MidRollOffsetSeconds { get; set; }
    public int? DisplayOrder { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>
/// Content-specific override — bổ sung ad riêng cho 1 content cụ thể.
/// Khi content có override ở 1 position, global slots ở cùng position đó bị bỏ qua.
/// Body cho POST /api/ads/{adId}/overrides.
/// </summary>
public class CreateOverrideDTO
{
    public AdContentType ContentType { get; set; }
    public Guid ContentId { get; set; }
    public AdPosition Position { get; set; }
    public int? MidRollOffsetSeconds { get; set; }
    public int DisplayOrder { get; set; } = 0;
}

/// <summary>Query params cho GET /api/ads (admin list).</summary>
public class FilterAdsDTO
{
    public bool? IsActive { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}