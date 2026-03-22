// UIAMovie.Application/Services/MovieService.cs

using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Services;

public interface IMovieService
{
    Task<PaginatedDTO<MovieDTO>> GetMoviesAsync(FilterMoviesDTO filter);
    Task<IEnumerable<MovieDTO>> GetTrendingMoviesAsync();
    Task<MovieDTO?> GetMovieByIdAsync(Guid movieId);
    Task<MovieDTO?> GetMovieByTmdbIdAsync(int tmdbId);
    Task<Guid> CreateMovieAsync(CreateMovieDTO dto);
    Task<bool> UpdateMovieAsync(Guid movieId, UpdateMovieDTO dto);
    Task<bool> DeleteMovieAsync(Guid movieId);
    Task<IEnumerable<MovieDTO>> SearchMoviesAsync(string query);
    Task<IEnumerable<MovieDTO>> SearchMoviesByActorAsync(string actorName);
    Task<IEnumerable<MovieDTO>> GetMoviesByGenreAsync(Guid genreId);
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
    private readonly IMovieRepository            _movieRepository;
    private readonly IRepository<MovieVideo>     _videoRepository;
    private readonly IRepository<Favorite>       _favoriteRepository;
    private readonly IRepository<WatchHistory>   _watchHistoryRepository;
    private readonly IRepository<Person>         _personRepository;
    private readonly IRepository<PersonImage>    _personImageRepository;
    private readonly IRepository<MovieCast>      _castRepository;
    private readonly IRepository<MovieDirector>  _directorRepository;
    private readonly IRepository<MovieImage>     _imageRepository;
    private readonly IRepository<MovieGenre>     _movieGenreRepository;
    private readonly ICacheService               _cacheService;

    private const string TRENDING_CACHE_KEY = "movies:trending";
    private const string GENRE_CACHE_KEY    = "movies:genre:{0}";
    private const string MOVIE_CACHE_KEY    = "movie:{0}";

    public MovieService(
        IMovieRepository            movieRepository,
        IRepository<MovieVideo>     videoRepository,
        IRepository<Favorite>       favoriteRepository,
        IRepository<WatchHistory>   watchHistoryRepository,
        IRepository<Person>         personRepository,
        IRepository<PersonImage>    personImageRepository,
        IRepository<MovieCast>      castRepository,
        IRepository<MovieDirector>  directorRepository,
        IRepository<MovieImage>     imageRepository,
        IRepository<MovieGenre>     movieGenreRepository,
        ICacheService               cacheService)
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
        _cacheService           = cacheService;
    }

    // ─── Movies ──────────────────────────────────────────────────────────────

    public async Task<PaginatedDTO<MovieDTO>> GetMoviesAsync(FilterMoviesDTO filter)
    {
        var movies = await _movieRepository.GetAllWithGenresAsync();

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

        if (!string.IsNullOrWhiteSpace(filter.OriginCountry))
            movies = movies.Where(m =>
                m.OriginCountry != null &&
                m.OriginCountry.Equals(filter.OriginCountry.Trim(), StringComparison.OrdinalIgnoreCase));

        movies = filter.SortBy?.ToLower() switch
        {
            "title"       => filter.SortDesc ? movies.OrderByDescending(m => m.Title)       : movies.OrderBy(m => m.Title),
            "releasedate" => filter.SortDesc ? movies.OrderByDescending(m => m.ReleaseDate) : movies.OrderBy(m => m.ReleaseDate),
            _             => filter.SortDesc ? movies.OrderByDescending(m => m.ImdbRating)  : movies.OrderBy(m => m.ImdbRating)
        };

        var totalCount = movies.Count();
        var items = movies
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(MapToDTO)
            .ToList();

        return new PaginatedDTO<MovieDTO>
        {
            Items      = items,
            TotalCount = totalCount,
            PageNumber = filter.Page,
            PageSize   = filter.PageSize
        };
    }

    public async Task<IEnumerable<MovieDTO>> GetTrendingMoviesAsync()
    {
        var cached = await _cacheService.GetAsync<List<MovieDTO>>(TRENDING_CACHE_KEY);
        if (cached != null) return cached;

        var movies = await _movieRepository.GetAllWithGenresAsync();
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

        // Lưu genres trước để MovieGenres có sẵn khi response
        if (dto.GenreIds.Any())  await SaveGenresAsync(movie.Id, dto.GenreIds);
        if (dto.Cast.Any())      await SaveCastAsync(movie.Id, dto.Cast);
        if (dto.Director != null) await SaveDirectorAsync(movie.Id, dto.Director);
        if (dto.Images.Any())    await SaveImagesAsync(movie.Id, dto.Images);
        if (dto.Trailers.Any())  await SaveTrailersAsync(movie.Id, dto.Trailers);

        // Xóa cache sau khi đã lưu toàn bộ dữ liệu — 1 round-trip duy nhất
        var keysToInvalidate = new List<string>
        {
            string.Format(MOVIE_CACHE_KEY, movie.Id),
            TRENDING_CACHE_KEY,
        };
        keysToInvalidate.AddRange(dto.GenreIds.Select(id => string.Format(GENRE_CACHE_KEY, id)));
        await _cacheService.RemoveManyAsync(keysToInvalidate.ToArray());

        return movie.Id;
    }

    public async Task<bool> UpdateMovieAsync(Guid movieId, UpdateMovieDTO dto)
    {
        var movie = await _movieRepository.GetByIdAsync(movieId);
        if (movie == null) return false;

        movie.Title       = dto.Title ?? movie.Title;
        movie.Description = dto.Description ?? movie.Description;
        movie.ImdbRating  = dto.ImdbRating ?? movie.ImdbRating;
        movie.UpdatedAt   = DateTime.UtcNow;

        _movieRepository.Update(movie);
        await _movieRepository.SaveChangesAsync();

        var movieWithGenres = await _movieRepository.GetByIdWithDetailsAsync(movieId);
        var keysToInvalidate = new List<string>
        {
            string.Format(MOVIE_CACHE_KEY, movieId),
            TRENDING_CACHE_KEY,
        };
        if (movieWithGenres != null)
            keysToInvalidate.AddRange(
                movieWithGenres.MovieGenres.Select(mg => string.Format(GENRE_CACHE_KEY, mg.GenreId)));

        await _cacheService.RemoveManyAsync(keysToInvalidate.ToArray());
        return true;
    }

    public async Task<bool> DeleteMovieAsync(Guid movieId)
    {
        var movie = await _movieRepository.GetByIdWithDetailsAsync(movieId);
        if (movie == null) return false;

        var personIds = movie.MovieCasts
            .Select(c => c.PersonId)
            .Concat(movie.MovieDirectors.Select(d => d.PersonId))
            .Distinct()
            .ToList();

        // Lấy genre ids trước khi xóa để clear cache sau
        var genreIds = movie.MovieGenres.Select(mg => mg.GenreId).ToList();

        // Xóa phim → MovieCast + MovieDirector + MovieImage + MovieVideo + MovieGenre tự cascade
        _movieRepository.Remove(movie);
        await _movieRepository.SaveChangesAsync();

        // Xóa Person không còn xuất hiện ở bất kỳ phim nào khác
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

        var keysToInvalidate = new List<string>
        {
            string.Format(MOVIE_CACHE_KEY, movieId),
            TRENDING_CACHE_KEY,
        };
        keysToInvalidate.AddRange(genreIds.Select(id => string.Format(GENRE_CACHE_KEY, id)));
        await _cacheService.RemoveManyAsync(keysToInvalidate.ToArray());
        return true;
    }

    // ─── Search & Filter ─────────────────────────────────────────────────────

    public async Task<IEnumerable<MovieDTO>> SearchMoviesAsync(string query)
    {
        var cacheKey = $"search:{query.ToLower()}";
        var cached   = await _cacheService.GetAsync<List<MovieDTO>>(cacheKey);
        if (cached != null) return cached;

        var movies = await _movieRepository.GetAllWithGenresAsync();
        var results = movies
            .Where(m => m.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(MapToDTO)
            .ToList();

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromMinutes(10));
        return results;
    }

    public async Task<IEnumerable<MovieDTO>> SearchMoviesByActorAsync(string actorName)
    {
        if (string.IsNullOrWhiteSpace(actorName))
            return Enumerable.Empty<MovieDTO>();

        var cacheKey = $"search:actor:{actorName.ToLower().Trim()}";
        var cached   = await _cacheService.GetAsync<List<MovieDTO>>(cacheKey);
        if (cached != null) return cached;

        var movies  = await _movieRepository.GetMoviesByActorNameAsync(actorName);
        var results = movies.Select(MapToDTO).ToList();

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromMinutes(10));
        return results;
    }

    public async Task<IEnumerable<MovieDTO>> GetMoviesByGenreAsync(Guid genreId)
    {
        var cacheKey = string.Format(GENRE_CACHE_KEY, genreId);
        var cached   = await _cacheService.GetAsync<List<MovieDTO>>(cacheKey);
        if (cached != null) return cached;

        var movies = await _movieRepository.GetAllWithGenresAsync();
        var results = movies
            .Where(m => m.MovieGenres.Any(g => g.GenreId == genreId))
            .Select(MapToDTO)
            .ToList();

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromMinutes(15));
        return results;
    }

    // ─── Videos ──────────────────────────────────────────────────────────────

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

        _videoRepository.Remove(video);
        await _videoRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(MOVIE_CACHE_KEY, video.MovieId));
        return true;
    }

    // ─── Favorites ───────────────────────────────────────────────────────────

    public async Task<bool> AddFavoriteAsync(Guid userId, Guid movieId)
    {
        var favorites = await _favoriteRepository.GetAllAsync();
        if (favorites.Any(f => f.UserId == userId && f.MovieId == movieId)) return false;

        await _favoriteRepository.AddAsync(new Favorite { UserId = userId, MovieId = movieId });
        await _favoriteRepository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveFavoriteAsync(Guid userId, Guid movieId)
    {
        var favorites = await _favoriteRepository.GetAllAsync();
        var favorite  = favorites.FirstOrDefault(f => f.UserId == userId && f.MovieId == movieId);
        if (favorite == null) return false;

        _favoriteRepository.Remove(favorite);
        await _favoriteRepository.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<FavoriteDTO>> GetFavoritesAsync(Guid userId)
    {
        var favorites = await _favoriteRepository.GetAllAsync();
        var movies    = await _movieRepository.GetAllWithGenresAsync();

        return favorites
            .Where(f => f.UserId == userId)
            .Join(movies, f => f.MovieId, m => m.Id, (f, m) => new FavoriteDTO
            {
                Id         = f.Id,
                MovieId    = m.Id,
                MovieTitle = m.Title,
                PosterUrl  = m.PosterUrl,
                Rating     = m.ImdbRating,
                AddedAt    = f.AddedAt
            })
            .OrderByDescending(f => f.AddedAt)
            .ToList();
    }

    // ─── Watch History ────────────────────────────────────────────────────────

    public async Task UpdateWatchProgressAsync(
        Guid userId, Guid movieId, int progressMinutes, bool isCompleted)
    {
        var histories = await _watchHistoryRepository.GetAllAsync();
        var existing  = histories.FirstOrDefault(h => h.UserId == userId && h.MovieId == movieId);

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
    }

    public async Task<IEnumerable<WatchHistoryDTO>> GetWatchHistoryAsync(Guid userId)
    {
        var histories = await _watchHistoryRepository.GetAllAsync();
        var movies    = await _movieRepository.GetAllWithGenresAsync();

        return histories
            .Where(h => h.UserId == userId)
            .Join(movies, h => h.MovieId, m => m.Id, (h, m) => new WatchHistoryDTO
            {
                Id              = h.Id,
                MovieId         = m.Id,
                MovieTitle      = m.Title,
                PosterUrl       = m.PosterUrl,
                WatchedAt       = h.WatchedAt,
                ProgressMinutes = h.ProgressMinutes,
                IsCompleted     = h.IsCompleted
            })
            .OrderByDescending(h => h.WatchedAt)
            .ToList();
    }

    public async Task<bool> DeleteWatchHistoryAsync(Guid userId, Guid historyId)
    {
        var histories = await _watchHistoryRepository.GetAllAsync();
        var record    = histories.FirstOrDefault(h => h.Id == historyId && h.UserId == userId);

        if (record == null) return false;

        _watchHistoryRepository.Remove(record);
        await _watchHistoryRepository.SaveChangesAsync();
        return true;
    }

    public async Task ClearWatchHistoryAsync(Guid userId)
    {
        var histories = await _watchHistoryRepository.GetAllAsync();
        var userRecords = histories.Where(h => h.UserId == userId).ToList();

        foreach (var record in userRecords)
            _watchHistoryRepository.Remove(record);

        await _watchHistoryRepository.SaveChangesAsync();
    }

    // ─── Private: lưu genres / cast / director / images / trailers ───────────

    /// <summary>
    /// Lưu MovieGenre records cho phim.
    /// Tự động bỏ qua duplicate nếu gọi nhiều lần.
    /// </summary>
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

    private async Task<Person> UpsertPersonAsync(
        int     tmdbPersonId,
        string  name,
        string? profileUrl,
        string? biography    = null,
        string? birthday     = null,
        string? placeOfBirth = null)
    {
        var existing = await _personRepository.FindOneAsync(p => p.TmdbPersonId == tmdbPersonId);

        if (existing != null)
        {
            if (string.IsNullOrEmpty(existing.Biography) && !string.IsNullOrEmpty(biography))
            {
                existing.Biography    = biography;
                existing.Birthday     = birthday;
                existing.PlaceOfBirth = placeOfBirth;
                _personRepository.Update(existing);
                await _personRepository.SaveChangesAsync();
            }
            return existing;
        }

        var person = new Person
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
        return person;
    }

    /// <summary>
    /// Lưu ảnh profile cho một người — bỏ qua nếu đã có ảnh rồi (tránh duplicate khi import lại).
    /// </summary>
    private async Task SavePersonImagesAsync(Guid personId, List<string> imageUrls)
    {
        if (!imageUrls.Any()) return;

        var existing = await _personImageRepository.FindAsync(i => i.PersonId == personId);
        if (existing.Any()) return; // đã có ảnh → không import lại

        foreach (var url in imageUrls)
        {
            await _personImageRepository.AddAsync(new PersonImage
            {
                PersonId = personId,
                Url      = url
            });
        }
        await _personImageRepository.SaveChangesAsync();
    }

    private async Task SaveCastAsync(Guid movieId, List<ImportCastDTO> castList)
    {
        foreach (var c in castList)
        {
            var person = await UpsertPersonAsync(
                c.TmdbPersonId, c.Name, c.ProfileUrl,
                c.Biography, c.Birthday, c.PlaceOfBirth);

            await SavePersonImagesAsync(person.Id, c.ProfileImages);

            var existingCast = await _castRepository.FindOneAsync(
                x => x.MovieId == movieId && x.PersonId == person.Id);

            if (existingCast == null)
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

        var existingDirector = await _directorRepository.FindOneAsync(
            x => x.MovieId == movieId && x.PersonId == person.Id);

        if (existingDirector == null)
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

    private static string? ExtractYoutubeKey(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        var v = System.Text.RegularExpressions.Regex.Match(url, @"[?&]v=([a-zA-Z0-9_-]{11})");
        if (v.Success) return v.Groups[1].Value;

        var s = System.Text.RegularExpressions.Regex.Match(url, @"youtu\.be/([a-zA-Z0-9_-]{11})");
        if (s.Success) return s.Groups[1].Value;

        return null;
    }

    // ─── MapToDTO ─────────────────────────────────────────────────────────────

    private static MovieDTO MapToDTO(Movie m) => new()
    {
        Id          = m.Id,
        Title       = m.Title,
        Description = m.Description,
        ReleaseDate = m.ReleaseDate,
        PosterUrl   = m.PosterUrl,
        BackdropUrl = m.BackdropUrl,
        Duration    = m.Duration,
        Rating      = m.ImdbRating,
        OriginCountry = m.OriginCountry,

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

    public async Task<IEnumerable<string>> GetPersonImagesAsync(Guid personId)
    {
        var images = await _personImageRepository.FindAsync(i => i.PersonId == personId);
        return images.Select(i => i.Url).ToList();
    }
}