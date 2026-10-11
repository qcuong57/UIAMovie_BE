// UIAMovie.Application/Interfaces/IContentMetaProvider.cs

namespace UIAMovie.Application.Interfaces;

/// <summary>Thông tin tối thiểu của phim/show mà heartbeat cần (không load cả entity).</summary>
/// <param name="DurationMinutes">Phim: Duration. TV show: EpisodeRuntime mặc định. NULL = không biết.</param>
public sealed record ContentMeta(bool IsPublished, bool IsPremium, int? DurationMinutes);

/// <summary>
/// Cấp metadata nhẹ cho đường nóng (heartbeat 15–30s/người xem). Có cache trong process
/// nên phần lớn heartbeat không tốn query nào cho việc tra phim/show.
/// </summary>
public interface IContentMetaProvider
{
    /// <summary>NULL nếu phim không tồn tại.</summary>
    Task<ContentMeta?> GetMovieAsync(Guid movieId);

    /// <summary>NULL nếu show không tồn tại.</summary>
    Task<ContentMeta?> GetTvShowAsync(Guid tvShowId);

    /// <summary>Runtime (phút) của tập thuộc show. NULL nếu không có tập đó hoặc tập không có runtime.</summary>
    Task<int?> GetEpisodeRuntimeMinutesAsync(Guid tvShowId, Guid episodeId);
}