// UIAMovie.Infrastructure/Data/Repositories/IEpisodeSubtitleRepository.cs

using UIAMovie.Domain.Entities;

namespace UIAMovie.Infrastructure.Data.Repositories;

public interface IEpisodeSubtitleRepository : IRepository<EpisodeSubtitle>
{
    /// <summary>Lấy danh sách subtitle (không kèm Content) của một tập phim.</summary>
    Task<IEnumerable<EpisodeSubtitle>> GetByEpisodeIdAsync(Guid episodeId);

    /// <summary>Lấy một subtitle kèm Content để stream ra player.</summary>
    Task<EpisodeSubtitle?> GetByIdAsync(Guid id);

    /// <summary>Kiểm tra tập phim đã có subtitle ngôn ngữ này chưa.</summary>
    Task<EpisodeSubtitle?> GetByEpisodeAndLanguageAsync(Guid episodeId, string languageCode);

    /// <summary>Unset IsDefault cho toàn bộ subtitle của tập (trước khi set default mới).</summary>
    Task ClearDefaultAsync(Guid episodeId);

    // ── Persistence — đồng nhất với ISubtitleRepository ──────────────────────

    /// <summary>Mark entity là modified trong ChangeTracker (không save ngay).</summary>
    void Update(EpisodeSubtitle entity);

    /// <summary>Mark entity là deleted trong ChangeTracker (không save ngay).</summary>
    void Remove(EpisodeSubtitle entity);

    /// <summary>Flush toàn bộ thay đổi pending xuống DB.</summary>
    Task SaveChangesAsync();
}