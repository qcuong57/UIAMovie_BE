using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Services;

public class PlaybackService : IPlaybackService
{
    private const string TypeHls = "HLS";
    private const string TypeMp4 = "MP4";
    private const string DefaultProvider = "Cloudinary";

    private readonly IMovieRepository _movieRepository;
    private readonly ITvShowRepository _tvShowRepository;

    private readonly IRepository<MovieVideo> _movieVideoRepository;
    private readonly IRepository<Episode> _episodeRepository;
    private readonly IRepository<Season> _seasonRepository;

    private readonly ISubscriptionChecker _subscriptionChecker;
    private readonly ICloudinaryService _cloudinaryService;

    public PlaybackService(
        IMovieRepository movieRepository,
        ITvShowRepository tvShowRepository,
        IRepository<MovieVideo> movieVideoRepository,
        IRepository<Episode> episodeRepository,
        IRepository<Season> seasonRepository,
        ISubscriptionChecker subscriptionChecker,
        ICloudinaryService cloudinaryService)
    {
        _movieRepository = movieRepository;
        _tvShowRepository = tvShowRepository;

        _movieVideoRepository = movieVideoRepository;
        _episodeRepository = episodeRepository;
        _seasonRepository = seasonRepository;

        _subscriptionChecker = subscriptionChecker;
        _cloudinaryService = cloudinaryService;
    }

    // ============================================================
    // MOVIE
    // ============================================================

    public async Task<PlaybackResponseDTO?> GetMoviePlaybackAsync(
        Guid movieId,
        Guid? userId)
    {
        var movie = await _movieRepository.GetByIdWithDetailsAsync(movieId);

        if (movie == null || !movie.IsPublished)
            return null;

        // PREMIUM ACCESS
        var access = await CheckPremiumAccessAsync(movie.IsPremium, userId);

        if (!access.CanWatch)
        {
            return new PlaybackResponseDTO
            {
                ContentType = "Movie",
                ContentId = movie.Id,
                ParentId = movie.Id,
                Title = movie.Title,
                IsPremium = movie.IsPremium,
                CanWatch = false,
                RequiresPremium = access.RequiresPremium,
                BlockReason = access.BlockReason
            };
        }

        // SELECT VIDEO: Whitelist chỉ lấy video chính ("main", "full_movie")
        var video = movie.MovieVideos?
            .Where(v => v.IsPublished)
            .Where(v => IsMainVideoType(v.VideoType))
            .Select(v => new { Video = v, Resolved = ResolvePlayback(v.StreamingType, v.PlaybackUrl, v.PublicId, v.VideoUrl, v.DeliveryType) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Resolved.Url))
            .OrderByDescending(x => x.Resolved.StreamingType == TypeHls)
            .ThenByDescending(x => QualityRank(x.Video.Quality))
            .FirstOrDefault();

        if (video == null)
            return null;

        var v0 = video.Video;
        var r = video.Resolved;
        var minutes = v0.Duration ?? movie.Duration;

        return new PlaybackResponseDTO
        {
            ContentType = "Movie",
            ContentId = movie.Id,
            ParentId = movie.Id,
            Title = movie.Title,
            IsPremium = movie.IsPremium,
            CanWatch = true,
            RequiresPremium = false,
            BlockReason = null,
            VideoId = v0.Id,
            StreamingType = r.StreamingType,
            PlaybackUrl = r.Url,
            Provider = NormalizeProvider(v0.Provider),
            PublicId = r.PublicId,
            Quality = v0.Quality,
            DurationSeconds = minutes.HasValue ? minutes.Value * 60 : null
        };
    }

    // ============================================================
    // TV SHOW EPISODE
    // ============================================================

    public async Task<PlaybackResponseDTO?> GetEpisodePlaybackAsync(
        Guid tvShowId,
        Guid episodeId,
        Guid? userId)
    {
        var tvShow = await _tvShowRepository.GetByIdWithDetailsAsync(tvShowId);
        if (tvShow == null || !tvShow.IsPublished)
            return null;

        var episode = await _episodeRepository.GetByIdAsync(episodeId);
        if (episode == null)
            return null;

        var season = await _seasonRepository.GetByIdAsync(episode.SeasonId);
        if (season == null || season.TvShowId != tvShowId)
            return null;

        var access = await CheckPremiumAccessAsync(tvShow.IsPremium, userId);

        if (!access.CanWatch)
        {
            return new PlaybackResponseDTO
            {
                ContentType = "TvEpisode",
                ContentId = episode.Id,
                ParentId = tvShow.Id,
                Title = tvShow.Title,
                SeasonNumber = season.SeasonNumber,
                EpisodeNumber = episode.EpisodeNumber,
                EpisodeTitle = episode.Title,
                IsPremium = tvShow.IsPremium,
                CanWatch = false,
                RequiresPremium = access.RequiresPremium,
                BlockReason = access.BlockReason
            };
        }

        var r = ResolvePlayback(
            episode.StreamingType,
            episode.PlaybackUrl,
            episode.PublicId,
            episode.VideoUrl,
            episode.DeliveryType);

        if (string.IsNullOrWhiteSpace(r.Url))
            return null;

        return new PlaybackResponseDTO
        {
            ContentType = "TvEpisode",
            ContentId = episode.Id,
            ParentId = tvShow.Id,
            Title = tvShow.Title,
            SeasonNumber = season.SeasonNumber,
            EpisodeNumber = episode.EpisodeNumber,
            EpisodeTitle = episode.Title,
            IsPremium = tvShow.IsPremium,
            CanWatch = true,
            RequiresPremium = false,
            BlockReason = null,
            VideoId = null,
            StreamingType = r.StreamingType,
            PlaybackUrl = r.Url,
            Provider = NormalizeProvider(episode.Provider),
            PublicId = r.PublicId,
            Quality = episode.Quality,
            DurationSeconds = episode.Runtime.HasValue
                ? episode.Runtime.Value * 60
                : null
        };
    }

    private async Task<(bool CanWatch, bool RequiresPremium, string? BlockReason)>
        CheckPremiumAccessAsync(bool isPremium, Guid? userId)
    {
        if (!isPremium)
            return (true, false, null);

        if (!userId.HasValue)
            return (false, true, "LOGIN_REQUIRED");

        var canWatchPremium =
            await _subscriptionChecker.CanWatchPremiumContentAsync(userId.Value);

        return canWatchPremium
            ? (true, false, null)
            : (false, true, "PREMIUM_REQUIRED");
    }

    private (string? Url, string StreamingType, string? PublicId) ResolvePlayback(
        string? streamingType,
        string? playbackUrl,
        string? publicId,
        string? videoUrl,
        string? deliveryType)
    {
        // ── Video được bảo vệ (type=authenticated) ──────────────────────────────
        // Gọi hàm này chỉ SAU KHI đã qua CheckPremiumAccessAsync. Tuyệt đối không trả
        // playbackUrl/videoUrl lưu trong DB (không có chữ ký) → luôn ký lại từ PublicId.
        // Nhận diện: cột DeliveryType, hoặc URL lưu trong DB có "/authenticated/" (video upload
        // trước khi có cột DeliveryType / service chưa lưu cột này).
        if (IsAuthenticated(deliveryType, videoUrl, playbackUrl))
        {
            var pid = !string.IsNullOrWhiteSpace(publicId)
                ? publicId
                : _cloudinaryService.ExtractPublicId(videoUrl)
                  ?? _cloudinaryService.ExtractPublicId(playbackUrl);

            // Không có PublicId → không thể ký; KHÔNG fallback sang URL công khai.
            if (string.IsNullOrWhiteSpace(pid))
                return (null, TypeHls, null);

            // Không trả PublicId cho client: không cần thiết, và là khóa để dựng link.
            return (_cloudinaryService.GenerateSignedHlsPlaybackUrl(pid), TypeHls, null);
        }

        var forceMp4 = string.Equals(streamingType, TypeMp4, StringComparison.OrdinalIgnoreCase);

        if (!forceMp4)
        {
            if (!string.IsNullOrWhiteSpace(playbackUrl))
            {
                var type = playbackUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
                    ? TypeHls
                    : TypeMp4;
                return (playbackUrl, type, publicId);
            }

            var pid = !string.IsNullOrWhiteSpace(publicId)
                ? publicId
                : _cloudinaryService.ExtractPublicId(videoUrl);

            if (!string.IsNullOrWhiteSpace(pid))
                return (_cloudinaryService.GenerateHlsPlaybackUrl(pid), TypeHls, pid);
        }

        if (!string.IsNullOrWhiteSpace(videoUrl))
            return (videoUrl, TypeMp4, publicId);

        return (null, TypeMp4, publicId);
    }

    private static bool IsAuthenticated(string? deliveryType, string? videoUrl, string? playbackUrl)
        => string.Equals(deliveryType, "authenticated", StringComparison.OrdinalIgnoreCase)
           || (videoUrl?.Contains("/authenticated/", StringComparison.OrdinalIgnoreCase) ?? false)
           || (playbackUrl?.Contains("/authenticated/", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// Whitelist chỉ chấp nhận video chính (main / full_movie).
    /// </summary>
    private static bool IsMainVideoType(string? videoType)
    {
        if (string.IsNullOrWhiteSpace(videoType)) return true;
        var type = videoType.Trim().ToLowerInvariant();
        return type == "main" || type == "full_movie";
    }

    private static string NormalizeProvider(string? provider)
        => string.IsNullOrWhiteSpace(provider)
            || provider.Equals("cloudinary", StringComparison.OrdinalIgnoreCase)
                ? DefaultProvider
                : provider;

    private static int QualityRank(string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality)) return 0;
        var q = quality.Trim();
        if (q.Equals("4k", StringComparison.OrdinalIgnoreCase)) return 2160;
        if (q.Equals("2k", StringComparison.OrdinalIgnoreCase)) return 1440;

        var digits = new string(q.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : 0;
    }
}