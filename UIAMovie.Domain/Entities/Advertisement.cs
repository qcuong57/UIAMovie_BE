// UIAMovie.Domain/Entities/Advertisement.cs

namespace UIAMovie.Domain.Entities;

public class Advertisement
{
    public Guid   Id    { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;

    // ── Video source (một trong hai) ─────────────────────────────────────────
    /// <summary>URL đầy đủ — nhập từ ngoài hoặc generated từ Cloudinary.</summary>
    public string? VideoUrl            { get; set; }
    /// <summary>Public ID trên Cloudinary — dùng khi upload trực tiếp.</summary>
    public string? CloudinaryPublicId  { get; set; }

    // ── Brand image (nhãn hiệu) ──────────────────────────────────────────────
    /// <summary>URL ảnh logo/nhãn hiệu — bắt buộc khi tạo ad.</summary>
    public string? BrandImageUrl                { get; set; }
    /// <summary>Public ID trên Cloudinary của ảnh nhãn hiệu (nếu upload trực tiếp).</summary>
    public string? BrandImageCloudinaryPublicId  { get; set; }

    // ── Playback config ──────────────────────────────────────────────────────
    public int     DurationSeconds     { get; set; }
    /// <summary>NULL = không được skip. 0 = skip ngay. >0 = skip sau N giây.</summary>
    public int?    SkipAfterSeconds    { get; set; }

    public string? ClickThroughUrl     { get; set; }

    // ── Status ───────────────────────────────────────────────────────────────
    public bool      IsActive   { get; set; } = true;
    public DateTime  CreatedAt  { get; set; } = DateTime.UtcNow;
    public DateTime  UpdatedAt  { get; set; } = DateTime.UtcNow;

    // ── Navigation ───────────────────────────────────────────────────────────
    public ICollection<GlobalAdSlot>        GlobalSlots { get; set; } = new List<GlobalAdSlot>();
    public ICollection<AdContentOverride>   Overrides   { get; set; } = new List<AdContentOverride>();
}