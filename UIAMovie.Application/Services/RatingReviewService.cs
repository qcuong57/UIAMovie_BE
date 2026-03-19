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

    // Cache keys
    private const string MOVIE_REVIEWS_CACHE_KEY = "reviews:movie:{0}";
    private const string MOVIE_STATS_CACHE_KEY = "stats:movie:{0}";
    private const string USER_REVIEWS_CACHE_KEY = "reviews:user:{0}";

    public RatingReviewService(
        IRepository<RatingReview> reviewRepository,
        IRepository<Movie> movieRepository,
        IRepository<User> userRepository,
        ICacheService cacheService)
    {
        _reviewRepository = reviewRepository;
        _movieRepository = movieRepository;
        _userRepository = userRepository;
        _cacheService = cacheService;
    }

    // ─── Create/Update/Delete ────────────────────────────────────────────────

    public async Task<Guid> CreateRatingReviewAsync(Guid userId, RatingReviewDTO dto)
    {
        // Validate movie exists
        var movie = await _movieRepository.GetByIdAsync(dto.MovieId);
        if (movie == null)
            throw new InvalidOperationException("Phim không tồn tại");

        // Validate rating
        if (dto.Rating < 1 || dto.Rating > 10)
            throw new ArgumentException("Đánh giá phải từ 1 đến 10");

        // Create review
        var review = new RatingReview
        {
            UserId = userId,
            MovieId = dto.MovieId,
            Rating = dto.Rating,
            ReviewText = dto.ReviewText,
            IsSpoiler = dto.IsSpoiler,
            IsPublished = true,
            CreatedAt = DateTime.UtcNow
        };

        await _reviewRepository.AddAsync(review);

        try
        {
            await _reviewRepository.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // DB có unique constraint (UserId, MovieId) — user đã review phim này rồi,
            // chuyển sang UPDATE thay vì INSERT
            var existingReviews = await _reviewRepository.GetAllAsync();
            var existing = existingReviews.FirstOrDefault(r =>
                r.UserId == userId && r.MovieId == dto.MovieId);

            if (existing != null)
            {
                existing.Rating = dto.Rating;
                existing.ReviewText = dto.ReviewText;
                existing.IsSpoiler = dto.IsSpoiler;
                existing.UpdatedAt = DateTime.UtcNow;
                _reviewRepository.Update(existing);
                await _reviewRepository.SaveChangesAsync();

                await InvalidateMovieCacheAsync(dto.MovieId);
                await InvalidateUserCacheAsync(userId);
                return existing.Id;
            }
            throw;
        }

        // Invalidate cache
        await InvalidateMovieCacheAsync(dto.MovieId);
        await InvalidateUserCacheAsync(userId);

        return review.Id;
    }

    public async Task<bool> UpdateRatingReviewAsync(Guid reviewId, Guid userId, RatingReviewDTO dto)
    {
        var review = await _reviewRepository.GetByIdAsync(reviewId);
        if (review == null) 
            return false;

        // Check ownership
        if (review.UserId != userId)
            throw new UnauthorizedAccessException("Bạn không có quyền cập nhật review này");

        // Validate rating
        if (dto.Rating < 1 || dto.Rating > 10)
            throw new ArgumentException("Đánh giá phải từ 1 đến 10");

        // Update
        review.Rating = dto.Rating;
        review.ReviewText = dto.ReviewText;
        review.IsSpoiler = dto.IsSpoiler;
        review.UpdatedAt = DateTime.UtcNow;

        _reviewRepository.Update(review);
        await _reviewRepository.SaveChangesAsync();

        // Invalidate cache
        await InvalidateMovieCacheAsync(review.MovieId);
        await InvalidateUserCacheAsync(userId);

        return true;
    }

    public async Task<bool> DeleteRatingReviewAsync(Guid reviewId, Guid userId)
    {
        var review = await _reviewRepository.GetByIdAsync(reviewId);
        if (review == null) 
            return false;

        // Check ownership
        if (review.UserId != userId)
            throw new UnauthorizedAccessException("Bạn không có quyền xóa review này");

        var movieId = review.MovieId;

        _reviewRepository.Remove(review);
        await _reviewRepository.SaveChangesAsync();

        // Invalidate cache
        await InvalidateMovieCacheAsync(movieId);
        await InvalidateUserCacheAsync(userId);

        return true;
    }

    // ─── Get Reviews ─────────────────────────────────────────────────────────

    public async Task<IEnumerable<ReviewDTO>> GetMovieReviewsAsync(Guid movieId, int pageNumber = 1, int pageSize = 20)
    {
        // Try cache
        var cacheKey = string.Format(MOVIE_REVIEWS_CACHE_KEY, movieId);
        var cached = await _cacheService.GetAsync<List<ReviewDTO>>(cacheKey);
        if (cached != null)
        {
            return cached
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }

        var reviews = await _reviewRepository.GetAllAsync();
        var users = await _userRepository.GetAllAsync();

        var result = reviews
            .Where(r => r.MovieId == movieId && r.IsPublished)
            .OrderByDescending(r => r.CreatedAt)
            .Join(users, r => r.UserId, u => u.Id, (r, u) => new ReviewDTO
            {
                Id = r.Id,
                MovieId = r.MovieId,
                UserId = r.UserId,
                UserName = u.Username,
                UserAvatar = u.AvatarUrl,
                Rating = r.Rating,
                ReviewText = r.ReviewText,
                IsSpoiler = r.IsSpoiler,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            })
            .ToList();

        // Cache all reviews
        await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromMinutes(15));

        // Return paginated
        return result
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();
    }

    public async Task<IEnumerable<ReviewDTO>> GetUserReviewsAsync(Guid userId)
    {
        var cacheKey = string.Format(USER_REVIEWS_CACHE_KEY, userId);
        var cached = await _cacheService.GetAsync<List<ReviewDTO>>(cacheKey);
        if (cached != null) 
            return cached;

        var reviews = await _reviewRepository.GetAllAsync();
        var users = await _userRepository.GetAllAsync();

        var result = reviews
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Join(users, r => r.UserId, u => u.Id, (r, u) => new ReviewDTO
            {
                Id = r.Id,
                MovieId = r.MovieId,
                UserId = r.UserId,
                UserName = u.Username,
                UserAvatar = u.AvatarUrl,
                Rating = r.Rating,
                ReviewText = r.ReviewText,
                IsSpoiler = r.IsSpoiler,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            })
            .ToList();

        await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromHours(1));
        return result;
    }

    public async Task<ReviewDTO?> GetReviewByIdAsync(Guid reviewId)
    {
        var review = await _reviewRepository.GetByIdAsync(reviewId);
        if (review == null) 
            return null;

        var user = await _userRepository.GetByIdAsync(review.UserId);
        if (user == null) 
            return null;

        return new ReviewDTO
        {
            Id = review.Id,
            MovieId = review.MovieId,
            UserId = review.UserId,
            UserName = user.Username,
            UserAvatar = user.AvatarUrl,
            Rating = review.Rating,
            ReviewText = review.ReviewText,
            IsSpoiler = review.IsSpoiler,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        };
    }

    // ─── Get Stats ───────────────────────────────────────────────────────────

    public async Task<MovieRatingStatsDTO?> GetMovieRatingStatsAsync(Guid movieId)
    {
        var cacheKey = string.Format(MOVIE_STATS_CACHE_KEY, movieId);
        var cached = await _cacheService.GetAsync<MovieRatingStatsDTO>(cacheKey);
        if (cached != null) 
            return cached;

        // Check movie exists
        var movie = await _movieRepository.GetByIdAsync(movieId);
        if (movie == null) 
            return null;

        var reviews = await _reviewRepository.GetAllAsync();
        var movieReviews = reviews
            .Where(r => r.MovieId == movieId && r.IsPublished)
            .ToList();

        if (!movieReviews.Any())
        {
            var emptyStats = new MovieRatingStatsDTO
            {
                MovieId = movieId,
                AverageRating = 0,
                TotalReviews = 0,
                RatingDistribution = Enumerable.Range(1, 10)
                    .ToDictionary(i => i, i => 0)
            };

            await _cacheService.SetAsync(cacheKey, emptyStats, TimeSpan.FromMinutes(30));
            return emptyStats;
        }

        // Calculate average
        var totalRating = movieReviews.Sum(r => r.Rating);
        var averageRating = (decimal)totalRating / movieReviews.Count;

        // Calculate distribution
        var distribution = Enumerable.Range(1, 10)
            .ToDictionary(i => i, i => movieReviews.Count(r => r.Rating == i));

        var stats = new MovieRatingStatsDTO
        {
            MovieId = movieId,
            AverageRating = Math.Round(averageRating, 2),
            TotalReviews = movieReviews.Count,
            RatingDistribution = distribution
        };

        await _cacheService.SetAsync(cacheKey, stats, TimeSpan.FromMinutes(30));
        return stats;
    }

    public async Task<int> GetMovieAverageRatingAsync(Guid movieId)
    {
        var stats = await GetMovieRatingStatsAsync(movieId);
        if (stats == null || stats.TotalReviews == 0)
            return 0;

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
        var review = reviews.FirstOrDefault(r => r.UserId == userId && r.MovieId == movieId);
        
        if (review == null) 
            return null;

        return await GetReviewByIdAsync(review.Id);
    }

    // ─── Helper Methods ──────────────────────────────────────────────────────

    private async Task InvalidateMovieCacheAsync(Guid movieId)
    {
        await _cacheService.RemoveAsync(string.Format(MOVIE_REVIEWS_CACHE_KEY, movieId));
        await _cacheService.RemoveAsync(string.Format(MOVIE_STATS_CACHE_KEY, movieId));
    }

    private async Task InvalidateUserCacheAsync(Guid userId)
    {
        await _cacheService.RemoveAsync(string.Format(USER_REVIEWS_CACHE_KEY, userId));
    }
}