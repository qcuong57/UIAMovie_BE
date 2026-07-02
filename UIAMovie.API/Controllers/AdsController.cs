// UIAMovie.API/Controllers/AdsController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;

namespace UIAMovie.API.Controllers;

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
    }

    /// <summary>Xóa content-specific override.</summary>
    [HttpDelete("overrides/{overrideId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteOverride(Guid overrideId)
    {
        var ok = await _adService.DeleteOverrideAsync(overrideId);
        return ok ? NoContent() : NotFound();
    }

    // ── Player API (authenticated users) ─────────────────────────────────────

    /// <summary>
    /// Player gọi endpoint này trước khi bắt đầu phát video.
    /// Trả về tất cả ads grouped by position (PreRoll / MidRoll / PostRoll).
    /// Merge logic: override per-content có ưu tiên hơn global slots.
    /// </summary>
    [HttpGet("content/{contentType}/{contentId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetAdsForContent(
        AdContentType contentType,
        Guid          contentId)
    {
        var result = await _adService.GetAdsForContentAsync(contentType, contentId);
        return Ok(result);
    }
}