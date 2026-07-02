// UIAMovie.Domain/Entities/AdSchedule.cs
//
// GlobalAdSlot — thay thế AdSchedule (per-content) bằng global rule.
//
// Cách hoạt động:
//   - AppliesTo = null  → áp dụng cho toàn bộ content (Movie + TvShow + Episode).
//   - AppliesTo = Movie → chỉ áp khi player xem phim.
//   - AppliesTo = TvShow → áp cho mọi episode thuộc bất kỳ TvShow nào.
//   - AppliesTo = Episode → áp cho mọi episode.
//
// Override (content-specific) vẫn hỗ trợ: AdContentOverride.

namespace UIAMovie.Domain.Entities;

/// <summary>
/// Quy tắc phát ad toàn cục (global slot).
/// Không gắn vào content cụ thể — áp dụng theo loại content hoặc toàn bộ.
/// </summary>
public class GlobalAdSlot
{
    public Guid   Id              { get; set; } = Guid.NewGuid();
    public Guid   AdvertisementId { get; set; }

    // ── Scope ────────────────────────────────────────────────────────────────
    /// <summary>
    /// NULL = áp cho tất cả loại content.
    /// Có giá trị = chỉ áp cho loại content đó.
    /// </summary>
    public AdContentType? AppliesTo { get; set; }

    // ── Placement ────────────────────────────────────────────────────────────
    public AdPosition Position              { get; set; }
    /// <summary>Chỉ dùng khi Position = MidRoll (giây).</summary>
    public int?       MidRollOffsetSeconds  { get; set; }

    /// <summary>Thứ tự phát nếu nhiều ad cùng Position. Nhỏ hơn = phát trước.</summary>
    public int  DisplayOrder { get; set; } = 0;
    public bool IsActive     { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ───────────────────────────────────────────────────────────
    public Advertisement Advertisement { get; set; } = null!;
}

/// <summary>
/// Override per-content: bỏ qua global ads cho một content cụ thể
/// hoặc bổ sung ad riêng cho content đó.
/// </summary>
public class AdContentOverride
{
    public Guid   Id              { get; set; } = Guid.NewGuid();
    public Guid   AdvertisementId { get; set; }

    // ── Target content ───────────────────────────────────────────────────────
    public AdContentType ContentType { get; set; }
    public Guid          ContentId   { get; set; }

    public AdPosition Position              { get; set; }
    public int?       MidRollOffsetSeconds  { get; set; }
    public int        DisplayOrder          { get; set; } = 0;
    public bool       IsActive              { get; set; } = true;
    public DateTime   CreatedAt             { get; set; } = DateTime.UtcNow;

    public Advertisement Advertisement { get; set; } = null!;
}

// ── Enums ────────────────────────────────────────────────────────────────────

/// <summary>
/// Loại content. Dùng trong AppliesTo (GlobalAdSlot) và ContentType (override).
/// Lưu dưới dạng string trong DB.
/// </summary>
public enum AdContentType
{
    Movie   = 1,
    TvShow  = 2,
    Episode = 3
}

/// <summary>Vị trí phát ad trong luồng xem.</summary>
public enum AdPosition
{
    PreRoll  = 1,
    MidRoll  = 2,
    PostRoll = 3
}

// ── Backward compat alias (giữ để các file cũ không lỗi ngay) ───────────────
/// <summary>
/// Deprecated — chỉ giữ để không phá compile. Dùng GlobalAdSlot thay thế.
/// </summary>
[Obsolete("Use GlobalAdSlot instead.")]
public class AdSchedule : GlobalAdSlot { }