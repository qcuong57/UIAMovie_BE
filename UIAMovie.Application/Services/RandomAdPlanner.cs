// UIAMovie.Application/Services/RandomAdPlanner.cs
//
// Logic thuần (không DB, không cache, không DI) dựng lịch quảng cáo ngẫu nhiên.
// Input là ContentAdsDTO đã merge global/override → pool = các ad đang gắn slot.
// Nhận Random từ ngoài để unit test được (seed cố định).

using UIAMovie.Application.DTOs;
using UIAMovie.Application.Options;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Services;

internal static class RandomAdPlanner
{
    public static ContentAdsDTO Plan(
        ContentAdsDTO source,
        int durationSeconds,
        AdScheduleOptions opt,
        Random rng)
    {
        var plan = new ContentAdsDTO { ContentType = source.ContentType, ContentId = source.ContentId };

        var pool = source.PreRoll.Concat(source.MidRoll).Concat(source.PostRoll)
            .Where(a => !string.IsNullOrEmpty(a.VideoUrl))
            .DistinctBy(a => a.AdId)
            .ToList();
        if (pool.Count == 0) return plan;

        var picker = new AdPicker(pool, rng);

        // 1. Pre-roll: số lượng theo trọng số (nghiêng về nhiều ad đầu video)
        // Không phát lặp 1 ad nhiều lần liền nhau khi pool nhỏ: tối đa = số ad khác nhau.
        var preCount = Math.Min(PickWeighted(opt.PreRollWeights, rng), pool.Count);
        for (var i = 0; i < preCount; i++)
            plan.PreRoll.Add(Place(picker.Next(), AdPosition.PreRoll, null, i));

        // 2. Mid-roll: cứ ~MidRollIntervalRatio tổng thời lượng thêm 1 ad
        var order = 0;
        foreach (var offset in BuildMidRollOffsets(durationSeconds, opt, rng))
            plan.MidRoll.Add(Place(picker.Next(), AdPosition.MidRoll, offset, order++));

        // 3. Post-roll: có xác suất
        if (rng.NextDouble() < opt.PostRollChance)
            plan.PostRoll.Add(Place(picker.Next(), AdPosition.PostRoll, null, 0));

        return plan;
    }

    /// <summary>
    /// Offset (giây) của các mid-roll, chia theo % thời lượng: điểm thứ k nằm quanh k × MidRollIntervalRatio,
    /// lệch ngẫu nhiên ± JitterRatio × khoảng cách. Mọi ngưỡng (khoảng cách tối thiểu, vùng cuối) cũng theo %,
    /// nên cùng một cấu hình cho ra cùng tỉ lệ với video bất kỳ độ dài nào.
    /// </summary>
    internal static IEnumerable<int> BuildMidRollOffsets(int duration, AdScheduleOptions o, Random rng)
    {
        if (duration <= 0 || duration < o.MinDurationForMidRollSeconds || o.MidRollIntervalRatio <= 0)
            yield break;

        var interval = duration * o.MidRollIntervalRatio;
        var minGap = duration * o.MinMidRollGapRatio;
        var limit = duration * (1 - o.EndGuardRatio);
        var prev = 0;

        for (var k = 1; k <= o.MaxMidRolls; k++)
        {
            var jitter = (rng.NextDouble() * 2 - 1) * o.JitterRatio * interval;
            var target = k * interval + jitter;
            // Làm tròn LÊN và giữ tăng dần để khoảng cách thực tế không nhỏ hơn mức tối thiểu
            var at = Math.Max((int)Math.Ceiling(Math.Max(target, prev + minGap)), prev + 1);
            if (at > limit) yield break;
            yield return at;
            prev = at;
        }
    }

    private static int PickWeighted(int[]? weights, Random rng)
    {
        if (weights == null || weights.Length == 0) return 0;
        var total = weights.Where(w => w > 0).Sum();
        if (total <= 0) return 0;

        var roll = rng.Next(total);
        for (var i = 0; i < weights.Length; i++)
        {
            if (weights[i] <= 0) continue;
            if (roll < weights[i]) return i;
            roll -= weights[i];
        }
        return 0;
    }

    // Mỗi vị trí phải có SlotId riêng: frontend dùng slotId làm khoá "đã phát" cho mid-roll,
    // cùng 1 ad xuất hiện 2 lần mà trùng khoá thì lần sau sẽ không bao giờ phát.
    private static AdPlaybackDTO Place(AdPlaybackDTO ad, AdPosition position, int? offset, int order) => new()
    {
        AdId = ad.AdId,
        Title = ad.Title,
        VideoUrl = ad.VideoUrl,
        BrandImageUrl = ad.BrandImageUrl,
        DurationSeconds = ad.DurationSeconds,
        SkipAfterSeconds = ad.SkipAfterSeconds,
        ClickThroughUrl = ad.ClickThroughUrl,
        SlotId = Guid.NewGuid(),
        Position = position,
        MidRollOffsetSeconds = offset,
        DisplayOrder = order
    };

    /// <summary>
    /// Xáo trộn cả pool rồi lấy lần lượt → hết vòng mới xáo lại; không lặp ad liền kề.
    /// Ngoại lệ: pool chỉ có 1 ad thì buộc phải lặp.
    /// </summary>
    private sealed class AdPicker
    {
        private readonly List<AdPlaybackDTO> _pool;
        private readonly Random _rng;
        private readonly Queue<AdPlaybackDTO> _bag = new();
        private Guid _last;

        public AdPicker(List<AdPlaybackDTO> pool, Random rng)
        {
            _pool = pool;
            _rng = rng;
        }

        public AdPlaybackDTO Next()
        {
            if (_bag.Count == 0) Refill();
            var ad = _bag.Dequeue();
            _last = ad.AdId;
            return ad;
        }

        private void Refill()
        {
            var items = _pool.ToList();
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
            if (items.Count > 1 && items[0].AdId == _last)
                (items[0], items[1]) = (items[1], items[0]);
            foreach (var it in items) _bag.Enqueue(it);
        }
    }
}