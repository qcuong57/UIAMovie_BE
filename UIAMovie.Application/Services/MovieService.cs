// UIAMovie.Application/Services/MovieService.cs

using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Services;

public interface IMovieService
{
    // ─── Movies ──────────────────────────────────────────────────────────────
    Task<PaginatedDTO<MovieDTO>> GetMoviesAsync(FilterMoviesDTO filter);
    Task<IEnumerable<MovieDTO>> GetTrendingMoviesAsync();
    Task<MovieDTO?> GetMovieByIdAsync(Guid movieId);
    Task<MovieDTO?> GetMovieByTmdbIdAsync(int tmdbId); // ✅ THÊM: kiểm tra duplicate khi import
    Task<Guid> CreateMovieAsync(CreateMovieDTO dto);
    Task<bool> UpdateMovieAsync(Guid movieId, UpdateMovieDTO dto);
    Task<bool> DeleteMovieAsync(Guid movieId);

    // ─── Search & Filter ─────────────────────────────────────────────────────
    Task<IEnumerable<MovieDTO>> SearchMoviesAsync(string query);
    Task<IEnumerable<MovieDTO>> GetMoviesByGenreAsync(Guid genreId);

    // ─── Videos ──────────────────────────────────────────────────────────────
    Task<bool> AddVideoAsync(Guid movieId, string videoUrl, string videoType, string? quality);
    Task<bool> DeleteVideoAsync(Guid videoId);

    // ─── Favorites ───────────────────────────────────────────────────────────
    Task<bool> AddFavoriteAsync(Guid userId, Guid movieId);
    Task<bool> RemoveFavoriteAsync(Guid userId, Guid movieId);
    Task<IEnumerable<FavoriteDTO>> GetFavoritesAsync(Guid userId);

    // ─── Watch History ────────────────────────────────────────────────────────
    Task UpdateWatchProgressAsync(Guid userId, Guid movieId, int progressMinutes, bool isCompleted);
    Task<IEnumerable<WatchHistoryDTO>> GetWatchHistoryAsync(Guid userId);
}

public class MovieService : IMovieService
{
    private readonly IRepository<Movie> _movieRepository;
    private readonly IRepository<MovieVideo> _videoRepository;
    private readonly IRepository<Favorite> _favoriteRepository;
    private readonly IRepository<WatchHistory> _watchHistoryRepository;
    private readonly ICacheService _cacheService;

    private const string TRENDING_CACHE_KEY = "movies:trending";
    private const string GENRE_CACHE_KEY = "movies:genre:{0}";
    private const string MOVIE_CACHE_KEY = "movie:{0}";

    public MovieService(
        IRepository<Movie> movieRepository,
        IRepository<MovieVideo> videoRepository,
        IRepository<Favorite> favoriteRepository,
        IRepository<WatchHistory> watchHistoryRepository,
        ICacheService cacheService)
    {
        _movieRepository = movieRepository;
        _videoRepository = videoRepository;
        _favoriteRepository = favoriteRepository;
        _watchHistoryRepository = watchHistoryRepository;
        _cacheService = cacheService;
    }

    // ─── Movies ──────────────────────────────────────────────────────────────

    public async Task<PaginatedDTO<MovieDTO>> GetMoviesAsync(FilterMoviesDTO filter)
    {
        var movies = await _movieRepository.GetAllAsync();

        if (!string.IsNullOrWhiteSpace(filter.Search))
            movies = movies.Where(m =>
                m.Title.Contains(filter.Search, StringComparison.OrdinalIgnoreCase));

        if (filter.GenreIds != null && filter.GenreIds.Any())
            movies = movies.Where(m =>
                m.MovieGenres.Any(g => filter.GenreIds.Contains(g.GenreId)));

        if (filter.MinRating.HasValue)
            movies = movies.Where(m => m.ImdbRating >= filter.MinRating);
        if (filter.MaxRating.HasValue)
            movies = movies.Where(m => m.ImdbRating <= filter.MaxRating);

        if (filter.FromReleaseDate.HasValue)
            movies = movies.Where(m => m.ReleaseDate >= filter.FromReleaseDate);
        if (filter.ToReleaseDate.HasValue)
            movies = movies.Where(m => m.ReleaseDate <= filter.ToReleaseDate);

        movies = filter.SortBy.ToLower() switch
        {
            "title" => filter.SortDesc
                ? movies.OrderByDescending(m => m.Title)
                : movies.OrderBy(m => m.Title),
            "releasedate" => filter.SortDesc
                ? movies.OrderByDescending(m => m.ReleaseDate)
                : movies.OrderBy(m => m.ReleaseDate),
            _ => filter.SortDesc
                ? movies.OrderByDescending(m => m.ImdbRating)
                : movies.OrderBy(m => m.ImdbRating)
        };

        var totalCount = movies.Count();
        var items = movies
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(MapToDTO)
            .ToList();

        return new PaginatedDTO<MovieDTO>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<IEnumerable<MovieDTO>> GetTrendingMoviesAsync()
    {
        var cached = await _cacheService.GetAsync<List<MovieDTO>>(TRENDING_CACHE_KEY);
        if (cached != null) return cached;

        var movies = await _movieRepository.GetAllAsync();
        var trending = movies
            .OrderByDescending(m => m.ImdbRating)
            .Take(20)
            .Select(MapToDTO)
            .ToList();

        await _cacheService.SetAsync(TRENDING_CACHE_KEY, trending, TimeSpan.FromHours(1));
        return trending;
    }

    public async Task<MovieDTO?> GetMovieByIdAsync(Guid movieId)
    {
        var cacheKey = string.Format(MOVIE_CACHE_KEY, movieId);
        var cached = await _cacheService.GetAsync<MovieDTO>(cacheKey);
        if (cached != null) return cached;

        var movie = await _movieRepository.GetByIdAsync(movieId);
        if (movie == null) return null;

        var dto = MapToDTO(movie);
        await _cacheService.SetAsync(cacheKey, dto, TimeSpan.FromHours(24));
        return dto;
    }

    // ✅ THÊM: Tìm phim theo TmdbId — dùng để kiểm tra duplicate trước khi import
    public async Task<MovieDTO?> GetMovieByTmdbIdAsync(int tmdbId)
    {
        var movies = await _movieRepository.GetAllAsync();
        var movie = movies.FirstOrDefault(m => m.TmdbId == tmdbId);
        return movie == null ? null : MapToDTO(movie);
    }

    public async Task<Guid> CreateMovieAsync(CreateMovieDTO dto)
    {
        var movie = new Movie
        {
            Title = dto.Title,
            // ✅ FIX: Description không được null — fallback về Title
            Description = string.IsNullOrEmpty(dto.Description) ? dto.Title : dto.Description,
            // ✅ FIX: Đảm bảo ReleaseDate luôn là UTC trước khi lưu vào PostgreSQL timestamptz
            ReleaseDate = dto.ReleaseDate.HasValue
                ? DateTime.SpecifyKind(dto.ReleaseDate.Value, DateTimeKind.Utc)
                : null,
            PosterUrl = dto.PosterUrl,
            BackdropUrl = dto.BackdropUrl,
            Duration = dto.Duration,
            ImdbRating = dto.ImdbRating,
            TmdbId = dto.TmdbId,
            ContentRating = dto.ContentRating,
            IsPublished = true
        };

        await _movieRepository.AddAsync(movie);
        await _movieRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(TRENDING_CACHE_KEY);
        return movie.Id;
    }

    public async Task<bool> UpdateMovieAsync(Guid movieId, UpdateMovieDTO dto)
    {
        var movie = await _movieRepository.GetByIdAsync(movieId);
        if (movie == null) return false;

        movie.Title = dto.Title ?? movie.Title;
        movie.Description = dto.Description ?? movie.Description;
        movie.ImdbRating = dto.ImdbRating ?? movie.ImdbRating;
        movie.UpdatedAt = DateTime.UtcNow;

        _movieRepository.Update(movie);
        await _movieRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(MOVIE_CACHE_KEY, movieId));
        await _cacheService.RemoveAsync(TRENDING_CACHE_KEY);

        return true;
    }

    public async Task<bool> DeleteMovieAsync(Guid movieId)
    {
        var movie = await _movieRepository.GetByIdAsync(movieId);
        if (movie == null) return false;

        _movieRepository.Remove(movie);
        await _movieRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(MOVIE_CACHE_KEY, movieId));
        await _cacheService.RemoveAsync(TRENDING_CACHE_KEY);

        return true;
    }

    // ─── Search & Filter ─────────────────────────────────────────────────────

    public async Task<IEnumerable<MovieDTO>> SearchMoviesAsync(string query)
    {
        var cacheKey = $"search:{query.ToLower()}";
        var cached = await _cacheService.GetAsync<List<MovieDTO>>(cacheKey);
        if (cached != null) return cached;

        var movies = await _movieRepository.GetAllAsync();
        var results = movies
            .Where(m => m.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(MapToDTO)
            .ToList();

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromMinutes(10));
        return results;
    }

    public async Task<IEnumerable<MovieDTO>> GetMoviesByGenreAsync(Guid genreId)
    {
        var cacheKey = string.Format(GENRE_CACHE_KEY, genreId);
        var cached = await _cacheService.GetAsync<List<MovieDTO>>(cacheKey);
        if (cached != null) return cached;

        var movies = await _movieRepository.GetAllAsync();
        var results = movies
            .Where(m => m.MovieGenres.Any(g => g.GenreId == genreId))
            .Select(MapToDTO)
            .ToList();

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromHours(1));
        return results;
    }

    // ─── Videos ──────────────────────────────────────────────────────────────

    public async Task<bool> AddVideoAsync(Guid movieId, string videoUrl, string videoType, string? quality)
    {
        var movie = await _movieRepository.GetByIdAsync(movieId);
        if (movie == null) return false;

        var video = new MovieVideo
        {
            MovieId = movieId,
            VideoUrl = videoUrl,
            VideoType = videoType,
            Quality = quality,
            IsPublished = true
        };

        await _videoRepository.AddAsync(video);
        await _videoRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(MOVIE_CACHE_KEY, movieId));
        return true;
    }

    public async Task<bool> DeleteVideoAsync(Guid videoId)
    {
        var video = await _videoRepository.GetByIdAsync(videoId);
        if (video == null) return false;

        _videoRepository.Remove(video);
        await _videoRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(MOVIE_CACHE_KEY, video.MovieId));
        return true;
    }

    // ─── Favorites ───────────────────────────────────────────────────────────

    public async Task<bool> AddFavoriteAsync(Guid userId, Guid movieId)
    {
        var favorites = await _favoriteRepository.GetAllAsync();
        var exists = favorites.Any(f => f.UserId == userId && f.MovieId == movieId);
        if (exists) return false;

        await _favoriteRepository.AddAsync(new Favorite
        {
            UserId = userId,
            MovieId = movieId
        });
        await _favoriteRepository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveFavoriteAsync(Guid userId, Guid movieId)
    {
        var favorites = await _favoriteRepository.GetAllAsync();
        var favorite = favorites.FirstOrDefault(f => f.UserId == userId && f.MovieId == movieId);
        if (favorite == null) return false;

        _favoriteRepository.Remove(favorite);
        await _favoriteRepository.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<FavoriteDTO>> GetFavoritesAsync(Guid userId)
    {
        var favorites = await _favoriteRepository.GetAllAsync();
        var movies = await _movieRepository.GetAllAsync();

        return favorites
            .Where(f => f.UserId == userId)
            .Join(movies, f => f.MovieId, m => m.Id, (f, m) => new FavoriteDTO
            {
                Id = f.Id,
                MovieId = m.Id,
                MovieTitle = m.Title,
                PosterUrl = m.PosterUrl,
                Rating = m.ImdbRating,
                AddedAt = f.AddedAt
            })
            .OrderByDescending(f => f.AddedAt)
            .ToList();
    }

    // ─── Watch History ────────────────────────────────────────────────────────

    public async Task UpdateWatchProgressAsync(
        Guid userId, Guid movieId, int progressMinutes, bool isCompleted)
    {
        var histories = await _watchHistoryRepository.GetAllAsync();
        var existing = histories.FirstOrDefault(h => h.UserId == userId && h.MovieId == movieId);

        if (existing != null)
        {
            existing.ProgressMinutes = progressMinutes;
            existing.IsCompleted = isCompleted;
            existing.WatchedAt = DateTime.UtcNow;
            _watchHistoryRepository.Update(existing);
        }
        else
        {
            await _watchHistoryRepository.AddAsync(new WatchHistory
            {
                UserId = userId,
                MovieId = movieId,
                ProgressMinutes = progressMinutes,
                IsCompleted = isCompleted
            });
        }

        await _watchHistoryRepository.SaveChangesAsync();
    }

    public async Task<IEnumerable<WatchHistoryDTO>> GetWatchHistoryAsync(Guid userId)
    {
        var histories = await _watchHistoryRepository.GetAllAsync();
        var movies = await _movieRepository.GetAllAsync();

        return histories
            .Where(h => h.UserId == userId)
            .Join(movies, h => h.MovieId, m => m.Id, (h, m) => new WatchHistoryDTO
            {
                Id = h.Id,
                MovieId = m.Id,
                MovieTitle = m.Title,
                PosterUrl = m.PosterUrl,
                WatchedAt = h.WatchedAt,
                ProgressMinutes = h.ProgressMinutes,
                IsCompleted = h.IsCompleted
            })
            .OrderByDescending(h => h.WatchedAt)
            .ToList();
    }

    // ─── Helper ──────────────────────────────────────────────────────────────

    private static MovieDTO MapToDTO(Movie m) => new()
    {
        Id = m.Id,
        Title = m.Title,
        Description = m.Description,
        ReleaseDate = m.ReleaseDate,
        PosterUrl = m.PosterUrl,
        BackdropUrl = m.BackdropUrl,
        Duration = m.Duration,
        Rating = m.ImdbRating,
        Genres = m.MovieGenres?.Select(g => g.Genre?.Name ?? "").ToList() ?? new(),
        Videos = m.MovieVideos?.Select(v => new MovieVideoDTO
        {
            Id = v.Id,
            VideoUrl = v.VideoUrl,
            VideoType = v.VideoType,
            Duration = v.Duration,
            Quality = v.Quality
        }).ToList() ?? new()
    };
}