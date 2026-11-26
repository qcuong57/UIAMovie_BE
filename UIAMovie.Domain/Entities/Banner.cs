// UIAMovie.Domain/Entities/Banner.cs
namespace UIAMovie.Domain.Entities;

/// <summary>Vị trí hiển thị banner trên website. Lưu dạng string trong DB.</summary>
public enum BannerPlacement
{
    HomeHero    = 1, // carousel lớn đầu trang chủ
    HomeMiddle  = 3, // banner ngang giữa trang chủ
    PricingPage = 4, // trang nạp gói / bảng giá
    PlayerPage  = 5, // dưới / cạnh trình phát
    Popup       = 6, // hộp vuông giữa màn hình, chỉ hiện ở lần đầu vào website
    MovieDetail = 7, // trang chi tiết phim
    Browse      = 8, // trang tìm kiếm / duyệt theo thể loại, quốc gia
    StickyBottom = 9 // thanh dính đáy màn hình (chỉ mobile)
}

/// <summary>Đối tượng được thấy banner. Lưu dạng string trong DB.</summary>
public enum BannerAudience
{
    All     = 1, // mọi người
    Guest   = 2, // khách chưa đăng nhập
    Free    = 3, // đã đăng nhập, chưa Premium (hoặc Premium đã hết hạn)
    Premium = 4  // đang Premium (vd: banner gia hạn, phim độc quyền...)
}

public static class BannerDevices
{
    public const string All = "all";
    public const string Desktop = "desktop";
    public const string Mobile = "mobile";
    public static bool IsValid(string? v) => v == All || v == Desktop || v == Mobile;
}

public static class BannerFitModes
{
    public const string Contain = "contain";
    public const string Cover = "cover";
    public static bool IsValid(string? v) => v == Contain || v == Cover;
}

public static class BannerEventTypes
{
    public const string View = "view";
    public const string Click = "click";
    /// <summary>Người dùng đóng popup (nút X / ESC / click nền).</summary>
    public const string Dismiss = "dismiss";
    /// <summary>Người dùng thực sự đến trang đích sau khi click CTA.</summary>
    public const string Conversion = "conversion";
    public static bool IsValid(string? t) => t == View || t == Click || t == Dismiss || t == Conversion;
}

/// <summary>Giá trị mặc định của Popup khi admin không đặt riêng cho campaign (BE trả về FE, FE không hard-code).</summary>
public static class BannerPopupDefaults
{
    public const int DisplayDelaySeconds = 8;
    public const int DismissCooldownHours = 24;        // đóng (X) → ẩn campaign 24 giờ
    public const int InteractionCooldownHours = 168;   // bấm CTA → ẩn campaign 7 ngày
}

/// <summary>Banner quảng bá — ưu đãi, sự kiện, giảm giá nạp gói.</summary>
public class Banner
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;

    // ── Nội dung động (FE render chữ/CTA đè lên ảnh nền, không phải "đóng đinh" trong ảnh) ──
    public string? Subtitle { get; set; }
    public string? CtaText { get; set; }      // vd: "Đăng ký ngay"
    public string? BadgeText { get; set; }    // vd: "-30%", "MỚI"

    /// <summary>Alt text cho accessibility. Null → FE/BE dùng Title.</summary>
    public string? AltText { get; set; }

    // ── Ảnh ──
    public string ImageUrl { get; set; } = string.Empty;
    public string? ImageCloudinaryPublicId { get; set; }
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }

    /// <summary>Ảnh riêng cho mobile (tuỳ chọn). Null = dùng ImageUrl.</summary>
    public string? MobileImageUrl { get; set; }
    public string? MobileImageCloudinaryPublicId { get; set; }
    public int? MobileImageWidth { get; set; }
    public int? MobileImageHeight { get; set; }

    /// <summary>Màu nền #RRGGBB hiển thị trong lúc ảnh đang tải (tránh nháy trắng / layout shift).</summary>
    public string? BackgroundColor { get; set; }

    /// <summary>Cách đặt ảnh vào khung: "contain" (vừa khung, không cắt) | "cover" (lấp đầy, có thể cắt).</summary>
    public string FitMode { get; set; } = BannerFitModes.Contain;

    /// <summary>Chiều cao tối đa (px) trên desktop. Null = mặc định theo vị trí (FE).</summary>
    public int? MaxHeight { get; set; }

    // ── Nhắm đối tượng nâng cao ──
    /// <summary>Thiết bị thấy banner: "all" | "desktop" | "mobile".</summary>
    public string Device { get; set; } = BannerDevices.All;

    /// <summary>Thể loại phim nhắm tới (tên, cách nhau dấu phẩy). Null = không nhắm thể loại.</summary>
    public string? TargetGenres { get; set; }

    /// <summary>Nhóm thử nghiệm A/B: các banner cùng nhóm + cùng vị trí chỉ hiện 1 cái / người (chọn ngẫu nhiên).</summary>
    public string? AbGroup { get; set; }

    /// <summary>Popup: số lần hiện tối đa / người (null = 1) và khoảng cách giữa 2 lần, tính bằng giờ (null = 24).</summary>
    public int? MaxShows { get; set; }
    public int? ShowIntervalHours { get; set; }

    /// <summary>Popup: chờ bao nhiêu giây sau khi vào trang mới hiện (null = BannerPopupDefaults.DisplayDelaySeconds).</summary>
    public int? DisplayDelaySeconds { get; set; }
    /// <summary>Popup: ẩn campaign bao nhiêu giờ sau khi người dùng đóng (null = 24).</summary>
    public int? DismissCooldownHours { get; set; }
    /// <summary>Popup: ẩn campaign bao nhiêu giờ sau khi người dùng bấm CTA (null = 168 = 7 ngày).</summary>
    public int? InteractionCooldownHours { get; set; }
    /// <summary>Các trang được hiện, đường dẫn nội bộ cách nhau dấu phẩy, FE khớp theo tiền tố. Null = mọi trang.</summary>
    public string? TargetPages { get; set; }

    // ── Link ──
    /// <summary>http(s) tuyệt đối hoặc đường dẫn nội bộ "/...". Rỗng + có PlanId → tự sinh /packages?plan={PlanId}.</summary>
    public string? LinkUrl { get; set; }
    public bool OpenInNewTab { get; set; }

    public BannerPlacement Placement { get; set; } = BannerPlacement.HomeHero;
    public BannerAudience Audience { get; set; } = BannerAudience.All;

    /// <summary>Hẹn giờ hiển thị (UTC). Null = không giới hạn.</summary>
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }

    public int DisplayOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Mã gói (SubscriptionPlan.PlanId) banner quảng bá. Khi có:
    /// banner tự ẩn nếu gói tắt / ngoài StartsAt-EndsAt / user không đủ điều kiện mua.
    /// </summary>
    public string? PlanId { get; set; }

    /// <summary>true → API trả kèm giá/tên gói để FE render giá động.</summary>
    public bool ShowPlanPrice { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Lượt hiển thị / click banner (để tính CTR, chuyển đổi).</summary>
public class BannerEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BannerId { get; set; }

    /// <summary>"view" | "click"</summary>
    public string Type { get; set; } = BannerEventTypes.View;

    public Guid? UserId { get; set; }
    public string? SessionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}