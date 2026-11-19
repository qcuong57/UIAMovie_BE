// UIAMovie.API/Controllers/HomeSectionsController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;
using UIAMovie.Domain.Constants;

namespace UIAMovie.Controllers;

[ApiController]
public class HomeSectionsController : ControllerBase
{
    private readonly IHomeSectionService _service;

    public HomeSectionsController(IHomeSectionService service)
    {
        _service = service;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PUBLIC
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Các khối trang chủ đang hiển thị, đã sắp theo SortOrder, kèm nội dung đã resolve.</summary>
    [HttpGet("api/home-sections")]
    public async Task<IActionResult> GetPublic()
        => Ok(await _service.GetPublicAsync());

    // ═══════════════════════════════════════════════════════════════════
    // ADMIN
    // ═══════════════════════════════════════════════════════════════════

    [HttpGet("api/admin/home-sections")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AdminGetAll()
        => Ok(await _service.GetAllForAdminAsync());

    /// <summary>Danh sách layout / auto rule / content type để admin UI dựng dropdown.</summary>
    [HttpGet("api/admin/home-sections/meta")]
    [Authorize(Roles = Roles.Admin)]
    public IActionResult Meta()
        => Ok(_service.GetMeta());

    [HttpGet("api/admin/home-sections/{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AdminGetById(Guid id)
    {
        var section = await _service.GetByIdAsync(id);
        return section == null ? NotFound(NotFoundBody()) : Ok(section);
    }

    [HttpPost("api/admin/home-sections")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create([FromBody] UpsertHomeSectionDTO dto)
    {
        try
        {
            var id = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(AdminGetById), new { id }, new { id });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponseDTO { Message = ex.Message, StatusCode = 400 });
        }
    }

    [HttpPut("api/admin/home-sections/{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertHomeSectionDTO dto)
    {
        try
        {
            var ok = await _service.UpdateAsync(id, dto);
            return ok ? NoContent() : NotFound(NotFoundBody());
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponseDTO { Message = ex.Message, StatusCode = 400 });
        }
    }

    [HttpDelete("api/admin/home-sections/{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(Guid id)
        => await _service.DeleteAsync(id) ? NoContent() : NotFound(NotFoundBody());

    /// <summary>Kéo thả đổi thứ tự các khối: gửi toàn bộ id theo thứ tự mới.</summary>
    [HttpPut("api/admin/home-sections/reorder")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Reorder([FromBody] ReorderHomeSectionsDTO dto)
    {
        try
        {
            await _service.ReorderAsync(dto.SectionIds);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponseDTO { Message = ex.Message, StatusCode = 400 });
        }
    }

    /// <summary>Bật/tắt nhanh một khối.</summary>
    [HttpPatch("api/admin/home-sections/{id:guid}/active")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetHomeSectionActiveDTO dto)
        => await _service.SetActiveAsync(id, dto.IsActive) ? NoContent() : NotFound(NotFoundBody());

    private static ApiErrorResponseDTO NotFoundBody()
        => new() { Message = "Không tìm thấy khối trang chủ", StatusCode = 404 };
}