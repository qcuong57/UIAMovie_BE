using Microsoft.EntityFrameworkCore;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data;
 
namespace UIAMovie.Infrastructure.Data.Repositories;
 
public class SubtitleRepository : Repository<MovieSubtitle>, ISubtitleRepository
{
    private readonly MovieDbContext _context;
 
    public SubtitleRepository(MovieDbContext context) : base(context)
    {
        _context = context;
    }
 
    public async Task<IEnumerable<MovieSubtitle>> GetByMovieIdAsync(Guid movieId)
    {
        return await _context.MovieSubtitles
            .AsNoTracking()
            .Where(s => s.MovieId == movieId)
            .OrderBy(s => s.LanguageName)
            // Chỉ lấy meta, không lấy Content để response nhẹ
            .Select(s => new MovieSubtitle
            {
                Id             = s.Id,
                MovieId        = s.MovieId,
                LanguageCode   = s.LanguageCode,
                LanguageName   = s.LanguageName,
                Source         = s.Source,
                Status         = s.Status,
                IsDefault      = s.IsDefault,
                TranslatedFrom = s.TranslatedFrom,
                ErrorMessage   = s.ErrorMessage,
                CreatedAt      = s.CreatedAt,
                UpdatedAt      = s.UpdatedAt,
                // Content bỏ ra — chỉ load khi GetByIdAsync
            })
            .ToListAsync();
    }
 
    public async Task<MovieSubtitle?> GetByIdAsync(Guid id)
    {
        return await _context.MovieSubtitles
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);
    }
 
    public async Task<MovieSubtitle?> GetByMovieAndLanguageAsync(Guid movieId, string languageCode)
    {
        return await _context.MovieSubtitles
            .AsNoTracking()
            .FirstOrDefaultAsync(s =>
                s.MovieId      == movieId &&
                s.LanguageCode == languageCode);
    }
 
    public async Task ClearDefaultAsync(Guid movieId)
    {
        // ExecuteUpdate — không cần load entity về RAM
        await _context.MovieSubtitles
            .Where(s => s.MovieId == movieId && s.IsDefault)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(s => s.IsDefault, false));
    }
}