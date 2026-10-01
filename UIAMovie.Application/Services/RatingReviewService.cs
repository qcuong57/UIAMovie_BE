// UIAMovie.Application/Services/RatingReviewService.cs

using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Services;

public interface IRatingReviewService
{
    // ── CRUD ─────────────────────────────────────────────────────────────────
    Task<Guid> CreateRatingReviewAsync(Guid userId, RatingReviewDTO dto);
    Task<bool> UpdateRatingReviewAsync(Guid reviewId, Guid userId, RatingReviewDTO dto);
    Task<bool> DeleteRatingReviewAsync(Guid reviewId, Guid userId);

    // ── Get lists ─────────────────────────────────────────────────────────────
    Task<AllReviewsResponseDTO> GetAllReviewsAsync(int pageNumber = 1, int pageSize = 50);
    Task<IEnumerable<ReviewDTO>> GetMovieReviewsAsync(Guid movieId, int pageNumber = 1, int pageSize = 20);
    Task<IEnumerable<ReviewDTO>> GetTvShowReviewsAsync(Guid tvShowId, int pageNumber = 1, int pageSize = 20);
    Task<IEnumerable<ReviewDTO>> GetEpisodeReviewsAsync(Guid episodeId, int pageNumber = 1, int pageSize = 20);
    Task<IEnumerable<ReviewDTO>> GetUserReviewsAsync(Guid userId);
    Task<ReviewDTO?> GetReviewByIdAsync(Guid reviewId);

    // ── Stats ─────────────────────────────────────────────────────────────────
    Task<MovieRatingStatsDTO?> GetMovieRatingStatsAsync(Guid movieId);
    Task<TvShowRatingStatsDTO?> GetTvShowRatingStatsAsync(Guid tvShowId);
    Task<EpisodeRatingStatsDTO?> GetEpisodeRatingStatsAsync(Guid episodeId);
    Task<int> GetMovieAverageRatingAsync(Guid movieId);

    // ── Check ─────────────────────────────────────────────────────────────────
    Task<bool> CheckUserHasReviewAsync(Guid userId, Guid movieId);
    Task<bool> CheckUserHasReviewForTvShowAsync(Guid userId, Guid tvShowId);
    Task<bool> CheckUserHasReviewForEpisodeAsync(Guid userId, Guid episodeId);
    Task<ReviewDTO?> GetUserReviewForMovieAsync(Guid userId, Guid movieId);
    Task<ReviewDTO?> GetUserReviewForTvShowAsync(Guid userId, Guid tvShowId);
    Task<ReviewDTO?> GetUserReviewForEpisodeAsync(Guid userId, Guid episodeId);

    // ── Replies (MỚI) ────────────────────────────────────────────────────────
    Task<Guid> CreateReplyAsync(Guid reviewId, Guid userId, ReviewReplyDTO dto);
    Task<bool> UpdateReplyAsync(Guid replyId, Guid userId, ReviewReplyDTO dto);
    Task<bool> DeleteReplyAsync(Guid replyId, Guid userId);
    Task<bool> AdminDeleteReplyAsync(Guid replyId);
    Task<ReviewRepliesResponseDTO?> GetRepliesAsync(Guid reviewId, int pageNumber = 1, int pageSize = 20);
}

public class RatingReviewService : IRatingReviewService
{
    private readonly IRepository<RatingReview> _reviewRepository;
    private readonly IRepository<Movie> _movieRepository;
    private readonly IRepository<TvShow> _tvShowRepository;
    private readonly IRepository<Season> _seasonRepository;
    private readonly IRepository<Episode> _episodeRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<ReviewReply> _replyRepository;
    private readonly ICacheService _cacheService;
    private readonly INotificationService _notificationService;

    private const string ALL_REVIEWS_CACHE_KEY = "reviews:all";
    private const string MOVIE_REVIEWS_CACHE_KEY = "reviews:movie:{0}";
    private const string TVSHOW_REVIEWS_CACHE_KEY = "reviews:tvshow:{0}";
    private const string EPISODE_REVIEWS_CACHE_KEY = "reviews:episode:{0}";
    private const string MOVIE_STATS_CACHE_KEY = "stats:movie:{0}";
    private const string TVSHOW_STATS_CACHE_KEY = "stats:tvshow:{0}";
    private const string EPISODE_STATS_CACHE_KEY = "stats:episode:{0}";
    private const string USER_REVIEWS_CACHE_KEY = "reviews:user:{0}";
    private const string REVIEW_REPLIES_CACHE_KEY = "replies:review:{0}";

    public RatingReviewService(
        IRepository<RatingReview> reviewRepository,
        IRepository<Movie> movieRepository,
        IRepository<TvShow> tvShowRepository,
        IRepository<Season> seasonRepository,
        IRepository<Episode> episodeRepository,
        IRepository<User> userRepository,
        IRepository<ReviewReply> replyRepository,
        ICacheService cacheService,
        INotificationService notificationService)
    {
        _reviewRepository = reviewRepository;
        _movieRepository = movieRepository;
        _tvShowRepository = tvShowRepository;
        _seasonRepository = seasonRepository;
        _episodeRepository = episodeRepository;
        _userRepository = userRepository;
        _replyRepository = replyRepository;
        _cacheService = cacheService;
        _notificationService = notificationService;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CRUD
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<Guid> CreateRatingReviewAsync(Guid userId, RatingReviewDTO dto)
    {
        ValidateTarget(dto);

        if (dto.Rating < 1 || dto.Rating > 10)
            throw new ArgumentException("Đánh giá phải từ 1 đến 10");

        // Validate target tồn tại & resolve TvShowId khi review episode
        // Episode không có TvShowId trực tiếp → join qua Season
        Guid? resolvedTvShowId = dto.TvShowId;

        if (dto.MovieId != null)
        {
            if (await _movieRepository.GetByIdAsync(dto.MovieId.Value) == null)
                throw new InvalidOperationException("Phim không tồn tại");
        }
        else if (dto.EpisodeId != null)
        {
            var episode = await _episodeRepository.GetByIdAsync(dto.EpisodeId.Value);
            if (episode == null)
                throw new InvalidOperationException("Tập phim không tồn tại");

            // Resolve TvShowId từ Season (Episode chỉ có SeasonId)
            var season = await _seasonRepository.GetByIdAsync(episode.SeasonId);
            if (season == null)
                throw new InvalidOperationException("Không tìm thấy season của tập phim");

            resolvedTvShowId = season.TvShowId;

            // Nếu caller truyền tvShowId, kiểm tra khớp
            if (dto.TvShowId != null && dto.TvShowId != resolvedTvShowId)
                throw new ArgumentException("tvShowId không khớp với TV show của tập này");
        }
        else
        {
            if (await _tvShowRepository.GetByIdAsync(dto.TvShowId!.Value) == null)
                throw new InvalidOperationException("TV show không tồn tại");
        }

        var review = new RatingReview
        {
            UserId = userId,
            MovieId = dto.MovieId,
            TvShowId = resolvedTvShowId,
            EpisodeId = dto.EpisodeId,
            Rating = dto.Rating,
            ReviewText = dto.ReviewText,
            IsSpoiler = dto.IsSpoiler,
            IsPublished = true,
            CreatedAt = DateTime.UtcNow
        };

        // Một user có thể đăng nhiều review cho cùng một phim/show/tập → luôn tạo mới.
        await _reviewRepository.AddAsync(review);
        await _reviewRepository.SaveChangesAsync();

        await InvalidateCachesAsync(dto.MovieId, resolvedTvShowId, dto.EpisodeId, userId);
        return review.Id;
    }

    public async Task<bool> UpdateRatingReviewAsync(Guid reviewId, Guid userId, RatingReviewDTO dto)
    {
        var review = await _reviewRepository.GetByIdAsync(reviewId);
        if (review == null) return false;

        if (review.UserId != userId)
            throw new UnauthorizedAccessException("Bạn không có quyền cập nhật review này");

        if (dto.Rating < 1 || dto.Rating > 10)
            throw new ArgumentException("Đánh giá phải từ 1 đến 10");

        review.Rating = dto.Rating;
        review.ReviewText = dto.ReviewText;
        review.IsSpoiler = dto.IsSpoiler;
        review.UpdatedAt = DateTime.UtcNow;

        _reviewRepository.Update(review);
        await _reviewRepository.SaveChangesAsync();
        await InvalidateCachesAsync(review.MovieId, review.TvShowId, review.EpisodeId, userId);
        return true;
    }

    public async Task<bool> DeleteRatingReviewAsync(Guid reviewId, Guid userId)
    {
        var review = await _reviewRepository.GetByIdAsync(reviewId);
        if (review == null) return false;

        if (review.UserId != userId)
            throw new UnauthorizedAccessException("Bạn không có quyền xóa review này");

        var (movieId, tvShowId, episodeId) = (review.MovieId, review.TvShowId, review.EpisodeId);
        _reviewRepository.Remove(review);
        await _reviewRepository.SaveChangesAsync();
        await InvalidateCachesAsync(movieId, tvShowId, episodeId, userId);

        // Review bị xóa → xóa luôn cache replies (các reply con sẽ cascade delete ở DB)
        await _cacheService.RemoveAsync(string.Format(REVIEW_REPLIES_CACHE_KEY, reviewId));
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET LISTS
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<AllReviewsResponseDTO> GetAllReviewsAsync(int pageNumber = 1, int pageSize = 50)
    {
        var all = await _cacheService.GetOrSetAsync(ALL_REVIEWS_CACHE_KEY, async () =>
        {
            // GetAllAsync() trả về in-memory, filter trên LINQ
            var reviews = (await _reviewRepository.GetAllAsync())
                .Where(r => r.IsPublished)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            var userMap = (await _userRepository.GetAllAsync()).ToDictionary(u => u.Id);

            return reviews
                .Select(r =>
                {
                    userMap.TryGetValue(r.UserId, out var u);
                    return MapToDTO(r, u);
                })
                .ToList();
        }, TimeSpan.FromMinutes(10));

        var list = all ?? new List<ReviewDTO>();
        return new AllReviewsResponseDTO
        {
            Items = await WithReplyCountsAsync(list.Skip((pageNumber - 1) * pageSize).Take(pageSize)),
            TotalCount = list.Count,
            PageNumber = pageNumber,
            PageSize = pageSize,
        };
    }

    public async Task<IEnumerable<ReviewDTO>> GetMovieReviewsAsync(Guid movieId, int pageNumber = 1, int pageSize = 20)
    {
        var cacheKey = string.Format(MOVIE_REVIEWS_CACHE_KEY, movieId);
        return await GetPagedAsync(cacheKey, pageNumber, pageSize, async () =>
        {
            var reviews = (await _reviewRepository.GetAllAsync())
                .Where(r => r.MovieId == movieId && r.IsPublished)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();
            return await MapWithUsersAsync(reviews);
        });
    }

    public async Task<IEnumerable<ReviewDTO>> GetTvShowReviewsAsync(Guid tvShowId, int pageNumber = 1,
        int pageSize = 20)
    {
        var cacheKey = string.Format(TVSHOW_REVIEWS_CACHE_KEY, tvShowId);
        return await GetPagedAsync(cacheKey, pageNumber, pageSize, async () =>
        {
            // Chỉ lấy review cấp show — KHÔNG kèm episode reviews
            var reviews = (await _reviewRepository.GetAllAsync())
                .Where(r => r.TvShowId == tvShowId && r.EpisodeId == null && r.IsPublished)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();
            return await MapWithUsersAsync(reviews);
        });
    }

    public async Task<IEnumerable<ReviewDTO>> GetEpisodeReviewsAsync(Guid episodeId, int pageNumber = 1,
        int pageSize = 20)
    {
        var cacheKey = string.Format(EPISODE_REVIEWS_CACHE_KEY, episodeId);
        return await GetPagedAsync(cacheKey, pageNumber, pageSize, async () =>
        {
            var reviews = (await _reviewRepository.GetAllAsync())
                .Where(r => r.EpisodeId == episodeId && r.IsPublished)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();
            return await MapWithUsersAsync(reviews);
        });
    }

    public async Task<IEnumerable<ReviewDTO>> GetUserReviewsAsync(Guid userId)
    {
        var cacheKey = string.Format(USER_REVIEWS_CACHE_KEY, userId);
        var result = await _cacheService.GetOrSetAsync(cacheKey, async () =>
        {
            var reviews = (await _reviewRepository.GetAllAsync())
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();
            var user = await _userRepository.GetByIdAsync(userId);
            return reviews.Select(r => MapToDTO(r, user)).ToList();
        }, TimeSpan.FromHours(1));

        return await WithReplyCountsAsync(result ?? new List<ReviewDTO>());
    }

    public async Task<ReviewDTO?> GetReviewByIdAsync(Guid reviewId)
    {
        var review = await _reviewRepository.GetByIdAsync(reviewId);
        if (review == null) return null;
        var user = await _userRepository.GetByIdAsync(review.UserId);
        var dto = MapToDTO(review, user);
        await WithReplyCountsAsync(new[] { dto });
        return dto;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STATS
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<MovieRatingStatsDTO?> GetMovieRatingStatsAsync(Guid movieId)
    {
        var cacheKey = string.Format(MOVIE_STATS_CACHE_KEY, movieId);
        var cached = await _cacheService.GetAsync<MovieRatingStatsDTO>(cacheKey);
        if (cached != null) return cached;

        if (await _movieRepository.GetByIdAsync(movieId) == null) return null;

        var target = (await _reviewRepository.GetAllAsync())
            .Where(r => r.MovieId == movieId && r.IsPublished)
            .ToList();

        var stats = BuildMovieStats(movieId, target);
        await _cacheService.SetAsync(cacheKey, stats, TimeSpan.FromMinutes(30));
        return stats;
    }

    public async Task<TvShowRatingStatsDTO?> GetTvShowRatingStatsAsync(Guid tvShowId)
    {
        var cacheKey = string.Format(TVSHOW_STATS_CACHE_KEY, tvShowId);
        var cached = await _cacheService.GetAsync<TvShowRatingStatsDTO>(cacheKey);
        if (cached != null) return cached;

        if (await _tvShowRepository.GetByIdAsync(tvShowId) == null) return null;

        // Thống kê cấp show — KHÔNG tính episode reviews
        var target = (await _reviewRepository.GetAllAsync())
            .Where(r => r.TvShowId == tvShowId && r.EpisodeId == null && r.IsPublished)
            .ToList();

        var stats = BuildTvShowStats(tvShowId, target);
        await _cacheService.SetAsync(cacheKey, stats, TimeSpan.FromMinutes(30));
        return stats;
    }

    public async Task<EpisodeRatingStatsDTO?> GetEpisodeRatingStatsAsync(Guid episodeId)
    {
        var cacheKey = string.Format(EPISODE_STATS_CACHE_KEY, episodeId);
        var cached = await _cacheService.GetAsync<EpisodeRatingStatsDTO>(cacheKey);
        if (cached != null) return cached;

        // Episode không có TvShowId trực tiếp → join qua Season
        var episode = await _episodeRepository.GetByIdAsync(episodeId);
        if (episode == null) return null;

        var season = await _seasonRepository.GetByIdAsync(episode.SeasonId);
        if (season == null) return null;

        var tvShowId = season.TvShowId;

        var target = (await _reviewRepository.GetAllAsync())
            .Where(r => r.EpisodeId == episodeId && r.IsPublished)
            .ToList();

        var stats = BuildEpisodeStats(episodeId, tvShowId, target);
        await _cacheService.SetAsync(cacheKey, stats, TimeSpan.FromMinutes(30));
        return stats;
    }

    public async Task<int> GetMovieAverageRatingAsync(Guid movieId)
    {
        var stats = await GetMovieRatingStatsAsync(movieId);
        return stats == null || stats.TotalReviews == 0 ? 0 : (int)Math.Round(stats.AverageRating);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CHECK
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<bool> CheckUserHasReviewAsync(Guid userId, Guid movieId)
    {
        var all = await _reviewRepository.GetAllAsync();
        return all.Any(r => r.UserId == userId && r.MovieId == movieId);
    }

    public async Task<bool> CheckUserHasReviewForTvShowAsync(Guid userId, Guid tvShowId)
    {
        var all = await _reviewRepository.GetAllAsync();
        return all.Any(r => r.UserId == userId && r.TvShowId == tvShowId && r.EpisodeId == null);
    }

    public async Task<bool> CheckUserHasReviewForEpisodeAsync(Guid userId, Guid episodeId)
    {
        var all = await _reviewRepository.GetAllAsync();
        return all.Any(r => r.UserId == userId && r.EpisodeId == episodeId);
    }

    public async Task<ReviewDTO?> GetUserReviewForMovieAsync(Guid userId, Guid movieId)
    {
        var all = await _reviewRepository.GetAllAsync();
        var r = all.FirstOrDefault(r => r.UserId == userId && r.MovieId == movieId);
        return r == null ? null : await GetReviewByIdAsync(r.Id);
    }

    public async Task<ReviewDTO?> GetUserReviewForTvShowAsync(Guid userId, Guid tvShowId)
    {
        var all = await _reviewRepository.GetAllAsync();
        var r = all.FirstOrDefault(r => r.UserId == userId && r.TvShowId == tvShowId && r.EpisodeId == null);
        return r == null ? null : await GetReviewByIdAsync(r.Id);
    }

    public async Task<ReviewDTO?> GetUserReviewForEpisodeAsync(Guid userId, Guid episodeId)
    {
        var all = await _reviewRepository.GetAllAsync();
        var r = all.FirstOrDefault(r => r.UserId == userId && r.EpisodeId == episodeId);
        return r == null ? null : await GetReviewByIdAsync(r.Id);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REPLIES (MỚI) — bất kỳ user đăng nhập nào cũng có thể reply 1 review
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<Guid> CreateReplyAsync(Guid reviewId, Guid userId, ReviewReplyDTO dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ReplyText))
            throw new ArgumentException("Nội dung trả lời không được để trống");

        if (dto.ReplyText.Length > 2000)
            throw new ArgumentException("Nội dung trả lời không được vượt quá 2000 ký tự");

        var review = await _reviewRepository.GetByIdAsync(reviewId);
        if (review == null)
            throw new InvalidOperationException("Không tìm thấy review để trả lời");

        // ── Xác định reply gốc / người được trả lời ──────────────────────────
        // Giới hạn lồng: 1 cấp (review → reply → reply con), giống Facebook/YouTube/Instagram/TikTok.
        // Trả lời một reply con thì vẫn gắn vào reply gốc, kèm @người được trả lời.
        Guid? parentId = null;
        Guid? replyToUserId = null;
        ReviewReply? target = null;

        if (dto.ParentReplyId != null)
        {
            target = await _replyRepository.GetByIdAsync(dto.ParentReplyId.Value);
            if (target == null || !target.IsPublished || target.RatingReviewId != reviewId)
                throw new InvalidOperationException("Không tìm thấy reply để trả lời");

            if (target.ParentReplyId == null)
            {
                parentId = target.Id; // trả lời reply gốc: không cần @tag
            }
            else
            {
                parentId = target.ParentReplyId; // trả lời reply con: gắn về reply gốc
                replyToUserId = target.UserId; // và @tag người đó
            }
        }

        var reply = new ReviewReply
        {
            RatingReviewId = reviewId,
            UserId = userId,
            ParentReplyId = parentId,
            ReplyToUserId = replyToUserId,
            ReplyText = dto.ReplyText.Trim(),
            IsPublished = true,
            CreatedAt = DateTime.UtcNow
        };

        await _replyRepository.AddAsync(reply);
        await _replyRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(REVIEW_REPLIES_CACHE_KEY, reviewId));

        // ── Thông báo: chủ review + chủ reply được trả lời (không báo trùng, không tự báo) ──
        try
        {
            var replier = await _userRepository.GetByIdAsync(userId);
            var name = replier?.Username ?? "Ai đó";

            // Review tập cũng dẫn về trang thông tin của show (không có route riêng cho tập)
            var linkUrl = review.MovieId != null
                ? $"/movie/{review.MovieId}/info?reviewId={reviewId}"
                : $"/tvshow/{review.TvShowId}/info?reviewId={reviewId}";

            // Ảnh thông báo = poster của phim/show có review này (fallback: avatar người trả lời)
            var thumbnail = await GetPosterUrlAsync(review) ?? replier?.AvatarUrl;

            var notified = new HashSet<Guid> { userId };

            if (target != null && notified.Add(target.UserId))
            {
                await _notificationService.SendNotificationAsync(
                    userId: target.UserId,
                    title: "Có người trả lời bình luận của bạn",
                    message: $"{name} đã trả lời: \"{Truncate(reply.ReplyText, 80)}\"",
                    linkUrl: linkUrl,
                    thumbnailUrl: thumbnail,
                    type: "review_reply");
            }

            if (notified.Add(review.UserId))
            {
                await _notificationService.SendNotificationAsync(
                    userId: review.UserId,
                    title: "Có người trả lời đánh giá của bạn",
                    message: $"{name} đã trả lời: \"{Truncate(reply.ReplyText, 80)}\"",
                    linkUrl: linkUrl,
                    thumbnailUrl: thumbnail,
                    type: "review_reply");
            }
        }
        catch
        {
            // Gửi thông báo lỗi không được làm hỏng việc tạo reply
        }

        return reply.Id;
    }

    public async Task<bool> UpdateReplyAsync(Guid replyId, Guid userId, ReviewReplyDTO dto)
    {
        var reply = await _replyRepository.GetByIdAsync(replyId);
        if (reply == null) return false;

        if (reply.UserId != userId)
            throw new UnauthorizedAccessException("Bạn không có quyền cập nhật trả lời này");

        if (string.IsNullOrWhiteSpace(dto.ReplyText))
            throw new ArgumentException("Nội dung trả lời không được để trống");

        if (dto.ReplyText.Length > 2000)
            throw new ArgumentException("Nội dung trả lời không được vượt quá 2000 ký tự");

        reply.ReplyText = dto.ReplyText.Trim();
        reply.UpdatedAt = DateTime.UtcNow;

        _replyRepository.Update(reply);
        await _replyRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(REVIEW_REPLIES_CACHE_KEY, reply.RatingReviewId));
        return true;
    }

    public async Task<bool> DeleteReplyAsync(Guid replyId, Guid userId)
    {
        var reply = await _replyRepository.GetByIdAsync(replyId);
        if (reply == null) return false;

        if (reply.UserId != userId)
            throw new UnauthorizedAccessException("Bạn không có quyền xóa trả lời này");

        var reviewId = reply.RatingReviewId;
        await RemoveReplyWithChildrenAsync(reply);
        await _replyRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(REVIEW_REPLIES_CACHE_KEY, reviewId));
        return true;
    }

    /// <summary>[Admin] Xóa reply bất kỳ, không cần kiểm tra chủ sở hữu.</summary>
    public async Task<bool> AdminDeleteReplyAsync(Guid replyId)
    {
        var reply = await _replyRepository.GetByIdAsync(replyId);
        if (reply == null) return false;

        var reviewId = reply.RatingReviewId;
        await RemoveReplyWithChildrenAsync(reply);
        await _replyRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(REVIEW_REPLIES_CACHE_KEY, reviewId));
        return true;
    }

    public async Task<ReviewRepliesResponseDTO?> GetRepliesAsync(Guid reviewId, int pageNumber = 1, int pageSize = 20)
    {
        var review = await _reviewRepository.GetByIdAsync(reviewId);
        if (review == null) return null;

        var cacheKey = string.Format(REVIEW_REPLIES_CACHE_KEY, reviewId);
        var all = await _cacheService.GetOrSetAsync(cacheKey, async () =>
        {
            var replies = (await _replyRepository.FindAsync(r => r.RatingReviewId == reviewId && r.IsPublished))
                .OrderBy(r => r.CreatedAt)
                .ToList();

            var userMap = (await _userRepository.GetAllAsync()).ToDictionary(u => u.Id);

            return replies.Select(r =>
            {
                userMap.TryGetValue(r.UserId, out var u);
                User? replyTo = null;
                if (r.ReplyToUserId != null) userMap.TryGetValue(r.ReplyToUserId.Value, out replyTo);
                return MapReplyToDTO(r, u, replyTo);
            }).ToList();
        }, TimeSpan.FromMinutes(15));

        var list = all ?? new List<ReplyDTO>();

        // Phân trang theo REPLY GỐC; mỗi trang kèm đủ reply con của các gốc đó,
        // để reply con không bị đẩy sang trang sau (phải bấm "Xem thêm" mới thấy).
        var roots = list.Where(r => r.ParentReplyId == null).ToList();
        var pageRoots = roots.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        var rootIds = pageRoots.Select(r => r.Id).ToHashSet();
        var children = list.Where(r => r.ParentReplyId != null && rootIds.Contains(r.ParentReplyId.Value));

        return new ReviewRepliesResponseDTO
        {
            RatingReviewId = reviewId,
            TotalReplies = list.Count,
            TotalRootReplies = roots.Count,
            Replies = pageRoots.Concat(children).OrderBy(r => r.CreatedAt).ToList()
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Poster của đối tượng được review: phim → Movie.PosterUrl;
    /// review show/tập → TvShow.PosterUrl (tập không có route riêng nên dùng poster show).
    /// </summary>
    private async Task<string?> GetPosterUrlAsync(RatingReview review)
    {
        if (review.MovieId != null)
            return (await _movieRepository.GetByIdAsync(review.MovieId.Value))?.PosterUrl;

        if (review.TvShowId != null)
            return (await _tvShowRepository.GetByIdAsync(review.TvShowId.Value))?.PosterUrl;

        return null;
    }

    /// <summary>Xóa reply; nếu là reply gốc thì xóa luôn các reply con (FK tự tham chiếu không cascade).</summary>
    private async Task RemoveReplyWithChildrenAsync(ReviewReply reply)
    {
        if (reply.ParentReplyId == null)
        {
            var children = await _replyRepository.FindAsync(r => r.ParentReplyId == reply.Id);
            foreach (var c in children.ToList())
                _replyRepository.Remove(c);
        }

        _replyRepository.Remove(reply);
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";

    private static void ValidateTarget(RatingReviewDTO dto)
    {
        bool hasMovie = dto.MovieId != null;
        bool hasTvShow = dto.TvShowId != null;
        bool hasEpisode = dto.EpisodeId != null;

        if (!hasMovie && !hasTvShow && !hasEpisode)
            throw new ArgumentException("Phải cung cấp movieId, tvShowId hoặc episodeId");

        if (hasMovie && (hasTvShow || hasEpisode))
            throw new ArgumentException("Không thể review Movie và TvShow/Episode cùng lúc");

        // Episode review chỉ cần episodeId — tvShowId sẽ được resolve tự động từ Season
    }

    /// <summary>
    /// Lấy từ cache hoặc build, rồi phân trang trên memory.
    /// Dùng GetAllAsync() + LINQ thay vì FindAsync(Expression) để tránh lỗi
    /// "Func is not assignable to Expression" khi predicate có null-check phức tạp.
    /// </summary>
    private async Task<IEnumerable<ReviewDTO>> GetPagedAsync(
        string cacheKey,
        int pageNumber,
        int pageSize,
        Func<Task<List<ReviewDTO>>> buildList)
    {
        var cached = await _cacheService.GetOrSetAsync(cacheKey, buildList, TimeSpan.FromMinutes(15));
        if (cached == null) return Enumerable.Empty<ReviewDTO>();
        return await WithReplyCountsAsync(cached.Skip((pageNumber - 1) * pageSize).Take(pageSize));
    }

    /// <summary>
    /// Gắn ReplyCount (đếm mới mỗi lần, KHÔNG nằm trong cache danh sách review
    /// để thêm/xóa reply không làm số bị cũ).
    /// </summary>
    private async Task<List<ReviewDTO>> WithReplyCountsAsync(IEnumerable<ReviewDTO> reviews)
    {
        var list = reviews.ToList();
        if (list.Count == 0) return list;

        var ids = list.Select(r => r.Id).ToList();
        var replies = await _replyRepository.FindAsync(r => ids.Contains(r.RatingReviewId) && r.IsPublished);
        var counts = replies.GroupBy(r => r.RatingReviewId).ToDictionary(g => g.Key, g => g.Count());

        foreach (var r in list)
            r.ReplyCount = counts.TryGetValue(r.Id, out var c) ? c : 0;

        return list;
    }

    /// <summary>Map list reviews kèm user lookup.</summary>
    private async Task<List<ReviewDTO>> MapWithUsersAsync(List<RatingReview> reviews)
    {
        var userMap = (await _userRepository.GetAllAsync()).ToDictionary(u => u.Id);
        return reviews.Select(r =>
        {
            userMap.TryGetValue(r.UserId, out var u);
            return MapToDTO(r, u);
        }).ToList();
    }

    private static ReviewDTO MapToDTO(RatingReview r, User? u) => new()
    {
        Id = r.Id,
        MovieId = r.MovieId,
        TvShowId = r.TvShowId,
        EpisodeId = r.EpisodeId,
        EpisodeLabel = null, // caller tự format nếu cần ("S1E3")
        UserId = r.UserId,
        UserName = u?.Username ?? "Ẩn danh",
        UserAvatar = u?.AvatarUrl,
        Rating = r.Rating,
        ReviewText = r.ReviewText,
        IsSpoiler = r.IsSpoiler,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };

    private static ReplyDTO MapReplyToDTO(ReviewReply r, User? u, User? replyTo = null) => new()
    {
        Id = r.Id,
        RatingReviewId = r.RatingReviewId,
        ParentReplyId = r.ParentReplyId,
        ReplyToUserId = r.ReplyToUserId,
        ReplyToUserName = r.ReplyToUserId == null ? null : (replyTo?.Username ?? "Ẩn danh"),
        UserId = r.UserId,
        UserName = u?.Username ?? "Ẩn danh",
        UserAvatar = u?.AvatarUrl,
        ReplyText = r.ReplyText,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };

    private static MovieRatingStatsDTO BuildMovieStats(Guid movieId, List<RatingReview> list)
    {
        if (!list.Any())
            return new() { MovieId = movieId, RatingDistribution = EmptyDistribution() };

        return new()
        {
            MovieId = movieId,
            AverageRating = Math.Round((decimal)list.Sum(r => r.Rating) / list.Count, 2),
            TotalReviews = list.Count,
            RatingDistribution = Enumerable.Range(1, 10).ToDictionary(i => i, i => list.Count(r => r.Rating == i))
        };
    }

    private static TvShowRatingStatsDTO BuildTvShowStats(Guid tvShowId, List<RatingReview> list)
    {
        if (!list.Any())
            return new() { TvShowId = tvShowId, RatingDistribution = EmptyDistribution() };

        return new()
        {
            TvShowId = tvShowId,
            AverageRating = Math.Round((decimal)list.Sum(r => r.Rating) / list.Count, 2),
            TotalReviews = list.Count,
            RatingDistribution = Enumerable.Range(1, 10).ToDictionary(i => i, i => list.Count(r => r.Rating == i))
        };
    }

    private static EpisodeRatingStatsDTO BuildEpisodeStats(Guid episodeId, Guid tvShowId, List<RatingReview> list)
    {
        if (!list.Any())
            return new() { EpisodeId = episodeId, TvShowId = tvShowId, RatingDistribution = EmptyDistribution() };

        return new()
        {
            EpisodeId = episodeId,
            TvShowId = tvShowId,
            AverageRating = Math.Round((decimal)list.Sum(r => r.Rating) / list.Count, 2),
            TotalReviews = list.Count,
            RatingDistribution = Enumerable.Range(1, 10).ToDictionary(i => i, i => list.Count(r => r.Rating == i))
        };
    }

    private static Dictionary<int, int> EmptyDistribution() =>
        Enumerable.Range(1, 10).ToDictionary(i => i, _ => 0);

    private async Task InvalidateCachesAsync(Guid? movieId, Guid? tvShowId, Guid? episodeId, Guid userId)
    {
        await _cacheService.RemoveAsync(ALL_REVIEWS_CACHE_KEY);

        if (movieId != null)
        {
            await _cacheService.RemoveAsync(string.Format(MOVIE_REVIEWS_CACHE_KEY, movieId));
            await _cacheService.RemoveAsync(string.Format(MOVIE_STATS_CACHE_KEY, movieId));
        }

        if (tvShowId != null)
        {
            await _cacheService.RemoveAsync(string.Format(TVSHOW_REVIEWS_CACHE_KEY, tvShowId));
            await _cacheService.RemoveAsync(string.Format(TVSHOW_STATS_CACHE_KEY, tvShowId));
        }

        if (episodeId != null)
        {
            await _cacheService.RemoveAsync(string.Format(EPISODE_REVIEWS_CACHE_KEY, episodeId));
            await _cacheService.RemoveAsync(string.Format(EPISODE_STATS_CACHE_KEY, episodeId));
        }

        await _cacheService.RemoveAsync(string.Format(USER_REVIEWS_CACHE_KEY, userId));
    }
}