// UIAMovie.API/Controllers/SubtitlesController.cs
//
// Endpoints:
//   GET    /api/movies/{movieId}/subtitles              → list subtitle (meta)
//   GET    /api/movies/{movieId}/subtitles/{id}/content → nội dung VTT để player load
//   POST   /api/movies/{movieId}/subtitles/upload       → Admin: import .srt/.vtt
//   POST   /api/movies/{movieId}/subtitles/translate    → Admin: AI dịch từ subtitle có sẵn
//   POST   /api/movies/{movieId}/subtitles/ai-generate  → Admin: AI dịch từ raw content paste vào
//   GET    /api/movies/{movieId}/subtitles/{id}/status  → Poll trạng thái AI đang dịch
//   PATCH  /api/movies/{movieId}/subtitles/{id}/default → Admin: đặt làm default
//   DELETE /api/movies/{movieId}/subtitles/{id}         → Admin: xóa

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;
using UIAMovie.Domain.Constants;

namespace UIAMovie.Controllers;

[ApiController]
[Route("api/movies/{movieId:guid}/subtitles")]
public class SubtitlesController : ControllerBase
{
    private readonly ISubtitleService _subtitleService;

    public SubtitlesController(ISubtitleService subtitleService)
    {
        _subtitleService = subtitleService;
    }

    // ─── PUBLIC ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Lấy danh sách subtitle của phim (không kèm content).
    /// Frontend dùng để render dropdown chọn ngôn ngữ.
    ///
    /// GET /api/movies/{movieId}/subtitles
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSubtitles(Guid movieId)
    {
        var subtitles = await _subtitleService.GetSubtitlesAsync(movieId);
        return Ok(new ApiResponseDTO<object>
        {
            Data    = subtitles,
            Message = "Thành công"
        });
    }

    /// <summary>
    /// Lấy nội dung WebVTT của subtitle để video player load.
    ///
    /// GET /api/movies/{movieId}/subtitles/{id}/content
    ///
    /// Trả về:
    ///   - JSON với content field (để player dùng Blob URL)
    ///   - Hoặc text/vtt trực tiếp nếu Accept: text/vtt
    /// </summary>
    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> GetSubtitleContent(Guid movieId, Guid id)
    {
        var subtitle = await _subtitleService.GetSubtitleContentAsync(id);
        if (subtitle == null)
            return NotFound(new ApiErrorResponseDTO
            {
                Message    = "Không tìm thấy subtitle",
                StatusCode = 404
            });

        // Nếu client muốn raw VTT (dùng với track element)
        if (Request.Headers["Accept"].ToString().Contains("text/vtt"))
        {
            return Content(subtitle.Content, "text/vtt; charset=utf-8");
        }

        return Ok(new ApiResponseDTO<object>
        {
            Data    = subtitle,
            Message = "Thành công"
        });
    }

    /// <summary>
    /// Poll trạng thái subtitle đang được AI dịch.
    ///
    /// GET /api/movies/{movieId}/subtitles/{id}/status
    /// Response: { id, status: "processing" | "ready" | "failed", errorMessage? }
    ///
    /// Frontend poll mỗi 3s cho đến khi status != "processing".
    /// </summary>
    [HttpGet("{id:guid}/status")]
    public async Task<IActionResult> GetStatus(Guid movieId, Guid id)
    {
        var subtitles = await _subtitleService.GetSubtitlesAsync(movieId);
        var subtitle  = subtitles.FirstOrDefault(s => s.Id == id);

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
    /// POST /api/movies/{movieId}/subtitles/upload
    /// Content-Type: multipart/form-data
    /// Fields: File, LanguageCode, LanguageName?, IsDefault
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Roles = Roles.Admin)]
    [RequestSizeLimit(10_485_760)] // 10MB
    public async Task<IActionResult> UploadSubtitle(
        Guid movieId, [FromForm] UploadSubtitleDTO dto)
    {
        if (string.IsNullOrWhiteSpace(dto.LanguageCode))
            return BadRequest(new ApiErrorResponseDTO
            {
                Message    = "LanguageCode không được để trống",
                StatusCode = 400
            });

        var result = await _subtitleService.UploadSubtitleAsync(movieId, dto, GetUserId());
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
    /// POST /api/movies/{movieId}/subtitles/translate
    /// Body: { sourceSubtitleId, targetLanguageCode, targetLanguageName? }
    ///
    /// Trả về ngay với status="processing".
    /// Frontend poll /status mỗi 3s cho đến khi status="ready".
    /// </summary>
    [HttpPost("translate")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> TranslateSubtitle(
        Guid movieId, [FromBody] TranslateSubtitleDTO dto)
    {
        if (string.IsNullOrWhiteSpace(dto.TargetLanguageCode))
            return BadRequest(new ApiErrorResponseDTO
            {
                Message    = "TargetLanguageCode không được để trống",
                StatusCode = 400
            });

        var result = await _subtitleService.TranslateSubtitleAsync(movieId, dto, GetUserId());
        return Accepted(new ApiResponseDTO<object>
        {
            Data    = result,
            Message = "Đang dịch subtitle bằng AI, vui lòng đợi..."
        });
    }

    /// <summary>
    /// AI dịch từ raw SRT/VTT content paste trực tiếp.
    /// Dùng khi chưa có subtitle trong DB — paste content tiếng Anh, AI dịch ra tiếng Việt.
    ///
    /// POST /api/movies/{movieId}/subtitles/ai-generate
    /// Body: { movieId, sourceContent, sourceLanguageCode, targetLanguageCode, targetLanguageName? }
    /// </summary>
    [HttpPost("ai-generate")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AiGenerate(
        Guid movieId, [FromBody] AiGenerateSubtitleDTO dto)
    {
        dto.MovieId = movieId; // Đảm bảo movieId nhất quán với route

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
    /// Đặt subtitle là mặc định khi phim load.
    ///
    /// PATCH /api/movies/{movieId}/subtitles/{id}/default
    /// </summary>
    [HttpPatch("{id:guid}/default")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SetDefault(Guid movieId, Guid id)
    {
        var success = await _subtitleService.SetDefaultAsync(movieId, id);
        return success
            ? Ok(new ApiResponseDTO<object>     { Message = "Đã đặt làm subtitle mặc định" })
            : NotFound(new ApiErrorResponseDTO  { Message = "Không tìm thấy subtitle", StatusCode = 404 });
    }

    /// <summary>
    /// Xóa subtitle.
    ///
    /// DELETE /api/movies/{movieId}/subtitles/{id}
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DeleteSubtitle(Guid movieId, Guid id)
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