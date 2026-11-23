// UIAMovie.Application/Services/PlaySessionService.cs
//
// Play session: server là nguồn sự thật về "người này đã xem đủ quảng cáo chưa".
//
//   1. POST /api/playback/session            → server chốt lịch quảng cáo (snapshot) + cấp playSessionId
//   2. POST /api/playback/session/{id}/ads/{ref}/start|complete
//                                            → server đo thời gian THỰC giữa start và complete
//   3. GET  playback movie/episode (kèm id)   → chỉ cấp link phim khi mọi pre-roll đã complete
//   4. POST heartbeat (kèm id + vị trí)       → chỉ đếm view khi các mid-roll đã qua vị trí hiện tại đều complete
//
// Giới hạn đã biết (xem ghi chú cuối file): không ép được phía CDN, chỉ ép cấp link + đếm view.
// Trạng thái lưu trong ICacheService (Redis). Read-modify-write được xếp hàng theo session trong 1 instance;
// nhiều instance cần khoá phân tán (cùng giới hạn với ViewTrackingService).

using System.Security.Cryptography;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Services;

public enum PlaySessionResult { Ok, NoSession, Forbidden, UnknownAd, OutOfOrder, TooEarly, AdsPending }

public class PlaySessionAd
{
    public string Ref { get; set; } = string.Empty;          // "pre-0", "mid-1", "post-0"
    public AdPosition Position { get; set; }
    public int OffsetSeconds { get; set; }
    public int DurationSeconds { get; set; }
    public int? SkipAfterSeconds { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class PlaySessionState
{
    public string Id { get; set; } = string.Empty;
    public string OwnerKey { get; set; } = string.Empty;     // "u:{userId}" | "g:{guestId}"
    public AdContentType ContentType { get; set; }
    public Guid ContentId { get; set; }
    public bool RequiresAds { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ContentGrantedAt { get; set; }
    public List<PlaySessionAd> Ads { get; set; } = new();
}

public interface IPlaySessionService
{
    Task<PlaySessionStartedDTO> StartAsync(Guid? userId, string? guestId, StartPlaySessionDTO dto);
    Task<PlaySessionResult> AdStartedAsync(string sessionId, string ownerKey, string adRef);
    Task<PlaySessionResult> AdCompletedAsync(string sessionId, string ownerKey, string adRef);

    /// <summary>Gọi ở endpoint playback ngay trước khi trả link phim.</summary>
    Task<PlaySessionResult> AuthorizeContentAsync(string? sessionId, string ownerKey, AdContentType type, Guid contentId);

    /// <summary>Gọi ở heartbeat. positionSeconds = currentTime của phim (không phải của quảng cáo).</summary>
    Task<PlaySessionResult> ValidateHeartbeatAsync(string? sessionId, string ownerKey, AdContentType type, Guid contentId, int? positionSeconds);
}

public class PlaySessionService : IPlaySessionService
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(6);
    private const int AdTimeToleranceSeconds = 2;            // trễ mạng giữa start và complete
    private const int MaxAdsPerSession = 30;

    private static readonly SemaphoreSlim[] Locks =
        Enumerable.Range(0, 256).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private static SemaphoreSlim LockFor(string id) => Locks[(uint)id.GetHashCode() % (uint)Locks.Length];

    private readonly ICacheService _cache;
    private readonly IAdService _ads;
    private readonly IContentMetaProvider _meta;
    private readonly ISubscriptionChecker _subscription;

    public PlaySessionService(ICacheService cache, IAdService ads, IContentMetaProvider meta, ISubscriptionChecker subscription)
    {
        _cache = cache;
        _ads = ads;
        _meta = meta;
        _subscription = subscription;
    }

    public static string OwnerKeyOf(Guid? userId, string? guestId)
        => userId.HasValue ? $"u:{userId}" : $"g:{guestId}";

    private static string Key(string id) => $"playsession:{id}";

    // ── Start ────────────────────────────────────────────────────────────────

    public async Task<PlaySessionStartedDTO> StartAsync(Guid? userId, string? guestId, StartPlaySessionDTO dto)
    {
        if (!userId.HasValue && string.IsNullOrWhiteSpace(guestId))
            throw new ArgumentException("Thiếu danh tính người xem.");
        if (dto.ContentType is not (AdContentType.Movie or AdContentType.Episode) || dto.ContentId == Guid.Empty)
            throw new ArgumentException("Loại nội dung không hợp lệ.");
        if (dto.ContentType == AdContentType.Episode && (dto.ParentId is null || dto.ParentId == Guid.Empty))
            throw new ArgumentException("Tập phim cần ParentId (id của bộ).");

        // Thời lượng lấy từ DB, KHÔNG tin client (client có thể khai 1s để không có mid-roll).
        var durationSeconds = await ResolveDurationSecondsAsync(dto);

        // Gói cho phép xem Premium = không quảng cáo. (Giả định: CanWatchPremiumContentAsync ⇔ đang có gói trả phí.
        // Nếu bạn có cờ riêng kiểu "AdFree", đổi ở đây.)
        var requiresAds = !(userId.HasValue && await _subscription.CanWatchPremiumContentAsync(userId.Value));

        var plan = requiresAds
            ? await _ads.GetAdsForContentAsync(dto.ContentType, dto.ContentId, durationSeconds, dto.ParentId)
            : new ContentAdsDTO { ContentType = dto.ContentType, ContentId = dto.ContentId };

        var state = new PlaySessionState
        {
            Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            OwnerKey = OwnerKeyOf(userId, guestId),
            ContentType = dto.ContentType,
            ContentId = dto.ContentId,
            RequiresAds = requiresAds,
            CreatedAt = DateTime.UtcNow
        };

        var response = new ContentAdsDTO { ContentType = plan.ContentType, ContentId = plan.ContentId };
        AddGroup(plan.PreRoll.Where(HasVideo), "pre", state, response.PreRoll);
        AddGroup(plan.MidRoll.Where(HasVideo), "mid", state, response.MidRoll);
        AddGroup(plan.PostRoll.Where(HasVideo), "post", state, response.PostRoll);

        await _cache.SetAsync(Key(state.Id), state, SessionTtl);

        return new PlaySessionStartedDTO
        {
            PlaySessionId = state.Id,
            RequiresAds = requiresAds && state.Ads.Count > 0,
            Ads = response,
            ExpiresInSeconds = (int)SessionTtl.TotalSeconds,
            PlanDurationSeconds = durationSeconds
        };
    }

    private static bool HasVideo(AdPlaybackDTO a) => !string.IsNullOrWhiteSpace(a.VideoUrl);

    private static void AddGroup(IEnumerable<AdPlaybackDTO> src, string prefix, PlaySessionState state, List<AdPlaybackDTO> output)
    {
        var i = 0;
        foreach (var a in src)
        {
            if (state.Ads.Count >= MaxAdsPerSession) return;
            var gref = $"{prefix}-{i++}";
            state.Ads.Add(new PlaySessionAd
            {
                Ref = gref,
                Position = a.Position,
                OffsetSeconds = a.MidRollOffsetSeconds ?? 0,
                DurationSeconds = Math.Max(a.DurationSeconds, 0),
                SkipAfterSeconds = a.SkipAfterSeconds
            });
            output.Add(new AdPlaybackDTO
            {
                GrantRef = gref,
                AdId = a.AdId,
                Title = a.Title,
                VideoUrl = a.VideoUrl,
                BrandImageUrl = a.BrandImageUrl,
                DurationSeconds = a.DurationSeconds,
                SkipAfterSeconds = a.SkipAfterSeconds,
                ClickThroughUrl = a.ClickThroughUrl,
                SlotId = a.SlotId,
                Position = a.Position,
                MidRollOffsetSeconds = a.MidRollOffsetSeconds,
                DisplayOrder = a.DisplayOrder
            });
        }
    }

    // DurationMinutes có thể là int? (không biết thời lượng) → 0 giây; RandomAdPlanner tự xử lý 0.
    private static int MinutesToSeconds(int? minutes) => (minutes ?? 0) * 60;

    private async Task<int> ResolveDurationSecondsAsync(StartPlaySessionDTO dto)
    {
        if (dto.ContentType == AdContentType.Movie)
        {
            var movie = await _meta.GetMovieAsync(dto.ContentId);
            if (movie is null || !movie.IsPublished) throw new KeyNotFoundException("Movie not found");
            return MinutesToSeconds(movie.DurationMinutes);
        }

        var show = await _meta.GetTvShowAsync(dto.ParentId!.Value);
        if (show is null || !show.IsPublished) throw new KeyNotFoundException("TV show not found");
        var minutes = await _meta.GetEpisodeRuntimeMinutesAsync(dto.ParentId.Value, dto.ContentId) ?? show.DurationMinutes;
        return MinutesToSeconds(minutes);
    }

    // ── Ad start / complete ──────────────────────────────────────────────────

    public Task<PlaySessionResult> AdStartedAsync(string sessionId, string ownerKey, string adRef)
        => MutateAsync(sessionId, ownerKey, state =>
        {
            var ad = state.Ads.FirstOrDefault(a => a.Ref == adRef);
            if (ad is null) return PlaySessionResult.UnknownAd;
            if (ad.CompletedAt != null) return PlaySessionResult.Ok;

            if (ad.Position == AdPosition.PreRoll)
            {
                // Pre-roll phải đi theo thứ tự: ad trước đó xong mới được bắt đầu ad sau.
                if (state.Ads.Where(a => a.Position == AdPosition.PreRoll)
                             .TakeWhile(a => a.Ref != adRef)
                             .Any(a => a.CompletedAt == null))
                    return PlaySessionResult.OutOfOrder;
            }
            else if (state.ContentGrantedAt == null)
            {
                return PlaySessionResult.OutOfOrder; // mid/post-roll chỉ có nghĩa sau khi phim đã được cấp
            }

            ad.StartedAt ??= DateTime.UtcNow;
            return PlaySessionResult.Ok;
        });

    public Task<PlaySessionResult> AdCompletedAsync(string sessionId, string ownerKey, string adRef)
        => MutateAsync(sessionId, ownerKey, state =>
        {
            var ad = state.Ads.FirstOrDefault(a => a.Ref == adRef);
            if (ad is null) return PlaySessionResult.UnknownAd;
            if (ad.CompletedAt != null) return PlaySessionResult.Ok;
            if (ad.StartedAt is null) return PlaySessionResult.OutOfOrder;

            // Phải xem tối thiểu: tới mốc skip (nếu có) hoặc hết ad. Đo bằng đồng hồ server.
            var required = ad.SkipAfterSeconds is { } s ? Math.Min(s, ad.DurationSeconds) : ad.DurationSeconds;
            var elapsed = (DateTime.UtcNow - ad.StartedAt.Value).TotalSeconds;
            if (elapsed < required - AdTimeToleranceSeconds) return PlaySessionResult.TooEarly;

            ad.CompletedAt = DateTime.UtcNow;
            return PlaySessionResult.Ok;
        });

    // ── Cấp link phim / hợp lệ hoá heartbeat ─────────────────────────────────

    public Task<PlaySessionResult> AuthorizeContentAsync(string? sessionId, string ownerKey, AdContentType type, Guid contentId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return Task.FromResult(PlaySessionResult.NoSession);

        return MutateAsync(sessionId, ownerKey, state =>
        {
            if (state.ContentType != type || state.ContentId != contentId) return PlaySessionResult.Forbidden;
            if (state.RequiresAds && state.Ads.Any(a => a.Position == AdPosition.PreRoll && a.CompletedAt == null))
                return PlaySessionResult.AdsPending;

            state.ContentGrantedAt ??= DateTime.UtcNow;
            return PlaySessionResult.Ok;
        });
    }

    public async Task<PlaySessionResult> ValidateHeartbeatAsync(
        string? sessionId, string ownerKey, AdContentType type, Guid contentId, int? positionSeconds)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return PlaySessionResult.NoSession;

        // Chỉ đọc (heartbeat là đường nóng): không cần khoá.
        var state = await _cache.GetAsync<PlaySessionState>(Key(sessionId));
        if (state is null) return PlaySessionResult.NoSession;
        if (state.OwnerKey != ownerKey || state.ContentType != type || state.ContentId != contentId)
            return PlaySessionResult.Forbidden;
        if (state.ContentGrantedAt == null) return PlaySessionResult.AdsPending;

        if (state.RequiresAds)
        {
            var midRolls = state.Ads.Where(a => a.Position == AdPosition.MidRoll).ToList();
            if (midRolls.Count > 0)
            {
                // Không khai vị trí = coi như đã ở cuối phim: mọi mid-roll phải xong mới được đếm.
                var pos = positionSeconds ?? int.MaxValue;
                if (midRolls.Any(a => a.OffsetSeconds <= pos && a.CompletedAt == null))
                    return PlaySessionResult.AdsPending;
            }
        }
        return PlaySessionResult.Ok;
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private async Task<PlaySessionResult> MutateAsync(string sessionId, string ownerKey, Func<PlaySessionState, PlaySessionResult> apply)
    {
        var gate = LockFor(sessionId);
        await gate.WaitAsync();
        try
        {
            var state = await _cache.GetAsync<PlaySessionState>(Key(sessionId));
            if (state is null) return PlaySessionResult.NoSession;
            if (state.OwnerKey != ownerKey) return PlaySessionResult.Forbidden;

            var result = apply(state);
            if (result == PlaySessionResult.Ok)
            {
                // Giữ TTL tính từ lần ghi cuối: phiên xem dài vẫn sống, phiên bỏ dở tự dọn.
                await _cache.SetAsync(Key(sessionId), state, SessionTtl);
            }
            return result;
        }
        finally
        {
            gate.Release();
        }
    }
}

// ── Giới hạn còn lại (nói thẳng) ─────────────────────────────────────────────
//  • Đã có link phim (đã ký, chưa hết hạn) thì người dùng có thể phát ở nơi khác, bỏ mid-roll: server không
//    nhìn thấy luồng video. Cái ép được là: cấp link (pre-roll) và đếm view (mid-roll).
//  • Muốn ép cả mid-roll ở phía CDN: link có hạn ngắn + cấp lại theo từng đoạn, hoặc SSAI.