// UIAMovie.API/Controllers/EpisodeSubtitlesController.cs
//
// Endpoints:
//   GET    /api/episodes/{episodeId}/subtitles              → list subtitle (meta)
//   GET    /api/episodes/{episodeId}/subtitles/{id}/content → nội dung VTT để player load
//   GET    /api/episodes/{episodeId}/subtitles/{id}/status  → poll trạng thái AI đang dịch
//   POST   /api/episodes/{episodeId}/subtitles/upload       → Admin: import .srt/.vtt
//   POST   /api/episodes/{episodeId}/subtitles/translate    → Admin: AI dịch từ subtitle có sẵn
//   POST   /api/episodes/{episodeId}/subtitles/ai-generate  → Admin: AI dịch từ raw content
//   PATCH  /api/episodes/{episodeId}/subtitles/{id}/default → Admin: đặt làm default
//   DELETE /api/episodes/{episodeId}/subtitles/{id}         → Admin: xóa

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;
using UIAMovie.Domain.Constants;

namespace UIAMovie.Controllers;

[ApiController]
[Route("api/episodes/{episodeId:guid}/subtitles")]
public class EpisodeSubtitlesController : ControllerBase
{
    private readonly IEpisodeSubtitleService _subtitleService;

    public EpisodeSubtitlesController(IEpisodeSubtitleService subtitleService)
    {
        _subtitleService = subtitleService;
    }

    // ─── PUBLIC ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Lấy danh sách subtitle của tập phim (không kèm content).
    /// Frontend dùng để render dropdown chọn ngôn ngữ.
    ///
    /// GET /api/episodes/{episodeId}/subtitles
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSubtitles(Guid episodeId)
    {
        var subtitles = await _subtitleService.GetSubtitlesAsync(episodeId);
        return Ok(new ApiResponseDTO<object>
        {
            Data    = subtitles,
            Message = "Thành công"
        });
    }

    /// <summary>
    /// Lấy nội dung WebVTT của subtitle để video player load.
    ///
    /// GET /api/episodes/{episodeId}/subtitles/{id}/content
    ///
    /// Trả về:
    ///   - JSON với content field (để player dùng Blob URL)
    ///   - Hoặc text/vtt trực tiếp nếu Accept: text/vtt
    /// </summary>
    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> GetSubtitleContent(Guid episodeId, Guid id)
    {
        var subtitle = await _subtitleService.GetSubtitleContentAsync(id);
        if (subtitle == null)
            return NotFound(new ApiErrorResponseDTO
            {
                Message    = "Không tìm thấy subtitle",
                StatusCode = 404
            });

        if (Request.Headers["Accept"].ToString().Contains("text/vtt"))
            return Content(subtitle.Content, "text/vtt; charset=utf-8");

        return Ok(new ApiResponseDTO<object>
        {
            Data    = subtitle,
            Message = "Thành công"
        });
    }

    /// <summary>
    /// Poll trạng thái subtitle đang được AI dịch.
    ///
    /// GET /api/episodes/{episodeId}/subtitles/{id}/status
    /// Response: { id, status: "processing" | "ready" | "failed", errorMessage? }
    ///
    /// Frontend poll mỗi 3s cho đến khi status != "processing".
    /// </summary>
    [HttpGet("{id:guid}/status")]
    public async Task<IActionResult> GetStatus(Guid episodeId, Guid id)
    {
        // [FIX] Query thẳng bằng id thay vì load cả list rồi FirstOrDefault.
        // Dùng GetSubtitleAsync (trả EpisodeSubtitleDTO) — có đủ Status + ErrorMessage,
        // không kéo theo Content (blob lớn không cần thiết ở đây).
        var subtitle = await _subtitleService.GetSubtitleAsync(id);

        if (subtitle == null)
            return NotFound(new ApiErrorResponseDTO
            {
                Message    = "Không tìm thấy subtitle",
                StatusCode = 404
            });

        return Ok(new ApiResponseDTO<object>
        {
            Data = new
            {
                subtitle.Id,
                subtitle.Status,
                subtitle.LanguageCode,
                subtitle.LanguageName,
                subtitle.ErrorMessage
            },
            Message = "Thành công"
        });
    }

    // ─── ADMIN: Import thủ công ───────────────────────────────────────────────

    /// <summary>
    /// Upload file subtitle .srt hoặc .vtt thủ công.
    /// Auto-convert SRT → VTT khi cần.
    ///
    /// POST /api/episodes/{episodeId}/subtitles/upload
    /// Content-Type: multipart/form-data
    /// Fields: File, LanguageCode, LanguageName?, IsDefault
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Roles = Roles.Admin)]
    [RequestSizeLimit(10_485_760)] // 10MB
    public async Task<IActionResult> UploadSubtitle(
        Guid episodeId, [FromForm] UploadSubtitleDTO dto)
    {
        if (string.IsNullOrWhiteSpace(dto.LanguageCode))
            return BadRequest(new ApiErrorResponseDTO
            {
                Message    = "LanguageCode không được để trống",
                StatusCode = 400
            });

        var result = await _subtitleService.UploadSubtitleAsync(episodeId, dto, GetUserId());
        return Ok(new ApiResponseDTO<object>
        {
            Data    = result,
            Message = "Upload subtitle thành công"
        });
    }

    // ─── ADMIN: AI dịch từ subtitle đã có ────────────────────────────────────

    /// <summary>
    /// AI dịch subtitle đã có trong DB sang ngôn ngữ khác.
    ///
    /// POST /api/episodes/{episodeId}/subtitles/translate
    /// Body: { sourceSubtitleId, targetLanguageCode, targetLanguageName? }
    ///
    /// Trả về ngay với status="processing".
    /// Frontend poll /status mỗi 3s cho đến khi status="ready".
    /// </summary>
    [HttpPost("translate")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> TranslateSubtitle(
        Guid episodeId, [FromBody] TranslateSubtitleDTO dto)
    {
        if (string.IsNullOrWhiteSpace(dto.TargetLanguageCode))
            return BadRequest(new ApiErrorResponseDTO
            {
                Message    = "TargetLanguageCode không được để trống",
                StatusCode = 400
            });

        var result = await _subtitleService.TranslateSubtitleAsync(episodeId, dto, GetUserId());
        return Accepted(new ApiResponseDTO<object>
        {
            Data    = result,
            Message = "Đang dịch subtitle bằng AI, vui lòng đợi..."
        });
    }

    /// <summary>
    /// AI dịch từ raw SRT/VTT content paste trực tiếp.
    ///
    /// POST /api/episodes/{episodeId}/subtitles/ai-generate
    /// Body: { sourceContent, sourceLanguageCode, targetLanguageCode, targetLanguageName? }
    /// </summary>
    [HttpPost("ai-generate")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AiGenerate(
        Guid episodeId, [FromBody] AiGenerateEpisodeSubtitleDTO dto)
    {
        dto.EpisodeId = episodeId;

        if (string.IsNullOrWhiteSpace(dto.SourceContent))
            return BadRequest(new ApiErrorResponseDTO
            {
                Message    = "SourceContent không được để trống",
                StatusCode = 400
            });

        var result = await _subtitleService.AiGenerateSubtitleAsync(dto, GetUserId());
        return Accepted(new ApiResponseDTO<object>
        {
            Data    = result,
            Message = "Đang tạo subtitle bằng AI, vui lòng đợi..."
        });
    }

    // ─── ADMIN: Quản lý ───────────────────────────────────────────────────────

    /// <summary>
    /// Đặt subtitle là mặc định khi player load tập phim.
    ///
    /// PATCH /api/episodes/{episodeId}/subtitles/{id}/default
    /// </summary>
    [HttpPatch("{id:guid}/default")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SetDefault(Guid episodeId, Guid id)
    {
        var success = await _subtitleService.SetDefaultAsync(episodeId, id);
        return success
            ? Ok(new ApiResponseDTO<object>    { Message = "Đã đặt làm subtitle mặc định" })
            : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy subtitle", StatusCode = 404 });
    }

    /// <summary>
    /// Xóa subtitle.
    ///
    /// DELETE /api/episodes/{episodeId}/subtitles/{id}
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DeleteSubtitle(Guid episodeId, Guid id)
    {
        var success = await _subtitleService.DeleteSubtitleAsync(id);
        return success
            ? Ok(new ApiResponseDTO<object>    { Message = "Đã xóa subtitle" })
            : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy subtitle", StatusCode = 404 });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());
}