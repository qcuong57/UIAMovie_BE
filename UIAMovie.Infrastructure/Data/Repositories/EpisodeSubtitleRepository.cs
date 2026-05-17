// UIAMovie.Infrastructure/Data/Repositories/EpisodeSubtitleRepository.cs

using Microsoft.EntityFrameworkCore;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Infrastructure.Data.Repositories;

public class EpisodeSubtitleRepository : Repository<EpisodeSubtitle>, IEpisodeSubtitleRepository
{
    private readonly MovieDbContext _context;

    public EpisodeSubtitleRepository(MovieDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<IEnumerable<EpisodeSubtitle>> GetByEpisodeIdAsync(Guid episodeId)
    {
        return await _context.EpisodeSubtitles
            .AsNoTracking()
            .Where(s => s.EpisodeId == episodeId)
            .OrderBy(s => s.LanguageName)
            // Chỉ lấy meta, không lấy Content để response nhẹ
            .Select(s => new EpisodeSubtitle
            {
                Id             = s.Id,
                EpisodeId      = s.EpisodeId,
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

    public async Task<EpisodeSubtitle?> GetByIdAsync(Guid id)
    {
        return await _context.EpisodeSubtitles
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<EpisodeSubtitle?> GetByEpisodeAndLanguageAsync(Guid episodeId, string languageCode)
    {
        return await _context.EpisodeSubtitles
            .AsNoTracking()
            .FirstOrDefaultAsync(s =>
                s.EpisodeId    == episodeId &&
                s.LanguageCode == languageCode);
    }

    public async Task ClearDefaultAsync(Guid episodeId)
    {
        await _context.EpisodeSubtitles
            .Where(s => s.EpisodeId == episodeId && s.IsDefault)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(s => s.IsDefault, false));
    }
}