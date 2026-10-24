// UIAMovie.API/Controllers/AdminPlanController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;

namespace UIAMovie.Controllers;

/// <summary>[Admin] Quản lý các gói Premium (tạo, sửa, bật/tắt bán). Không có xóa cứng để giữ lịch sử đơn.</summary>
[ApiController]
[Route("api/admin/plans")]
[Authorize(Roles = "Admin")]
public class AdminPlanController : ControllerBase
{
    private readonly ISubscriptionPlanService _plans;
    private readonly ILogger<AdminPlanController> _logger;

    public AdminPlanController(ISubscriptionPlanService plans, ILogger<AdminPlanController> logger)
    {
        _plans = plans;
        _logger = logger;
    }

    /// <summary>GET /api/admin/plans — tất cả gói (cả đã tắt)</summary>
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _plans.AdminListAsync());

    /// <summary>POST /api/admin/plans</summary>
    [HttpPost]
    public Task<IActionResult> Create([FromBody] UpsertPlanDTO dto)
        => Run(async () => Ok(await _plans.AdminCreateAsync(dto)));

    /// <summary>PUT /api/admin/plans/{id} — PlanId không đổi được</summary>
    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, [FromBody] UpsertPlanDTO dto)
        => Run(async () => Ok(await _plans.AdminUpdateAsync(id, dto)));

    /// <summary>PATCH /api/admin/plans/{id}/active — bật/tắt bán gói</summary>
    [HttpPatch("{id:guid}/active")]
    public Task<IActionResult> SetActive(Guid id, [FromBody] SetPlanActiveDTO dto)
        => Run(async () =>
        {
            await _plans.AdminSetActiveAsync(id, dto.IsActive);
            return Ok(new { message = dto.IsActive ? "Đã bật gói" : "Đã tắt gói" });
        });

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin plan operation failed");
            return StatusCode(500, new { message = "Có lỗi xảy ra. Vui lòng thử lại." });
        }
    }
}