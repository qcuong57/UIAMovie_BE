// UIAMovie.Infrastructure/Data/Repositories/MovieRepository.cs

using Microsoft.EntityFrameworkCore;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data;

namespace UIAMovie.Infrastructure.Data.Repositories;

public class MovieRepository : Repository<Movie>, IMovieRepository
{
    private readonly MovieDbContext _context;

    public MovieRepository(MovieDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<Movie?> GetByIdWithDetailsAsync(Guid id)
    {
        return await _context.Movies
            // ── Cast → Person → Images ────────────────────────────────────
            .Include(m => m.MovieCasts.OrderBy(c => c.Order))
                .ThenInclude(c => c.Person)
                    .ThenInclude(p => p.Images)          // ← thêm
            // ── Director → Person → Images ───────────────────────────────
            .Include(m => m.MovieDirectors)
                .ThenInclude(d => d.Person)
                    .ThenInclude(p => p.Images)          // ← thêm
            // ── Hình ảnh phim ─────────────────────────────────────────────
            .Include(m => m.MovieImages)
            // ── Video / Trailer ───────────────────────────────────────────
            .Include(m => m.MovieVideos)
            // ── Genre ─────────────────────────────────────────────────────
            .Include(m => m.MovieGenres)
                .ThenInclude(g => g.Genre)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<IEnumerable<Movie>> GetAllWithGenresAsync()
    {
        return await _context.Movies
            .Include(m => m.MovieGenres)
                .ThenInclude(g => g.Genre)
            .ToListAsync();
    }

    public async Task<Movie?> GetByTmdbIdAsync(int tmdbId)
    {
        return await _context.Movies
            .FirstOrDefaultAsync(m => m.TmdbId == tmdbId);
    }

    public async Task<IEnumerable<Movie>> GetMoviesByActorNameAsync(string actorName)
    {
        return await _context.Movies
            .Include(m => m.MovieGenres).ThenInclude(g => g.Genre)
            .Include(m => m.MovieCasts)
                .ThenInclude(c => c.Person)
                    .ThenInclude(p => p.Images)          // ← thêm
            .Include(m => m.MovieDirectors)
                .ThenInclude(d => d.Person)
                    .ThenInclude(p => p.Images)          // ← thêm
            .Include(m => m.MovieVideos)
            .Include(m => m.MovieImages)
            .Where(m => m.MovieCasts
                .Any(c => c.Person != null &&
                          c.Person.Name.Contains(actorName)))
            .ToListAsync();
    }
}