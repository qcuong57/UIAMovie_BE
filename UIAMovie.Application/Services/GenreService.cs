// UIAMovie.Application/Services/GenreService.cs

using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Services;

public interface IGenreService
{
    Task<IEnumerable<GenreDTO>> GetAllAsync();
    Task<GenreDTO?> GetByIdAsync(Guid id);
    Task<Guid> CreateAsync(CreateGenreDTO dto);
    Task<bool> UpdateAsync(Guid id, UpdateGenreDTO dto);
    Task<bool> DeleteAsync(Guid id);

    /// <summary>
    /// Đồng bộ danh sách genre (lấy từ TMDB bên ngoài) vào DB.
    /// Trả về số genre được tạo mới.
    /// </summary>
    Task<int> SyncFromTmdbAsync(List<TmdbGenreDTO> tmdbGenres);

    /// <summary>
    /// Nhận danh sách TMDB genre id → trả về danh sách Guid tương ứng trong DB.
    /// Genre nào chưa có trong DB sẽ bị bỏ qua (cần sync trước).
    /// </summary>
    Task<List<Guid>> ResolveGenreIdsFromTmdbAsync(IEnumerable<int> tmdbGenreIds);
}

public class GenreService : IGenreService
{
    private readonly IRepository<Genre> _genreRepository;
    private readonly ICacheService      _cacheService;

    private const string ALL_GENRES_CACHE_KEY = "genres:all";
    private const string GENRE_CACHE_KEY      = "genre:{0}";

    public GenreService(
        IRepository<Genre> genreRepository,
        ICacheService      cacheService)
    {
        _genreRepository = genreRepository;
        _cacheService    = cacheService;
    }

    // ─── Public CRUD ──────────────────────────────────────────────────────────

    public async Task<IEnumerable<GenreDTO>> GetAllAsync()
    {
        var cached = await _cacheService.GetAsync<List<GenreDTO>>(ALL_GENRES_CACHE_KEY);
        if (cached != null) return cached;

        var genres = await _genreRepository.GetAllAsync();
        var result = genres
            .OrderBy(g => g.Name)
            .Select(MapToDTO)
            .ToList();

        await _cacheService.SetAsync(ALL_GENRES_CACHE_KEY, result, TimeSpan.FromHours(6));
        return result;
    }

    public async Task<GenreDTO?> GetByIdAsync(Guid id)
    {
        var cacheKey = string.Format(GENRE_CACHE_KEY, id);
        var cached   = await _cacheService.GetAsync<GenreDTO>(cacheKey);
        if (cached != null) return cached;

        var genre = await _genreRepository.GetByIdAsync(id);
        if (genre == null) return null;

        var dto = MapToDTO(genre);
        await _cacheService.SetAsync(cacheKey, dto, TimeSpan.FromHours(6));
        return dto;
    }

    public async Task<Guid> CreateAsync(CreateGenreDTO dto)
    {
        var genres    = await _genreRepository.GetAllAsync();
        var duplicate = genres.Any(g =>
            string.Equals(g.Name, dto.Name, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
            throw new InvalidOperationException($"Genre '{dto.Name}' đã tồn tại.");

        var genre = new Genre
        {
            Name        = dto.Name.Trim(),
            Description = dto.Description?.Trim()
        };

        await _genreRepository.AddAsync(genre);
        await _genreRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(ALL_GENRES_CACHE_KEY);
        return genre.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateGenreDTO dto)
    {
        var genre = await _genreRepository.GetByIdAsync(id);
        if (genre == null) return false;

        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            var genres    = await _genreRepository.GetAllAsync();
            var duplicate = genres.Any(g =>
                g.Id != id &&
                string.Equals(g.Name, dto.Name, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
                throw new InvalidOperationException($"Genre '{dto.Name}' đã tồn tại.");

            genre.Name = dto.Name.Trim();
        }

        if (dto.Description != null)
            genre.Description = dto.Description.Trim();

        _genreRepository.Update(genre);
        await _genreRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(ALL_GENRES_CACHE_KEY);
        await _cacheService.RemoveAsync(string.Format(GENRE_CACHE_KEY, id));
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var genre = await _genreRepository.GetByIdAsync(id);
        if (genre == null) return false;

        if (genre.MovieGenres.Any())
            throw new InvalidOperationException(
                $"Không thể xóa genre '{genre.Name}' vì đang được dùng bởi {genre.MovieGenres.Count} phim.");

        _genreRepository.Remove(genre);
        await _genreRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(ALL_GENRES_CACHE_KEY);
        await _cacheService.RemoveAsync(string.Format(GENRE_CACHE_KEY, id));
        return true;
    }

    // ─── TMDB Sync ────────────────────────────────────────────────────────────

    /// <summary>
    /// Đồng bộ toàn bộ genre từ TMDB vào DB.
    /// - Genre chưa có (theo TmdbGenreId) → thêm mới.
    /// - Genre đã có nhưng đổi tên → cập nhật tên.
    /// Trả về số genre được tạo mới.
    /// </summary>
    public async Task<int> SyncFromTmdbAsync(List<TmdbGenreDTO> tmdbGenres)
    {
        var existing = (await _genreRepository.GetAllAsync()).ToList();
        int created  = 0;

        foreach (var tg in tmdbGenres)
        {
            var found = existing.FirstOrDefault(g => g.TmdbGenreId == tg.Id);

            if (found == null)
            {
                await _genreRepository.AddAsync(new Genre
                {
                    TmdbGenreId = tg.Id,
                    Name        = tg.Name
                });
                created++;
            }
            else if (!string.Equals(found.Name, tg.Name, StringComparison.OrdinalIgnoreCase))
            {
                found.Name = tg.Name;
                _genreRepository.Update(found);
            }
        }

        await _genreRepository.SaveChangesAsync();
        await _cacheService.RemoveAsync(ALL_GENRES_CACHE_KEY);
        return created;
    }

    /// <summary>
    /// Nhận danh sách TMDB genre id → trả về danh sách Guid tương ứng trong DB.
    /// Genre nào chưa sync sẽ bị bỏ qua.
    /// </summary>
    public async Task<List<Guid>> ResolveGenreIdsFromTmdbAsync(IEnumerable<int> tmdbGenreIds)
    {
        var all = (await _genreRepository.GetAllAsync()).ToList();

        return tmdbGenreIds
            .Select(tmdbId => all.FirstOrDefault(g => g.TmdbGenreId == tmdbId))
            .Where(g => g != null)
            .Select(g => g!.Id)
            .ToList();
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    private static GenreDTO MapToDTO(Genre g) => new()
    {
        Id          = g.Id,
        Name        = g.Name,
        Description = g.Description,
        MovieCount  = g.MovieGenres?.Count ?? 0
    };
}