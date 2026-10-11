// UIAMovie.Application/Services/ViewTrackingService.cs

using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Services;

/// <summary>
/// Đường nóng: mỗi người đang xem gửi 1 heartbeat / 15–30s.
/// Chi phí mỗi heartbeat (session đã tồn tại): 1 SELECT session + 1 UPDATE; tra phim/show đi qua cache trong process.
/// </summary>
public class ViewTrackingService : IViewTrackingService
{
    /// <summary>Heartbeat FE gửi mỗi 15–30s; cho phép tới 30s. Ngoài khoảng → bị chặn.</summary>
    private const int MAX_BEAT_SECONDS = 30;

    /// <summary>Dung sai (giây) khi so với thời gian thực trôi qua giữa 2 heartbeat.</summary>
    private const int ELAPSED_TOLERANCE_SECONDS = 3;

    /// <summary>Ngưỡng chuẩn: 120s. Với nội dung ngắn hơn 2400s (40 phút) dùng 5% thời lượng.</summary>
    private const int BASE_THRESHOLD_SECONDS = 120;
    private const int SHORT_CONTENT_SECONDS  = 2400;
    private const double SHORT_CONTENT_RATIO = 0.05;

    private readonly IViewSessionRepository _sessionRepository;
    private readonly IContentMetaProvider _meta;
    private readonly ISubscriptionChecker _subscriptionChecker;
    private readonly IClock _clock;

    public ViewTrackingService(
        IViewSessionRepository sessionRepository,
        IContentMetaProvider meta,
        ISubscriptionChecker subscriptionChecker,
        IClock clock)
    {
        _sessionRepository = sessionRepository;
        _meta = meta;
        _subscriptionChecker = subscriptionChecker;
        _clock = clock;
    }

    // ── MOVIE ───────────────────────────────────────────────────────────────
    public async Task<HeartbeatResultDTO> RecordMovieHeartbeatAsync(Guid? userId, string? guestId, HeartbeatDTO dto)
    {
        EnsureIdentity(userId, guestId);

        // Phim chưa publish coi như không tồn tại → không bơm view được vào phim ẩn
        var movie = await _meta.GetMovieAsync(dto.MovieId);
        if (movie is null || !movie.IsPublished)
            throw new KeyNotFoundException("Movie not found");

        var (session, isNew) = await GetOrCreateMovieSessionAsync(userId, guestId, dto.MovieId, _clock.Today, movie.IsPremium);

        var add = ClampSeconds(dto.SecondsSinceLastBeat, isNew ? null : session.LastHeartbeatAt);
        await _sessionRepository.AddMovieSecondsAsync(session.Id, add); // 1 UPDATE nguyên tử, không SELECT lại

        // Tổng ước lượng từ snapshot + phần vừa cộng. Nếu 2 heartbeat chồng nhau, lần này có thể thấp hơn DB
        // một chút → chỉ trễ đúng 1 heartbeat khi qua ngưỡng; ViewCount vẫn chính xác (claim nguyên tử).
        var total = session.AccumulatedSeconds + add;
        var counted = session.IsCountedAsView || total >= ComputeThreshold(movie.DurationMinutes * 60);

        if (counted && !session.IsCountedAsView)
            await _sessionRepository.TryClaimMovieViewAsync(session);

        return new HeartbeatResultDTO { AccumulatedSeconds = total, IsCountedAsView = counted };
    }

    private async Task<(MovieViewSession Session, bool IsNew)> GetOrCreateMovieSessionAsync(
        Guid? userId, string? guestId, Guid movieId, DateOnly date, bool isPremium)
    {
        var existing = await _sessionRepository.GetMovieSessionAsync(userId, guestId, movieId, date);
        if (existing != null) return (existing, false);

        // Chỉ kiểm tra quyền Premium khi TẠO session mới (heartbeat đầu tiên trong ngày);
        // session đã có nghĩa là đã qua kiểm tra → không tốn query kiểm tra gói ở mỗi heartbeat.
        await EnsurePremiumAccessAsync(isPremium, userId);

        var created = new MovieViewSession
        {
            UserId = userId,
            GuestId = userId.HasValue ? null : guestId,
            MovieId = movieId,
            ViewDate = date
        };
        if (await _sessionRepository.TryAddMovieSessionAsync(created)) return (created, true);

        // Request khác vừa tạo trước → load lại để cộng dồn
        var reloaded = await _sessionRepository.GetMovieSessionAsync(userId, guestId, movieId, date)
                       ?? throw new InvalidOperationException("Could not record heartbeat");
        return (reloaded, false);
    }

    // ── TV SHOW ─────────────────────────────────────────────────────────────
    public async Task<HeartbeatResultDTO> RecordTvShowHeartbeatAsync(Guid? userId, string? guestId, TvShowHeartbeatDTO dto)
    {
        EnsureIdentity(userId, guestId);

        var show = await _meta.GetTvShowAsync(dto.TvShowId);
        if (show is null || !show.IsPublished)
            throw new KeyNotFoundException("TV show not found");

        // Thời lượng tính ngưỡng: runtime tập đang xem (phút) → fallback runtime mặc định của show
        var runtimeMinutes = show.DurationMinutes;
        if (dto.EpisodeId.HasValue)
            runtimeMinutes = await _meta.GetEpisodeRuntimeMinutesAsync(dto.TvShowId, dto.EpisodeId.Value) ?? runtimeMinutes;

        var (session, isNew) = await GetOrCreateTvShowSessionAsync(userId, guestId, dto.TvShowId, _clock.Today, show.IsPremium);

        var add = ClampSeconds(dto.SecondsSinceLastBeat, isNew ? null : session.LastHeartbeatAt);
        await _sessionRepository.AddTvShowSecondsAsync(session.Id, add);

        var total = session.AccumulatedSeconds + add;
        var counted = session.IsCountedAsView || total >= ComputeThreshold(runtimeMinutes * 60);

        if (counted && !session.IsCountedAsView)
            await _sessionRepository.TryClaimTvShowViewAsync(session);

        return new HeartbeatResultDTO { AccumulatedSeconds = total, IsCountedAsView = counted };
    }

    private async Task<(TvShowViewSession Session, bool IsNew)> GetOrCreateTvShowSessionAsync(
        Guid? userId, string? guestId, Guid tvShowId, DateOnly date, bool isPremium)
    {
        var existing = await _sessionRepository.GetTvShowSessionAsync(userId, guestId, tvShowId, date);
        if (existing != null) return (existing, false);

        await EnsurePremiumAccessAsync(isPremium, userId);

        var created = new TvShowViewSession
        {
            UserId = userId,
            GuestId = userId.HasValue ? null : guestId,
            TvShowId = tvShowId,
            ViewDate = date
        };
        if (await _sessionRepository.TryAddTvShowSessionAsync(created)) return (created, true);

        var reloaded = await _sessionRepository.GetTvShowSessionAsync(userId, guestId, tvShowId, date)
                       ?? throw new InvalidOperationException("Could not record heartbeat");
        return (reloaded, false);
    }

    // ── Logic dùng chung ────────────────────────────────────────────────────

    private static void EnsureIdentity(Guid? userId, string? guestId)
    {
        if (!userId.HasValue && string.IsNullOrWhiteSpace(guestId))
            throw new ArgumentException("Missing user or guest identity");
    }

    /// <summary>
    /// Nội dung Premium: chỉ user đã đăng nhập VÀ có quyền xem mới được đếm view
    /// (khớp với kiểm tra ở endpoint watch/playback). Guest hoặc user free → UnauthorizedAccessException (403).
    /// </summary>
    private async Task EnsurePremiumAccessAsync(bool isPremium, Guid? userId)
    {
        if (!isPremium) return;
        if (!userId.HasValue || !await _subscriptionChecker.CanWatchPremiumContentAsync(userId.Value))
            throw new UnauthorizedAccessException("Premium content");
    }

    /// <summary>Ngưỡng view hợp lệ (giây). durationSeconds NULL (không biết thời lượng) → 120s.</summary>
    private static double ComputeThreshold(int? durationSeconds) =>
        durationSeconds is > 0 and < SHORT_CONTENT_SECONDS
            ? durationSeconds.Value * SHORT_CONTENT_RATIO
            : BASE_THRESHOLD_SECONDS;

    /// <summary>
    /// Chống gian lận: số giây cộng thêm nằm trong [0, 30] và không vượt quá thời gian thực đã trôi qua
    /// kể từ heartbeat trước (+ dung sai). Session mới chưa có mốc trước → chỉ chặn 0–30.
    /// </summary>
    private static int ClampSeconds(int requested, DateTime? lastHeartbeatUtc)
    {
        var seconds = Math.Clamp(requested, 0, MAX_BEAT_SECONDS);
        if (lastHeartbeatUtc.HasValue)
        {
            var elapsed = (int)(DateTime.UtcNow - lastHeartbeatUtc.Value).TotalSeconds + ELAPSED_TOLERANCE_SECONDS;
            seconds = Math.Min(seconds, Math.Max(elapsed, 0));
        }
        return seconds;
    }
}