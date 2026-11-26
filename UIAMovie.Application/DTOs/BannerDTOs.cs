// UIAMovie.Application/DTOs/BannerDTOs.cs

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.DTOs;

// ── Plan gắn kèm banner (public) ──────────────────────────────────────────────

public class BannerPlanDTO
{
    public string PlanId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceVnd { get; set; }
    public long? OriginalPriceVnd { get; set; }
    public string PriceDisplay { get; set; } = string.Empty; // "69.000đ"
    public string? OriginalPriceDisplay { get; set; } // "99.000đ" (gạch ngang)
    public int? DiscountPercent { get; set; } // 30
    public int DurationDays { get; set; }
    public bool IsTrial { get; set; }
    public DateTime? EndsAt { get; set; } // FE có thể làm đồng hồ đếm ngược
    public bool IsEligible { get; set; } = true;
}

// ── Public ────────────────────────────────────────────────────────────────────

/// <summary>Dữ liệu public — chỉ những gì FE cần để render.</summary>
public class BannerDTO
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? CtaText { get; set; }
    public string? BadgeText { get; set; }
    public string AltText { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public string? MobileImageUrl { get; set; }
    public int? MobileImageWidth { get; set; }
    public int? MobileImageHeight { get; set; }
    public string? BackgroundColor { get; set; }

    /// <summary>"contain" | "cover"</summary>
    public string FitMode { get; set; } = "contain";

    /// <summary>Chiều cao tối đa desktop (px). Null = mặc định theo vị trí.</summary>
    public int? MaxHeight { get; set; }

    /// <summary>"all" | "desktop" | "mobile" — FE lọc theo thiết bị.</summary>
    public string Device { get; set; } = "all";

    public string? TargetGenres { get; set; }
    public string? AbGroup { get; set; }
    public int? MaxShows { get; set; }
    public int? ShowIntervalHours { get; set; }

    /// <summary>Popup. Public: đã áp mặc định BE (luôn có giá trị nếu Placement=Popup). Admin: giá trị admin đặt (null = dùng mặc định).</summary>
    public int? DisplayDelaySeconds { get; set; }

    public int? DismissCooldownHours { get; set; }
    public int? InteractionCooldownHours { get; set; }

    /// <summary>Danh sách tiền tố đường dẫn, cách nhau dấu phẩy. Null = mọi trang. FE khớp theo location.pathname.</summary>
    public string? TargetPages { get; set; }

    /// <summary>Link đã xử lý: có deep link /packages?plan=...&amp;banner={id} khi banner gắn gói.</summary>
    public string? LinkUrl { get; set; }

    public bool OpenInNewTab { get; set; }

    public BannerPlacement Placement { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Có khi banner gắn gói và ShowPlanPrice = true.</summary>
    public BannerPlanDTO? Plan { get; set; }
}

// ── Admin ─────────────────────────────────────────────────────────────────────

/// <summary>Dữ liệu đầy đủ cho admin. Đồng thời là kiểu được cache (BannerService).</summary>
public class AdminBannerDTO : BannerDTO
{
    public bool IsActive { get; set; }
    public BannerAudience Audience { get; set; }
    public string? PlanId { get; set; }
    public bool ShowPlanPrice { get; set; }

    /// <summary>LinkUrl admin nhập (chưa sinh deep link).</summary>
    public string? RawLinkUrl { get; set; }

    /// <summary>"inactive" | "scheduled" | "live" | "expired" — tính lúc trả về cho admin.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// multipart/form-data. Ảnh: gửi ImageFile (upload) HOẶC ImageUrl (https). Có cả hai → ImageFile thắng.
/// StartAt/EndAt: nên gửi ISO có offset (2026-12-01T00:00:00+07:00); nếu không có offset sẽ hiểu là GIỜ VIỆT NAM.
/// </summary>
public class CreateBannerDTO
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? CtaText { get; set; }
    public string? BadgeText { get; set; }
    public string? AltText { get; set; }

    public IFormFile? ImageFile { get; set; }
    public string? ImageUrl { get; set; }
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public IFormFile? MobileImageFile { get; set; }
    public string? MobileImageUrl { get; set; }
    public int? MobileImageWidth { get; set; }
    public int? MobileImageHeight { get; set; }
    public string? BackgroundColor { get; set; }

    public string? FitMode { get; set; }
    public int? MaxHeight { get; set; }

    public string? Device { get; set; }
    public string? TargetGenres { get; set; }
    public string? AbGroup { get; set; }
    public int? MaxShows { get; set; }
    public int? ShowIntervalHours { get; set; }
    public int? DisplayDelaySeconds { get; set; }
    public int? DismissCooldownHours { get; set; }
    public int? InteractionCooldownHours { get; set; }
    public string? TargetPages { get; set; }

    public string? LinkUrl { get; set; }
    public bool OpenInNewTab { get; set; }

    public BannerPlacement Placement { get; set; } = BannerPlacement.HomeHero;
    public BannerAudience Audience { get; set; } = BannerAudience.All;
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public string? PlanId { get; set; }
    public bool ShowPlanPrice { get; set; }
}

/// <summary>
/// PUT multipart. Field null = giữ nguyên. Chuỗi rỗng "" = xoá (Subtitle, CtaText, BadgeText, AltText, BackgroundColor, PlanId).
/// Xoá StartAt/EndAt/LinkUrl/MobileImage dùng cờ Clear*.
/// </summary>
public class UpdateBannerDTO
{
    public string? Title { get; set; }

    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? Subtitle { get; set; }

    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? CtaText { get; set; }

    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? BadgeText { get; set; }

    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? AltText { get; set; }

    public IFormFile? ImageFile { get; set; }
    public string? ImageUrl { get; set; }
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public IFormFile? MobileImageFile { get; set; }
    public string? MobileImageUrl { get; set; }
    public bool ClearMobileImage { get; set; }
    public int? MobileImageWidth { get; set; }
    public int? MobileImageHeight { get; set; }

    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? BackgroundColor { get; set; }

    public string? FitMode { get; set; }
    public int? MaxHeight { get; set; }
    public bool ClearMaxHeight { get; set; }

    public string? Device { get; set; }

    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? TargetGenres { get; set; }

    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? AbGroup { get; set; }

    public int? MaxShows { get; set; }
    public int? ShowIntervalHours { get; set; }
    public int? DisplayDelaySeconds { get; set; }
    public int? DismissCooldownHours { get; set; }
    public int? InteractionCooldownHours { get; set; }

    /// <summary>Xoá MaxShows, ShowIntervalHours, DisplayDelaySeconds và 2 cooldown (về mặc định).</summary>
    public bool ClearFrequency { get; set; }

    /// <summary>"" = xoá (hiện mọi trang).</summary>
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? TargetPages { get; set; }

    public string? LinkUrl { get; set; }
    public bool ClearLink { get; set; }
    public bool? OpenInNewTab { get; set; }

    public BannerPlacement? Placement { get; set; }
    public BannerAudience? Audience { get; set; }
    public DateTime? StartAt { get; set; }
    public bool ClearStartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool ClearEndAt { get; set; }
    public int? DisplayOrder { get; set; }
    public bool? IsActive { get; set; }

    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? PlanId { get; set; }

    public bool? ShowPlanPrice { get; set; }
}

public class SetBannerActiveDTO
{
    public bool IsActive { get; set; }
}

public class FilterBannersDTO
{
    public bool? IsActive { get; set; }
    public BannerPlacement? Placement { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// ── Tracking ──────────────────────────────────────────────────────────────────

public class BannerEventInputDTO
{
    public Guid BannerId { get; set; }

    /// <summary>"view" | "click"</summary>
    public string Type { get; set; } = string.Empty;

    public string? SessionId { get; set; }
}

public class BannerEventBatchDTO
{
    public List<BannerEventInputDTO> Events { get; set; } = new();
}

public class BannerStatsDTO
{
    public Guid BannerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Views { get; set; }
    public int Clicks { get; set; }
    public int Dismisses { get; set; }

    /// <summary>Số lần người dùng thực sự đến trang đích sau khi click.</summary>
    public int Conversions { get; set; }

    /// <summary>Clicks / Views (0..1)</summary>
    public double Ctr { get; set; }

    public int OrdersCreated { get; set; }
    public int OrdersPaid { get; set; }
    public long Revenue { get; set; }

    /// <summary>OrdersPaid / Clicks (0..1)</summary>
    public double ConversionRate { get; set; }
}

// ── Quy tắc theo vị trí ───────────────────────────────────────────────────────

public class BannerPlacementRule
{
    public BannerPlacement Placement { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int MobileWidth { get; set; }
    public int MobileHeight { get; set; }
    public int MaxItems { get; set; }
}

public static class BannerPlacementRules
{
    public static readonly IReadOnlyList<BannerPlacementRule> All = new List<BannerPlacementRule>
    {
        new()
        {
            Placement = BannerPlacement.HomeHero, Width = 1920, Height = 720, MobileWidth = 960, MobileHeight = 1200,
            MaxItems = 8
        },
        new()
        {
            Placement = BannerPlacement.HomeMiddle, Width = 1920, Height = 400, MobileWidth = 960, MobileHeight = 480,
            MaxItems = 3
        },
        new()
        {
            Placement = BannerPlacement.PricingPage, Width = 1600, Height = 500, MobileWidth = 960, MobileHeight = 600,
            MaxItems = 3
        },
        new()
        {
            Placement = BannerPlacement.PlayerPage, Width = 970, Height = 250, MobileWidth = 640, MobileHeight = 200,
            MaxItems = 2
        },
        // Popup: trả tối đa 5 ứng viên theo ưu tiên; FE chỉ hiện 1 popup/phiên và bỏ qua campaign đã bị giới hạn tần suất.
        new()
        {
            Placement = BannerPlacement.Popup, Width = 1080, Height = 1080, MobileWidth = 1080, MobileHeight = 1080,
            MaxItems = 5
        },
        new()
        {
            Placement = BannerPlacement.MovieDetail, Width = 1600, Height = 400, MobileWidth = 960, MobileHeight = 480,
            MaxItems = 2
        },
        new()
        {
            Placement = BannerPlacement.Browse, Width = 1920, Height = 300, MobileWidth = 960, MobileHeight = 360,
            MaxItems = 2
        },
        new()
        {
            Placement = BannerPlacement.StickyBottom, Width = 960, Height = 120, MobileWidth = 960, MobileHeight = 120,
            MaxItems = 1
        },
    };

    public static int MaxItems(BannerPlacement p) => All.FirstOrDefault(r => r.Placement == p)?.MaxItems ?? 3;
}