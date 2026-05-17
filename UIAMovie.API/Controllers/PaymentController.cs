// UIAMovie.API/Controllers/PaymentController.cs

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;

namespace UIAMovie.API.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentController> _logger;

    public PaymentController(IPaymentService paymentService, ILogger<PaymentController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    // ── Plans ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lấy danh sách gói subscription để hiển thị cho user chọn.
    /// GET /api/payments/plans
    /// </summary>
    [HttpGet("plans")]
    public IActionResult GetPlans()
    {
        var plans = _paymentService.GetSubscriptionPlans();
        return Ok(plans);
    }

    // ── Bước 1: Tạo order ─────────────────────────────────────────────────────

    /// <summary>
    /// User chọn gói → tạo PaymentOrder → trả về URL redirect sang VNPay.
    /// POST /api/payments/create-order
    /// Body: { "planId": "monthly_premium", "paymentProvider": "vnpay" }
    /// </summary>
    [Authorize]
    [HttpPost("create-order")]
    public async Task<IActionResult> CreateOrder([FromBody] CreatePaymentOrderDTO dto)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        // Lấy IP thật của client (cần cho VNPay)
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

        if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
            ipAddress = forwardedFor.ToString().Split(',')[0].Trim();

        // Convert ::1 (IPv6 loopback) sang 127.0.0.1
        if (ipAddress == "::1") ipAddress = "127.0.0.1";

        // Build backendBaseUrl từ Request thay vì cấu hình cứng trong appsettings
        // VD: "https://api.yourdomain.com" hoặc "http://localhost:5000" khi dev
        var backendBaseUrl = $"{Request.Scheme}://{Request.Host}";

        try
        {
            var result = await _paymentService.CreateOrderAsync(userId.Value, dto, ipAddress, backendBaseUrl);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateOrder failed for user {UserId}", userId);
            return StatusCode(500, new { message = "Không thể tạo đơn thanh toán. Vui lòng thử lại." });
        }
    }

    // ── Bước 2a: VNPay IPN (server-to-server) ────────────────────────────────

    /// <summary>
    /// VNPay gọi endpoint này sau khi user thanh toán xong.
    /// GET /api/payments/vnpay/ipn  (VNPay dùng GET, không phải POST)
    /// KHÔNG dùng [Authorize].
    /// </summary>
    [HttpGet("vnpay/ipn")]
    public async Task<IActionResult> VnpayIpn([FromQuery] VnpayIpnDTO ipn)
    {
        _logger.LogInformation("VNPay IPN received: txnRef={TxnRef}", ipn.vnp_TxnRef);

        var success = await _paymentService.HandleVnpayIpnAsync(ipn);

        // VNPay yêu cầu trả về JSON { RspCode, Message }
        return success
            ? Ok(new { RspCode = "00", Message = "Confirm Success" })
            : Ok(new { RspCode = "97", Message = "Invalid Signature" });
    }

    /// <summary>
    /// URL backend nhận redirect từ VNPay sau khi user hoàn tất thanh toán.
    /// GET /api/payments/vnpay/return
    ///
    /// Xử lý IPN (idempotent) rồi redirect về React frontend.
    /// frontendBase ưu tiên biến môi trường FRONTEND_URL, fallback về localhost:3000.
    /// </summary>
    [HttpGet("vnpay/return")]
    public async Task<IActionResult> VnpayReturn([FromQuery] VnpayIpnDTO ipn)
    {
        await _paymentService.HandleVnpayIpnAsync(ipn);

        var status    = ipn.vnp_ResponseCode == "00" ? "success" : "failed";
        var orderCode = ipn.vnp_TxnRef;

        var frontendBase = Environment.GetEnvironmentVariable("FRONTEND_URL")
                           ?? "http://localhost:5173";

        var redirectUrl = $"{frontendBase.TrimEnd('/')}/payment/result?status={status}&orderCode={orderCode}";

        _logger.LogInformation("VNPay Return → redirecting to {Url}", redirectUrl);
        return Redirect(redirectUrl);
    }

    // ── Query ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lịch sử thanh toán của user đang đăng nhập.
    /// GET /api/payments/history
    /// </summary>
    [Authorize]
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var history = await _paymentService.GetPaymentHistoryAsync(userId.Value);
        return Ok(history);
    }

    /// <summary>
    /// Trạng thái subscription hiện tại của user.
    /// GET /api/payments/subscription-status
    /// </summary>
    [Authorize]
    [HttpGet("subscription-status")]
    public async Task<IActionResult> GetSubscriptionStatus()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var status = await _paymentService.GetSubscriptionStatusAsync(userId.Value);
        return Ok(status);
    }


    // ── Admin: Cấp / Gia hạn Premium ─────────────────────────────────────────

    /// <summary>
    /// [Admin] Cấp hoặc gia hạn Premium cho user bất kỳ.
    /// POST /api/payments/admin/grant-subscription
    /// Body: { "userId": "...", "planId": "monthly_premium"|"yearly_premium", "expiredAt": "2026-12-31T00:00:00Z" }
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("admin/grant-subscription")]
    public async Task<IActionResult> AdminGrantSubscription([FromBody] AdminGrantSubscriptionDTO dto)
    {
        try
        {
            await _paymentService.AdminGrantSubscriptionAsync(dto);
            return Ok(new { message = "Cấp Premium thành công" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdminGrantSubscription failed for user {UserId}", dto.UserId);
            return StatusCode(500, new { message = "Có lỗi xảy ra. Vui lòng thử lại." });
        }
    }

    // ── Admin: Thu hồi Premium ────────────────────────────────────────────────

    /// <summary>
    /// [Admin] Thu hồi Premium của user.
    /// DELETE /api/payments/admin/subscription/{userId}
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpDelete("admin/subscription/{userId:guid}")]
    public async Task<IActionResult> AdminRevokeSubscription(Guid userId)
    {
        try
        {
            await _paymentService.AdminRevokeSubscriptionAsync(userId);
            return Ok(new { message = "Thu hồi Premium thành công" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdminRevokeSubscription failed for user {UserId}", userId);
            return StatusCode(500, new { message = "Có lỗi xảy ra. Vui lòng thử lại." });
        }
    }
    
     /// <summary>
    /// [Admin] Tổng quan doanh thu: tổng tiền, MoM growth, active Premium users...
    /// GET /api/payments/admin/revenue/summary
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("admin/revenue/summary")]
    public async Task<IActionResult> GetRevenueSummary()
    {
        try
        {
            var summary = await _paymentService.GetRevenueSummaryAsync();
            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetRevenueSummary failed");
            return StatusCode(500, new { message = "Không thể lấy dữ liệu doanh thu." });
        }
    }
 
    /// <summary>
    /// [Admin] Biểu đồ doanh thu theo ngày hoặc tháng.
    ///
    /// Ví dụ:
    ///   GET /api/payments/admin/revenue/chart?groupBy=month&amp;year=2024
    ///   GET /api/payments/admin/revenue/chart?groupBy=day&amp;year=2024&amp;month=5
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("admin/revenue/chart")]
    public async Task<IActionResult> GetRevenueChart(
        [FromQuery] string groupBy = "month",
        [FromQuery] int    year    = 0,
        [FromQuery] int?   month   = null)
    {
        if (year == 0) year = DateTime.UtcNow.Year;
 
        if (groupBy != "day" && groupBy != "month")
            return BadRequest(new { message = "groupBy phải là 'day' hoặc 'month'." });
 
        if (groupBy == "day" && month == null)
            return BadRequest(new { message = "Cần truyền month khi groupBy=day." });
 
        try
        {
            var chart = await _paymentService.GetRevenueChartAsync(groupBy, year, month);
            return Ok(chart);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetRevenueChart failed");
            return StatusCode(500, new { message = "Không thể lấy dữ liệu biểu đồ." });
        }
    }
 
    /// <summary>
    /// [Admin] Doanh thu split theo từng gói (monthly_premium / yearly_premium).
    ///
    /// GET /api/payments/admin/revenue/by-plan
    /// GET /api/payments/admin/revenue/by-plan?from=2024-01-01&amp;to=2024-12-31
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("admin/revenue/by-plan")]
    public async Task<IActionResult> GetRevenueByPlan(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to   = null)
    {
        try
        {
            var result = await _paymentService.GetRevenueByPlanAsync(from, to);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetRevenueByPlan failed");
            return StatusCode(500, new { message = "Không thể lấy dữ liệu theo gói." });
        }
    }
 
    /// <summary>
    /// [Admin] Danh sách tất cả đơn hàng — filter, search, phân trang.
    ///
    /// GET /api/payments/admin/orders
    /// GET /api/payments/admin/orders?status=success&amp;planId=monthly_premium&amp;page=1&amp;pageSize=20
    /// GET /api/payments/admin/orders?search=user@email.com
    /// GET /api/payments/admin/orders?from=2024-05-01&amp;to=2024-05-31
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("admin/orders")]
    public async Task<IActionResult> GetAdminOrders([FromQuery] AdminOrderFilterDTO filter)
    {
        try
        {
            var result = await _paymentService.GetAdminOrdersAsync(filter);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetAdminOrders failed");
            return StatusCode(500, new { message = "Không thể lấy danh sách đơn hàng." });
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private Guid? GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirstValue("sub");

        return Guid.TryParse(claim, out var id) ? id : null;
    }
}