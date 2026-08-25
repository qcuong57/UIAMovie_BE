// UIAMovie.Application/Services/MovieService.cs

using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Services;

public interface IMovieService
{
    Task<PaginatedDTO<MovieDTO>> GetMoviesAsync(FilterMoviesDTO filter);
    Task<IEnumerable<TrendingMovieDTO>> GetTrendingMoviesAsync();
    Task<MovieDTO?> GetMovieByIdAsync(Guid movieId);
    Task<MovieDTO?> GetMovieByTmdbIdAsync(int tmdbId);
    Task<Guid> CreateMovieAsync(CreateMovieDTO dto);
    Task<bool> UpdateMovieAsync(Guid movieId, UpdateMovieDTO dto);
    Task<bool> DeleteMovieAsync(Guid movieId);
    Task<IEnumerable<MovieDTO>> SearchMoviesAsync(string query);
    Task<IEnumerable<MovieDTO>> SearchMoviesByActorAsync(string actorName);
    Task<IEnumerable<MovieDTO>> GetMoviesByGenreAsync(Guid genreId);
    Task<IEnumerable<string>> GetAvailableCountriesAsync();
    Task<bool> AddVideoAsync(Guid movieId, string videoUrl, string videoType, string? quality);
    Task<bool> DeleteVideoAsync(Guid videoId);
    Task<bool> AddFavoriteAsync(Guid userId, Guid movieId);
    Task<bool> RemoveFavoriteAsync(Guid userId, Guid movieId);
    Task<IEnumerable<FavoriteDTO>> GetFavoritesAsync(Guid userId);
    Task UpdateWatchProgressAsync(Guid userId, Guid movieId, int progressMinutes, bool isCompleted);
    Task<IEnumerable<WatchHistoryDTO>> GetWatchHistoryAsync(Guid userId);
    Task<bool> DeleteWatchHistoryAsync(Guid userId, Guid historyId);
    Task ClearWatchHistoryAsync(Guid userId);
    Task<IEnumerable<string>> GetPersonImagesAsync(Guid personId);

    /// <summary>Tìm Person có sẵn trong DB theo tên (dùng cho dropdown chọn diễn viên/đạo diễn).</summary>
    Task<IEnumerable<PersonSearchDTO>> SearchPersonsAsync(string query);
}

public class MovieService : IMovieService
{
    private readonly IMovieRepository _movieRepository;
    private readonly IRepository<MovieVideo> _videoRepository;
    private readonly IRepository<Favorite> _favoriteRepository;
    private readonly IRepository<WatchHistory> _watchHistoryRepository;
    private readonly IRepository<Person> _personRepository;
    private readonly IRepository<PersonImage> _personImageRepository;
    private readonly IRepository<MovieCast> _castRepository;
    private readonly IRepository<MovieDirector> _directorRepository;
    private readonly IRepository<MovieImage> _imageRepository;
    private readonly IRepository<MovieGenre> _movieGenreRepository;
    private readonly IRepository<Genre> _genreRepository;
    private readonly ICacheService _cacheService;
    private readonly ICloudinaryService _cloudinaryService;

    private const string TRENDING_CACHE_KEY = "movies:trending";
    private const string GENRE_CACHE_KEY = "movies:genre:{0}";
    private const string MOVIE_CACHE_KEY = "movie:{0}";

    // AI cache keys — cần invalidate khi catalog thay đổi
    private const string AI_CONTEXTS_CACHE_KEY = "ai:movie_contexts";
    private const string AI_ALL_DTOS_CACHE_KEY = "ai:all_movie_dtos";

    public MovieService(
        IMovieRepository movieRepository,
        IRepository<MovieVideo> videoRepository,
        IRepository<Favorite> favoriteRepository,
        IRepository<WatchHistory> watchHistoryRepository,
        IRepository<Person> personRepository,
        IRepository<PersonImage> personImageRepository,
        IRepository<MovieCast> castRepository,
        IRepository<MovieDirector> directorRepository,
        IRepository<MovieImage> imageRepository,
        IRepository<MovieGenre> movieGenreRepository,
        IRepository<Genre> genreRepository,
        ICloudinaryService cloudinaryService,
        ICacheService cacheService)
    {
        _movieRepository = movieRepository;
        _videoRepository = videoRepository;
        _favoriteRepository = favoriteRepository;
        _watchHistoryRepository = watchHistoryRepository;
        _personRepository = personRepository;
        _personImageRepository = personImageRepository;
        _castRepository = castRepository;
        _directorRepository = directorRepository;
        _imageRepository = imageRepository;
        _movieGenreRepository = movieGenreRepository;
        _genreRepository = genreRepository;
        _cloudinaryService = cloudinaryService;
        _cacheService = cacheService;
    }

    // ─── Movies ───────────────────────────────────────────────────────────────

    /// <summary>
    /// FIX CHÍNH: Dùng GetPagedAsync thay GetAllWithGenresAsync.
    ///
    /// Pattern cũ:
    ///   GetAllWithGenresAsync() → filter/sort/paginate trong C# (tải toàn bộ DB về RAM)
    ///
    /// Pattern mới:
    ///   GetPagedAsync(filter) → SQL WHERE + ORDER BY + OFFSET/FETCH
    ///   Chỉ trả về đúng số lượng cần thiết (PageSize rows)
    /// </summary>
    public async Task<PaginatedDTO<MovieDTO>> GetMoviesAsync(FilterMoviesDTO filter)
    {
        var (movies, totalCount) = await _movieRepository.GetPagedAsync(filter);

        var items = movies.Select(MapToDTO).ToList();

        return new PaginatedDTO<MovieDTO>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<IEnumerable<TrendingMovieDTO>> GetTrendingMoviesAsync()
    {
        var cached = await _cacheService.GetAsync<List<TrendingMovieDTO>>(TRENDING_CACHE_KEY);
        if (cached != null) return cached;

        var now = DateTime.UtcNow;
        var cutoff7 = now.AddDays(-7);
        var cutoff30 = now.AddDays(-30);

        var projections = await _movieRepository.GetTrendingAsync(cutoff7, cutoff30, take: 20);

        var trending = projections
            .Select((p, index) =>
            {
                var dto = MapToTrendingDTO(p.Movie);
                dto.TrendingRank = index + 1;
                dto.Views7d = p.Views7d;
                dto.Views30d = p.Views30d;
                dto.TrendingScore = p.Score;
                return dto;
            })
            .ToList();

        await _cacheService.SetAsync(TRENDING_CACHE_KEY, trending, TimeSpan.FromMinutes(30));
        return trending;
    }

    public async Task<MovieDTO?> GetMovieByIdAsync(Guid movieId)
    {
        var cacheKey = string.Format(MOVIE_CACHE_KEY, movieId);
        var cached = await _cacheService.GetAsync<MovieDTO>(cacheKey);
        if (cached != null) return cached;

        var movie = await _movieRepository.GetByIdWithDetailsAsync(movieId);
        if (movie == null) return null;

        var dto = MapToDTO(movie);
        await _cacheService.SetAsync(cacheKey, dto, TimeSpan.FromHours(24));
        return dto;
    }

    public async Task<MovieDTO?> GetMovieByTmdbIdAsync(int tmdbId)
    {
        var movie = await _movieRepository.GetByTmdbIdAsync(tmdbId);
        return movie == null ? null : MapToDTO(movie);
    }

    public async Task<Guid> CreateMovieAsync(CreateMovieDTO dto)
    {
        // FIX: Validate GenreIds tồn tại thật trong bảng Genres TRƯỚC khi tạo Movie.
        // Trước đây SaveGenresAsync chỉ check trùng lặp MovieId+GenreId, không check
        // GenreId có thật hay không → FE gửi nhầm GUID sẽ crash 500 giữa chừng (movie đã tạo)
        // hoặc tạo MovieGenre mồ côi tùy schema. Fail-fast ở đây tránh cả 2 trường hợp.
        if (dto.GenreIds.Any())
        {
            var distinctGenreIds = dto.GenreIds.Distinct().ToList();
            var existingGenres = await _genreRepository.FindAsync(g => distinctGenreIds.Contains(g.Id));
            var existingIds = existingGenres.Select(g => g.Id).ToHashSet();
            var missingIds = distinctGenreIds.Where(id => !existingIds.Contains(id)).ToList();

            if (missingIds.Any())
                throw new ArgumentException(
                    $"Thể loại không tồn tại: {string.Join(", ", missingIds)}");
        }

        var movie = new Movie
        {
            Title = dto.Title,
            Description = string.IsNullOrEmpty(dto.Description) ? dto.Title : dto.Description,
            ReleaseDate = dto.ReleaseDate.HasValue
                ? DateTime.SpecifyKind(dto.ReleaseDate.Value, DateTimeKind.Utc)
                : null,
            PosterUrl = dto.PosterUrl,
            BackdropUrl = dto.BackdropUrl,
            Duration = dto.Duration,
            ImdbRating = dto.ImdbRating,
            TmdbId = dto.TmdbId,
            ContentRating = dto.ContentRating,
            OriginCountry = dto.OriginCountry,
            IsPremium = dto.IsPremium, // FIX: trước đây bị bỏ sót, phim mới luôn ra Free bất kể admin chọn gì
            IsPublished = true
        };

        await _movieRepository.AddAsync(movie);
        await _movieRepository.SaveChangesAsync();

        // FIX: Compensating action — các repository dùng chung 1 DbContext nhưng service
        // layer không có quyền mở IDbContextTransaction (không lộ ra IMovieRepository).
        // Nếu 1 trong 5 bước dưới đây fail giữa chừng (VD ảnh URL vượt cột DB), ta xóa luôn
        // movie vừa tạo để không để lại phim rác thiếu cast/ảnh/genre trong DB.
        // Lưu ý: đây KHÔNG tương đương transaction thật — các row đã insert thành công ở
        // các bước trước đó (VD genres) vẫn "thoáng qua" tồn tại trong DB trước khi bị dọn.
        // Nếu muốn atomic thật sự, cần expose DbContext.Database.BeginTransactionAsync()
        // qua IMovieRepository hoặc 1 IUnitOfWork riêng.
        try
        {
            if (dto.GenreIds.Any()) await SaveGenresAsync(movie.Id, dto.GenreIds);
            if (dto.Cast.Any()) await SaveCastAsync(movie.Id, dto.Cast);
            if (dto.Director != null) await SaveDirectorAsync(movie.Id, dto.Director);
            if (dto.Images.Any()) await SaveImagesAsync(movie.Id, dto.Images);
            if (dto.Trailers.Any()) await SaveTrailersAsync(movie.Id, dto.Trailers);
        }
        catch
        {
            _movieRepository.Remove(movie);
            await _movieRepository.SaveChangesAsync();
            throw;
        }

        // FIX: Invalidate cả AI cache khi thêm phim mới
        await InvalidateMovieCachesAsync(movie.Id, dto.GenreIds);
        return movie.Id;
    }

    public async Task<bool> UpdateMovieAsync(Guid movieId, UpdateMovieDTO dto)
    {
        var movie = await _movieRepository.GetByIdAsync(movieId);
        if (movie == null) return false;

        movie.Title = dto.Title ?? movie.Title;
        movie.Description = dto.Description ?? movie.Description;
        movie.ImdbRating = dto.ImdbRating ?? movie.ImdbRating;
        if (dto.IsPremium.HasValue) movie.IsPremium = dto.IsPremium.Value;
        if (dto.PosterUrl != null) movie.PosterUrl = dto.PosterUrl;
        if (dto.BackdropUrl != null) movie.BackdropUrl = dto.BackdropUrl;
        movie.UpdatedAt = DateTime.UtcNow;

        _movieRepository.Update(movie);
        await _movieRepository.SaveChangesAsync();

        // Thay thế cast nếu FE có gửi (NULL = giữ nguyên, [] = xóa hết)
        if (dto.Cast != null)
        {
            await ReplaceCastAsync(movieId, dto.Cast);
        }

        // Thay thế đạo diễn nếu FE có gửi
        if (dto.Director != null)
        {
            await ReplaceDirectorAsync(movieId, dto.Director);
        }

        // Thay thế thể loại nếu FE có gửi (NULL = giữ nguyên, [] = xóa hết)
        if (dto.GenreIds != null)
        {
            var distinctGenreIds = dto.GenreIds.Distinct().ToList();
            if (distinctGenreIds.Any())
            {
                var existingGenres = await _genreRepository.FindAsync(g => distinctGenreIds.Contains(g.Id));
                var existingIds = existingGenres.Select(g => g.Id).ToHashSet();
                var missingIds = distinctGenreIds.Where(id => !existingIds.Contains(id)).ToList();

                if (missingIds.Any())
                    throw new ArgumentException(
                        $"Thể loại không tồn tại: {string.Join(", ", missingIds)}");
            }
            await ReplaceGenresAsync(movieId, distinctGenreIds);
        }

        // Thay thế ảnh backdrop (gallery) nếu FE có gửi (NULL = giữ nguyên, [] = xóa hết)
        if (dto.BackdropImages != null)
        {
            await ReplaceImagesByTypeAsync(movieId, "backdrop", dto.BackdropImages);
        }

        var movieWithGenres = await _movieRepository.GetByIdWithDetailsAsync(movieId);
        var genreIds = movieWithGenres?.MovieGenres.Select(mg => mg.GenreId).ToList() ?? new();

        // FIX: Invalidate cả AI cache khi cập nhật phim
        await InvalidateMovieCachesAsync(movieId, genreIds);
        return true;
    }

    /// <summary>Tìm Person có sẵn trong DB theo tên — dùng cho ô autocomplete chọn diễn viên/đạo diễn ở FE.</summary>
    public async Task<IEnumerable<PersonSearchDTO>> SearchPersonsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return Enumerable.Empty<PersonSearchDTO>();

        var normalized = query.Trim().ToLower();
        var persons = await _personRepository.FindAsync(p => p.Name.ToLower().Contains(normalized));

        return persons
            .OrderBy(p => p.Name)
            .Take(20)
            .Select(p => new PersonSearchDTO
            {
                Id = p.Id,
                Name = p.Name,
                ProfileUrl = p.ProfileUrl,
                TmdbPersonId = p.TmdbPersonId
            });
    }

    public async Task<bool> DeleteMovieAsync(Guid movieId)
    {
        var movie = await _movieRepository.GetByIdWithDetailsAsync(movieId);
        if (movie == null) return false;

        foreach (var video in movie.MovieVideos ?? Enumerable.Empty<MovieVideo>())
        {
            var publicId = ExtractCloudinaryPublicId(video.VideoUrl);
            if (publicId != null)
            {
                try
                {
                    await _cloudinaryService.DeleteFileAsync(publicId);
                }
                catch
                {
                    /* Tiếp tục xóa DB dù Cloudinary có lỗi */
                }
            }
        }

        var personIds = movie.MovieCasts
            .Select(c => c.PersonId)
            .Concat(movie.MovieDirectors.Select(d => d.PersonId))
            .Distinct()
            .ToList();

        var genreIds = movie.MovieGenres.Select(mg => mg.GenreId).ToList();

        _movieRepository.Remove(movie);
        await _movieRepository.SaveChangesAsync();

        foreach (var personId in personIds)
        {
            var stillInCast = await _castRepository.FindOneAsync(c => c.PersonId == personId);
            var stillInDir = await _directorRepository.FindOneAsync(d => d.PersonId == personId);

            if (stillInCast == null && stillInDir == null)
            {
                var person = await _personRepository.GetByIdAsync(personId);
                if (person != null)
                {
                    _personRepository.Remove(person);
                    await _personRepository.SaveChangesAsync();
                }
            }
        }

        // FIX: Invalidate cả AI cache khi xóa phim
        await InvalidateMovieCachesAsync(movieId, genreIds);
        return true;
    }

    // ─── Search & Filter ──────────────────────────────────────────────────────

    /// <summary>
    /// FIX: Dùng SearchByTitleAsync (SQL LIKE) thay GetAllWithGenresAsync + .Where() trong RAM.
    /// </summary>
    public async Task<IEnumerable<MovieDTO>> SearchMoviesAsync(string query)
    {
        var normalizedKey = query.ToLower().Trim();
        var cacheKey = $"search:{normalizedKey}";

        var cached = await _cacheService.GetAsync<List<MovieDTO>>(cacheKey);
        if (cached != null) return cached;

        var movies = await _movieRepository.SearchByTitleAsync(query);
        var results = movies.Select(MapToDTO).ToList();

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromMinutes(10));
        return results;
    }

    public async Task<IEnumerable<MovieDTO>> SearchMoviesByActorAsync(string actorName)
    {
        if (string.IsNullOrWhiteSpace(actorName)) return Enumerable.Empty<MovieDTO>();

        var cacheKey = $"search:actor:{actorName.ToLower().Trim()}";
        var cached = await _cacheService.GetAsync<List<MovieDTO>>(cacheKey);
        if (cached != null) return cached;

        var movies = await _movieRepository.GetMoviesByActorNameAsync(actorName);
        var results = movies.Select(MapToDTO).ToList();

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromMinutes(10));
        return results;
    }

    /// <summary>
    /// FIX: Dùng GetByGenreAsync (SQL WHERE) thay GetAllWithGenresAsync + .Where() trong RAM.
    /// </summary>
    public async Task<IEnumerable<MovieDTO>> GetMoviesByGenreAsync(Guid genreId)
    {
        var cacheKey = string.Format(GENRE_CACHE_KEY, genreId);
        var cached = await _cacheService.GetAsync<List<MovieDTO>>(cacheKey);
        if (cached != null) return cached;

        var movies = await _movieRepository.GetByGenreAsync(genreId);
        var results = movies.Select(MapToDTO).ToList();

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromMinutes(15));
        return results;
    }

    /// <summary>
    /// FIX: Dùng GetAvailableCountriesAsync (SQL DISTINCT) thay GetMoviesAsync(PageSize=9999).
    /// </summary>
    public async Task<IEnumerable<string>> GetAvailableCountriesAsync()
    {
        return await _movieRepository.GetAvailableCountriesAsync();
    }

    // ─── Videos ───────────────────────────────────────────────────────────────

    public async Task<bool> AddVideoAsync(Guid movieId, string videoUrl, string videoType, string? quality)
    {
        var movie = await _movieRepository.GetByIdAsync(movieId);
        if (movie == null) return false;

        // FIX: xóa (các) video cùng VideoType đã tồn tại trước khi thêm video mới.
        // Trước đây chỉ Add mà không Remove → nếu admin upload lại video "main",
        // DB sẽ có 2+ row cùng VideoType="main". EF không đảm bảo thứ tự trả về,
        // trong khi FE chọn video bằng `movie.videos.find(v => v.videoType === "main")`
        // (lấy record ĐẦU TIÊN khớp) → rất dễ vẫn lấy phải video CŨ thay vì video
        // vừa upload, khiến "video mới upload lên không phát được".
        var oldVideos = await _videoRepository.FindAsync(
            v => v.MovieId == movieId && v.VideoType == videoType);

        foreach (var old in oldVideos)
        {
            var oldPublicId = ExtractCloudinaryPublicId(old.VideoUrl);
            if (oldPublicId != null)
            {
                try
                {
                    await _cloudinaryService.DeleteFileAsync(oldPublicId);
                }
                catch
                {
                    /* Tiếp tục dù Cloudinary lỗi — không chặn việc thay video */
                }
            }
            _videoRepository.Remove(old);
        }

        await _videoRepository.AddAsync(new MovieVideo
        {
            MovieId = movieId,
            VideoUrl = videoUrl,
            VideoType = videoType,
            Quality = quality,
            IsPublished = true
        });
        await _videoRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(MOVIE_CACHE_KEY, movieId));
        return true;
    }

    public async Task<bool> DeleteVideoAsync(Guid videoId)
    {
        var video = await _videoRepository.GetByIdAsync(videoId);
        if (video == null) return false;

        var publicId = ExtractCloudinaryPublicId(video.VideoUrl);
        if (publicId != null)
        {
            try
            {
                await _cloudinaryService.DeleteFileAsync(publicId);
            }
            catch
            {
                /* Tiếp tục xóa DB dù Cloudinary có lỗi */
            }
        }

        _videoRepository.Remove(video);
        await _videoRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(MOVIE_CACHE_KEY, video.MovieId));
        return true;
    }

    // ─── Favorites ────────────────────────────────────────────────────────────

    public async Task<bool> AddFavoriteAsync(Guid userId, Guid movieId)
    {
        var existing = await _favoriteRepository.FindOneAsync(f => f.UserId == userId && f.MovieId == movieId);
        if (existing != null) return false;

        await _favoriteRepository.AddAsync(new Favorite { UserId = userId, MovieId = movieId });
        await _favoriteRepository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveFavoriteAsync(Guid userId, Guid movieId)
    {
        var favorite = await _favoriteRepository.FindOneAsync(f => f.UserId == userId && f.MovieId == movieId);
        if (favorite == null) return false;

        _favoriteRepository.Remove(favorite);
        await _favoriteRepository.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<FavoriteDTO>> GetFavoritesAsync(Guid userId)
    {
        var favorites = await _favoriteRepository.FindAsync(f => f.UserId == userId);
        var movieIds = favorites.Select(f => f.MovieId).Distinct().ToList();
        var movies = await _movieRepository.GetPagedAsync(new FilterMoviesDTO
        {
            Ids = movieIds,
            PageSize = movieIds.Count > 0 ? movieIds.Count : 1
        });

        var movieMap = movies.Items.ToDictionary(m => m.Id);

        return favorites
            .Where(f => movieMap.ContainsKey(f.MovieId))
            .Select(f =>
            {
                var m = movieMap[f.MovieId];
                return new FavoriteDTO
                {
                    Id = f.Id,
                    MovieId = m.Id,
                    MovieTitle = m.Title,
                    PosterUrl = m.PosterUrl,
                    Rating = m.ImdbRating,
                    AddedAt = f.AddedAt
                };
            })
            .OrderByDescending(f => f.AddedAt)
            .ToList();
    }

    // ─── Watch History ────────────────────────────────────────────────────────

    public async Task UpdateWatchProgressAsync(
        Guid userId, Guid movieId, int progressMinutes, bool isCompleted)
    {
        var existing = await _watchHistoryRepository.FindOneAsync(h => h.UserId == userId && h.MovieId == movieId);

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
        await _cacheService.RemoveAsync(TRENDING_CACHE_KEY);
    }

    public async Task<IEnumerable<WatchHistoryDTO>> GetWatchHistoryAsync(Guid userId)
    {
        var histories = await _watchHistoryRepository.FindAsync(h => h.UserId == userId);
        var movieIds = histories.Select(h => h.MovieId).Distinct().ToList();
        var movies = await _movieRepository.GetPagedAsync(new FilterMoviesDTO
        {
            Ids = movieIds,
            PageSize = movieIds.Count > 0 ? movieIds.Count : 1
        });

        var movieMap = movies.Items.ToDictionary(m => m.Id);

        return histories
            .Where(h => movieMap.ContainsKey(h.MovieId))
            .Select(h =>
            {
                var m = movieMap[h.MovieId];
                return new WatchHistoryDTO
                {
                    Id = h.Id,
                    MovieId = m.Id,
                    MovieTitle = m.Title,
                    PosterUrl = m.PosterUrl,
                    WatchedAt = h.WatchedAt,
                    ProgressMinutes = h.ProgressMinutes,
                    IsCompleted = h.IsCompleted
                };
            })
            .OrderByDescending(h => h.WatchedAt)
            .ToList();
    }

    public async Task<bool> DeleteWatchHistoryAsync(Guid userId, Guid historyId)
    {
        var record = await _watchHistoryRepository.FindOneAsync(h => h.Id == historyId && h.UserId == userId);
        if (record == null) return false;

        _watchHistoryRepository.Remove(record);
        await _watchHistoryRepository.SaveChangesAsync();
        return true;
    }

    public async Task ClearWatchHistoryAsync(Guid userId)
    {
        var userRecords = await _watchHistoryRepository.FindAsync(h => h.UserId == userId);

        foreach (var record in userRecords)
            _watchHistoryRepository.Remove(record);

        await _watchHistoryRepository.SaveChangesAsync();
    }

    // ─── Private: Invalidate cache ────────────────────────────────────────────

    /// <summary>
    /// Invalidate tất cả cache liên quan khi catalog thay đổi (thêm/sửa/xóa phim).
    /// Bao gồm AI context cache — để lần sau AI nhận được data mới nhất.
    /// </summary>
    private async Task InvalidateMovieCachesAsync(Guid movieId, List<Guid> genreIds)
    {
        var keys = new List<string>
        {
            string.Format(MOVIE_CACHE_KEY, movieId),
            TRENDING_CACHE_KEY,
            AI_CONTEXTS_CACHE_KEY, // FIX: AI cache cũng cần reset khi phim thay đổi
            AI_ALL_DTOS_CACHE_KEY
        };
        keys.AddRange(genreIds.Select(id => string.Format(GENRE_CACHE_KEY, id)));
        await _cacheService.RemoveManyAsync(keys.ToArray());
    }

    // ─── Private: lưu genres / cast / director / images / trailers ───────────

    private async Task SaveGenresAsync(Guid movieId, List<Guid> genreIds)
    {
        foreach (var genreId in genreIds.Distinct())
        {
            var exists = await _movieGenreRepository.FindOneAsync(x => x.MovieId == movieId && x.GenreId == genreId);

            if (exists == null)
            {
                await _movieGenreRepository.AddAsync(new MovieGenre
                {
                    MovieId = movieId,
                    GenreId = genreId
                });
            }
        }

        await _movieGenreRepository.SaveChangesAsync();
    }

    /// <summary>
    /// Thay thế toàn bộ thể loại của phim bằng danh sách mới (dùng khi chỉnh sửa).
    /// Xóa hết MovieGenre cũ rồi tạo lại theo danh sách gửi lên.
    /// </summary>
    private async Task ReplaceGenresAsync(Guid movieId, List<Guid> genreIds)
    {
        var old = (await _movieGenreRepository.FindAsync(x => x.MovieId == movieId)).ToList();
        foreach (var o in old)
        {
            _movieGenreRepository.Remove(o);
        }
        await _movieGenreRepository.SaveChangesAsync();

        foreach (var genreId in genreIds.Distinct())
        {
            await _movieGenreRepository.AddAsync(new MovieGenre
            {
                MovieId = movieId,
                GenreId = genreId
            });
        }
        await _movieGenreRepository.SaveChangesAsync();
    }

    /// <summary>
    /// Thay thế toàn bộ ảnh của phim theo 1 ImageType cụ thể (VD "backdrop") bằng danh sách mới —
    /// dùng cho gallery ảnh khi chỉnh sửa thủ công. Không đụng tới ImageType khác.
    /// </summary>
    private async Task ReplaceImagesByTypeAsync(Guid movieId, string imageType, List<ImportImageDTO> images)
    {
        var old = (await _imageRepository.FindAsync(i => i.MovieId == movieId && i.ImageType == imageType)).ToList();
        foreach (var o in old)
        {
            _imageRepository.Remove(o);
        }
        await _imageRepository.SaveChangesAsync();

        foreach (var img in images)
        {
            await _imageRepository.AddAsync(new MovieImage
            {
                MovieId = movieId,
                Url = img.Url,
                ImageType = imageType
            });
        }
        await _imageRepository.SaveChangesAsync();
    }

    private async Task SaveCastAsync(Guid movieId, List<ImportCastDTO> cast)
    {
        foreach (var c in cast)
        {
            var person = await UpsertPersonAsync(c.PersonId,
                c.TmdbPersonId, c.Name, c.ProfileUrl,
                c.Biography, c.Birthday, c.PlaceOfBirth);

            await SavePersonImagesAsync(person.Id, c.ProfileImages);

            var existing = await _castRepository.FindOneAsync(x => x.MovieId == movieId && x.PersonId == person.Id);

            if (existing == null)
            {
                await _castRepository.AddAsync(new MovieCast
                {
                    MovieId = movieId,
                    PersonId = person.Id,
                    Character = c.Character,
                    Order = c.Order
                });
            }
        }

        await _castRepository.SaveChangesAsync();
    }

    private async Task SaveDirectorAsync(Guid movieId, ImportDirectorDTO dto)
    {
        var person = await UpsertPersonAsync(dto.PersonId,
            dto.TmdbPersonId, dto.Name, dto.ProfileUrl,
            dto.Biography, dto.Birthday, dto.PlaceOfBirth);

        await SavePersonImagesAsync(person.Id, dto.ProfileImages);

        var existing = await _directorRepository.FindOneAsync(x => x.MovieId == movieId && x.PersonId == person.Id);

        if (existing == null)
        {
            await _directorRepository.AddAsync(new MovieDirector
            {
                MovieId = movieId,
                PersonId = person.Id
            });
            await _directorRepository.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Thay thế toàn bộ cast của phim bằng danh sách mới (dùng cho thêm thủ công / chỉnh sửa).
    /// Xóa hết MovieCast cũ rồi tạo lại theo thứ tự trong list; Person nào không còn xuất hiện
    /// ở phim/đạo diễn nào khác sẽ được dọn (xóa) để tránh rác dữ liệu.
    /// </summary>
    private async Task ReplaceCastAsync(Guid movieId, List<ImportCastDTO> cast)
    {
        var oldCasts = (await _castRepository.FindAsync(c => c.MovieId == movieId)).ToList();
        var oldPersonIds = oldCasts.Select(c => c.PersonId).Distinct().ToList();

        foreach (var old in oldCasts)
        {
            _castRepository.Remove(old);
        }
        await _castRepository.SaveChangesAsync();

        for (int i = 0; i < cast.Count; i++)
        {
            var c = cast[i];
            var person = await UpsertPersonAsync(c.PersonId,
                c.TmdbPersonId, c.Name, c.ProfileUrl,
                c.Biography, c.Birthday, c.PlaceOfBirth);

            if (c.ProfileImages?.Count > 0)
                await SavePersonImagesAsync(person.Id, c.ProfileImages);

            await _castRepository.AddAsync(new MovieCast
            {
                MovieId = movieId,
                PersonId = person.Id,
                Character = c.Character,
                Order = c.Order != 0 ? c.Order : i
            });
        }
        await _castRepository.SaveChangesAsync();

        await CleanupOrphanPersonsAsync(oldPersonIds);
    }

    /// <summary>Thay thế đạo diễn của phim. Director.Name rỗng/null = chỉ xóa đạo diễn hiện có.</summary>
    private async Task ReplaceDirectorAsync(Guid movieId, ImportDirectorDTO director)
    {
        var oldDirectors = (await _directorRepository.FindAsync(d => d.MovieId == movieId)).ToList();
        var oldPersonIds = oldDirectors.Select(d => d.PersonId).Distinct().ToList();

        foreach (var old in oldDirectors)
        {
            _directorRepository.Remove(old);
        }
        await _directorRepository.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(director.Name))
        {
            var person = await UpsertPersonAsync(director.PersonId,
                director.TmdbPersonId, director.Name, director.ProfileUrl,
                director.Biography, director.Birthday, director.PlaceOfBirth);

            if (director.ProfileImages?.Count > 0)
                await SavePersonImagesAsync(person.Id, director.ProfileImages);

            await _directorRepository.AddAsync(new MovieDirector
            {
                MovieId = movieId,
                PersonId = person.Id
            });
            await _directorRepository.SaveChangesAsync();
        }

        await CleanupOrphanPersonsAsync(oldPersonIds);
    }

    /// <summary>Xóa các Person không còn xuất hiện trong bất kỳ MovieCast/MovieDirector nào — tránh rác dữ liệu.</summary>
    private async Task CleanupOrphanPersonsAsync(IEnumerable<Guid> personIds)
    {
        foreach (var personId in personIds.Distinct())
        {
            var stillInCast = await _castRepository.FindOneAsync(c => c.PersonId == personId);
            var stillInDir = await _directorRepository.FindOneAsync(d => d.PersonId == personId);

            if (stillInCast == null && stillInDir == null)
            {
                var person = await _personRepository.GetByIdAsync(personId);
                if (person != null)
                {
                    _personRepository.Remove(person);
                    await _personRepository.SaveChangesAsync();
                }
            }
        }
    }

    private async Task SaveImagesAsync(Guid movieId, List<ImportImageDTO> images)
    {
        foreach (var img in images)
        {
            await _imageRepository.AddAsync(new MovieImage
            {
                MovieId = movieId,
                Url = img.Url,
                ImageType = img.ImageType
            });
        }

        await _imageRepository.SaveChangesAsync();
    }

    private async Task SaveTrailersAsync(Guid movieId, List<ImportTrailerDTO> trailers)
    {
        foreach (var t in trailers)
        {
            await _videoRepository.AddAsync(new MovieVideo
            {
                MovieId = movieId,
                VideoUrl = t.YoutubeUrl,
                VideoType = "trailer",
                Quality = t.Name,
                IsPublished = true
            });
        }

        await _videoRepository.SaveChangesAsync();
    }

    private async Task<Person> UpsertPersonAsync(
        Guid? personId,
        int? tmdbPersonId,
        string name,
        string? profileUrl,
        string? biography,
        string? birthday,
        string? placeOfBirth)
    {
        // Ưu tiên 1: FE đã chọn Person cụ thể từ dropdown -> dùng thẳng
        Person? person = personId.HasValue
            ? await _personRepository.GetByIdAsync(personId.Value)
            : null;

        // Ưu tiên 2: match theo TmdbPersonId (luồng auto-import)
        person ??= tmdbPersonId.HasValue
            ? await _personRepository.FindOneAsync(p => p.TmdbPersonId == tmdbPersonId)
            : null;

        // Ưu tiên 3: fallback theo Name
        person ??= await _personRepository.FindOneAsync(p => p.Name.ToLower() == name.Trim().ToLower());

        if (person == null)
        {
            person = new Person
            {
                TmdbPersonId = tmdbPersonId, Name = name, ProfileUrl = profileUrl,
                Biography = biography, Birthday = birthday, PlaceOfBirth = placeOfBirth
            };
            await _personRepository.AddAsync(person);
            await _personRepository.SaveChangesAsync();
        }
        else
        {
            bool changed = false;
            if (!person.TmdbPersonId.HasValue && tmdbPersonId.HasValue)
            {
                person.TmdbPersonId = tmdbPersonId;
                changed = true;
            }

            if (string.IsNullOrEmpty(person.Biography) && !string.IsNullOrEmpty(biography))
            {
                person.Biography = biography;
                changed = true;
            }

            if (string.IsNullOrEmpty(person.Birthday) && !string.IsNullOrEmpty(birthday))
            {
                person.Birthday = birthday;
                changed = true;
            }

            if (string.IsNullOrEmpty(person.PlaceOfBirth) && !string.IsNullOrEmpty(placeOfBirth))
            {
                person.PlaceOfBirth = placeOfBirth;
                changed = true;
            }

            if (string.IsNullOrEmpty(person.ProfileUrl) && !string.IsNullOrEmpty(profileUrl))
            {
                person.ProfileUrl = profileUrl;
                changed = true;
            }

            if (changed)
            {
                _personRepository.Update(person);
                await _personRepository.SaveChangesAsync();
            }
        }

        return person;
    }

    private async Task SavePersonImagesAsync(Guid personId, List<string> imageUrls)
    {
        var existing = await _personImageRepository.FindAsync(i => i.PersonId == personId);
        var existingUrls = existing.Select(i => i.Url).ToHashSet();

        foreach (var url in imageUrls.Where(u => !string.IsNullOrEmpty(u) && !existingUrls.Contains(u)))
        {
            await _personImageRepository.AddAsync(new PersonImage
            {
                PersonId = personId,
                Url = url
            });
        }

        await _personImageRepository.SaveChangesAsync();
    }

    public async Task<IEnumerable<string>> GetPersonImagesAsync(Guid personId)
    {
        var images = await _personImageRepository.FindAsync(i => i.PersonId == personId);
        return images.Select(i => i.Url).ToList();
    }

    // ─── Static helpers ───────────────────────────────────────────────────────

    private static string? ExtractYoutubeKey(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        var v = System.Text.RegularExpressions.Regex.Match(url, @"[?&]v=([a-zA-Z0-9_-]{11})");
        if (v.Success) return v.Groups[1].Value;

        var s = System.Text.RegularExpressions.Regex.Match(url, @"youtu\.be/([a-zA-Z0-9_-]{11})");
        if (s.Success) return s.Groups[1].Value;

        return null;
    }

    private static string? ExtractCloudinaryPublicId(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.Contains("youtube.com") || url.Contains("youtu.be")) return null;
        if (!url.Contains("cloudinary.com")) return null;

        var match = System.Text.RegularExpressions.Regex.Match(
            url, @"/upload/(?:v\d+/)?(.+?)(?:\.[^./]+)?$");

        return match.Success ? match.Groups[1].Value : null;
    }

    // ─── MapToDTO ─────────────────────────────────────────────────────────────

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
        OriginCountry = m.OriginCountry,
        IsPremium = m.IsPremium,

        Genres = m.MovieGenres?
            .Select(g => g.Genre?.Name ?? "")
            .Where(n => n != "")
            .ToList() ?? new(),

        Videos = m.MovieVideos?
            .Select(v => new MovieVideoDTO
            {
                Id = v.Id,
                VideoUrl = v.VideoUrl,
                VideoType = v.VideoType,
                Duration = v.Duration,
                Quality = v.Quality
            }).ToList() ?? new(),

        TrailerKey = m.MovieVideos?
            .Where(v => v.VideoType == "trailer" && !string.IsNullOrEmpty(v.VideoUrl))
            .Select(v => ExtractYoutubeKey(v.VideoUrl))
            .FirstOrDefault(k => k != null),

        Cast = m.MovieCasts?
            .OrderBy(c => c.Order)
            .Where(c => c.Person != null)
            .Select(c => new MovieCastDTO
            {
                Name = c.Person!.Name,
                Character = c.Character,
                Order = c.Order,
                ProfileUrl = c.Person.ProfileUrl,
                TmdbPersonId = c.Person.TmdbPersonId,
                Biography = c.Person.Biography,
                Birthday = c.Person.Birthday,
                PlaceOfBirth = c.Person.PlaceOfBirth,
                ProfileImages = c.Person.Images
                    .OrderByDescending(i => i.CreatedAt)
                    .Select(i => i.Url)
                    .ToList()
            }).ToList() ?? new(),

        Director = m.MovieDirectors?
            .Select(d => d.Person?.Name)
            .FirstOrDefault(),

        DirectorDetail = m.MovieDirectors?
            .Where(d => d.Person != null)
            .Select(d => new PersonDetailDTO
            {
                Name = d.Person!.Name,
                ProfileUrl = d.Person.ProfileUrl,
                TmdbPersonId = d.Person.TmdbPersonId,
                Biography = d.Person.Biography,
                Birthday = d.Person.Birthday,
                PlaceOfBirth = d.Person.PlaceOfBirth,
                ProfileImages = d.Person.Images
                    .OrderByDescending(i => i.CreatedAt)
                    .Select(i => i.Url)
                    .ToList()
            })
            .FirstOrDefault(),

        Images = m.MovieImages?
            .Select(i => new MovieImageDTO
            {
                Id = i.Id,
                Url = i.Url,
                ImageType = i.ImageType
            }).ToList() ?? new()
    };

    private static TrendingMovieDTO MapToTrendingDTO(Movie m)
    {
        var base_ = MapToDTO(m);
        return new TrendingMovieDTO
        {
            Id = base_.Id,
            Title = base_.Title,
            Description = base_.Description,
            ReleaseDate = base_.ReleaseDate,
            PosterUrl = base_.PosterUrl,
            BackdropUrl = base_.BackdropUrl,
            Duration = base_.Duration,
            Rating = base_.Rating,
            OriginCountry = base_.OriginCountry,
            IsPremium = base_.IsPremium,
            Genres = base_.Genres,
            Videos = base_.Videos,
            TrailerKey = base_.TrailerKey,
            Cast = base_.Cast,
            Images = base_.Images,
            Director = base_.Director,
            DirectorDetail = base_.DirectorDetail,
            TrendingRank = 0,
            Views7d = 0,
            Views30d = 0,
            TrendingScore = 0
        };
    }
}