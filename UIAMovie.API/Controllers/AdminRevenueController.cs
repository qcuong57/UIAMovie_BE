// UIAMovie.API/Controllers/AdminRevenueController.cs
// Giữ NGUYÊN URL cũ (api/payments/admin/...) nên frontend không phải đổi đường dẫn.

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.API.Services;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;

namespace UIAMovie.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/payments/admin")]
public class AdminRevenueController : ControllerBase
{
    private readonly IRevenueService _revenue;
    private readonly ILogger<AdminRevenueController> _logger;

    public AdminRevenueController(IRevenueService revenue, ILogger<AdminRevenueController> logger)
    {
        _revenue = revenue;
        _logger  = logger;
    }

    /// <summary>GET /api/payments/admin/revenue/summary</summary>
    [HttpGet("revenue/summary")]
    public Task<IActionResult> GetSummary(CancellationToken ct) =>
        Run(async () => Ok(await _revenue.GetSummaryAsync(ct)), "Không thể lấy dữ liệu doanh thu.");

    /// <summary>
    /// GET /api/payments/admin/revenue/chart?groupBy=month&amp;year=2026
    /// GET /api/payments/admin/revenue/chart?groupBy=day&amp;year=2026&amp;month=10
    /// </summary>
    [HttpGet("revenue/chart")]
    public Task<IActionResult> GetChart(
        [FromQuery] string groupBy = "month", [FromQuery] int year = 0, [FromQuery] int? month = null,
        CancellationToken ct = default)
    {
        if (year == 0) year = DateTime.UtcNow.AddHours(7).Year;

        if (groupBy is not ("day" or "month"))
            return Task.FromResult<IActionResult>(BadRequest(new { message = "groupBy phải là 'day' hoặc 'month'." }));
        if (groupBy == "day" && month == null)
            return Task.FromResult<IActionResult>(BadRequest(new { message = "Cần truyền month khi groupBy=day." }));
        if (year is < 2000 or > 2100)
            return Task.FromResult<IActionResult>(BadRequest(new { message = "year không hợp lệ." }));
        if (month is < 1 or > 12)
            return Task.FromResult<IActionResult>(BadRequest(new { message = "month phải từ 1 đến 12." }));

        return Run(async () => Ok(await _revenue.GetChartAsync(groupBy, year, month, ct)),
            "Không thể lấy dữ liệu biểu đồ.");
    }

    /// <summary>GET /api/payments/admin/revenue/by-plan?from=2026-01-01&amp;to=2026-12-31 (ngày VN, gồm cả ngày to)</summary>
    [HttpGet("revenue/by-plan")]
    public Task<IActionResult> GetByPlan([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) =>
        Run(async () => Ok(await _revenue.GetByPlanAsync(from, to, ct)), "Không thể lấy dữ liệu theo gói.");

    /// <summary>GET /api/payments/admin/orders?status=success&amp;planId=monthly_premium&amp;page=1&amp;pageSize=20</summary>
    [HttpGet("orders")]
    public Task<IActionResult> GetOrders([FromQuery] AdminOrderFilterDTO filter, CancellationToken ct) =>
        Run(async () => Ok(await _revenue.GetOrdersAsync(filter, ct)), "Không thể lấy danh sách đơn hàng.");

    /// <summary>
    /// GET /api/payments/admin/revenue/export?from=2026-10-01&amp;to=2026-10-31&amp;status=success
    /// Trả file .xlsx (3 sheet: Tổng quan / Theo gói / Đơn hàng).
    /// </summary>
    [HttpGet("revenue/export")]
    public async Task<IActionResult> Export([FromQuery] RevenueExportFilterDTO filter, CancellationToken ct)
    {
        try
        {
            var data  = await _revenue.GetExportAsync(filter, ct);
            var bytes = RevenueExcelExporter.Build(data);

            // File chứa email khách hàng (dữ liệu cá nhân) → ghi log ai xuất, khi nào, bao nhiêu dòng
            _logger.LogInformation(
                "[Audit] Revenue export by admin {AdminId}: from={From} to={To} status={Status} rows={Rows}",
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"),
                filter.From, filter.To, filter.Status ?? "success", data.Orders.Count);

            var range = filter.From is null && filter.To is null
                ? "toan-thoi-gian"
                : $"{filter.From:yyyyMMdd}_{filter.To:yyyyMMdd}";

            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"doanh-thu_{range}.xlsx");
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Revenue export failed");
            return StatusCode(500, new { message = "Không thể xuất báo cáo." });
        }
    }

    // ── Helper: gom try/catch lặp lại ở mọi endpoint ─────────────────────────

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action, string errorMessage)
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
            _logger.LogError(ex, "{Message}", errorMessage);
            return StatusCode(500, new { message = errorMessage });
        }
    }
}