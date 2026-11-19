// UIAMovie.Infrastructure/Data/Repositories/PersonRepository.cs

using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data;

namespace UIAMovie.Infrastructure.Data.Repositories;

public class PersonRepository : Repository<Person>, IPersonRepository
{
    private readonly MovieDbContext _context;

    public PersonRepository(MovieDbContext context) : base(context)
    {
        _context = context;
    }

    /// <summary>
    /// Cùng pattern với MovieRepository.GetPagedAsync: build IQueryable, để EF dịch
    /// sang SQL WHERE ILIKE + ORDER BY + OFFSET/FETCH — không kéo cả bảng Person về RAM.
    /// </summary>
    public async Task<(IEnumerable<Person> Items, int TotalCount)> SearchPagedAsync(
        string? query, int page, int pageSize)
    {
        var q = _context.Persons.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            q = q.Where(p => EF.Functions.ILike(p.Name, pattern));
        }

        q = q.OrderBy(p => p.Name);

        var totalCount = await q.CountAsync();

        var items = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Person?> GetByIdWithImagesAsync(Guid id)
    {
        return await _context.Persons
            .AsNoTracking()
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    /// <summary>
    /// GROUP BY tên đã chuẩn hóa NGAY TRÊN DB (Trim + ToLower dịch được sang SQL),
    /// HAVING COUNT > 1 để chỉ lấy các nhóm thực sự trùng — rồi mới load full Person
    /// của các Id đó (kèm Images) để trả về cho FE hiển thị so sánh.
    /// </summary>
    public async Task<IEnumerable<IGrouping<string, Person>>> FindDuplicatesByNameAsync()
    {
        var duplicateKeys = await _context.Persons
            .AsNoTracking()
            .GroupBy(p => p.Name.Trim().ToLower())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync();

        if (duplicateKeys.Count == 0)
            return Enumerable.Empty<IGrouping<string, Person>>();

        var people = await _context.Persons
            .AsNoTracking()
            .Include(p => p.Images)
            .Where(p => duplicateKeys.Contains(p.Name.Trim().ToLower()))
            .ToListAsync();

        return people.GroupBy(p => p.Name.Trim().ToLower());
    }

    /// <summary>
    /// Person có đang gắn với Movie hoặc TvShow (cast / director) không — chặn xóa cứng nếu true.
    /// </summary>
    public async Task<bool> IsReferencedByAnyMovieAsync(Guid personId)
    {
        if (await _context.MovieCasts.AsNoTracking().AnyAsync(c => c.PersonId == personId)) return true;
        if (await _context.MovieDirectors.AsNoTracking().AnyAsync(d => d.PersonId == personId)) return true;
        if (await _context.TvShowCasts.AsNoTracking().AnyAsync(c => c.PersonId == personId)) return true;

        return await _context.TvShowDirectors.AsNoTracking().AnyAsync(d => d.PersonId == personId);
    }

    /// <summary>
    /// Filmography (Movie + TvShow) truy vấn thẳng theo PersonId — không phụ thuộc tên,
    /// không lệch khi có Person trùng tên. Một tác phẩm vừa đóng vừa đạo diễn → 1 dòng,
    /// Roles = ["cast", "director"]. TvShow chưa publish bị ẩn.
    /// </summary>
    public async Task<List<PersonMovieDTO>> GetFilmographyAsync(Guid personId)
    {
        // ── Movie ────────────────────────────────────────────────────────────
        // ASSUMPTION: Movie có Title, PosterUrl, ReleaseDate, ImdbRating (decimal?)
        var movieCast = await _context.MovieCasts
            .AsNoTracking()
            .Where(c => c.PersonId == personId && c.Movie != null)
            .OrderBy(c => c.Order)
            .Select(c => new
            {
                Id = c.MovieId,
                c.Movie!.Title,
                c.Movie.PosterUrl,
                ReleaseDate = c.Movie.ReleaseDate,
                Rating = c.Movie.ImdbRating,
                c.Character
            })
            .ToListAsync();

        var movieDirected = await _context.MovieDirectors
            .AsNoTracking()
            .Where(d => d.PersonId == personId && d.Movie != null)
            .Select(d => new
            {
                Id = d.MovieId,
                d.Movie!.Title,
                d.Movie.PosterUrl,
                ReleaseDate = d.Movie.ReleaseDate,
                Rating = d.Movie.ImdbRating
            })
            .ToListAsync();

        // ── TvShow ───────────────────────────────────────────────────────────
        var tvCast = await _context.TvShowCasts
            .AsNoTracking()
            .Where(c => c.PersonId == personId && c.TvShow.IsPublished)
            .OrderBy(c => c.Order)
            .Select(c => new
            {
                Id = c.TvShowId,
                c.TvShow.Title,
                c.TvShow.PosterUrl,
                ReleaseDate = c.TvShow.FirstAirDate,
                Rating = c.TvShow.ImdbRating,
                c.Character
            })
            .ToListAsync();

        var tvDirected = await _context.TvShowDirectors
            .AsNoTracking()
            .Where(d => d.PersonId == personId && d.TvShow.IsPublished)
            .Select(d => new
            {
                Id = d.TvShowId,
                d.TvShow.Title,
                d.TvShow.PosterUrl,
                ReleaseDate = d.TvShow.FirstAirDate,
                Rating = d.TvShow.ImdbRating
            })
            .ToListAsync();

        // ── Gộp ──────────────────────────────────────────────────────────────
        var map = new Dictionary<(string Type, Guid Id), PersonMovieDTO>();

        PersonMovieDTO GetOrAdd(string type, Guid id, string title, string? poster, DateTime? date, decimal? rating)
        {
            if (!map.TryGetValue((type, id), out var dto))
                map[(type, id)] = dto = new PersonMovieDTO
                {
                    Id = id,
                    MediaType = type,
                    Title = title,
                    PosterUrl = poster,
                    ReleaseDate = date,
                    Rating = rating
                };
            return dto;
        }

        void AddRole(PersonMovieDTO dto, string role)
        {
            if (!dto.Roles.Contains(role)) dto.Roles.Add(role);
        }

        foreach (var c in movieCast)
        {
            var dto = GetOrAdd("movie", c.Id, c.Title, c.PosterUrl, c.ReleaseDate, c.Rating);
            if (string.IsNullOrEmpty(dto.Character) && !string.IsNullOrWhiteSpace(c.Character))
                dto.Character = c.Character;
            AddRole(dto, "cast");
        }

        foreach (var d in movieDirected)
            AddRole(GetOrAdd("movie", d.Id, d.Title, d.PosterUrl, d.ReleaseDate, d.Rating), "director");

        foreach (var c in tvCast)
        {
            var dto = GetOrAdd("tv", c.Id, c.Title, c.PosterUrl, c.ReleaseDate, c.Rating);
            if (string.IsNullOrEmpty(dto.Character) && !string.IsNullOrWhiteSpace(c.Character))
                dto.Character = c.Character;
            AddRole(dto, "cast");
        }

        foreach (var d in tvDirected)
            AddRole(GetOrAdd("tv", d.Id, d.Title, d.PosterUrl, d.ReleaseDate, d.Rating), "director");

        return map.Values
            .OrderByDescending(m => m.ReleaseDate)
            .ThenBy(m => m.Title)
            .ToList();
    }
}