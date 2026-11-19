// UIAMovie.Application/DTOs/HomeSectionDTOs.cs
namespace UIAMovie.Application.DTOs;

/// <summary>Constants dùng chung cho validate + trả về /meta cho admin UI.</summary>
public static class HomeSectionConstants
{
    public static readonly string[] Layouts =
    {
        "carousel", "grid", "hero", "ranked", "banner",
        "spotlight", "mosaic", "accordion", "list",
        "billboard", "trailer", "split"
    };
    public static readonly string[] AutoRules = { "trending", "latest", "top_rated", "genre" };
    public static readonly string[] ContentTypes = { "movie", "tvshow" };
    public static readonly string[] AutoContentTypes = { "movie", "tvshow", "all" };

    public const int MaxManualItems = 50;
    public const int MaxConfigLength = 4000;

    /// <summary>Số phim tối đa layout thực sự hiển thị. Layout không có trong bảng = chỉ bị chặn bởi MaxManualItems.</summary>
    public static readonly Dictionary<string, int> LayoutLimits = new()
    {
        ["hero"] = 5, ["billboard"] = 5, ["spotlight"] = 8,
        ["mosaic"] = 5, ["accordion"] = 6, ["split"] = 10, ["ranked"] = 10
    };

    /// <summary>Kẹp số lượng theo trần của layout (và MaxManualItems). requested &lt;= 0 → dùng trần.</summary>
    public static int EffectiveLimit(string? layout, int requested)
    {
        var cap = layout != null && LayoutLimits.TryGetValue(layout, out var m) ? Math.Min(m, MaxManualItems) : MaxManualItems;
        return Math.Clamp(requested <= 0 ? Math.Min(20, cap) : requested, 1, cap);
    }
}

/// <summary>Thẻ nội dung gọn, dùng chung cho cả phim lẻ và phim bộ để FE render 1 kiểu.</summary>
public class HomeContentCardDTO
{
    /// <summary>movie | tvshow — FE dùng để build link /movies/{id} hay /tvshows/{id}.</summary>
    public string ContentType { get; set; } = "movie";
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public decimal? Rating { get; set; }

    /// <summary>Movie.ReleaseDate hoặc TvShow.FirstAirDate.</summary>
    public DateTime? ReleaseDate { get; set; }
    public string? OriginCountry { get; set; }
    public bool IsPremium { get; set; }
    public bool IsUpcoming { get; set; }
    public List<string> Genres { get; set; } = new();
    public string? TrailerKey { get; set; }
    public string? TrailerVideoUrl { get; set; }

    /// <summary>Chỉ movie (phút).</summary>
    public int? Duration { get; set; }

    /// <summary>Chỉ tvshow.</summary>
    public int? NumberOfSeasons { get; set; }
}

public class HomeSectionItemDTO
{
    /// <summary>Guid.Empty với khối Auto (item không lưu DB).</summary>
    public Guid Id { get; set; }
    public string ContentType { get; set; } = "movie";
    public Guid ContentId { get; set; }
    public int Order { get; set; }

    /// <summary>Đã resolve sẵn để FE render, không phải gọi thêm API.</summary>
    public HomeContentCardDTO Content { get; set; } = new();
}

public class HomeSectionDTO
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string Layout { get; set; } = "carousel";
    public Dictionary<string, object>? Config { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }

    public string SourceType { get; set; } = "Manual";
    public string? AutoRule { get; set; }
    public string? AutoParam { get; set; }
    public string AutoContentType { get; set; } = "all";
    public int ItemLimit { get; set; } = 20;

    public List<HomeSectionItemDTO> Items { get; set; } = new();
}

public class UpsertHomeSectionDTO
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string Layout { get; set; } = "carousel";
    public Dictionary<string, object>? Config { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }

    public string SourceType { get; set; } = "Manual";
    public string? AutoRule { get; set; }
    public string? AutoParam { get; set; }
    public string AutoContentType { get; set; } = "all";
    public int ItemLimit { get; set; } = 20;

    /// <summary>Chỉ dùng khi SourceType = Manual. Thứ tự trong list = thứ tự hiển thị.</summary>
    public List<HomeSectionItemInputDTO> Items { get; set; } = new();
}

public class HomeSectionItemInputDTO
{
    public string ContentType { get; set; } = "movie";
    public Guid ContentId { get; set; }
}

/// <summary>PUT /api/admin/home-sections/reorder</summary>
public class ReorderHomeSectionsDTO
{
    public List<Guid> SectionIds { get; set; } = new();
}

/// <summary>PATCH /api/admin/home-sections/{id}/active</summary>
public class SetHomeSectionActiveDTO
{
    public bool IsActive { get; set; }
}

/// <summary>GET /api/admin/home-sections/meta — để admin UI dựng dropdown mà không hard code.</summary>
public class HomeSectionMetaDTO
{
    public List<string> Layouts { get; set; } = new();
    public List<string> AutoRules { get; set; } = new();
    public List<string> ContentTypes { get; set; } = new();
    public List<string> AutoContentTypes { get; set; } = new();
    public int MaxManualItems { get; set; }

    /// <summary>layout → số phim tối đa thực sự hiển thị.</summary>
    public Dictionary<string, int> LayoutLimits { get; set; } = new();

    /// <summary>Chi tiết từng rule tự động (label + kiểu tham số) để admin UI dựng form động.</summary>
    public List<AutoRuleMetaDTO> AutoRuleDetails { get; set; } = new();
}

public class AutoRuleMetaDTO
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>none | genre | year</summary>
    public string ParamKind { get; set; } = "none";
}