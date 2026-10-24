// UIAMovie.API/Controllers/AdsController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Application.Options;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdsController : ControllerBase
{
    private readonly IAdService _adService;

    public AdsController(IAdService adService)
    {
        _adService = adService;
    }

    // ── Advertisement CRUD (Admin) ────────────────────────────────────────────

    /// <summary>Paged list cho admin panel.</summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAds([FromQuery] FilterAdsDTO filter)
    {
        var (items, total) = await _adService.GetAdsAsync(filter);
        return Ok(new { items, total });
    }

    /// <summary>Chi tiết 1 ad kèm danh sách schedules.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAd(Guid id)
    {
        var ad = await _adService.GetAdByIdAsync(id);
        return ad == null ? NotFound() : Ok(ad);
    }

    /// <summary>Tạo ad mới (hỗ trợ upload video hoặc nhập URL ngoài).</summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreateAd([FromForm] CreateAdDTO dto)
    {
        var id = await _adService.CreateAdAsync(dto);
        return CreatedAtAction(nameof(GetAd), new { id }, new { id });
    }

    /// <summary>Cập nhật thông tin ad / thay video.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdateAd(Guid id, [FromForm] UpdateAdDTO dto)
    {
        var ok = await _adService.UpdateAdAsync(id, dto);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>Xóa ad và file Cloudinary liên quan.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteAd(Guid id)
    {
        var ok = await _adService.DeleteAdAsync(id);
        return ok ? NoContent() : NotFound();
    }

    // ── Global Slots (Admin) ──────────────────────────────────────────────────

    /// <summary>Tạo global slot — gắn ad vào tất cả content hoặc theo loại content.</summary>
    [HttpPost("{adId:guid}/global-slots")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateGlobalSlot(Guid adId, [FromBody] CreateGlobalSlotDTO dto)
    {
        try
        {
            var slotId = await _adService.CreateGlobalSlotAsync(adId, dto);
            return Ok(new { slotId });
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Cập nhật position / scope / trạng thái của 1 global slot.</summary>
    [HttpPatch("global-slots/{slotId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateGlobalSlot(Guid slotId, [FromBody] UpdateGlobalSlotDTO dto)
    {
        var ok = await _adService.UpdateGlobalSlotAsync(slotId, dto);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>Xóa global slot.</summary>
    [HttpDelete("global-slots/{slotId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteGlobalSlot(Guid slotId)
    {
        var ok = await _adService.DeleteGlobalSlotAsync(slotId);
        return ok ? NoContent() : NotFound();
    }

    // ── Content-specific Overrides (Admin) ────────────────────────────────────

    /// <summary>
    /// Tạo override cho 1 content cụ thể.
    /// Khi content có override ở position X, global slots ở position X bị bỏ qua.
    /// </summary>
    [HttpPost("{adId:guid}/overrides")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateOverride(Guid adId, [FromBody] CreateOverrideDTO dto)
    {
        try
        {
            var overrideId = await _adService.CreateOverrideAsync(adId, dto);
            return Ok(new { overrideId });
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Xóa content-specific override.</summary>
    [HttpDelete("overrides/{overrideId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteOverride(Guid overrideId)
    {
        var ok = await _adService.DeleteOverrideAsync(overrideId);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Số thứ tự hiển thị tiếp theo cho cùng vị trí + phạm vi (để form admin tự nhảy số).
    /// Global: position (+ appliesTo). Override: position + contentType + contentId.
    /// </summary>
    [HttpGet("next-display-order")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetNextDisplayOrder(
        [FromQuery] AdPosition position,
        [FromQuery] AdContentType? appliesTo = null,
        [FromQuery] AdContentType? contentType = null,
        [FromQuery] Guid? contentId = null)
    {
        if (!Enum.IsDefined(position))
            return BadRequest(new { error = "Vị trí không hợp lệ." });
        var next = await _adService.GetNextDisplayOrderAsync(position, appliesTo, contentType, contentId);
        return Ok(new { next });
    }

    // ── Schedule settings (Admin) ─────────────────────────────────────────────

    /// <summary>Cấu hình lập lịch: Fixed (slot admin đặt) hoặc Random (kiểu YouTube).</summary>
    [HttpGet("schedule-settings")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetScheduleSettings()
        => Ok(await _adService.GetScheduleSettingsAsync());

    [HttpPut("schedule-settings")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateScheduleSettings([FromBody] AdScheduleOptions dto)
    {
        if (dto == null) return BadRequest(new { error = "Thiếu cấu hình." });
        if (!Enum.IsDefined(dto.Mode))
            return BadRequest(new { error = "Chế độ không hợp lệ (Fixed | Random)." });
        if (dto.PostRollChance < 0 || dto.PostRollChance > 1)
            return BadRequest(new { error = "PostRollChance phải trong khoảng 0–1." });
        if (dto.JitterRatio < 0 || dto.JitterRatio > 1)
            return BadRequest(new { error = "JitterRatio phải trong khoảng 0–1." });
        if (dto.MidRollIntervalRatio < 0)
            return BadRequest(new { error = "MidRollIntervalRatio không được âm." });
        if (dto.MaxMidRolls < 0 || dto.MinDurationForMidRollSeconds < 0)
            return BadRequest(new { error = "Số lượng / số giây không được âm." });
        if (dto.MinMidRollGapRatio < 0 || dto.MinMidRollGapRatio > 1 || dto.EndGuardRatio < 0 || dto.EndGuardRatio > 1)
            return BadRequest(new { error = "MinMidRollGapRatio và EndGuardRatio phải trong khoảng 0–1." });
        if (dto.PreRollWeights == null || dto.PreRollWeights.Length == 0 || dto.PreRollWeights.Any(w => w < 0))
            return BadRequest(new { error = "PreRollWeights phải có phần tử và không âm." });
        if (dto.Mode == AdScheduleMode.Random && dto.PreRollWeights.Sum() <= 0)
            return BadRequest(new { error = "Tổng trọng số pre-roll phải lớn hơn 0." });

        return Ok(await _adService.UpdateScheduleSettingsAsync(dto));
    }

    /// <summary>Quay về cấu hình mặc định (appsettings) — xóa phần admin đã lưu.</summary>
    [HttpDelete("schedule-settings")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ResetScheduleSettings()
        => Ok(await _adService.ResetScheduleSettingsAsync());

    /// <summary>
    /// Xem thử 1 lượt xem (không lưu gì): Random → lịch ngẫu nhiên mẫu, Fixed → lịch cố định.
    /// contentType = null → tất cả hình thức.
    /// </summary>
    [HttpPost("schedule-preview")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PreviewSchedule([FromBody] SchedulePreviewRequestDTO dto)
    {
        if (dto == null) return BadRequest(new { error = "Thiếu dữ liệu." });
        if (dto.ContentType is { } ct && !Enum.IsDefined(ct))
            return BadRequest(new { error = "Loại nội dung không hợp lệ." });
        if (dto.DurationSeconds < 0)
            return BadRequest(new { error = "Thời lượng không được âm." });
        return Ok(await _adService.PreviewScheduleAsync(dto));
    }

    // ── Player API (public) ─────────────────────────────────────

    /// <summary>
    /// Player gọi endpoint này trước khi bắt đầu phát video (cho phép cả khách chưa đăng nhập).
    /// Trả về tất cả ads grouped by position (PreRoll / MidRoll / PostRoll).
    /// Merge logic: override per-content có ưu tiên hơn global slots.
    /// </summary>
    [HttpGet("content/{contentType}/{contentId:guid}")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // lịch random: không để browser/CDN cache
    public async Task<IActionResult> GetAdsForContent(
        AdContentType contentType,
        Guid          contentId,
        [FromQuery] int? durationSeconds = null,
        [FromQuery] Guid? parentId = null)   // Episode: id của bộ (TvShow) để áp override của cả bộ
    {
        var result = await _adService.GetAdsForContentAsync(contentType, contentId, durationSeconds, parentId);
        return Ok(result);
    }
}