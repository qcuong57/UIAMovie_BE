// UIAMovie.Application/Services/RatingReviewService.cs

using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Services;

public interface IRatingReviewService
{
    // ── CRUD ─────────────────────────────────────────────────────────────────
    Task<Guid> CreateRatingReviewAsync(Guid userId, RatingReviewDTO dto);
    Task<bool> UpdateRatingReviewAsync(Guid reviewId, Guid userId, RatingReviewDTO dto);
    Task<bool> DeleteRatingReviewAsync(Guid reviewId, Guid userId);

    // ── Get lists ─────────────────────────────────────────────────────────────
    Task<AllReviewsResponseDTO>  GetAllReviewsAsync(int pageNumber = 1, int pageSize = 50);
    Task<IEnumerable<ReviewDTO>> GetMovieReviewsAsync(Guid movieId, int pageNumber = 1, int pageSize = 20);
    Task<IEnumerable<ReviewDTO>> GetTvShowReviewsAsync(Guid tvShowId, int pageNumber = 1, int pageSize = 20);
    Task<IEnumerable<ReviewDTO>> GetEpisodeReviewsAsync(Guid episodeId, int pageNumber = 1, int pageSize = 20);
    Task<IEnumerable<ReviewDTO>> GetUserReviewsAsync(Guid userId);
    Task<ReviewDTO?>             GetReviewByIdAsync(Guid reviewId);

    // ── Stats ─────────────────────────────────────────────────────────────────
    Task<MovieRatingStatsDTO?>   GetMovieRatingStatsAsync(Guid movieId);
    Task<TvShowRatingStatsDTO?>  GetTvShowRatingStatsAsync(Guid tvShowId);
    Task<EpisodeRatingStatsDTO?> GetEpisodeRatingStatsAsync(Guid episodeId);
    Task<int>                    GetMovieAverageRatingAsync(Guid movieId);

    // ── Check ─────────────────────────────────────────────────────────────────
    Task<bool>       CheckUserHasReviewAsync(Guid userId, Guid movieId);
    Task<bool>       CheckUserHasReviewForTvShowAsync(Guid userId, Guid tvShowId);
    Task<bool>       CheckUserHasReviewForEpisodeAsync(Guid userId, Guid episodeId);
    Task<ReviewDTO?> GetUserReviewForMovieAsync(Guid userId, Guid movieId);
    Task<ReviewDTO?> GetUserReviewForTvShowAsync(Guid userId, Guid tvShowId);
    Task<ReviewDTO?> GetUserReviewForEpisodeAsync(Guid userId, Guid episodeId);
}

public class RatingReviewService : IRatingReviewService
{
    private readonly IRepository<RatingReview> _reviewRepository;
    private readonly IRepository<Movie>        _movieRepository;
    private readonly IRepository<TvShow>       _tvShowRepository;
    private readonly IRepository<Season>       _seasonRepository;
    private readonly IRepository<Episode>      _episodeRepository;
    private readonly IRepository<User>         _userRepository;
    private readonly ICacheService             _cacheService;

    private const string ALL_REVIEWS_CACHE_KEY     = "reviews:all";
    private const string MOVIE_REVIEWS_CACHE_KEY   = "reviews:movie:{0}";
    private const string TVSHOW_REVIEWS_CACHE_KEY  = "reviews:tvshow:{0}";
    private const string EPISODE_REVIEWS_CACHE_KEY = "reviews:episode:{0}";
    private const string MOVIE_STATS_CACHE_KEY     = "stats:movie:{0}";
    private const string TVSHOW_STATS_CACHE_KEY    = "stats:tvshow:{0}";
    private const string EPISODE_STATS_CACHE_KEY   = "stats:episode:{0}";
    private const string USER_REVIEWS_CACHE_KEY    = "reviews:user:{0}";

    public RatingReviewService(
        IRepository<RatingReview> reviewRepository,
        IRepository<Movie>        movieRepository,
        IRepository<TvShow>       tvShowRepository,
        IRepository<Season>       seasonRepository,
        IRepository<Episode>      episodeRepository,
        IRepository<User>         userRepository,
        ICacheService             cacheService)
    {
        _reviewRepository  = reviewRepository;
        _movieRepository   = movieRepository;
        _tvShowRepository  = tvShowRepository;
        _seasonRepository  = seasonRepository;
        _episodeRepository = episodeRepository;
        _userRepository    = userRepository;
        _cacheService      = cacheService;
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
            UserId      = userId,
            MovieId     = dto.MovieId,
            TvShowId    = resolvedTvShowId,
            EpisodeId   = dto.EpisodeId,
            Rating      = dto.Rating,
            ReviewText  = dto.ReviewText,
            IsSpoiler   = dto.IsSpoiler,
            IsPublished = true,
            CreatedAt   = DateTime.UtcNow
        };

        await _reviewRepository.AddAsync(review);

        try
        {
            await _reviewRepository.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique constraint → chuyển sang UPDATE
            var all      = await _reviewRepository.GetAllAsync();
            var existing = FindExisting(all, userId, dto);

            if (existing != null)
            {
                existing.Rating     = dto.Rating;
                existing.ReviewText = dto.ReviewText;
                existing.IsSpoiler  = dto.IsSpoiler;
                existing.UpdatedAt  = DateTime.UtcNow;
                _reviewRepository.Update(existing);
                await _reviewRepository.SaveChangesAsync();
                await InvalidateCachesAsync(dto.MovieId, resolvedTvShowId, dto.EpisodeId, userId);
                return existing.Id;
            }
            throw;
        }

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

        review.Rating     = dto.Rating;
        review.ReviewText = dto.ReviewText;
        review.IsSpoiler  = dto.IsSpoiler;
        review.UpdatedAt  = DateTime.UtcNow;

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
                .Select(r => { userMap.TryGetValue(r.UserId, out var u); return MapToDTO(r, u); })
                .ToList();
        }, TimeSpan.FromMinutes(10));

        var list = all ?? new List<ReviewDTO>();
        return new AllReviewsResponseDTO
        {
            Items      = list.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(),
            TotalCount = list.Count,
            PageNumber = pageNumber,
            PageSize   = pageSize,
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

    public async Task<IEnumerable<ReviewDTO>> GetTvShowReviewsAsync(Guid tvShowId, int pageNumber = 1, int pageSize = 20)
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

    public async Task<IEnumerable<ReviewDTO>> GetEpisodeReviewsAsync(Guid episodeId, int pageNumber = 1, int pageSize = 20)
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
        var result   = await _cacheService.GetOrSetAsync(cacheKey, async () =>
        {
            var reviews = (await _reviewRepository.GetAllAsync())
                          .Where(r => r.UserId == userId)
                          .OrderByDescending(r => r.CreatedAt)
                          .ToList();
            var user = await _userRepository.GetByIdAsync(userId);
            return reviews.Select(r => MapToDTO(r, user)).ToList();
        }, TimeSpan.FromHours(1));

        return result ?? Enumerable.Empty<ReviewDTO>();
    }

    public async Task<ReviewDTO?> GetReviewByIdAsync(Guid reviewId)
    {
        var review = await _reviewRepository.GetByIdAsync(reviewId);
        if (review == null) return null;
        var user = await _userRepository.GetByIdAsync(review.UserId);
        return MapToDTO(review, user);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STATS
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<MovieRatingStatsDTO?> GetMovieRatingStatsAsync(Guid movieId)
    {
        var cacheKey = string.Format(MOVIE_STATS_CACHE_KEY, movieId);
        var cached   = await _cacheService.GetAsync<MovieRatingStatsDTO>(cacheKey);
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
        var cached   = await _cacheService.GetAsync<TvShowRatingStatsDTO>(cacheKey);
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
        var cached   = await _cacheService.GetAsync<EpisodeRatingStatsDTO>(cacheKey);
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
        var r   = all.FirstOrDefault(r => r.UserId == userId && r.MovieId == movieId);
        return r == null ? null : await GetReviewByIdAsync(r.Id);
    }

    public async Task<ReviewDTO?> GetUserReviewForTvShowAsync(Guid userId, Guid tvShowId)
    {
        var all = await _reviewRepository.GetAllAsync();
        var r   = all.FirstOrDefault(r => r.UserId == userId && r.TvShowId == tvShowId && r.EpisodeId == null);
        return r == null ? null : await GetReviewByIdAsync(r.Id);
    }

    public async Task<ReviewDTO?> GetUserReviewForEpisodeAsync(Guid userId, Guid episodeId)
    {
        var all = await _reviewRepository.GetAllAsync();
        var r   = all.FirstOrDefault(r => r.UserId == userId && r.EpisodeId == episodeId);
        return r == null ? null : await GetReviewByIdAsync(r.Id);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateTarget(RatingReviewDTO dto)
    {
        bool hasMovie   = dto.MovieId   != null;
        bool hasTvShow  = dto.TvShowId  != null;
        bool hasEpisode = dto.EpisodeId != null;

        if (!hasMovie && !hasTvShow && !hasEpisode)
            throw new ArgumentException("Phải cung cấp movieId, tvShowId hoặc episodeId");

        if (hasMovie && (hasTvShow || hasEpisode))
            throw new ArgumentException("Không thể review Movie và TvShow/Episode cùng lúc");

        // Episode review chỉ cần episodeId — tvShowId sẽ được resolve tự động từ Season
    }

    private static RatingReview? FindExisting(IEnumerable<RatingReview> all, Guid userId, RatingReviewDTO dto)
    {
        if (dto.MovieId != null)
            return all.FirstOrDefault(r => r.UserId == userId && r.MovieId == dto.MovieId);

        if (dto.EpisodeId != null)
            return all.FirstOrDefault(r => r.UserId == userId && r.EpisodeId == dto.EpisodeId);

        return all.FirstOrDefault(r => r.UserId == userId && r.TvShowId == dto.TvShowId && r.EpisodeId == null);
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
        return cached.Skip((pageNumber - 1) * pageSize).Take(pageSize);
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
        Id           = r.Id,
        MovieId      = r.MovieId,
        TvShowId     = r.TvShowId,
        EpisodeId    = r.EpisodeId,
        EpisodeLabel = null,   // caller tự format nếu cần ("S1E3")
        UserId       = r.UserId,
        UserName     = u?.Username ?? "Ẩn danh",
        UserAvatar   = u?.AvatarUrl,
        Rating       = r.Rating,
        ReviewText   = r.ReviewText,
        IsSpoiler    = r.IsSpoiler,
        CreatedAt    = r.CreatedAt,
        UpdatedAt    = r.UpdatedAt,
    };

    private static MovieRatingStatsDTO BuildMovieStats(Guid movieId, List<RatingReview> list)
    {
        if (!list.Any())
            return new() { MovieId = movieId, RatingDistribution = EmptyDistribution() };

        return new()
        {
            MovieId            = movieId,
            AverageRating      = Math.Round((decimal)list.Sum(r => r.Rating) / list.Count, 2),
            TotalReviews       = list.Count,
            RatingDistribution = Enumerable.Range(1, 10).ToDictionary(i => i, i => list.Count(r => r.Rating == i))
        };
    }

    private static TvShowRatingStatsDTO BuildTvShowStats(Guid tvShowId, List<RatingReview> list)
    {
        if (!list.Any())
            return new() { TvShowId = tvShowId, RatingDistribution = EmptyDistribution() };

        return new()
        {
            TvShowId           = tvShowId,
            AverageRating      = Math.Round((decimal)list.Sum(r => r.Rating) / list.Count, 2),
            TotalReviews       = list.Count,
            RatingDistribution = Enumerable.Range(1, 10).ToDictionary(i => i, i => list.Count(r => r.Rating == i))
        };
    }

    private static EpisodeRatingStatsDTO BuildEpisodeStats(Guid episodeId, Guid tvShowId, List<RatingReview> list)
    {
        if (!list.Any())
            return new() { EpisodeId = episodeId, TvShowId = tvShowId, RatingDistribution = EmptyDistribution() };

        return new()
        {
            EpisodeId          = episodeId,
            TvShowId           = tvShowId,
            AverageRating      = Math.Round((decimal)list.Sum(r => r.Rating) / list.Count, 2),
            TotalReviews       = list.Count,
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