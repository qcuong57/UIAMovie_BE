// UIAMovie.Application/Services/IEpisodeSubtitleService.cs

using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Services;

public interface IEpisodeSubtitleService
{
    /// <summary>Lấy danh sách subtitle (meta, không kèm content) của tập phim.</summary>
    Task<IEnumerable<EpisodeSubtitleDTO>> GetSubtitlesAsync(Guid episodeId);

    /// <summary>Lấy meta của một subtitle theo id (dùng cho status polling).</summary>
    Task<EpisodeSubtitleDTO?> GetSubtitleAsync(Guid subtitleId);

    /// <summary>Lấy nội dung WebVTT của một subtitle để player dùng.</summary>
    Task<EpisodeSubtitleContentDTO?> GetSubtitleContentAsync(Guid subtitleId);

    /// <summary>Import file .srt hoặc .vtt thủ công. Auto-convert SRT → VTT.</summary>
    Task<EpisodeSubtitleDTO> UploadSubtitleAsync(Guid episodeId, UploadSubtitleDTO dto, Guid uploadedBy);

    /// <summary>AI dịch subtitle đã có trong DB sang ngôn ngữ khác.</summary>
    Task<EpisodeSubtitleDTO> TranslateSubtitleAsync(Guid episodeId, TranslateSubtitleDTO dto, Guid requestedBy);

    /// <summary>AI dịch raw content SRT/VTT paste trực tiếp.</summary>
    Task<EpisodeSubtitleDTO> AiGenerateSubtitleAsync(AiGenerateEpisodeSubtitleDTO dto, Guid requestedBy);

    /// <summary>Xóa một subtitle.</summary>
    Task<bool> DeleteSubtitleAsync(Guid subtitleId);

    /// <summary>Đặt subtitle là mặc định khi tập phim load.</summary>
    Task<bool> SetDefaultAsync(Guid episodeId, Guid subtitleId);
}