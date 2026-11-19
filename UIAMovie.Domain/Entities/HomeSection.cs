// UIAMovie.Domain/Entities/HomeSection.cs
namespace UIAMovie.Domain.Entities;

/// <summary>
/// Một khối trên trang chủ do admin cấu hình ("Phim mới nè", "Phim này cực hay"...).
/// Layout chỉ là KEY (carousel/grid/hero/ranked/banner) — FE map sang component, BE không chứa UI.
/// </summary>
public class HomeSection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }

    /// <summary>carousel | grid | hero | ranked | banner (whitelist ở HomeSectionConstants).</summary>
    public string Layout { get; set; } = "carousel";

    /// <summary>JSON tuỳ chọn riêng theo layout (số cột, aspect, showRank...). Lưu jsonb.</summary>
    public string? ConfigJson { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Hẹn giờ hiển thị (UTC). NULL = không giới hạn.</summary>
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }

    /// <summary>"Manual" (admin chọn từng phim) | "Auto" (BE tự query theo AutoRule).</summary>
    public string SourceType { get; set; } = "Manual";

    /// <summary>Auto: trending | latest | top_rated | genre</summary>
    public string? AutoRule { get; set; }

    /// <summary>Auto: tham số của rule (genre → GenreId dạng Guid).</summary>
    public string? AutoParam { get; set; }

    /// <summary>Auto: movie | tvshow | all</summary>
    public string AutoContentType { get; set; } = "all";

    /// <summary>Auto: số item tối đa.</summary>
    public int ItemLimit { get; set; } = 20;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<HomeSectionItem> Items { get; set; } = new List<HomeSectionItem>();
}

/// <summary>Phim/TV show trong khối Manual. Không có FK cứng tới Movie/TvShow vì ContentType là polymorphic.</summary>
public class HomeSectionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HomeSectionId { get; set; }

    /// <summary>"movie" | "tvshow"</summary>
    public string ContentType { get; set; } = "movie";
    public Guid ContentId { get; set; }
    public int Order { get; set; }

    public HomeSection? HomeSection { get; set; }
}