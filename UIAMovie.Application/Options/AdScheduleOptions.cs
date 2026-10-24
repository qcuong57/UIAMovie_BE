// UIAMovie.Application/Options/AdScheduleOptions.cs
//
// Cấu hình chế độ lập lịch quảng cáo.
//   - Mặc định bind từ appsettings: "Ads:Schedule".
//   - Admin chỉnh được trên giao diện (PUT /api/ads/schedule-settings) → AdService lưu đè lên mặc định.
//   Fixed  = dùng đúng global slot / override admin đã đặt (hành vi cũ).
//   Random = kiểu YouTube: dựng lịch ngẫu nhiên mỗi lượt xem từ các ad đã gắn global slot.

using System.Text.Json.Serialization;

namespace UIAMovie.Application.Options;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AdScheduleMode
{
    Fixed = 0,
    Random = 1
}

public class AdScheduleOptions
{
    public const string SectionName = "Ads:Schedule";
    private static readonly int[] DefaultPreRollWeights = { 5, 40, 40, 15 };
    private const int MaxPreRollSlots = 6;

    public AdScheduleMode Mode { get; set; } = AdScheduleMode.Fixed;

    /// <summary>
    /// Trọng số số lượng pre-roll: phần tử thứ i = độ ưu tiên của "i ad đầu video".
    /// Mặc định {5,40,40,15}: 0 ad 5% · 1 ad 40% · 2 ad 40% · 3 ad 15% → nghiêng về nhiều ad ở đầu.
    /// </summary>
    public int[] PreRollWeights { get; set; } = (int[])DefaultPreRollWeights.Clone();

    // ── Mid-roll: TẤT CẢ tính theo % thời lượng của chính video → video 2 phút hay 2 tiếng đều chia cùng tỉ lệ ──

    /// <summary>Cứ mỗi bao nhiêu % thời lượng thì thêm 1 mid-roll (0.2 = ở 20%, 40%, 60%, 80%…).</summary>
    public double MidRollIntervalRatio { get; set; } = 0.2;

    /// <summary>Lệch ngẫu nhiên ± so với khoảng cách trên (0.25 = ±25% của khoảng cách) để không đều như máy.</summary>
    public double JitterRatio { get; set; } = 0.25;

    /// <summary>Khoảng cách tối thiểu giữa 2 mid-roll liền nhau (và từ đầu video đến mid-roll đầu), theo % thời lượng.</summary>
    public double MinMidRollGapRatio { get; set; } = 0.1;

    /// <summary>Không chèn mid-roll trong N% cuối video (tránh sát post-roll / credits).</summary>
    public double EndGuardRatio { get; set; } = 0.1;

    /// <summary>Tuỳ chọn: video ngắn hơn mức này (giây) thì bỏ mid-roll. 0 = tắt, video nào cũng chia theo %.</summary>
    public int MinDurationForMidRollSeconds { get; set; } = 0;

    public int MaxMidRolls { get; set; } = 6;

    /// <summary>Xác suất có 1 post-roll (0..1).</summary>
    public double PostRollChance { get; set; } = 0.5;

    /// <summary>
    /// Bản sao đã ép về giới hạn hợp lệ (dữ liệu admin nhập không tin tưởng được).
    /// Trả object MỚI, không đụng vào instance gốc (IOptions là singleton dùng chung).
    /// </summary>
    public AdScheduleOptions Normalized()
    {
        var weights = (PreRollWeights ?? Array.Empty<int>())
            .Take(MaxPreRollSlots)
            .Select(w => Math.Clamp(w, 0, 1000))
            .ToArray();
        if (weights.Sum() <= 0) weights = (int[])DefaultPreRollWeights.Clone();

        return new AdScheduleOptions
        {
            Mode = Enum.IsDefined(Mode) ? Mode : AdScheduleMode.Fixed,
            PreRollWeights = weights,
            MidRollIntervalRatio = Math.Clamp(MidRollIntervalRatio, 0, 1),
            JitterRatio = Math.Clamp(JitterRatio, 0, 0.9),
            MinMidRollGapRatio = Math.Clamp(MinMidRollGapRatio, 0, 0.5),
            EndGuardRatio = Math.Clamp(EndGuardRatio, 0, 0.9),
            MinDurationForMidRollSeconds = Math.Max(0, MinDurationForMidRollSeconds),
            MaxMidRolls = Math.Clamp(MaxMidRolls, 0, 20),
            PostRollChance = Math.Clamp(PostRollChance, 0, 1)
        };
    }
}