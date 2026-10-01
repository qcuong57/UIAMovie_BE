using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Interfaces.IServices;

public interface ISubtitleService
{
    /// <summary>Lấy danh sách subtitle (meta, không kèm content) của phim.</summary>
    Task<IEnumerable<SubtitleInfoDTO>> GetSubtitlesAsync(Guid movieId);

    /// <summary>Lấy nội dung WebVTT của một subtitle để player dùng.</summary>
    Task<SubtitleContentDTO?> GetSubtitleContentAsync(Guid subtitleId);

    /// <summary>Import file .srt hoặc .vtt thủ công. Auto-convert SRT → VTT.</summary>
    Task<SubtitleInfoDTO> UploadSubtitleAsync(Guid movieId, UploadSubtitleDTO dto, Guid uploadedBy);

    /// <summary>AI dịch subtitle đã có trong DB sang ngôn ngữ khác.</summary>
    Task<SubtitleInfoDTO> TranslateSubtitleAsync(Guid movieId, TranslateSubtitleDTO dto, Guid requestedBy);

    /// <summary>AI dịch raw content SRT/VTT được paste trực tiếp.</summary>
    Task<SubtitleInfoDTO> AiGenerateSubtitleAsync(AiGenerateSubtitleDTO dto, Guid requestedBy);

    /// <summary>Xóa một subtitle.</summary>
    Task<bool> DeleteSubtitleAsync(Guid subtitleId);

    /// <summary>Đặt subtitle là mặc định khi phim load.</summary>
    Task<bool> SetDefaultAsync(Guid movieId, Guid subtitleId);
}