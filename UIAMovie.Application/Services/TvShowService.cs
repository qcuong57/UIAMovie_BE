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

    // ── Videos ────────────────────────────────────────────────────────────────
    Task<bool> AddVideoAsync(Guid tvShowId, string videoUrl, string videoType, string? quality);
    Task<bool> DeleteVideoAsync(Guid videoId);

    // ── Episode Video ─────────────────────────────────────────────────────────
    Task<(bool found, string? oldUrl)> SetEpisodeVideoAsync(Guid episodeId, string videoUrl);
    Task<(bool found, string? oldUrl)> RemoveEpisodeVideoAsync(Guid episodeId);

    // ── Favorites ──────────────────────────────────────────────────────────────
    Task<bool> AddFavoriteAsync(Guid userId, Guid tvShowId);
    Task<bool> RemoveFavoriteAsync(Guid userId, Guid tvShowId);
    Task<IEnumerable<TvShowFavoriteDTO>> GetFavoritesAsync(Guid userId);

    // ── Watch History ──────────────────────────────────────────────────────────
    Task UpdateWatchProgressAsync(Guid userId, Guid tvShowId, Guid? episodeId, int progressSeconds, bool isCompleted);
    Task<IEnumerable<TvShowWatchHistoryDTO>> GetWatchHistoryAsync(Guid userId);
    Task<bool> DeleteWatchHistoryAsync(Guid userId, Guid historyId);
    Task ClearWatchHistoryAsync(Guid userId);
}

public class TvShowService : ITvShowService
{
    private readonly ITvShowRepository               _tvShowRepository;
    private readonly IRepository<TvShowVideo>        _videoRepository;
    private readonly IRepository<TvShowImage>        _imageRepository;
    private readonly IRepository<TvShowGenre>        _tvShowGenreRepository;
    private readonly IRepository<TvShowCast>         _castRepository;
    private readonly IRepository<TvShowDirector>     _directorRepository;
    private readonly IRepository<Season>             _seasonRepository;
    private readonly IRepository<Episode>            _episodeRepository;
    private readonly IRepository<Person>             _personRepository;
    private readonly IRepository<PersonImage>        _personImageRepository;
    private readonly IRepository<TvShowFavorite>     _favoriteRepository;
    private readonly IRepository<TvShowWatchHistory> _watchHistoryRepository;
    private readonly ICacheService                   _cacheService;
    private readonly ICloudinaryService              _cloudinaryService;

    // ── Cache keys ────────────────────────────────────────────────────────────
    private const string TVSHOW_CACHE_KEY = "tvshow:{0}";
    private const string SEASON_CACHE_KEY = "tvshow:{0}:season:{1}";
    private const string GENRE_CACHE_KEY  = "tvshows:genre:{0}";
    private const string AI_CONTEXTS_KEY  = "ai:tvshow_contexts";
    private const string AI_ALL_DTOS_KEY  = "ai:all_tvshow_dtos";

    public TvShowService(
        ITvShowRepository                tvShowRepository,
        IRepository<TvShowVideo>         videoRepository,
        IRepository<TvShowImage>         imageRepository,
        IRepository<TvShowGenre>         tvShowGenreRepository,
        IRepository<TvShowCast>          castRepository,
        IRepository<TvShowDirector>      directorRepository,
        IRepository<Season>              seasonRepository,
        IRepository<Episode>             episodeRepository,
        IRepository<Person>              personRepository,
        IRepository<PersonImage>         personImageRepository,
        IRepository<TvShowFavorite>      favoriteRepository,
        IRepository<TvShowWatchHistory>  watchHistoryRepository,
        ICacheService                    cacheService,
        ICloudinaryService               cloudinaryService)
    {
        _tvShowRepository       = tvShowRepository;
        _videoRepository        = videoRepository;
        _imageRepository        = imageRepository;
        _tvShowGenreRepository  = tvShowGenreRepository;
        _castRepository         = castRepository;
        _directorRepository     = directorRepository;
        _seasonRepository       = seasonRepository;
        _episodeRepository      = episodeRepository;
        _personRepository       = personRepository;
        _personImageRepository  = personImageRepository;
        _favoriteRepository     = favoriteRepository;
        _watchHistoryRepository = watchHistoryRepository;
        _cacheService           = cacheService;
        _cloudinaryService      = cloudinaryService;
    }

    // ─── Query ────────────────────────────────────────────────────────────────

    public async Task<PaginatedDTO<TvShowSummaryDTO>> GetTvShowsAsync(FilterTvShowsDTO filter)
    {
        var (shows, totalCount) = await _tvShowRepository.GetPagedAsync(filter);
        var items = shows.Select(MapToSummaryDTO).ToList();

        return new PaginatedDTO<TvShowSummaryDTO>
        {
            Items      = items,
            TotalCount = totalCount,
            PageNumber = filter.Page,
            PageSize   = filter.PageSize
        };
    }

    public async Task<TvShowDTO?> GetTvShowByIdAsync(Guid id)
    {
        var cacheKey = string.Format(TVSHOW_CACHE_KEY, id);
        var cached   = await _cacheService.GetAsync<TvShowDTO>(cacheKey);
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
        var cacheKey      = $"tvshow:search:{normalizedKey}";

        var cached = await _cacheService.GetAsync<List<TvShowSummaryDTO>>(cacheKey);
        if (cached != null) return cached;

        var shows   = await _tvShowRepository.SearchByTitleAsync(query);
        var results = shows.Select(MapToSummaryDTO).ToList();

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromMinutes(10));
        return results;
    }

    public async Task<IEnumerable<TvShowSummaryDTO>> GetTvShowsByGenreAsync(Guid genreId)
    {
        var cacheKey = string.Format(GENRE_CACHE_KEY, genreId);
        var cached   = await _cacheService.GetAsync<List<TvShowSummaryDTO>>(cacheKey);
        if (cached != null) return cached;

        var shows   = await _tvShowRepository.GetByGenreAsync(genreId);
        var results = shows.Select(MapToSummaryDTO).ToList();

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromMinutes(15));
        return results;
    }

    public async Task<IEnumerable<string>> GetAvailableCountriesAsync()
        => await _tvShowRepository.GetAvailableCountriesAsync();

    public async Task<IEnumerable<TvShowDTO>> SearchTvShowsByActorAsync(string actorName)
    {
        var normalizedKey = actorName.ToLower().Trim();
        var cacheKey      = $"tvshow:search:actor:{normalizedKey}";

        var cached = await _cacheService.GetAsync<List<TvShowDTO>>(cacheKey);
        if (cached != null) return cached;

        var shows   = await _tvShowRepository.SearchByActorNameAsync(actorName);
        var results = shows.Select(MapToDTO).ToList();

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromMinutes(10));
        return results;
    }

    // ─── Season / Episode ─────────────────────────────────────────────────────

    public async Task<SeasonDTO?> GetSeasonAsync(Guid tvShowId, int seasonNumber)
    {
        var cacheKey = string.Format(SEASON_CACHE_KEY, tvShowId, seasonNumber);
        var cached   = await _cacheService.GetAsync<SeasonDTO>(cacheKey);
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

        var season = await _seasonRepository.FindOneAsync(
            s => s.TvShowId == tvShowId && s.SeasonNumber == seasonNumber);
        if (season == null) return null;

        var episode = await _episodeRepository.FindOneAsync(
            e => e.SeasonId == season.Id && e.EpisodeNumber == episodeNumber);

        return episode == null ? null : MapEpisodeToDTO(episode);
    }

    // ─── Create ───────────────────────────────────────────────────────────────

    public async Task<Guid> CreateTvShowAsync(CreateTvShowDTO dto)
    {
        var show = new TvShow
        {
            Title            = dto.Title,
            Description      = string.IsNullOrEmpty(dto.Description) ? dto.Title : dto.Description,
            FirstAirDate     = dto.FirstAirDate.HasValue
                                   ? DateTime.SpecifyKind(dto.FirstAirDate.Value, DateTimeKind.Utc)
                                   : null,
            LastAirDate      = dto.LastAirDate.HasValue
                                   ? DateTime.SpecifyKind(dto.LastAirDate.Value, DateTimeKind.Utc)
                                   : null,
            PosterUrl        = dto.PosterUrl,
            BackdropUrl      = dto.BackdropUrl,
            EpisodeRuntime   = dto.EpisodeRuntime,
            ImdbRating       = dto.ImdbRating,
            TmdbId           = dto.TmdbId,
            OriginCountry    = dto.OriginCountry,
            Status           = dto.Status,
            NumberOfSeasons  = dto.NumberOfSeasons,
            NumberOfEpisodes = dto.NumberOfEpisodes,
            IsPremium        = dto.IsPremium,
            IsPublished      = true
        };

        await _tvShowRepository.AddAsync(show);
        await _tvShowRepository.SaveChangesAsync();

        if (dto.GenreIds.Any())   await SaveGenresAsync(show.Id, dto.GenreIds);
        if (dto.Cast.Any())       await SaveCastAsync(show.Id, dto.Cast);
        if (dto.Director != null) await SaveDirectorAsync(show.Id, dto.Director);
        if (dto.Images.Any())     await SaveImagesAsync(show.Id, dto.Images);
        if (dto.Trailers.Any())   await SaveTrailersAsync(show.Id, dto.Trailers);
        if (dto.Seasons.Any())    await SaveSeasonsAsync(show.Id, dto.Seasons);

        await InvalidateTvShowCachesAsync(show.Id, dto.GenreIds);
        return show.Id;
    }

    // ─── Update / Delete ──────────────────────────────────────────────────────

    public async Task<bool> UpdateTvShowAsync(Guid id, UpdateTvShowDTO dto)
    {
        var show = await _tvShowRepository.GetByIdAsync(id);
        if (show == null) return false;

        show.Title       = dto.Title       ?? show.Title;
        show.Description = dto.Description ?? show.Description;
        show.ImdbRating  = dto.ImdbRating  ?? show.ImdbRating;
        show.Status      = dto.Status      ?? show.Status;
        if (dto.IsPremium.HasValue) show.IsPremium = dto.IsPremium.Value;

        _tvShowRepository.Update(show);
        await _tvShowRepository.SaveChangesAsync();

        var genreIds    = await _tvShowGenreRepository.FindAsync(g => g.TvShowId == id);
        var genreIdList = genreIds.Select(g => g.GenreId).ToList();

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

        var genreIds    = await _tvShowGenreRepository.FindAsync(g => g.TvShowId == id);
        var genreIdList = genreIds.Select(g => g.GenreId).ToList();
        await InvalidateTvShowCachesAsync(id, genreIdList);
        return true;
    }

    public async Task<bool> DeleteTvShowAsync(Guid id)
    {
        var show = await _tvShowRepository.GetByIdAsync(id);
        if (show == null) return false;

        var genreRows = await _tvShowGenreRepository.FindAsync(g => g.TvShowId == id);
        var genreIds  = genreRows.Select(g => g.GenreId).ToList();

        _tvShowRepository.Remove(show);
        await _tvShowRepository.SaveChangesAsync();

        await InvalidateTvShowCachesAsync(id, genreIds);
        return true;
    }

    // ─── Videos ───────────────────────────────────────────────────────────────

    public async Task<bool> AddVideoAsync(Guid tvShowId, string videoUrl, string videoType, string? quality)
    {
        var show = await _tvShowRepository.GetByIdAsync(tvShowId);
        if (show == null) return false;

        await _videoRepository.AddAsync(new TvShowVideo
        {
            TvShowId  = tvShowId,
            VideoUrl  = videoUrl,
            VideoType = videoType,
            Quality   = quality
        });
        await _videoRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(TVSHOW_CACHE_KEY, tvShowId));
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

        await _cacheService.RemoveAsync(string.Format(TVSHOW_CACHE_KEY, video.TvShowId));
        return true;
    }

    // ─── Episode Video ────────────────────────────────────────────────────────

    /// <summary>
    /// Gán VideoUrl cho episode. Trả về (found, oldUrl) để controller
    /// có thể xóa file cũ trên Cloudinary nếu cần.
    /// </summary>
    public async Task<(bool found, string? oldUrl)> SetEpisodeVideoAsync(Guid episodeId, string videoUrl)
    {
        var episode = await _episodeRepository.GetByIdAsync(episodeId);
        if (episode == null) return (false, null);

        var oldUrl = episode.VideoUrl;
        episode.VideoUrl = videoUrl;
        _episodeRepository.Update(episode);
        await _episodeRepository.SaveChangesAsync();

        // Invalidate cache season chứa episode này
        var season = await _seasonRepository.GetByIdAsync(episode.SeasonId);
        if (season != null)
            await _cacheService.RemoveAsync(string.Format(SEASON_CACHE_KEY, season.TvShowId, season.SeasonNumber));

        return (true, oldUrl);
    }

    /// <summary>
    /// Xóa VideoUrl của episode. Trả về (found, oldUrl) để controller
    /// xóa file trên Cloudinary.
    /// </summary>
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
        var existing = await _favoriteRepository.FindOneAsync(
            f => f.UserId == userId && f.TvShowId == tvShowId);
        if (existing != null) return false;

        await _favoriteRepository.AddAsync(new TvShowFavorite { UserId = userId, TvShowId = tvShowId });
        await _favoriteRepository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveFavoriteAsync(Guid userId, Guid tvShowId)
    {
        var favorite = await _favoriteRepository.FindOneAsync(
            f => f.UserId == userId && f.TvShowId == tvShowId);
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
            Ids      = tvShowIds,
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
                    Id          = f.Id,
                    TvShowId    = s.Id,
                    TvShowTitle = s.Title,
                    PosterUrl   = s.PosterUrl,
                    Rating      = s.ImdbRating,
                    AddedAt     = f.AddedAt
                };
            })
            .OrderByDescending(f => f.AddedAt)
            .ToList();
    }

    // ─── Watch History ────────────────────────────────────────────────────────

    /// <summary>
    /// Upsert tiến độ xem — nếu đã có record (userId + tvShowId + episodeId)
    /// thì cập nhật, chưa có thì tạo mới.
    ///
    /// episodeId = null → track ở level show.
    /// episodeId = có giá trị → track từng episode cụ thể.
    /// </summary>
    public async Task UpdateWatchProgressAsync(
        Guid  userId,
        Guid  tvShowId,
        Guid? episodeId,
        int   progressSeconds,
        bool  isCompleted)
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
            existing.IsCompleted     = isCompleted;
            existing.WatchedAt       = DateTime.UtcNow;
            _watchHistoryRepository.Update(existing);
        }
        else
        {
            await _watchHistoryRepository.AddAsync(new TvShowWatchHistory
            {
                UserId          = userId,
                TvShowId        = tvShowId,
                EpisodeId       = episodeId,
                ProgressSeconds = progressSeconds,
                IsCompleted     = isCompleted
            });
        }

        await _watchHistoryRepository.SaveChangesAsync();
    }

    public async Task<IEnumerable<TvShowWatchHistoryDTO>> GetWatchHistoryAsync(Guid userId)
    {
        var histories = await _watchHistoryRepository.FindAsync(h => h.UserId == userId);
        var tvShowIds = histories.Select(h => h.TvShowId).Distinct().ToList();

        // FIX: GetPagedAsync trả về tuple (IEnumerable<TvShow>, int)
        // không phải PaginatedDTO — phải destructure đúng cách
        var (showList, _) = await _tvShowRepository.GetPagedAsync(new FilterTvShowsDTO
        {
            Ids      = tvShowIds,
            PageSize = tvShowIds.Count > 0 ? tvShowIds.Count : 1
        });

        var showMap = showList.ToDictionary(t => t.Id);

        // Load episode + season metadata cho tất cả episodeId có trong history
        var episodeIds = histories
            .Where(h => h.EpisodeId.HasValue)
            .Select(h => h.EpisodeId!.Value)
            .Distinct()
            .ToList();

        // episodeId → (SeasonNumber, EpisodeNumber, Title, Runtime)
        var episodeMeta = new Dictionary<Guid, (int SeasonNumber, int EpisodeNumber, string? Title, int? Runtime)>();

        if (episodeIds.Any())
        {
            var episodes  = await _episodeRepository.FindAsync(e => episodeIds.Contains(e.Id));
            var seasonIds = episodes.Select(e => e.SeasonId).Distinct().ToList();
            var seasons   = await _seasonRepository.FindAsync(s => seasonIds.Contains(s.Id));
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
                    Id              = h.Id,
                    TvShowId        = h.TvShowId,
                    TvShowTitle     = t.Title,
                    PosterUrl       = t.PosterUrl,
                    EpisodeId       = h.EpisodeId,
                    SeasonNumber    = ep?.SeasonNumber,
                    EpisodeNumber   = ep?.EpisodeNumber,
                    EpisodeName     = ep?.Title,
                    EpisodeRuntime  = ep?.Runtime,
                    WatchedAt       = h.WatchedAt,
                    ProgressSeconds = h.ProgressSeconds,
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

    // ─── Sync ─────────────────────────────────────────────────────────────────

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
        int newSeasons  = 0;

        foreach (var tmdbSeason in full.SeasonDetails.Values.Where(s => s.SeasonNumber > 0))
        {
            var season = await _seasonRepository.FindOneAsync(
                s => s.TvShowId == id && s.SeasonNumber == tmdbSeason.SeasonNumber);

            if (season == null)
            {
                season = new Season
                {
                    TvShowId     = id,
                    SeasonNumber = tmdbSeason.SeasonNumber,
                    Name         = tmdbSeason.Name,
                    Overview     = tmdbSeason.Overview,
                    PosterUrl    = tmdbSeason.PosterUrl,
                    AirDate      = DateTime.TryParse(tmdbSeason.AirDate ?? string.Empty, out var ad)
                                       ? DateTime.SpecifyKind(ad, DateTimeKind.Utc) : null,
                    EpisodeCount = tmdbSeason.Episodes.Count
                };
                await _seasonRepository.AddAsync(season);
                await _seasonRepository.SaveChangesAsync();
                newSeasons++;
            }

            var existingEps  = await _episodeRepository.FindAsync(e => e.SeasonId == season.Id);
            var existingNums = existingEps.Select(e => e.EpisodeNumber).ToHashSet();

            var episodesToInsert = tmdbSeason.Episodes
                .Where(e => !existingNums.Contains(e.EpisodeNumber))
                .ToList();

            foreach (var ep in episodesToInsert)
            {
                await _episodeRepository.AddAsync(new Episode
                {
                    SeasonId      = season.Id,
                    EpisodeNumber = ep.EpisodeNumber,
                    Title         = ep.Title,
                    Overview      = string.IsNullOrEmpty(ep.Overview) ? null : ep.Overview,
                    StillUrl      = ep.StillUrl,
                    Runtime       = ep.Runtime,
                    Rating        = ep.VoteAverage > 0 ? (decimal)ep.VoteAverage : null,
                    AirDate       = DateTime.TryParse(ep.AirDate ?? string.Empty, out var ea)
                                        ? DateTime.SpecifyKind(ea, DateTimeKind.Utc) : null
                });
                newEpisodes++;
            }

            if (episodesToInsert.Count > 0)
            {
                season.EpisodeCount = existingNums.Count + episodesToInsert.Count;
                _seasonRepository.Update(season);

                // Bug 1 fix: flush episodes AND the season.EpisodeCount update together
                // so neither is lost when repositories do not share the same DbContext unit.
                await _episodeRepository.SaveChangesAsync();
                await _seasonRepository.SaveChangesAsync();
            }
        }

        // Bug 3 fix: always invalidate show cache when admin explicitly triggers sync,
        // even when no new content was found (metadata / status may still have changed).
        var syncedSeasonNumbers = full.SeasonDetails.Values
            .Where(s => s.SeasonNumber > 0)
            .Select(s => s.SeasonNumber)
            .ToList();
        var genreRowsAlways = await _tvShowGenreRepository.FindAsync(g => g.TvShowId == id);
        var genreIdsAlways  = genreRowsAlways.Select(g => g.GenreId).ToList();
        // Bug 2 fix: season numbers forwarded here so their cache keys are evicted too.
        await InvalidateTvShowCachesAsync(id, genreIdsAlways, syncedSeasonNumbers);

        if (newEpisodes > 0 || newSeasons > 0)
        {

            show.NumberOfSeasons  = full.Detail.NumberOfSeasons;
            show.NumberOfEpisodes = full.Detail.NumberOfEpisodes;
            show.Status           = full.Detail.Status;
            show.LastAirDate      = DateTime.TryParse(full.Detail.LastAirDate ?? string.Empty, out var lad)
                                        ? DateTime.SpecifyKind(lad, DateTimeKind.Utc) : null;

            _tvShowRepository.Update(show);
            await _tvShowRepository.SaveChangesAsync();
        }

        return new SyncResultDTO
        {
            Success            = true,
            NewEpisodes        = newEpisodes,
            NewSeasons         = newSeasons,
            Message            = $"Sync thành công: Cập nhật thêm {newSeasons} season mới, {newEpisodes} tập mới",
            // Bug 4 fix: tell the frontend exactly which seasons were cache-busted.
            InvalidatedSeasons = syncedSeasonNumbers
        };
    }

    // ─── Save helpers ─────────────────────────────────────────────────────────

    private async Task SaveGenresAsync(Guid showId, List<Guid> genreIds)
    {
        foreach (var genreId in genreIds)
        {
            await _tvShowGenreRepository.AddAsync(new TvShowGenre
            {
                TvShowId = showId,
                GenreId  = genreId
            });
        }
        await _tvShowGenreRepository.SaveChangesAsync();
    }

    private async Task SaveCastAsync(Guid showId, List<ImportCastDTO> cast)
    {
        foreach (var c in cast)
        {
            var person = await UpsertPersonAsync(
                c.TmdbPersonId, c.Name, c.ProfileUrl,
                c.Biography, c.Birthday, c.PlaceOfBirth);

            await SavePersonImagesAsync(person.Id, c.ProfileImages);

            var existing = await _castRepository.FindOneAsync(
                x => x.TvShowId == showId && x.PersonId == person.Id);
            if (existing != null) continue;

            await _castRepository.AddAsync(new TvShowCast
            {
                TvShowId  = showId,
                PersonId  = person.Id,
                Character = c.Character,
                Order     = c.Order
            });
        }
        await _castRepository.SaveChangesAsync();
    }

    private async Task SaveDirectorAsync(Guid showId, ImportDirectorDTO dir)
    {
        var person = await UpsertPersonAsync(
            dir.TmdbPersonId, dir.Name, dir.ProfileUrl,
            dir.Biography, dir.Birthday, dir.PlaceOfBirth);

        await SavePersonImagesAsync(person.Id, dir.ProfileImages);

        var existing = await _directorRepository.FindOneAsync(
            x => x.TvShowId == showId && x.PersonId == person.Id);
        if (existing != null) return;

        await _directorRepository.AddAsync(new TvShowDirector
        {
            TvShowId = showId,
            PersonId = person.Id
        });
        await _directorRepository.SaveChangesAsync();
    }

    private async Task SaveImagesAsync(Guid showId, List<ImportImageDTO> images)
    {
        foreach (var img in images.Where(i => !string.IsNullOrEmpty(i.Url)))
        {
            await _imageRepository.AddAsync(new TvShowImage
            {
                TvShowId  = showId,
                Url       = img.Url,
                ImageType = img.ImageType
            });
        }
        await _imageRepository.SaveChangesAsync();
    }

    private async Task SaveTrailersAsync(Guid showId, List<ImportTrailerDTO> trailers)
    {
        foreach (var t in trailers.Where(t => !string.IsNullOrEmpty(t.YoutubeUrl)))
        {
            await _videoRepository.AddAsync(new TvShowVideo
            {
                TvShowId  = showId,
                VideoUrl  = t.YoutubeUrl,
                VideoType = "trailer"
            });
        }
        await _videoRepository.SaveChangesAsync();
    }

    private async Task SaveSeasonsAsync(Guid showId, List<CreateSeasonDTO> seasons)
    {
        foreach (var s in seasons.Where(s => s.SeasonNumber > 0))
        {
            var season = new Season
            {
                TvShowId     = showId,
                SeasonNumber = s.SeasonNumber,
                Name         = s.Name,
                Overview     = s.Overview,
                PosterUrl    = s.PosterUrl,
                AirDate      = s.AirDate.HasValue
                                   ? DateTime.SpecifyKind(s.AirDate.Value, DateTimeKind.Utc)
                                   : null,
                EpisodeCount = s.Episodes.Count
            };

            await _seasonRepository.AddAsync(season);
            await _seasonRepository.SaveChangesAsync();

            foreach (var e in s.Episodes)
            {
                await _episodeRepository.AddAsync(new Episode
                {
                    SeasonId      = season.Id,
                    EpisodeNumber = e.EpisodeNumber,
                    Title         = e.Title,
                    Overview      = e.Overview,
                    StillUrl      = e.StillUrl,
                    Runtime       = e.Runtime,
                    Rating        = e.Rating,
                    AirDate       = e.AirDate.HasValue
                                        ? DateTime.SpecifyKind(e.AirDate.Value, DateTimeKind.Utc)
                                        : null
                });
            }
            await _episodeRepository.SaveChangesAsync();
        }
    }

    // ─── Person helpers ───────────────────────────────────────────────────────

    private async Task<Person> UpsertPersonAsync(
        int tmdbPersonId, string name, string? profileUrl,
        string? biography, string? birthday, string? placeOfBirth)
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
            if (string.IsNullOrEmpty(person.Biography)    && !string.IsNullOrEmpty(biography))
            { person.Biography    = biography;    changed = true; }
            if (string.IsNullOrEmpty(person.Birthday)     && !string.IsNullOrEmpty(birthday))
            { person.Birthday     = birthday;     changed = true; }
            if (string.IsNullOrEmpty(person.PlaceOfBirth) && !string.IsNullOrEmpty(placeOfBirth))
            { person.PlaceOfBirth = placeOfBirth; changed = true; }
            if (string.IsNullOrEmpty(person.ProfileUrl)   && !string.IsNullOrEmpty(profileUrl))
            { person.ProfileUrl   = profileUrl;   changed = true; }

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
        var existing     = await _personImageRepository.FindAsync(i => i.PersonId == personId);
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

    // ─── Cache invalidation ───────────────────────────────────────────────────

    private async Task InvalidateTvShowCachesAsync(Guid showId, List<Guid> genreIds,
        IEnumerable<int>? seasonNumbers = null)
    {
        await _cacheService.RemoveAsync(string.Format(TVSHOW_CACHE_KEY, showId));

        var keysToRemove = genreIds
            .Select(gid => string.Format(GENRE_CACHE_KEY, gid))
            .Append(AI_CONTEXTS_KEY)
            .Append(AI_ALL_DTOS_KEY);

        // Bug 2 fix: evict every season cache that was touched during sync.
        // Without this, GET /api/tvshows/{id}/seasons/{n} keeps returning stale
        // episodes from Redis for up to 6 hours after a sync.
        if (seasonNumbers != null)
        {
            keysToRemove = keysToRemove.Concat(
                seasonNumbers.Select(n => string.Format(SEASON_CACHE_KEY, showId, n)));
        }

        await _cacheService.RemoveManyAsync(keysToRemove.ToArray());
    }

    // ─── Mapping ──────────────────────────────────────────────────────────────

    private static TvShowSummaryDTO MapToSummaryDTO(TvShow t) => new()
    {
        Id               = t.Id,
        Title            = t.Title,
        Description      = t.Description,
        FirstAirDate     = t.FirstAirDate,
        PosterUrl        = t.PosterUrl,
        BackdropUrl      = t.BackdropUrl,
        Rating           = t.ImdbRating,
        OriginCountry    = t.OriginCountry,
        Status           = t.Status,
        NumberOfSeasons  = t.NumberOfSeasons,
        NumberOfEpisodes = t.NumberOfEpisodes,
        TrailerKey       = t.TvShowVideos?
            .Where(v => v.VideoType == "trailer" && !string.IsNullOrEmpty(v.VideoUrl))
            .Select(v => ExtractYoutubeKey(v.VideoUrl))
            .FirstOrDefault(k => k != null),
        IsPremium = t.IsPremium,
        Genres = t.TvShowGenres?
            .Select(g => g.Genre?.Name ?? "")
            .Where(n => n != "")
            .ToList() ?? new()
    };

    private static TvShowDTO MapToDTO(TvShow t) => new()
    {
        Id               = t.Id,
        Title            = t.Title,
        Description      = t.Description,
        FirstAirDate     = t.FirstAirDate,
        LastAirDate      = t.LastAirDate,
        PosterUrl        = t.PosterUrl,
        BackdropUrl      = t.BackdropUrl,
        EpisodeRuntime   = t.EpisodeRuntime,
        Rating           = t.ImdbRating,
        OriginCountry    = t.OriginCountry,
        Status           = t.Status,
        NumberOfSeasons  = t.NumberOfSeasons,
        NumberOfEpisodes = t.NumberOfEpisodes,
        IsPremium        = t.IsPremium,

        Genres = t.TvShowGenres?
            .Select(g => g.Genre?.Name ?? "")
            .Where(n => n != "")
            .ToList() ?? new(),

        Videos = t.TvShowVideos?
            .Select(v => new TvShowVideoDTO
            {
                Id        = v.Id,
                VideoUrl  = v.VideoUrl,
                VideoType = v.VideoType,
                Duration  = v.Duration,
                Quality   = v.Quality
            }).ToList() ?? new(),

        TrailerKey = t.TvShowVideos?
            .Where(v => v.VideoType == "trailer" && !string.IsNullOrEmpty(v.VideoUrl))
            .Select(v => ExtractYoutubeKey(v.VideoUrl))
            .FirstOrDefault(k => k != null),

        Cast = t.TvShowCasts?
            .OrderBy(c => c.Order)
            .Where(c => c.Person != null)
            .Take(6)
            .Select(c => new TvShowCastDTO
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

        Director = t.TvShowDirectors?
            .Where(d => d.Person != null)
            .Select(d => d.Person?.Name)
            .FirstOrDefault(),

        DirectorDetail = t.TvShowDirectors?
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

        Images = t.TvShowImages?
            .Select(i => new TvShowImageDTO
            {
                Url       = i.Url,
                ImageType = i.ImageType
            }).ToList() ?? new(),

        Seasons = t.Seasons?
            .OrderBy(s => s.SeasonNumber)
            .Select(s => new SeasonDTO
            {
                Id           = s.Id,
                SeasonNumber = s.SeasonNumber,
                Name         = s.Name,
                Overview     = s.Overview,
                PosterUrl    = s.PosterUrl,
                AirDate      = s.AirDate,
                EpisodeCount = s.EpisodeCount,
                Episodes     = new()
            })
            .ToList() ?? new()
    };

    // ─── Season / Episode mappers ─────────────────────────────────────────────

    private static SeasonDTO MapSeasonToDTO(Season s, IEnumerable<Episode>? episodes = null) => new()
    {
        Id           = s.Id,
        SeasonNumber = s.SeasonNumber,
        Name         = s.Name,
        Overview     = s.Overview,
        PosterUrl    = s.PosterUrl,
        AirDate      = s.AirDate,
        EpisodeCount = s.EpisodeCount,
        Episodes     = (episodes ?? s.Episodes ?? Enumerable.Empty<Episode>())
            .OrderBy(e => e.EpisodeNumber)
            .Select(MapEpisodeToDTO)
            .ToList()
    };

    private static EpisodeDTO MapEpisodeToDTO(Episode e) => new()
    {
        Id            = e.Id,
        EpisodeNumber = e.EpisodeNumber,
        Title         = e.Title,
        Overview      = e.Overview,
        StillUrl      = e.StillUrl,
        Runtime       = e.Runtime,
        Rating        = e.Rating,
        AirDate       = e.AirDate,
        VideoUrl      = e.VideoUrl
    };

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
}