// UIAMovie.Application/Services/RatingReviewService.cs

using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Services;

/// <summary>Interface for Rating & Review Service</summary>
public interface IRatingReviewService
{
    // ─── Create/Update/Delete ────────────────────────────────────────────────
    Task<Guid> CreateRatingReviewAsync(Guid userId, RatingReviewDTO dto);
    Task<bool> UpdateRatingReviewAsync(Guid reviewId, Guid userId, RatingReviewDTO dto);
    Task<bool> DeleteRatingReviewAsync(Guid reviewId, Guid userId);

    // ─── Get Reviews ─────────────────────────────────────────────────────────
    Task<AllReviewsResponseDTO> GetAllReviewsAsync(int pageNumber = 1, int pageSize = 50); // ← MỚI
    Task<IEnumerable<ReviewDTO>> GetMovieReviewsAsync(Guid movieId, int pageNumber = 1, int pageSize = 20);
    Task<IEnumerable<ReviewDTO>> GetUserReviewsAsync(Guid userId);
    Task<ReviewDTO?> GetReviewByIdAsync(Guid reviewId);

    // ─── Get Stats ───────────────────────────────────────────────────────────
    Task<MovieRatingStatsDTO?> GetMovieRatingStatsAsync(Guid movieId);
    Task<int> GetMovieAverageRatingAsync(Guid movieId);

    // ─── Check/Verify ────────────────────────────────────────────────────────
    Task<bool> CheckUserHasReviewAsync(Guid userId, Guid movieId);
    Task<ReviewDTO?> GetUserReviewForMovieAsync(Guid userId, Guid movieId);
}

/// <summary>Implementation of Rating & Review Service</summary>
public class RatingReviewService : IRatingReviewService
{
    private readonly IRepository<RatingReview> _reviewRepository;
    private readonly IRepository<Movie> _movieRepository;
    private readonly IRepository<User> _userRepository;
    private readonly ICacheService _cacheService;

    private const string ALL_REVIEWS_CACHE_KEY    = "reviews:all";
    private const string MOVIE_REVIEWS_CACHE_KEY  = "reviews:movie:{0}";
    private const string MOVIE_STATS_CACHE_KEY    = "stats:movie:{0}";
    private const string USER_REVIEWS_CACHE_KEY   = "reviews:user:{0}";

    public RatingReviewService(
        IRepository<RatingReview> reviewRepository,
        IRepository<Movie> movieRepository,
        IRepository<User> userRepository,
        ICacheService cacheService)
    {
        _reviewRepository = reviewRepository;
        _movieRepository  = movieRepository;
        _userRepository   = userRepository;
        _cacheService     = cacheService;
    }

    // ─── Create/Update/Delete ────────────────────────────────────────────────

    public async Task<Guid> CreateRatingReviewAsync(Guid userId, RatingReviewDTO dto)
    {
        var movie = await _movieRepository.GetByIdAsync(dto.MovieId);
        if (movie == null)
            throw new InvalidOperationException("Phim không tồn tại");

        if (dto.Rating < 1 || dto.Rating > 10)
            throw new ArgumentException("Đánh giá phải từ 1 đến 10");

        var review = new RatingReview
        {
            UserId      = userId,
            MovieId     = dto.MovieId,
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
            // Unique constraint (UserId, MovieId) — chuyển sang UPDATE
            var existingReviews = await _reviewRepository.GetAllAsync();
            var existing = existingReviews.FirstOrDefault(r =>
                r.UserId == userId && r.MovieId == dto.MovieId);

            if (existing != null)
            {
                existing.Rating     = dto.Rating;
                existing.ReviewText = dto.ReviewText;
                existing.IsSpoiler  = dto.IsSpoiler;
                existing.UpdatedAt  = DateTime.UtcNow;
                _reviewRepository.Update(existing);
                await _reviewRepository.SaveChangesAsync();

                await InvalidateAllCachesAsync(dto.MovieId, userId);
                return existing.Id;
            }
            throw;
        }

        await InvalidateAllCachesAsync(dto.MovieId, userId);
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

        await InvalidateAllCachesAsync(review.MovieId, userId);
        return true;
    }

    public async Task<bool> DeleteRatingReviewAsync(Guid reviewId, Guid userId)
    {
        var review = await _reviewRepository.GetByIdAsync(reviewId);
        if (review == null) return false;

        if (review.UserId != userId)
            throw new UnauthorizedAccessException("Bạn không có quyền xóa review này");

        var movieId = review.MovieId;
        _reviewRepository.Remove(review);
        await _reviewRepository.SaveChangesAsync();

        await InvalidateAllCachesAsync(movieId, userId);
        return true;
    }

    // ─── Get Reviews ─────────────────────────────────────────────────────────

    /// <summary>
    /// Lấy TẤT CẢ reviews toàn hệ thống (cache 10 phút).
    /// Frontend dùng endpoint này để render homepage carousel — 1 request thay vì N.
    /// ReviewDTO đã có MovieId nên client tự join với danh sách phim để lấy posterUrl.
    /// </summary>
    public async Task<AllReviewsResponseDTO> GetAllReviewsAsync(int pageNumber = 1, int pageSize = 50)
    {
        // Cache toàn bộ list (không phân trang) rồi slice trên memory
        var allReviews = await _cacheService.GetOrSetAsync(ALL_REVIEWS_CACHE_KEY, async () =>
        {
            var reviews = await _reviewRepository.FindAsync(r => r.IsPublished);
            var users   = await _userRepository.GetAllAsync();
            var userMap = users.ToDictionary(u => u.Id);

            return reviews
                .OrderByDescending(r => r.CreatedAt)
                .Select(r =>
                {
                    userMap.TryGetValue(r.UserId, out var u);
                    return new ReviewDTO
                    {
                        Id         = r.Id,
                        MovieId    = r.MovieId,
                        UserId     = r.UserId,
                        UserName   = u?.Username ?? "Ẩn danh",
                        UserAvatar = u?.AvatarUrl,
                        Rating     = r.Rating,
                        ReviewText = r.ReviewText,
                        IsSpoiler  = r.IsSpoiler,
                        CreatedAt  = r.CreatedAt,
                        UpdatedAt  = r.UpdatedAt,
                    };
                })
                .ToList();
        }, TimeSpan.FromMinutes(10));

        var list       = allReviews ?? new List<ReviewDTO>();
        var totalCount = list.Count;
        var items      = list
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new AllReviewsResponseDTO
        {
            Items       = items,
            TotalCount  = totalCount,
            PageNumber  = pageNumber,
            PageSize    = pageSize,
        };
    }

    public async Task<IEnumerable<ReviewDTO>> GetMovieReviewsAsync(Guid movieId, int pageNumber = 1, int pageSize = 20)
    {
        var cacheKey = string.Format(MOVIE_REVIEWS_CACHE_KEY, movieId);

        var allReviews = await _cacheService.GetOrSetAsync(cacheKey, async () =>
        {
            var reviews = await _reviewRepository.FindAsync(r => r.MovieId == movieId && r.IsPublished);
            var users   = await _userRepository.GetAllAsync();
            var userMap = users.ToDictionary(u => u.Id);

            return reviews
                .OrderByDescending(r => r.CreatedAt)
                .Select(r =>
                {
                    userMap.TryGetValue(r.UserId, out var u);
                    return new ReviewDTO
                    {
                        Id         = r.Id,
                        MovieId    = r.MovieId,
                        UserId     = r.UserId,
                        UserName   = u?.Username ?? "Ẩn danh",
                        UserAvatar = u?.AvatarUrl,
                        Rating     = r.Rating,
                        ReviewText = r.ReviewText,
                        IsSpoiler  = r.IsSpoiler,
                        CreatedAt  = r.CreatedAt,
                        UpdatedAt  = r.UpdatedAt,
                    };
                })
                .ToList();
        }, TimeSpan.FromMinutes(15));

        if (allReviews == null) return Enumerable.Empty<ReviewDTO>();

        return allReviews
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize);
    }

    public async Task<IEnumerable<ReviewDTO>> GetUserReviewsAsync(Guid userId)
    {
        var cacheKey = string.Format(USER_REVIEWS_CACHE_KEY, userId);

        var result = await _cacheService.GetOrSetAsync(cacheKey, async () =>
        {
            var reviews = await _reviewRepository.FindAsync(r => r.UserId == userId);
            var user    = await _userRepository.GetByIdAsync(userId);

            return reviews
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new ReviewDTO
                {
                    Id         = r.Id,
                    MovieId    = r.MovieId,
                    UserId     = r.UserId,
                    UserName   = user?.Username ?? "Ẩn danh",
                    UserAvatar = user?.AvatarUrl,
                    Rating     = r.Rating,
                    ReviewText = r.ReviewText,
                    IsSpoiler  = r.IsSpoiler,
                    CreatedAt  = r.CreatedAt,
                    UpdatedAt  = r.UpdatedAt,
                })
                .ToList();
        }, TimeSpan.FromHours(1));

        return result ?? Enumerable.Empty<ReviewDTO>();
    }

    public async Task<ReviewDTO?> GetReviewByIdAsync(Guid reviewId)
    {
        var review = await _reviewRepository.GetByIdAsync(reviewId);
        if (review == null) return null;

        var user = await _userRepository.GetByIdAsync(review.UserId);

        return new ReviewDTO
        {
            Id         = review.Id,
            MovieId    = review.MovieId,
            UserId     = review.UserId,
            UserName   = user?.Username ?? "Ẩn danh",
            UserAvatar = user?.AvatarUrl,
            Rating     = review.Rating,
            ReviewText = review.ReviewText,
            IsSpoiler  = review.IsSpoiler,
            CreatedAt  = review.CreatedAt,
            UpdatedAt  = review.UpdatedAt
        };
    }

    // ─── Get Stats ───────────────────────────────────────────────────────────

    public async Task<MovieRatingStatsDTO?> GetMovieRatingStatsAsync(Guid movieId)
    {
        var cacheKey = string.Format(MOVIE_STATS_CACHE_KEY, movieId);
        var cached   = await _cacheService.GetAsync<MovieRatingStatsDTO>(cacheKey);
        if (cached != null) return cached;

        var movie = await _movieRepository.GetByIdAsync(movieId);
        if (movie == null) return null;

        var reviews      = await _reviewRepository.GetAllAsync();
        var movieReviews = reviews.Where(r => r.MovieId == movieId && r.IsPublished).ToList();

        MovieRatingStatsDTO stats;

        if (!movieReviews.Any())
        {
            stats = new MovieRatingStatsDTO
            {
                MovieId             = movieId,
                AverageRating       = 0,
                TotalReviews        = 0,
                RatingDistribution  = Enumerable.Range(1, 10).ToDictionary(i => i, i => 0)
            };
        }
        else
        {
            var avg = (decimal)movieReviews.Sum(r => r.Rating) / movieReviews.Count;
            stats = new MovieRatingStatsDTO
            {
                MovieId             = movieId,
                AverageRating       = Math.Round(avg, 2),
                TotalReviews        = movieReviews.Count,
                RatingDistribution  = Enumerable.Range(1, 10)
                    .ToDictionary(i => i, i => movieReviews.Count(r => r.Rating == i))
            };
        }

        await _cacheService.SetAsync(cacheKey, stats, TimeSpan.FromMinutes(30));
        return stats;
    }

    public async Task<int> GetMovieAverageRatingAsync(Guid movieId)
    {
        var stats = await GetMovieRatingStatsAsync(movieId);
        if (stats == null || stats.TotalReviews == 0) return 0;
        return (int)Math.Round(stats.AverageRating);
    }

    // ─── Check/Verify ────────────────────────────────────────────────────────

    public async Task<bool> CheckUserHasReviewAsync(Guid userId, Guid movieId)
    {
        var reviews = await _reviewRepository.GetAllAsync();
        return reviews.Any(r => r.UserId == userId && r.MovieId == movieId);
    }

    public async Task<ReviewDTO?> GetUserReviewForMovieAsync(Guid userId, Guid movieId)
    {
        var reviews = await _reviewRepository.GetAllAsync();
        var review  = reviews.FirstOrDefault(r => r.UserId == userId && r.MovieId == movieId);
        if (review == null) return null;
        return await GetReviewByIdAsync(review.Id);
    }

    // ─── Cache Helpers ────────────────────────────────────────────────────────

    private async Task InvalidateAllCachesAsync(Guid movieId, Guid userId)
    {
        await _cacheService.RemoveAsync(ALL_REVIEWS_CACHE_KEY);  // ← invalidate all-reviews cache
        await _cacheService.RemoveAsync(string.Format(MOVIE_REVIEWS_CACHE_KEY, movieId));
        await _cacheService.RemoveAsync(string.Format(MOVIE_STATS_CACHE_KEY, movieId));
        await _cacheService.RemoveAsync(string.Format(USER_REVIEWS_CACHE_KEY, userId));
    }
}