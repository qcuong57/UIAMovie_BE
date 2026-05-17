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
}

public class MovieService : IMovieService
{
    private readonly IMovieRepository             _movieRepository;
    private readonly IRepository<MovieVideo>      _videoRepository;
    private readonly IRepository<Favorite>        _favoriteRepository;
    private readonly IRepository<WatchHistory>    _watchHistoryRepository;
    private readonly IRepository<Person>          _personRepository;
    private readonly IRepository<PersonImage>     _personImageRepository;
    private readonly IRepository<MovieCast>       _castRepository;
    private readonly IRepository<MovieDirector>   _directorRepository;
    private readonly IRepository<MovieImage>      _imageRepository;
    private readonly IRepository<MovieGenre>      _movieGenreRepository;
    private readonly ICacheService                _cacheService;
    private readonly ICloudinaryService           _cloudinaryService;

    private const string TRENDING_CACHE_KEY = "movies:trending";
    private const string GENRE_CACHE_KEY    = "movies:genre:{0}";
    private const string MOVIE_CACHE_KEY    = "movie:{0}";

    // AI cache keys — cần invalidate khi catalog thay đổi
    private const string AI_CONTEXTS_CACHE_KEY  = "ai:movie_contexts";
    private const string AI_ALL_DTOS_CACHE_KEY  = "ai:all_movie_dtos";

    public MovieService(
        IMovieRepository             movieRepository,
        IRepository<MovieVideo>      videoRepository,
        IRepository<Favorite>        favoriteRepository,
        IRepository<WatchHistory>    watchHistoryRepository,
        IRepository<Person>          personRepository,
        IRepository<PersonImage>     personImageRepository,
        IRepository<MovieCast>       castRepository,
        IRepository<MovieDirector>   directorRepository,
        IRepository<MovieImage>      imageRepository,
        IRepository<MovieGenre>      movieGenreRepository,
        ICloudinaryService           cloudinaryService,
        ICacheService                cacheService)
    {
        _movieRepository        = movieRepository;
        _videoRepository        = videoRepository;
        _favoriteRepository     = favoriteRepository;
        _watchHistoryRepository = watchHistoryRepository;
        _personRepository       = personRepository;
        _personImageRepository  = personImageRepository;
        _castRepository         = castRepository;
        _directorRepository     = directorRepository;
        _imageRepository        = imageRepository;
        _movieGenreRepository   = movieGenreRepository;
        _cloudinaryService      = cloudinaryService;
        _cacheService           = cacheService;
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
            Items      = items,
            TotalCount = totalCount,
            PageNumber = filter.Page,
            PageSize   = filter.PageSize
        };
    }

    public async Task<IEnumerable<TrendingMovieDTO>> GetTrendingMoviesAsync()
    {
        var cached = await _cacheService.GetAsync<List<TrendingMovieDTO>>(TRENDING_CACHE_KEY);
        if (cached != null) return cached;

        var now      = DateTime.UtcNow;
        var cutoff7  = now.AddDays(-7);
        var cutoff30 = now.AddDays(-30);

        var projections = await _movieRepository.GetTrendingAsync(cutoff7, cutoff30, take: 20);

        var trending = projections
            .Select((p, index) =>
            {
                var dto = MapToTrendingDTO(p.Movie);
                dto.TrendingRank  = index + 1;
                dto.Views7d       = p.Views7d;
                dto.Views30d      = p.Views30d;
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
        var cached   = await _cacheService.GetAsync<MovieDTO>(cacheKey);
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
        var movie = new Movie
        {
            Title         = dto.Title,
            Description   = string.IsNullOrEmpty(dto.Description) ? dto.Title : dto.Description,
            ReleaseDate   = dto.ReleaseDate.HasValue
                                ? DateTime.SpecifyKind(dto.ReleaseDate.Value, DateTimeKind.Utc)
                                : null,
            PosterUrl     = dto.PosterUrl,
            BackdropUrl   = dto.BackdropUrl,
            Duration      = dto.Duration,
            ImdbRating    = dto.ImdbRating,
            TmdbId        = dto.TmdbId,
            ContentRating = dto.ContentRating,
            OriginCountry = dto.OriginCountry,
            IsPublished   = true
        };

        await _movieRepository.AddAsync(movie);
        await _movieRepository.SaveChangesAsync();

        if (dto.GenreIds.Any())    await SaveGenresAsync(movie.Id, dto.GenreIds);
        if (dto.Cast.Any())        await SaveCastAsync(movie.Id, dto.Cast);
        if (dto.Director != null)  await SaveDirectorAsync(movie.Id, dto.Director);
        if (dto.Images.Any())      await SaveImagesAsync(movie.Id, dto.Images);
        if (dto.Trailers.Any())    await SaveTrailersAsync(movie.Id, dto.Trailers);

        // FIX: Invalidate cả AI cache khi thêm phim mới
        await InvalidateMovieCachesAsync(movie.Id, dto.GenreIds);
        return movie.Id;
    }

    public async Task<bool> UpdateMovieAsync(Guid movieId, UpdateMovieDTO dto)
    {
        var movie = await _movieRepository.GetByIdAsync(movieId);
        if (movie == null) return false;

        movie.Title       = dto.Title       ?? movie.Title;
        movie.Description = dto.Description ?? movie.Description;
        movie.ImdbRating  = dto.ImdbRating  ?? movie.ImdbRating;
        if (dto.IsPremium.HasValue) movie.IsPremium = dto.IsPremium.Value;
        movie.UpdatedAt   = DateTime.UtcNow;

        _movieRepository.Update(movie);
        await _movieRepository.SaveChangesAsync();

        var movieWithGenres = await _movieRepository.GetByIdWithDetailsAsync(movieId);
        var genreIds        = movieWithGenres?.MovieGenres.Select(mg => mg.GenreId).ToList() ?? new();

        // FIX: Invalidate cả AI cache khi cập nhật phim
        await InvalidateMovieCachesAsync(movieId, genreIds);
        return true;
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
                try { await _cloudinaryService.DeleteFileAsync(publicId); }
                catch { /* Tiếp tục xóa DB dù Cloudinary có lỗi */ }
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
            var stillInDir  = await _directorRepository.FindOneAsync(d => d.PersonId == personId);

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
        var cacheKey      = $"search:{normalizedKey}";

        var cached = await _cacheService.GetAsync<List<MovieDTO>>(cacheKey);
        if (cached != null) return cached;

        var movies  = await _movieRepository.SearchByTitleAsync(query);
        var results = movies.Select(MapToDTO).ToList();

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromMinutes(10));
        return results;
    }

    public async Task<IEnumerable<MovieDTO>> SearchMoviesByActorAsync(string actorName)
    {
        if (string.IsNullOrWhiteSpace(actorName)) return Enumerable.Empty<MovieDTO>();

        var cacheKey = $"search:actor:{actorName.ToLower().Trim()}";
        var cached   = await _cacheService.GetAsync<List<MovieDTO>>(cacheKey);
        if (cached != null) return cached;

        var movies  = await _movieRepository.GetMoviesByActorNameAsync(actorName);
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
        var cached   = await _cacheService.GetAsync<List<MovieDTO>>(cacheKey);
        if (cached != null) return cached;

        var movies  = await _movieRepository.GetByGenreAsync(genreId);
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

        await _videoRepository.AddAsync(new MovieVideo
        {
            MovieId     = movieId,
            VideoUrl    = videoUrl,
            VideoType   = videoType,
            Quality     = quality,
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
            try { await _cloudinaryService.DeleteFileAsync(publicId); }
            catch { /* Tiếp tục xóa DB dù Cloudinary có lỗi */ }
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
        var movieIds  = favorites.Select(f => f.MovieId).Distinct().ToList();
        var movies    = await _movieRepository.GetPagedAsync(new FilterMoviesDTO
        {
            Ids      = movieIds,
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
                    Id         = f.Id,
                    MovieId    = m.Id,
                    MovieTitle = m.Title,
                    PosterUrl  = m.PosterUrl,
                    Rating     = m.ImdbRating,
                    AddedAt    = f.AddedAt
                };
            })
            .OrderByDescending(f => f.AddedAt)
            .ToList();
    }

    // ─── Watch History ────────────────────────────────────────────────────────

    public async Task UpdateWatchProgressAsync(
        Guid userId, Guid movieId, int progressMinutes, bool isCompleted)
    {
        var existing = await _watchHistoryRepository.FindOneAsync(
            h => h.UserId == userId && h.MovieId == movieId);

        if (existing != null)
        {
            existing.ProgressMinutes = progressMinutes;
            existing.IsCompleted     = isCompleted;
            existing.WatchedAt       = DateTime.UtcNow;
            _watchHistoryRepository.Update(existing);
        }
        else
        {
            await _watchHistoryRepository.AddAsync(new WatchHistory
            {
                UserId          = userId,
                MovieId         = movieId,
                ProgressMinutes = progressMinutes,
                IsCompleted     = isCompleted
            });
        }

        await _watchHistoryRepository.SaveChangesAsync();
        await _cacheService.RemoveAsync(TRENDING_CACHE_KEY);
    }

    public async Task<IEnumerable<WatchHistoryDTO>> GetWatchHistoryAsync(Guid userId)
    {
        var histories = await _watchHistoryRepository.FindAsync(h => h.UserId == userId);
        var movieIds  = histories.Select(h => h.MovieId).Distinct().ToList();
        var movies    = await _movieRepository.GetPagedAsync(new FilterMoviesDTO
        {
            Ids      = movieIds,
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
                    Id              = h.Id,
                    MovieId         = m.Id,
                    MovieTitle      = m.Title,
                    PosterUrl       = m.PosterUrl,
                    WatchedAt       = h.WatchedAt,
                    ProgressMinutes = h.ProgressMinutes,
                    IsCompleted     = h.IsCompleted
                };
            })
            .OrderByDescending(h => h.WatchedAt)
            .ToList();
    }

    public async Task<bool> DeleteWatchHistoryAsync(Guid userId, Guid historyId)
    {
        var record = await _watchHistoryRepository.FindOneAsync(
            h => h.Id == historyId && h.UserId == userId);
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
            AI_CONTEXTS_CACHE_KEY,  // FIX: AI cache cũng cần reset khi phim thay đổi
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
            var exists = await _movieGenreRepository.FindOneAsync(
                x => x.MovieId == movieId && x.GenreId == genreId);

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

    private async Task SaveCastAsync(Guid movieId, List<ImportCastDTO> cast)
    {
        foreach (var c in cast)
        {
            var person = await UpsertPersonAsync(
                c.TmdbPersonId, c.Name, c.ProfileUrl,
                c.Biography, c.Birthday, c.PlaceOfBirth);

            await SavePersonImagesAsync(person.Id, c.ProfileImages);

            var existing = await _castRepository.FindOneAsync(
                x => x.MovieId == movieId && x.PersonId == person.Id);

            if (existing == null)
            {
                await _castRepository.AddAsync(new MovieCast
                {
                    MovieId   = movieId,
                    PersonId  = person.Id,
                    Character = c.Character,
                    Order     = c.Order
                });
            }
        }
        await _castRepository.SaveChangesAsync();
    }

    private async Task SaveDirectorAsync(Guid movieId, ImportDirectorDTO dto)
    {
        var person = await UpsertPersonAsync(
            dto.TmdbPersonId, dto.Name, dto.ProfileUrl,
            dto.Biography, dto.Birthday, dto.PlaceOfBirth);

        await SavePersonImagesAsync(person.Id, dto.ProfileImages);

        var existing = await _directorRepository.FindOneAsync(
            x => x.MovieId == movieId && x.PersonId == person.Id);

        if (existing == null)
        {
            await _directorRepository.AddAsync(new MovieDirector
            {
                MovieId  = movieId,
                PersonId = person.Id
            });
            await _directorRepository.SaveChangesAsync();
        }
    }

    private async Task SaveImagesAsync(Guid movieId, List<ImportImageDTO> images)
    {
        foreach (var img in images)
        {
            await _imageRepository.AddAsync(new MovieImage
            {
                MovieId   = movieId,
                Url       = img.Url,
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
                MovieId     = movieId,
                VideoUrl    = t.YoutubeUrl,
                VideoType   = "trailer",
                Quality     = t.Name,
                IsPublished = true
            });
        }
        await _videoRepository.SaveChangesAsync();
    }

    private async Task<Person> UpsertPersonAsync(
        int     tmdbPersonId,
        string  name,
        string? profileUrl,
        string? biography,
        string? birthday,
        string? placeOfBirth)
    {
        var person = await _personRepository.FindOneAsync(p => p.TmdbPersonId == tmdbPersonId);

        if (person == null)
        {
            person = new Person
            {
                TmdbPersonId = tmdbPersonId,
                Name         = name,
                ProfileUrl   = profileUrl,
                Biography    = biography,
                Birthday     = birthday,
                PlaceOfBirth = placeOfBirth
            };
            await _personRepository.AddAsync(person);
            await _personRepository.SaveChangesAsync();
        }
        else
        {
            bool changed = false;
            if (string.IsNullOrEmpty(person.Biography) && !string.IsNullOrEmpty(biography))
            { person.Biography = biography; changed = true; }
            if (string.IsNullOrEmpty(person.Birthday) && !string.IsNullOrEmpty(birthday))
            { person.Birthday = birthday; changed = true; }
            if (string.IsNullOrEmpty(person.PlaceOfBirth) && !string.IsNullOrEmpty(placeOfBirth))
            { person.PlaceOfBirth = placeOfBirth; changed = true; }
            if (string.IsNullOrEmpty(person.ProfileUrl) && !string.IsNullOrEmpty(profileUrl))
            { person.ProfileUrl = profileUrl; changed = true; }

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
                Url      = url
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
        Id            = m.Id,
        Title         = m.Title,
        Description   = m.Description,
        ReleaseDate   = m.ReleaseDate,
        PosterUrl     = m.PosterUrl,
        BackdropUrl   = m.BackdropUrl,
        Duration      = m.Duration,
        Rating        = m.ImdbRating,
        OriginCountry = m.OriginCountry,
        IsPremium     = m.IsPremium,

        Genres = m.MovieGenres?
            .Select(g => g.Genre?.Name ?? "")
            .Where(n => n != "")
            .ToList() ?? new(),

        Videos = m.MovieVideos?
            .Select(v => new MovieVideoDTO
            {
                Id        = v.Id,
                VideoUrl  = v.VideoUrl,
                VideoType = v.VideoType,
                Duration  = v.Duration,
                Quality   = v.Quality
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
                Name          = c.Person!.Name,
                Character     = c.Character,
                Order         = c.Order,
                ProfileUrl    = c.Person.ProfileUrl,
                TmdbPersonId  = c.Person.TmdbPersonId,
                Biography     = c.Person.Biography,
                Birthday      = c.Person.Birthday,
                PlaceOfBirth  = c.Person.PlaceOfBirth,
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
                Name          = d.Person!.Name,
                ProfileUrl    = d.Person.ProfileUrl,
                TmdbPersonId  = d.Person.TmdbPersonId,
                Biography     = d.Person.Biography,
                Birthday      = d.Person.Birthday,
                PlaceOfBirth  = d.Person.PlaceOfBirth,
                ProfileImages = d.Person.Images
                    .OrderByDescending(i => i.CreatedAt)
                    .Select(i => i.Url)
                    .ToList()
            })
            .FirstOrDefault(),

        Images = m.MovieImages?
            .Select(i => new MovieImageDTO
            {
                Url       = i.Url,
                ImageType = i.ImageType
            }).ToList() ?? new()
    };

    private static TrendingMovieDTO MapToTrendingDTO(Movie m)
    {
        var base_ = MapToDTO(m);
        return new TrendingMovieDTO
        {
            Id             = base_.Id,
            Title          = base_.Title,
            Description    = base_.Description,
            ReleaseDate    = base_.ReleaseDate,
            PosterUrl      = base_.PosterUrl,
            BackdropUrl    = base_.BackdropUrl,
            Duration       = base_.Duration,
            Rating         = base_.Rating,
            OriginCountry  = base_.OriginCountry,
            IsPremium      = base_.IsPremium,
            Genres         = base_.Genres,
            Videos         = base_.Videos,
            TrailerKey     = base_.TrailerKey,
            Cast           = base_.Cast,
            Images         = base_.Images,
            Director       = base_.Director,
            DirectorDetail = base_.DirectorDetail,
            TrendingRank   = 0,
            Views7d        = 0,
            Views30d       = 0,
            TrendingScore  = 0
        };
    }
}