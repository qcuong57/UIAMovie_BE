// UIAMovie.API/Controllers/BannersController.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BannersController : ControllerBase
{
    private readonly IBannerService _service;

    public BannersController(IBannerService service) => _service = service;

    /// <summary>Id user nếu request có token hợp lệ (endpoint [AllowAnonymous] vẫn đọc được).</summary>
    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
            ? id : null;

    // ── Public ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Banner đang hiển thị của 1 vị trí, đã lọc theo khung giờ, đối tượng (khách/free/premium)
    /// và tình trạng gói gắn kèm. Cho cả khách chưa đăng nhập.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetActive([FromQuery] BannerPlacement placement)
    {
        if (!Enum.IsDefined(placement))
            return BadRequest(new { error = "Vị trí không hợp lệ." });

        var userId = CurrentUserId;
        var items = await _service.GetActiveAsync(placement, userId);

        // Kết quả phụ thuộc người xem → khách: cache công khai (CDN được); đã đăng nhập: chỉ cache riêng ở trình duyệt.
        Response.Headers.CacheControl = userId == null ? "public, max-age=60" : "private, max-age=60";
        Response.Headers.Vary = "Authorization";
        return Ok(items);
    }

    /// <summary>Kích thước ảnh khuyến nghị + số banner tối đa mỗi vị trí (cho form admin).</summary>
    [HttpGet("placements")]
    [AllowAnonymous]
    public IActionResult GetPlacements()
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        return Ok(BannerPlacementRules.All);
    }

    /// <summary>Ghi nhận lượt hiển thị / click (FE gửi theo lô, mỗi banner 1 view / phiên).</summary>
    [HttpPost("events")]
    [AllowAnonymous]
    [EnableRateLimiting("banner-events")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> TrackEvents([FromBody] BannerEventBatchDTO dto)
    {
        if (dto?.Events == null || dto.Events.Count == 0) return NoContent();
        await _service.RecordEventsAsync(dto.Events, CurrentUserId);
        return NoContent();
    }

    // ── Admin ─────────────────────────────────────────────────────────────────

    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetPaged([FromQuery] FilterBannersDTO filter)
    {
        var (items, total) = await _service.GetPagedAsync(filter);
        return Ok(new { items, total });
    }

    /// <summary>Thống kê hiệu quả: view, click, CTR, đơn tạo ra, đơn trả tiền, doanh thu.</summary>
    [HttpGet("admin/stats")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetStats([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        // `to` là ngày cuối (bao gồm) → cộng 1 ngày để thành mốc loại trừ
        var fromUtc = from.HasValue ? DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc) : (DateTime?)null;
        var toExclusive = to.HasValue ? DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc) : (DateTime?)null;
        return Ok(await _service.GetStatsAsync(fromUtc, toExclusive));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Get(Guid id)
    {
        var b = await _service.GetByIdAsync(id);
        return b == null ? NotFound() : Ok(b);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Create([FromForm] CreateBannerDTO dto)
    {
        try
        {
            var id = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(Get), new { id }, new { id });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Update(Guid id, [FromForm] UpdateBannerDTO dto)
    {
        try
        {
            var ok = await _service.UpdateAsync(id, dto);
            return ok ? NoContent() : NotFound();
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Bật/tắt nhanh (không cần gửi lại form).</summary>
    [HttpPatch("{id:guid}/active")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetBannerActiveDTO dto)
    {
        var ok = await _service.SetActiveAsync(id, dto.IsActive);
        return ok ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ok = await _service.DeleteAsync(id);
        return ok ? NoContent() : NotFound();
    }
}