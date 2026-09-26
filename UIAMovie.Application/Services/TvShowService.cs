// UIAMovie.Application/Services/TvShowService.cs

using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Services;

public interface ITvShowService
{
    Task<PaginatedDTO<TvShowSummaryDTO>> GetTvShowsAsync(FilterTvShowsDTO filter);
    Task<TvShowDTO?> GetTvShowByIdAsync(Guid id);
    Task<TvShowDTO?> GetTvShowByTmdbIdAsync(int tmdbId);
    Task<Guid> CreateTvShowAsync(CreateTvShowDTO dto);
    Task<IEnumerable<PersonSearchDTO>> SearchPersonsAsync(string query);
    Task<bool> UpdateTvShowAsync(Guid id, UpdateTvShowDTO dto);
    Task<bool> SetPremiumAsync(Guid id, bool isPremium);
    Task<bool> DeleteTvShowAsync(Guid id);
    Task<IEnumerable<TvShowSummaryDTO>> SearchTvShowsAsync(string query);
    Task<IEnumerable<TvShowSummaryDTO>> GetTvShowsByGenreAsync(Guid genreId);
    Task<IEnumerable<string>> GetAvailableCountriesAsync();
    Task<IEnumerable<TvShowDTO>> SearchTvShowsByActorAsync(string actorName);
    Task<int?> GetTmdbIdAsync(Guid id);
    Task<SyncResultDTO> SyncNewEpisodesAsync(Guid id, TmdbFullTvShowDTO full);
    Task<SeasonDTO?> GetSeasonAsync(Guid tvShowId, int seasonNumber);
    Task<EpisodeDTO?> GetEpisodeAsync(Guid tvShowId, int seasonNumber, int episodeNumber);
    Task<bool> UpdateSeasonAsync(Guid tvShowId, int seasonNumber, UpdateSeasonDTO dto);
    Task<bool> UpdateEpisodeAsync(Guid episodeId, UpdateEpisodeDTO dto);
    Task<EpisodeDTO?> AddEpisodeAsync(Guid tvShowId, int seasonNumber, CreateEpisodeDTO dto);
    Task<(bool found, string? oldVideoUrl)> DeleteEpisodeAsync(Guid episodeId);
    Task<bool> AddVideoAsync(Guid tvShowId, string videoUrl, string videoType, string? quality);
    Task<bool> SetTrailerYoutubeAsync(Guid tvShowId, string youtubeUrl);
    Task<bool> DeleteVideoAsync(Guid videoId);
    Task<(bool found, string? oldUrl)> SetEpisodeVideoAsync(Guid episodeId, string videoUrl);
    Task<(bool found, string? oldUrl)> RemoveEpisodeVideoAsync(Guid episodeId);
    Task<bool> AddFavoriteAsync(Guid userId, Guid tvShowId);
    Task<bool> RemoveFavoriteAsync(Guid userId, Guid tvShowId);
    Task<IEnumerable<TvShowFavoriteDTO>> GetFavoritesAsync(Guid userId);
    Task UpdateWatchProgressAsync(Guid userId, Guid tvShowId, Guid? episodeId, int progressSeconds, bool isCompleted);
    Task<IEnumerable<TvShowWatchHistoryDTO>> GetWatchHistoryAsync(Guid userId);
    Task<bool> DeleteWatchHistoryAsync(Guid userId, Guid historyId);
    Task ClearWatchHistoryAsync(Guid userId);
}

public class TvShowService : ITvShowService
{
    private readonly ITvShowRepository _tvShowRepository;
    private readonly IRepository<TvShowVideo> _videoRepository;
    private readonly IRepository<TvShowImage> _imageRepository;
    private readonly IRepository<TvShowGenre> _tvShowGenreRepository;
    private readonly IRepository<TvShowCast> _castRepository;
    private readonly IRepository<TvShowDirector> _directorRepository;
    private readonly IRepository<Season> _seasonRepository;
    private readonly IRepository<Episode> _episodeRepository;
    private readonly IRepository<Person> _personRepository;
    private readonly IRepository<PersonImage> _personImageRepository;
    private readonly IRepository<Genre> _genreRepository;
    private readonly IRepository<TvShowFavorite> _favoriteRepository;
    private readonly IRepository<TvShowWatchHistory> _watchHistoryRepository;
    private readonly ICacheService _cacheService;
    private readonly ICloudinaryService _cloudinaryService;
    private readonly INotificationService _notificationService;

    private const string TVSHOW_CACHE_KEY = "tvshow:{0}";
    private const string SEASON_CACHE_KEY = "tvshow:{0}:season:{1}";
    private const string GENRE_CACHE_KEY = "tvshows:genre:{0}";
    private const string AI_CONTEXTS_KEY = "ai:tvshow_contexts";
    private const string AI_ALL_DTOS_KEY = "ai:all_tvshow_dtos";

    public TvShowService(
        ITvShowRepository tvShowRepository,
        IRepository<TvShowVideo> videoRepository,
        IRepository<TvShowImage> imageRepository,
        IRepository<TvShowGenre> tvShowGenreRepository,
        IRepository<TvShowCast> castRepository,
        IRepository<TvShowDirector> directorRepository,
        IRepository<Season> seasonRepository,
        IRepository<Episode> episodeRepository,
        IRepository<Person> personRepository,
        IRepository<PersonImage> personImageRepository,
        IRepository<Genre> genreRepository,
        IRepository<TvShowFavorite> favoriteRepository,
        IRepository<TvShowWatchHistory> watchHistoryRepository,
        ICacheService cacheService,
        ICloudinaryService cloudinaryService,
        INotificationService notificationService)
    {
        _tvShowRepository = tvShowRepository;
        _videoRepository = videoRepository;
        _imageRepository = imageRepository;
        _tvShowGenreRepository = tvShowGenreRepository;
        _castRepository = castRepository;
        _directorRepository = directorRepository;
        _seasonRepository = seasonRepository;
        _episodeRepository = episodeRepository;
        _personRepository = personRepository;
        _personImageRepository = personImageRepository;
        _genreRepository = genreRepository;
        _favoriteRepository = favoriteRepository;
        _watchHistoryRepository = watchHistoryRepository;
        _cacheService = cacheService;
        _cloudinaryService = cloudinaryService;
        _notificationService = notificationService;
    }

    public async Task<PaginatedDTO<TvShowSummaryDTO>> GetTvShowsAsync(FilterTvShowsDTO filter)
    {
        var (shows, totalCount) = await _tvShowRepository.GetPagedAsync(filter);
        var items = shows.Select(MapToSummaryDTO).ToList();

        return new PaginatedDTO<TvShowSummaryDTO>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<TvShowDTO?> GetTvShowByIdAsync(Guid id)
    {
        var cacheKey = string.Format(TVSHOW_CACHE_KEY, id);
        var cached = await _cacheService.GetAsync<TvShowDTO>(cacheKey);
        if (cached != null) return cached;

        var show = await _tvShowRepository.GetByIdWithDetailsAsync(id);
        if (show == null) return null;

        var dto = MapToDTO(show);
        await _cacheService.SetAsync(cacheKey, dto, TimeSpan.FromHours(24));
        return dto;
    }

    public async Task<TvShowDTO?> GetTvShowByTmdbIdAsync(int tmdbId)
    {
        var show = await _tvShowRepository.GetByTmdbIdAsync(tmdbId);
        return show == null ? null : MapToDTO(show);
    }

    public async Task<IEnumerable<TvShowSummaryDTO>> SearchTvShowsAsync(string query)
    {
        var normalizedKey = query.ToLower().Trim();
        var cacheKey = $"tvshow:search:{normalizedKey}";

        var cached = await _cacheService.GetAsync<List<TvShowSummaryDTO>>(cacheKey);
        if (cached != null) return cached;

        var shows = await _tvShowRepository.SearchByTitleAsync(query);
        var results = shows.Select(MapToSummaryDTO).ToList();

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromMinutes(10));
        return results;
    }

    public async Task<IEnumerable<TvShowSummaryDTO>> GetTvShowsByGenreAsync(Guid genreId)
    {
        var cacheKey = string.Format(GENRE_CACHE_KEY, genreId);
        var cached = await _cacheService.GetAsync<List<TvShowSummaryDTO>>(cacheKey);
        if (cached != null) return cached;

        var shows = await _tvShowRepository.GetByGenreAsync(genreId);
        var results = shows.Select(MapToSummaryDTO).ToList();

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromMinutes(15));
        return results;
    }

    public async Task<IEnumerable<string>> GetAvailableCountriesAsync()
        => await _tvShowRepository.GetAvailableCountriesAsync();

    public async Task<IEnumerable<TvShowDTO>> SearchTvShowsByActorAsync(string actorName)
    {
        var normalizedKey = actorName.ToLower().Trim();
        var cacheKey = $"tvshow:search:actor:{normalizedKey}";

        var cached = await _cacheService.GetAsync<List<TvShowDTO>>(cacheKey);
        if (cached != null) return cached;

        var shows = await _tvShowRepository.SearchByActorNameAsync(actorName);
        var results = shows.Select(MapToDTO).ToList();

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromMinutes(10));
        return results;
    }

    public async Task<SeasonDTO?> GetSeasonAsync(Guid tvShowId, int seasonNumber)
    {
        var cacheKey = string.Format(SEASON_CACHE_KEY, tvShowId, seasonNumber);
        var cached = await _cacheService.GetAsync<SeasonDTO>(cacheKey);
        if (cached != null) return cached;

        var season = await _tvShowRepository.GetSeasonWithEpisodesAsync(tvShowId, seasonNumber);
        if (season == null) return null;

        var dto = MapSeasonToDTO(season, season.Episodes);
        await _cacheService.SetAsync(cacheKey, dto, TimeSpan.FromHours(6));
        return dto;
    }

    public async Task<EpisodeDTO?> GetEpisodeAsync(Guid tvShowId, int seasonNumber, int episodeNumber)
    {
        var cacheKey = string.Format(SEASON_CACHE_KEY, tvShowId, seasonNumber);
        var cachedSeason = await _cacheService.GetAsync<SeasonDTO>(cacheKey);
        if (cachedSeason != null)
            return cachedSeason.Episodes.FirstOrDefault(e => e.EpisodeNumber == episodeNumber);

        var season = await _seasonRepository.FindOneAsync(s => s.TvShowId == tvShowId && s.SeasonNumber == seasonNumber);
        if (season == null) return null;

        var episode = await _episodeRepository.FindOneAsync(e => e.SeasonId == season.Id && e.EpisodeNumber == episodeNumber);
        return episode == null ? null : MapEpisodeToDTO(episode);
    }

    public async Task<bool> UpdateSeasonAsync(Guid tvShowId, int seasonNumber, UpdateSeasonDTO dto)
    {
        var season = await _seasonRepository.FindOneAsync(s => s.TvShowId == tvShowId && s.SeasonNumber == seasonNumber);
        if (season == null) return false;

        if (dto.Name != null) season.Name = dto.Name;
        if (dto.Overview != null) season.Overview = dto.Overview;
        if (dto.PosterUrl != null) season.PosterUrl = dto.PosterUrl;
        if (dto.AirDate.HasValue) season.AirDate = DateTime.SpecifyKind(dto.AirDate.Value, DateTimeKind.Utc);

        _seasonRepository.Update(season);
        await _seasonRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(SEASON_CACHE_KEY, tvShowId, seasonNumber));
        await _cacheService.RemoveAsync(string.Format(TVSHOW_CACHE_KEY, tvShowId));
        return true;
    }

    public async Task<bool> UpdateEpisodeAsync(Guid episodeId, UpdateEpisodeDTO dto)
    {
        var episode = await _episodeRepository.GetByIdAsync(episodeId);
        if (episode == null) return false;

        if (dto.Title != null) episode.Title = dto.Title;
        if (dto.Overview != null) episode.Overview = dto.Overview;
        if (dto.StillUrl != null) episode.StillUrl = dto.StillUrl;
        if (dto.Runtime.HasValue) episode.Runtime = dto.Runtime;
        if (dto.Rating.HasValue) episode.Rating = dto.Rating;
        if (dto.AirDate.HasValue) episode.AirDate = DateTime.SpecifyKind(dto.AirDate.Value, DateTimeKind.Utc);

        _episodeRepository.Update(episode);
        await _episodeRepository.SaveChangesAsync();

        var season = await _seasonRepository.GetByIdAsync(episode.SeasonId);
        if (season != null)
        {
            await _cacheService.RemoveAsync(string.Format(SEASON_CACHE_KEY, season.TvShowId, season.SeasonNumber));
            await _cacheService.RemoveAsync(string.Format(TVSHOW_CACHE_KEY, season.TvShowId));
        }
        return true;
    }

    public async Task<EpisodeDTO?> AddEpisodeAsync(Guid tvShowId, int seasonNumber, CreateEpisodeDTO dto)
    {
        var season = await _seasonRepository.FindOneAsync(s => s.TvShowId == tvShowId && s.SeasonNumber == seasonNumber);
        if (season == null) return null;

        var episode = new Episode
        {
            SeasonId = season.Id,
            EpisodeNumber = dto.EpisodeNumber,
            Title = dto.Title,
            Overview = dto.Overview,
            StillUrl = dto.StillUrl,
            Runtime = dto.Runtime,
            Rating = dto.Rating,
            AirDate = dto.AirDate.HasValue ? DateTime.SpecifyKind(dto.AirDate.Value, DateTimeKind.Utc) : null
        };
        await _episodeRepository.AddAsync(episode);
        await _episodeRepository.SaveChangesAsync();

        season.EpisodeCount += 1;
        _seasonRepository.Update(season);
        await _seasonRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(SEASON_CACHE_KEY, tvShowId, seasonNumber));
        await _cacheService.RemoveAsync(string.Format(TVSHOW_CACHE_KEY, tvShowId));

        return MapEpisodeToDTO(episode);
    }

    public async Task<(bool found, string? oldVideoUrl)> DeleteEpisodeAsync(Guid episodeId)
    {
        var episode = await _episodeRepository.GetByIdAsync(episodeId);
        if (episode == null) return (false, null);

        var season = await _seasonRepository.GetByIdAsync(episode.SeasonId);
        var oldVideoUrl = episode.VideoUrl;

        _episodeRepository.Remove(episode);
        await _episodeRepository.SaveChangesAsync();

        if (season != null)
        {
            season.EpisodeCount = Math.Max(0, season.EpisodeCount - 1);
            _seasonRepository.Update(season);
            await _seasonRepository.SaveChangesAsync();

            await _cacheService.RemoveAsync(string.Format(SEASON_CACHE_KEY, season.TvShowId, season.SeasonNumber));
            await _cacheService.RemoveAsync(string.Format(TVSHOW_CACHE_KEY, season.TvShowId));
        }

        return (true, oldVideoUrl);
    }

    public async Task<Guid> CreateTvShowAsync(CreateTvShowDTO dto)
    {
        if (dto.GenreIds.Any())
        {
            var distinctGenreIds = dto.GenreIds.Distinct().ToList();
            var existingGenres = await _genreRepository.FindAsync(g => distinctGenreIds.Contains(g.Id));
            var existingIds = existingGenres.Select(g => g.Id).ToHashSet();
            var missingIds = distinctGenreIds.Where(id => !existingIds.Contains(id)).ToList();

            if (missingIds.Any())
                throw new ArgumentException($"Thể loại không tồn tại: {string.Join(", ", missingIds)}");
        }

        var show = new TvShow
        {
            Title = dto.Title,
            Description = string.IsNullOrEmpty(dto.Description) ? dto.Title : dto.Description,
            FirstAirDate = dto.FirstAirDate.HasValue ? DateTime.SpecifyKind(dto.FirstAirDate.Value, DateTimeKind.Utc) : null,
            LastAirDate = dto.LastAirDate.HasValue ? DateTime.SpecifyKind(dto.LastAirDate.Value, DateTimeKind.Utc) : null,
            PosterUrl = dto.PosterUrl,
            BackdropUrl = dto.BackdropUrl,
            EpisodeRuntime = dto.EpisodeRuntime,
            ImdbRating = dto.ImdbRating,
            TmdbId = dto.TmdbId,
            OriginCountry = dto.OriginCountry,
            Status = dto.Status,
            NumberOfSeasons = dto.NumberOfSeasons,
            NumberOfEpisodes = dto.NumberOfEpisodes,
            IsPremium = dto.IsPremium,
            IsPublished = true
        };

        await _tvShowRepository.AddAsync(show);
        await _tvShowRepository.SaveChangesAsync();

        try
        {
            if (dto.GenreIds.Any()) await SaveGenresAsync(show.Id, dto.GenreIds);
            if (dto.Cast.Any()) await SaveCastAsync(show.Id, dto.Cast);
            if (dto.Director != null) await SaveDirectorAsync(show.Id, dto.Director);
            if (dto.Images.Any()) await SaveImagesAsync(show.Id, dto.Images);
            if (dto.Trailers.Any()) await SaveTrailersAsync(show.Id, dto.Trailers);
            if (dto.Seasons.Any()) await SaveSeasonsAsync(show.Id, dto.Seasons);
        }
        catch
        {
            _tvShowRepository.Remove(show);
            await _tvShowRepository.SaveChangesAsync();
            throw;
        }

        await InvalidateTvShowCachesAsync(show.Id, dto.GenreIds);

        // Bắn thông báo phát hành TV Show mới kèm Poster[cite: 3]
        try
        {
            await _notificationService.BroadcastNotificationAsync(
                title: "TV Show mới ra mắt!",
                message: $"TV Show \"{show.Title}\" vừa được bổ sung vào danh mục. Khám phá ngay!",
                linkUrl: $"/tvshow/{show.Id}/info",
                thumbnailUrl: show.PosterUrl, // ← Kèm Poster URL
                type: "movie_release"
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TvShowService BroadcastNotification Error]: {ex}");
        }

        return show.Id;
    }

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

    public async Task<bool> UpdateTvShowAsync(Guid id, UpdateTvShowDTO dto)
    {
        var show = await _tvShowRepository.GetByIdAsync(id);
        if (show == null) return false;

        show.Title = dto.Title ?? show.Title;
        show.Description = dto.Description ?? show.Description;
        show.ImdbRating = dto.ImdbRating ?? show.ImdbRating;
        show.Status = dto.Status ?? show.Status;
        if (dto.IsPremium.HasValue) show.IsPremium = dto.IsPremium.Value;
        if (dto.PosterUrl != null) show.PosterUrl = dto.PosterUrl;
        if (dto.BackdropUrl != null) show.BackdropUrl = dto.BackdropUrl;

        _tvShowRepository.Update(show);
        await _tvShowRepository.SaveChangesAsync();

        if (dto.Cast != null) await ReplaceCastAsync(id, dto.Cast);
        if (dto.Director != null) await ReplaceDirectorAsync(id, dto.Director);

        if (dto.GenreIds != null)
        {
            var distinctGenreIds = dto.GenreIds.Distinct().ToList();
            if (distinctGenreIds.Any())
            {
                var existingGenres = await _genreRepository.FindAsync(g => distinctGenreIds.Contains(g.Id));
                var existingIds = existingGenres.Select(g => g.Id).ToHashSet();
                var missingIds = distinctGenreIds.Where(gid => !existingIds.Contains(gid)).ToList();

                if (missingIds.Any())
                    throw new ArgumentException($"Thể loại không tồn tại: {string.Join(", ", missingIds)}");
            }
            await ReplaceGenresAsync(id, distinctGenreIds);
        }

        if (dto.BackdropImages != null)
        {
            await ReplaceImagesByTypeAsync(id, "backdrop", dto.BackdropImages);
        }

        var showWithGenres = await _tvShowRepository.GetByIdWithDetailsAsync(id);
        var genreIdList = showWithGenres?.TvShowGenres.Select(g => g.GenreId).ToList() ?? new();

        await InvalidateTvShowCachesAsync(id, genreIdList);
        return true;
    }

    public async Task<bool> SetPremiumAsync(Guid id, bool isPremium)
    {
        var show = await _tvShowRepository.GetByIdAsync(id);
        if (show == null) return false;

        show.IsPremium = isPremium;
        _tvShowRepository.Update(show);
        await _tvShowRepository.SaveChangesAsync();

        var genreIds = await _tvShowGenreRepository.FindAsync(g => g.TvShowId == id);
        var genreIdList = genreIds.Select(g => g.GenreId).ToList();
        await InvalidateTvShowCachesAsync(id, genreIdList);
        return true;
    }

    public async Task<bool> DeleteTvShowAsync(Guid id)
    {
        var show = await _tvShowRepository.GetByIdAsync(id);
        if (show == null) return false;

        var genreRows = await _tvShowGenreRepository.FindAsync(g => g.TvShowId == id);
        var genreIds = genreRows.Select(g => g.GenreId).ToList();

        _tvShowRepository.Remove(show);
        await _tvShowRepository.SaveChangesAsync();

        await InvalidateTvShowCachesAsync(id, genreIds);
        return true;
    }

    public async Task<bool> AddVideoAsync(Guid tvShowId, string videoUrl, string videoType, string? quality)
    {
        var show = await _tvShowRepository.GetByIdAsync(tvShowId);
        if (show == null) return false;

        var oldVideos = await _videoRepository.FindAsync(v => v.TvShowId == tvShowId && v.VideoType == videoType);

        foreach (var old in oldVideos)
        {
            var oldPublicId = ExtractCloudinaryPublicId(old.VideoUrl);
            if (oldPublicId != null)
            {
                try { await _cloudinaryService.DeleteFileAsync(oldPublicId); } catch { }
            }
            _videoRepository.Remove(old);
        }

        await _videoRepository.AddAsync(new TvShowVideo
        {
            TvShowId = tvShowId,
            VideoUrl = videoUrl,
            VideoType = videoType,
            Quality = quality
        });
        await _videoRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(TVSHOW_CACHE_KEY, tvShowId));
        return true;
    }

    public async Task<bool> SetTrailerYoutubeAsync(Guid tvShowId, string youtubeUrl)
        => await AddVideoAsync(tvShowId, youtubeUrl, "trailer", quality: null);

    public async Task<bool> DeleteVideoAsync(Guid videoId)
    {
        var video = await _videoRepository.GetByIdAsync(videoId);
        if (video == null) return false;

        var publicId = ExtractCloudinaryPublicId(video.VideoUrl);
        if (publicId != null)
        {
            try { await _cloudinaryService.DeleteFileAsync(publicId); } catch { }
        }

        _videoRepository.Remove(video);
        await _videoRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(TVSHOW_CACHE_KEY, video.TvShowId));
        return true;
    }

    public async Task<(bool found, string? oldUrl)> SetEpisodeVideoAsync(Guid episodeId, string videoUrl)
    {
        var episode = await _episodeRepository.GetByIdAsync(episodeId);
        if (episode == null) return (false, null);

        var oldUrl = episode.VideoUrl;
        episode.VideoUrl = videoUrl;
        _episodeRepository.Update(episode);
        await _episodeRepository.SaveChangesAsync();

        var season = await _seasonRepository.GetByIdAsync(episode.SeasonId);
        if (season != null)
            await _cacheService.RemoveAsync(string.Format(SEASON_CACHE_KEY, season.TvShowId, season.SeasonNumber));

        return (true, oldUrl);
    }

    public async Task<(bool found, string? oldUrl)> RemoveEpisodeVideoAsync(Guid episodeId)
    {
        var episode = await _episodeRepository.GetByIdAsync(episodeId);
        if (episode == null) return (false, null);

        var oldUrl = episode.VideoUrl;
        episode.VideoUrl = null;
        _episodeRepository.Update(episode);
        await _episodeRepository.SaveChangesAsync();

        var season = await _seasonRepository.GetByIdAsync(episode.SeasonId);
        if (season != null)
            await _cacheService.RemoveAsync(string.Format(SEASON_CACHE_KEY, season.TvShowId, season.SeasonNumber));

        return (true, oldUrl);
    }

    // ─── Favorites ────────────────────────────────────────────────────────────

    public async Task<bool> AddFavoriteAsync(Guid userId, Guid tvShowId)
    {
        var existing = await _favoriteRepository.FindOneAsync(f => f.UserId == userId && f.TvShowId == tvShowId);
        if (existing != null) return false;

        await _favoriteRepository.AddAsync(new TvShowFavorite { UserId = userId, TvShowId = tvShowId });
        await _favoriteRepository.SaveChangesAsync();

        // ĐÃ XOÁ: Không gửi thông báo "Đã thêm vào yêu thích" để tránh spam người dùng
        return true;
    }

    public async Task<bool> RemoveFavoriteAsync(Guid userId, Guid tvShowId)
    {
        var favorite = await _favoriteRepository.FindOneAsync(f => f.UserId == userId && f.TvShowId == tvShowId);
        if (favorite == null) return false;

        _favoriteRepository.Remove(favorite);
        await _favoriteRepository.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<TvShowFavoriteDTO>> GetFavoritesAsync(Guid userId)
    {
        var favorites = await _favoriteRepository.FindAsync(f => f.UserId == userId);
        var tvShowIds = favorites.Select(f => f.TvShowId).Distinct().ToList();

        var shows = await _tvShowRepository.GetPagedAsync(new FilterTvShowsDTO
        {
            Ids = tvShowIds,
            PageSize = tvShowIds.Count > 0 ? tvShowIds.Count : 1
        });

        var showMap = shows.Items.ToDictionary(s => s.Id);

        return favorites
            .Where(f => showMap.ContainsKey(f.TvShowId))
            .Select(f =>
            {
                var s = showMap[f.TvShowId];
                return new TvShowFavoriteDTO
                {
                    Id = f.Id,
                    TvShowId = s.Id,
                    TvShowTitle = s.Title,
                    PosterUrl = s.PosterUrl,
                    Rating = s.ImdbRating,
                    AddedAt = f.AddedAt
                };
            })
            .OrderByDescending(f => f.AddedAt)
            .ToList();
    }

    // ─── Watch History ────────────────────────────────────────────────────────

    public async Task UpdateWatchProgressAsync(
        Guid userId,
        Guid tvShowId,
        Guid? episodeId,
        int progressSeconds,
        bool isCompleted)
    {
        TvShowWatchHistory? existing;

        if (episodeId.HasValue)
        {
            existing = await _watchHistoryRepository.FindOneAsync(
                h => h.UserId == userId && h.TvShowId == tvShowId && h.EpisodeId == episodeId);
        }
        else
        {
            existing = await _watchHistoryRepository.FindOneAsync(
                h => h.UserId == userId && h.TvShowId == tvShowId && h.EpisodeId == null);
        }

        if (existing != null)
        {
            existing.ProgressSeconds = progressSeconds;
            existing.IsCompleted = isCompleted;
            existing.WatchedAt = DateTime.UtcNow;
            _watchHistoryRepository.Update(existing);
        }
        else
        {
            await _watchHistoryRepository.AddAsync(new TvShowWatchHistory
            {
                UserId = userId,
                TvShowId = tvShowId,
                EpisodeId = episodeId,
                ProgressSeconds = progressSeconds,
                IsCompleted = isCompleted
            });
        }

        await _watchHistoryRepository.SaveChangesAsync();
    }

    public async Task<IEnumerable<TvShowWatchHistoryDTO>> GetWatchHistoryAsync(Guid userId)
    {
        var histories = await _watchHistoryRepository.FindAsync(h => h.UserId == userId);
        var tvShowIds = histories.Select(h => h.TvShowId).Distinct().ToList();

        var (showList, _) = await _tvShowRepository.GetPagedAsync(new FilterTvShowsDTO
        {
            Ids = tvShowIds,
            PageSize = tvShowIds.Count > 0 ? tvShowIds.Count : 1
        });

        var showMap = showList.ToDictionary(t => t.Id);

        var episodeIds = histories
            .Where(h => h.EpisodeId.HasValue)
            .Select(h => h.EpisodeId!.Value)
            .Distinct()
            .ToList();

        var episodeMeta = new Dictionary<Guid, (int SeasonNumber, int EpisodeNumber, string? Title, int? Runtime)>();

        if (episodeIds.Any())
        {
            var episodes = await _episodeRepository.FindAsync(e => episodeIds.Contains(e.Id));
            var seasonIds = episodes.Select(e => e.SeasonId).Distinct().ToList();
            var seasons = await _seasonRepository.FindAsync(s => seasonIds.Contains(s.Id));
            var seasonMap = seasons.ToDictionary(s => s.Id, s => s.SeasonNumber);

            foreach (var e in episodes)
            {
                var seasonNumber = seasonMap.TryGetValue(e.SeasonId, out var sn) ? sn : 0;
                episodeMeta[e.Id] = (seasonNumber, e.EpisodeNumber, e.Title, e.Runtime);
            }
        }

        return histories
            .Where(h => showMap.ContainsKey(h.TvShowId))
            .Select(h =>
            {
                var t = showMap[h.TvShowId];
                (int SeasonNumber, int EpisodeNumber, string? Title, int? Runtime)? ep =
                    h.EpisodeId.HasValue && episodeMeta.TryGetValue(h.EpisodeId.Value, out var meta)
                        ? meta : null;

                return new TvShowWatchHistoryDTO
                {
                    Id = h.Id,
                    TvShowId = h.TvShowId,
                    TvShowTitle = t.Title,
                    PosterUrl = t.PosterUrl,
                    EpisodeId = h.EpisodeId,
                    SeasonNumber = ep?.SeasonNumber,
                    EpisodeNumber = ep?.EpisodeNumber,
                    EpisodeName = ep?.Title,
                    EpisodeRuntime = ep?.Runtime,
                    WatchedAt = h.WatchedAt,
                    ProgressSeconds = h.ProgressSeconds,
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
        foreach (var record in userRecords) _watchHistoryRepository.Remove(record);
        await _watchHistoryRepository.SaveChangesAsync();
    }

    public async Task<int?> GetTmdbIdAsync(Guid id)
    {
        var show = await _tvShowRepository.GetByIdAsync(id);
        return show?.TmdbId;
    }

    public async Task<SyncResultDTO> SyncNewEpisodesAsync(Guid id, TmdbFullTvShowDTO full)
    {
        var show = await _tvShowRepository.GetByIdAsync(id);
        if (show == null)
            return new SyncResultDTO { Success = false, Message = "Show không tồn tại trong DB" };

        int newEpisodes = 0;
        int newSeasons = 0;

        foreach (var tmdbSeason in full.SeasonDetails.Values.Where(s => s.SeasonNumber > 0))
        {
            var season = await _seasonRepository.FindOneAsync(s => s.TvShowId == id && s.SeasonNumber == tmdbSeason.SeasonNumber);

            if (season == null)
            {
                season = new Season
                {
                    TvShowId = id,
                    SeasonNumber = tmdbSeason.SeasonNumber,
                    Name = tmdbSeason.Name,
                    Overview = tmdbSeason.Overview,
                    PosterUrl = tmdbSeason.PosterUrl,
                    AirDate = DateTime.TryParse(tmdbSeason.AirDate ?? string.Empty, out var ad) ? DateTime.SpecifyKind(ad, DateTimeKind.Utc) : null,
                    EpisodeCount = tmdbSeason.Episodes.Count
                };
                await _seasonRepository.AddAsync(season);
                await _seasonRepository.SaveChangesAsync();
                newSeasons++;
            }

            var existingEps = await _episodeRepository.FindAsync(e => e.SeasonId == season.Id);
            var existingNums = existingEps.Select(e => e.EpisodeNumber).ToHashSet();

            var episodesToInsert = tmdbSeason.Episodes.Where(e => !existingNums.Contains(e.EpisodeNumber)).ToList();

            foreach (var ep in episodesToInsert)
            {
                await _episodeRepository.AddAsync(new Episode
                {
                    SeasonId = season.Id,
                    EpisodeNumber = ep.EpisodeNumber,
                    Title = ep.Title,
                    Overview = string.IsNullOrEmpty(ep.Overview) ? null : ep.Overview,
                    StillUrl = ep.StillUrl,
                    Runtime = ep.Runtime,
                    Rating = ep.VoteAverage > 0 ? (decimal)ep.VoteAverage : null,
                    AirDate = DateTime.TryParse(ep.AirDate ?? string.Empty, out var ea) ? DateTime.SpecifyKind(ea, DateTimeKind.Utc) : null
                });
                newEpisodes++;
            }

            if (episodesToInsert.Count > 0)
            {
                season.EpisodeCount = existingNums.Count + episodesToInsert.Count;
                _seasonRepository.Update(season);
                await _episodeRepository.SaveChangesAsync();
                await _seasonRepository.SaveChangesAsync();
            }
        }

        var syncedSeasonNumbers = full.SeasonDetails.Values.Where(s => s.SeasonNumber > 0).Select(s => s.SeasonNumber).ToList();
        var genreRowsAlways = await _tvShowGenreRepository.FindAsync(g => g.TvShowId == id);
        var genreIdsAlways = genreRowsAlways.Select(g => g.GenreId).ToList();

        await InvalidateTvShowCachesAsync(id, genreIdsAlways, syncedSeasonNumbers);

        if (newEpisodes > 0 || newSeasons > 0)
        {
            show.NumberOfSeasons = full.Detail.NumberOfSeasons;
            show.NumberOfEpisodes = full.Detail.NumberOfEpisodes;
            show.Status = full.Detail.Status;
            show.LastAirDate = DateTime.TryParse(full.Detail.LastAirDate ?? string.Empty, out var lad) ? DateTime.SpecifyKind(lad, DateTimeKind.Utc) : null;

            _tvShowRepository.Update(show);
            await _tvShowRepository.SaveChangesAsync();
        }

        return new SyncResultDTO
        {
            Success = true,
            NewEpisodes = newEpisodes,
            NewSeasons = newSeasons,
            Message = $"Sync thành công: Cập nhật thêm {newSeasons} season mới, {newEpisodes} tập mới",
            InvalidatedSeasons = syncedSeasonNumbers
        };
    }

    private async Task ReplaceGenresAsync(Guid showId, List<Guid> genreIds)
    {
        var old = (await _tvShowGenreRepository.FindAsync(x => x.TvShowId == showId)).ToList();
        foreach (var o in old) _tvShowGenreRepository.Remove(o);
        await _tvShowGenreRepository.SaveChangesAsync();

        foreach (var genreId in genreIds.Distinct())
        {
            await _tvShowGenreRepository.AddAsync(new TvShowGenre { TvShowId = showId, GenreId = genreId });
        }
        await _tvShowGenreRepository.SaveChangesAsync();
    }

    private async Task ReplaceImagesByTypeAsync(Guid showId, string imageType, List<ImportImageDTO> images)
    {
        var old = (await _imageRepository.FindAsync(i => i.TvShowId == showId && i.ImageType == imageType)).ToList();
        foreach (var o in old) _imageRepository.Remove(o);
        await _imageRepository.SaveChangesAsync();

        foreach (var img in images)
        {
            await _imageRepository.AddAsync(new TvShowImage { TvShowId = showId, Url = img.Url, ImageType = imageType });
        }
        await _imageRepository.SaveChangesAsync();
    }

    private async Task ReplaceCastAsync(Guid showId, List<ImportCastDTO> cast)
    {
        var oldCasts = (await _castRepository.FindAsync(c => c.TvShowId == showId)).ToList();
        var oldPersonIds = oldCasts.Select(c => c.PersonId).Distinct().ToList();

        foreach (var old in oldCasts) _castRepository.Remove(old);
        await _castRepository.SaveChangesAsync();

        for (int i = 0; i < cast.Count; i++)
        {
            var c = cast[i];
            var person = await UpsertPersonAsync(c.PersonId, c.TmdbPersonId, c.Name, c.ProfileUrl, c.Biography, c.Birthday, c.PlaceOfBirth);

            if (c.ProfileImages?.Count > 0) await SavePersonImagesAsync(person.Id, c.ProfileImages);

            await _castRepository.AddAsync(new TvShowCast
            {
                TvShowId = showId,
                PersonId = person.Id,
                Character = c.Character,
                Order = c.Order != 0 ? c.Order : i
            });
        }
        await _castRepository.SaveChangesAsync();
        await CleanupOrphanPersonsAsync(oldPersonIds);
    }

    private async Task ReplaceDirectorAsync(Guid showId, ImportDirectorDTO director)
    {
        var oldDirectors = (await _directorRepository.FindAsync(d => d.TvShowId == showId)).ToList();
        var oldPersonIds = oldDirectors.Select(d => d.PersonId).Distinct().ToList();

        foreach (var old in oldDirectors) _directorRepository.Remove(old);
        await _directorRepository.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(director.Name))
        {
            var person = await UpsertPersonAsync(director.PersonId, director.TmdbPersonId, director.Name, director.ProfileUrl, director.Biography, director.Birthday, director.PlaceOfBirth);

            if (director.ProfileImages?.Count > 0) await SavePersonImagesAsync(person.Id, director.ProfileImages);

            await _directorRepository.AddAsync(new TvShowDirector { TvShowId = showId, PersonId = person.Id });
            await _directorRepository.SaveChangesAsync();
        }

        await CleanupOrphanPersonsAsync(oldPersonIds);
    }

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

    private async Task SaveGenresAsync(Guid showId, List<Guid> genreIds)
    {
        foreach (var genreId in genreIds.Distinct())
        {
            var exists = await _tvShowGenreRepository.FindOneAsync(x => x.TvShowId == showId && x.GenreId == genreId);
            if (exists == null)
            {
                await _tvShowGenreRepository.AddAsync(new TvShowGenre { TvShowId = showId, GenreId = genreId });
            }
        }
        await _tvShowGenreRepository.SaveChangesAsync();
    }

    private async Task SaveCastAsync(Guid showId, List<ImportCastDTO> cast)
    {
        foreach (var c in cast)
        {
            var person = await UpsertPersonAsync(c.PersonId, c.TmdbPersonId, c.Name, c.ProfileUrl, c.Biography, c.Birthday, c.PlaceOfBirth);
            await SavePersonImagesAsync(person.Id, c.ProfileImages);

            var existing = await _castRepository.FindOneAsync(x => x.TvShowId == showId && x.PersonId == person.Id);
            if (existing != null) continue;

            await _castRepository.AddAsync(new TvShowCast { TvShowId = showId, PersonId = person.Id, Character = c.Character, Order = c.Order });
        }
        await _castRepository.SaveChangesAsync();
    }

    private async Task SaveDirectorAsync(Guid showId, ImportDirectorDTO dir)
    {
        var person = await UpsertPersonAsync(dir.PersonId, dir.TmdbPersonId, dir.Name, dir.ProfileUrl, dir.Biography, dir.Birthday, dir.PlaceOfBirth);
        await SavePersonImagesAsync(person.Id, dir.ProfileImages);

        var existing = await _directorRepository.FindOneAsync(x => x.TvShowId == showId && x.PersonId == person.Id);
        if (existing != null) return;

        await _directorRepository.AddAsync(new TvShowDirector { TvShowId = showId, PersonId = person.Id });
        await _directorRepository.SaveChangesAsync();
    }

    private async Task SaveImagesAsync(Guid showId, List<ImportImageDTO> images)
    {
        foreach (var img in images.Where(i => !string.IsNullOrEmpty(i.Url)))
        {
            await _imageRepository.AddAsync(new TvShowImage { TvShowId = showId, Url = img.Url, ImageType = img.ImageType });
        }
        await _imageRepository.SaveChangesAsync();
    }

    private async Task SaveTrailersAsync(Guid showId, List<ImportTrailerDTO> trailers)
    {
        foreach (var t in trailers.Where(t => !string.IsNullOrEmpty(t.YoutubeUrl)))
        {
            await _videoRepository.AddAsync(new TvShowVideo { TvShowId = showId, VideoUrl = t.YoutubeUrl, VideoType = "trailer" });
        }
        await _videoRepository.SaveChangesAsync();
    }

    private async Task SaveSeasonsAsync(Guid showId, List<CreateSeasonDTO> seasons)
    {
        foreach (var s in seasons.Where(s => s.SeasonNumber > 0))
        {
            var season = new Season
            {
                TvShowId = showId,
                SeasonNumber = s.SeasonNumber,
                Name = s.Name,
                Overview = s.Overview,
                PosterUrl = s.PosterUrl,
                AirDate = s.AirDate.HasValue ? DateTime.SpecifyKind(s.AirDate.Value, DateTimeKind.Utc) : null,
                EpisodeCount = s.Episodes.Count
            };

            await _seasonRepository.AddAsync(season);
            await _seasonRepository.SaveChangesAsync();

            foreach (var e in s.Episodes)
            {
                await _episodeRepository.AddAsync(new Episode
                {
                    SeasonId = season.Id,
                    EpisodeNumber = e.EpisodeNumber,
                    Title = e.Title,
                    Overview = e.Overview,
                    StillUrl = e.StillUrl,
                    Runtime = e.Runtime,
                    Rating = e.Rating,
                    AirDate = e.AirDate.HasValue ? DateTime.SpecifyKind(e.AirDate.Value, DateTimeKind.Utc) : null
                });
            }
            await _episodeRepository.SaveChangesAsync();
        }
    }

    private async Task<Person> UpsertPersonAsync(Guid? personId, int? tmdbPersonId, string name, string? profileUrl, string? biography, string? birthday, string? placeOfBirth)
    {
        Person? person = personId.HasValue ? await _personRepository.GetByIdAsync(personId.Value) : null;
        person ??= tmdbPersonId.HasValue ? await _personRepository.FindOneAsync(p => p.TmdbPersonId == tmdbPersonId) : null;
        person ??= await _personRepository.FindOneAsync(p => p.Name.ToLower() == name.Trim().ToLower());

        if (person == null)
        {
            person = new Person
            {
                TmdbPersonId = tmdbPersonId,
                Name = name,
                ProfileUrl = profileUrl,
                Biography = biography,
                Birthday = birthday,
                PlaceOfBirth = placeOfBirth
            };
            await _personRepository.AddAsync(person);
            await _personRepository.SaveChangesAsync();
        }
        else
        {
            bool changed = false;
            if (!person.TmdbPersonId.HasValue && tmdbPersonId.HasValue) { person.TmdbPersonId = tmdbPersonId; changed = true; }
            if (string.IsNullOrEmpty(person.Biography) && !string.IsNullOrEmpty(biography)) { person.Biography = biography; changed = true; }
            if (string.IsNullOrEmpty(person.Birthday) && !string.IsNullOrEmpty(birthday)) { person.Birthday = birthday; changed = true; }
            if (string.IsNullOrEmpty(person.PlaceOfBirth) && !string.IsNullOrEmpty(placeOfBirth)) { person.PlaceOfBirth = placeOfBirth; changed = true; }
            if (string.IsNullOrEmpty(person.ProfileUrl) && !string.IsNullOrEmpty(profileUrl)) { person.ProfileUrl = profileUrl; changed = true; }

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
            await _personImageRepository.AddAsync(new PersonImage { PersonId = personId, Url = url });
        }
        await _personImageRepository.SaveChangesAsync();
    }

    private async Task InvalidateTvShowCachesAsync(Guid showId, List<Guid> genreIds, IEnumerable<int>? seasonNumbers = null)
    {
        await _cacheService.RemoveAsync(string.Format(TVSHOW_CACHE_KEY, showId));

        var keysToRemove = genreIds
            .Select(gid => string.Format(GENRE_CACHE_KEY, gid))
            .Append(AI_CONTEXTS_KEY)
            .Append(AI_ALL_DTOS_KEY);

        if (seasonNumbers != null)
        {
            keysToRemove = keysToRemove.Concat(seasonNumbers.Select(n => string.Format(SEASON_CACHE_KEY, showId, n)));
        }

        await _cacheService.RemoveManyAsync(keysToRemove.ToArray());
    }

    private static TvShowSummaryDTO MapToSummaryDTO(TvShow t) => new()
    {
        Id = t.Id, Title = t.Title, Description = t.Description, FirstAirDate = t.FirstAirDate,
        PosterUrl = t.PosterUrl, BackdropUrl = t.BackdropUrl, Rating = t.ImdbRating,
        OriginCountry = t.OriginCountry, Status = t.Status, NumberOfSeasons = t.NumberOfSeasons,
        NumberOfEpisodes = t.NumberOfEpisodes,
        TrailerKey = t.TvShowVideos?.Where(v => v.VideoType == "trailer" && !string.IsNullOrEmpty(v.VideoUrl)).Select(v => ExtractYoutubeKey(v.VideoUrl)).FirstOrDefault(k => k != null),
        TrailerVideoUrl = t.TvShowVideos?.Where(v => v.VideoType == "trailer_upload" && !string.IsNullOrEmpty(v.VideoUrl)).Select(v => v.VideoUrl).FirstOrDefault(),
        IsPremium = t.IsPremium, IsUpcoming = t.FirstAirDate.HasValue && t.FirstAirDate.Value > DateTime.UtcNow,
        Genres = t.TvShowGenres?.Select(g => g.Genre?.Name ?? "").Where(n => n != "").ToList() ?? new()
    };

    private static TvShowDTO MapToDTO(TvShow t) => new()
    {
        Id = t.Id, Title = t.Title, Description = t.Description, FirstAirDate = t.FirstAirDate,
        LastAirDate = t.LastAirDate, PosterUrl = t.PosterUrl, BackdropUrl = t.BackdropUrl,
        EpisodeRuntime = t.EpisodeRuntime, Rating = t.ImdbRating, OriginCountry = t.OriginCountry,
        Status = t.Status, NumberOfSeasons = t.NumberOfSeasons, NumberOfEpisodes = t.NumberOfEpisodes,
        IsPremium = t.IsPremium, IsUpcoming = t.FirstAirDate.HasValue && t.FirstAirDate.Value > DateTime.UtcNow,
        Genres = t.TvShowGenres?.Select(g => g.Genre?.Name ?? "").Where(n => n != "").ToList() ?? new(),
        Videos = t.TvShowVideos?.Select(v => new TvShowVideoDTO { Id = v.Id, VideoUrl = v.VideoUrl, VideoType = v.VideoType, Duration = v.Duration, Quality = v.Quality }).ToList() ?? new(),
        TrailerKey = t.TvShowVideos?.Where(v => v.VideoType == "trailer" && !string.IsNullOrEmpty(v.VideoUrl)).Select(v => ExtractYoutubeKey(v.VideoUrl)).FirstOrDefault(k => k != null),
        TrailerVideoUrl = t.TvShowVideos?.Where(v => v.VideoType == "trailer_upload" && !string.IsNullOrEmpty(v.VideoUrl)).Select(v => v.VideoUrl).FirstOrDefault(),
        Cast = t.TvShowCasts?.OrderBy(c => c.Order).Where(c => c.Person != null).Take(6).Select(c => new TvShowCastDTO
        {
            Name = c.Person!.Name, Character = c.Character, Order = c.Order, ProfileUrl = c.Person.ProfileUrl,
            TmdbPersonId = c.Person.TmdbPersonId, Biography = c.Person.Biography, Birthday = c.Person.Birthday,
            PlaceOfBirth = c.Person.PlaceOfBirth, ProfileImages = c.Person.Images.OrderByDescending(i => i.CreatedAt).Select(i => i.Url).ToList()
        }).ToList() ?? new(),
        Director = t.TvShowDirectors?.Where(d => d.Person != null).Select(d => d.Person?.Name).FirstOrDefault(),
        DirectorDetail = t.TvShowDirectors?.Where(d => d.Person != null).Select(d => new PersonDetailDTO
        {
            Name = d.Person!.Name, ProfileUrl = d.Person.ProfileUrl, TmdbPersonId = d.Person.TmdbPersonId,
            Biography = d.Person.Biography, Birthday = d.Person.Birthday, PlaceOfBirth = d.Person.PlaceOfBirth,
            ProfileImages = d.Person.Images.OrderByDescending(i => i.CreatedAt).Select(i => i.Url).ToList()
        }).FirstOrDefault(),
        Images = t.TvShowImages?.Select(i => new TvShowImageDTO { Url = i.Url, ImageType = i.ImageType }).ToList() ?? new(),
        Seasons = t.Seasons?.OrderBy(s => s.SeasonNumber).Select(s => new SeasonDTO
        {
            Id = s.Id, SeasonNumber = s.SeasonNumber, Name = s.Name, Overview = s.Overview,
            PosterUrl = s.PosterUrl, AirDate = s.AirDate, EpisodeCount = s.EpisodeCount, Episodes = new()
        }).ToList() ?? new()
    };

    private static SeasonDTO MapSeasonToDTO(Season s, IEnumerable<Episode>? episodes = null) => new()
    {
        Id = s.Id, SeasonNumber = s.SeasonNumber, Name = s.Name, Overview = s.Overview,
        PosterUrl = s.PosterUrl, AirDate = s.AirDate, EpisodeCount = s.EpisodeCount,
        Episodes = (episodes ?? s.Episodes ?? Enumerable.Empty<Episode>()).OrderBy(e => e.EpisodeNumber).Select(MapEpisodeToDTO).ToList()
    };

    private static EpisodeDTO MapEpisodeToDTO(Episode e) => new()
    {
        Id = e.Id, EpisodeNumber = e.EpisodeNumber, Title = e.Title, Overview = e.Overview,
        StillUrl = e.StillUrl, Runtime = e.Runtime, Rating = e.Rating, AirDate = e.AirDate, VideoUrl = e.VideoUrl
    };

    private static string? ExtractYoutubeKey(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var v = System.Text.RegularExpressions.Regex.Match(url, @"[?&]v=([a-zA-Z0-9_-]{11})");
        if (v.Success) return v.Groups[1].Value;
        var s = System.Text.RegularExpressions.Regex.Match(url, @"youtu\.be/([a-zA-Z0-9_-]{11})");
        return s.Success ? s.Groups[1].Value : null;
    }

    private static string? ExtractCloudinaryPublicId(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.Contains("youtube.com") || url.Contains("youtu.be")) return null;
        if (!url.Contains("cloudinary.com")) return null;

        var match = System.Text.RegularExpressions.Regex.Match(url, @"/upload/(?:v\d+/)?(.+?)(?:\.[^./]+)?$");
        return match.Success ? match.Groups[1].Value : null;
    }
}