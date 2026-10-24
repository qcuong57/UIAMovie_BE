// UIAMovie.API/Controllers/PaymentController.cs

using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.API.Services;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;

namespace UIAMovie.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentController> _logger;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;

    public PaymentController(
        IPaymentService paymentService,
        ILogger<PaymentController> logger,
        IConfiguration config,
        IHostEnvironment env)
    {
        _paymentService = paymentService;
        _logger = logger;
        _config = config;
        _env = env;
    }

    // ── Plans ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/payments/plans — các gói đang bán (cấu hình bởi admin).
    /// Không bắt buộc đăng nhập; nếu request có token hợp lệ thì mỗi gói kèm isEligible / ineligibleReason
    /// (đã dùng thử, sai domain email, ...) để FE ẩn hoặc làm mờ nút mua.
    /// </summary>
    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await _paymentService.GetSubscriptionPlansAsync(GetUserId());
        return Ok(plans);
    }

    // ── Bước 1: Tạo order ─────────────────────────────────────────────────────

    /// <summary>
    /// User chọn gói → kiểm tra điều kiện → tạo PaymentOrder → trả về thông tin chuyển sang cổng thanh toán.
    /// POST /api/payments/create-order
    /// Body: { "planId": "monthly_premium", "paymentProvider": "vnpay" | "sepay" }
    /// Response.paymentMethod = "redirect"  → location.href = paymentUrl
    /// Response.paymentMethod = "form_post" → POST form tới paymentUrl với formFields
    /// Response.paymentMethod = "none"      → gói miễn phí/dùng thử đã kích hoạt xong, KHÔNG redirect
    /// 400 + { message } khi gói không tồn tại / ngừng bán / user không đủ điều kiện.
    /// </summary>
    [Authorize]
    [HttpPost("create-order")]
    public async Task<IActionResult> CreateOrder([FromBody] CreatePaymentOrderDTO dto)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        // IP client: chỉ tin RemoteIpAddress đã được ForwardedHeaders middleware xử lý
        // (middleware chỉ tin proxy trong KnownNetworks), không tự đọc X-Forwarded-For.
        var ip = HttpContext.Connection.RemoteIpAddress;
        if (ip is null || IPAddress.IsLoopback(ip)) ip = IPAddress.Loopback;
        else if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var ipAddress = ip.ToString();

        // Base URL lấy từ cấu hình, không lấy từ Request.Host (Host header có thể bị giả)
        var backendBaseUrl = _config["Backend:PublicUrl"]?.TrimEnd('/');
        if (string.IsNullOrEmpty(backendBaseUrl))
        {
            if (!_env.IsDevelopment())
                return StatusCode(500, new { message = "Thiếu cấu hình Backend:PublicUrl." });
            backendBaseUrl = $"{Request.Scheme}://{Request.Host}";
        }

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

        // VNPay yêu cầu luôn trả JSON { RspCode, Message } với mã đúng theo từng tình huống.
        try
        {
            var result = await _paymentService.HandleVnpayIpnAsync(ipn, GetVnpayQuery());
            return result switch
            {
                PaymentCallbackResult.Confirmed => Ok(new { RspCode = "00", Message = "Confirm Success" }),
                PaymentCallbackResult.AlreadyConfirmed => Ok(
                    new { RspCode = "02", Message = "Order already confirmed" }),
                PaymentCallbackResult.OrderNotFound => Ok(new { RspCode = "01", Message = "Order not found" }),
                PaymentCallbackResult.InvalidAmount => Ok(new { RspCode = "04", Message = "Invalid amount" }),
                PaymentCallbackResult.InvalidSignature => Ok(new { RspCode = "97", Message = "Invalid Signature" }),
                _ => Ok(new { RspCode = "99", Message = "Processing, please retry" })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VNPay IPN failed for txnRef={TxnRef}", ipn.vnp_TxnRef);
            return Ok(new { RspCode = "99", Message = "Unknown error" });
        }
    }

    // ── Bước 2b: Return URL (trình duyệt) ────────────────────────────────────

    /// <summary>
    /// URL backend nhận redirect từ VNPay sau khi user hoàn tất thanh toán.
    /// GET /api/payments/vnpay/return
    ///
    /// Xử lý callback (idempotent, có khóa) rồi redirect về frontend. `status` trên URL chỉ để hiển thị:
    /// chữ ký sai / không thấy đơn / sai số tiền đều redirect với status=failed.
    /// FE vẫn nên gọi /subscription-status hoặc /history để xác nhận kết quả thật.
    ///
    /// Frontend base lấy từ Frontend:Url (config) hoặc biến môi trường FRONTEND_URL;
    /// chỉ fallback localhost khi chạy Development.
    /// </summary>
    [HttpGet("vnpay/return")]
    public async Task<IActionResult> VnpayReturn([FromQuery] VnpayIpnDTO ipn)
    {
        var frontendBase = ResolveFrontendBase();
        if (frontendBase == null)
        {
            _logger.LogError("VNPay Return: thiếu cấu hình Frontend:Url / FRONTEND_URL");
            return StatusCode(500, new { message = "Thiếu cấu hình Frontend URL." });
        }

        var orderCode = ipn.vnp_TxnRef;
        string status;

        try
        {
            var result = await _paymentService.HandleVnpayIpnAsync(ipn, GetVnpayQuery());

            var trusted = result is PaymentCallbackResult.Confirmed
                or PaymentCallbackResult.AlreadyConfirmed
                or PaymentCallbackResult.Busy;

            status = trusted && ipn.vnp_ResponseCode == "00" && ipn.vnp_TransactionStatus == "00"
                ? "success"
                : "failed";
        }
        catch (Exception ex)
        {
            // Luôn đưa user về FE kể cả khi xử lý lỗi; IPN của VNPay sẽ gửi lại để xử lý.
            _logger.LogError(ex, "VNPay Return failed for txnRef={TxnRef}", orderCode);
            status = "failed";
        }

        var redirectUrl =
            $"{frontendBase.TrimEnd('/')}/payment/result?status={status}&orderCode={Uri.EscapeDataString(orderCode)}";

        _logger.LogInformation("VNPay Return → redirecting to {Url}", redirectUrl);
        return Redirect(redirectUrl);
    }

    // ── Bước 2c: SePay IPN (server-to-server) ────────────────────────────────

    /// <summary>
    /// SePay gọi endpoint này sau khi thanh toán thành công.
    /// POST /api/payments/sepay/ipn   (JSON body, header X-Secret-Key)
    /// KHÔNG dùng [Authorize]. Cấu hình URL này (HTTPS công khai) ở my.sepay.vn → Cổng thanh toán → Cấu hình → IPN.
    /// SePay cần HTTP 200 để coi là đã nhận.
    /// </summary>
    [HttpPost("sepay/ipn")]
    public async Task<IActionResult> SepayIpn([FromBody] SepayIpnDTO ipn)
    {
        try
        {
            var secret = Request.Headers["X-Secret-Key"].FirstOrDefault();
            var result = await _paymentService.HandleSepayIpnAsync(ipn, secret);

            return result switch
            {
                PaymentCallbackResult.Confirmed
                    or PaymentCallbackResult.AlreadyConfirmed
                    or PaymentCallbackResult.Ignored => Ok(new { success = true }),
                PaymentCallbackResult.InvalidSignature => Unauthorized(
                    new { success = false, message = "Unauthorized" }),
                PaymentCallbackResult.Busy => StatusCode(409,
                    new { success = false, message = "Processing, please retry" }),
                // OrderNotFound / InvalidAmount: gửi lại cũng không sửa được → 200 để SePay ngừng gửi (đã log để xem tay)
                _ => Ok(new { success = false })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SePay IPN failed");
            return StatusCode(500, new { success = false, message = "Unknown error" });
        }
    }

    // ── Bước 2d: SePay Return URL (trình duyệt) ──────────────────────────────

    /// <summary>
    /// SePay redirect trình duyệt về đây (success_url / error_url / cancel_url được ký trong form checkout).
    /// GET /api/payments/sepay/return/{orderCode}/{result}   result: success | error | cancel
    ///
    /// KHÔNG nâng gói ở đây: URL này không mang dữ liệu đáng tin (ai cũng gọi giả được).
    /// Chỉ IPN mới xác nhận thanh toán. Vì IPN có thể đến sau redirect vài giây, với "success"
    /// ta trả status=pending_confirm → FE poll /subscription-status (hoặc /history) để biết kết quả thật.
    /// </summary>
    [HttpGet("sepay/return/{orderCode}/{result}")]
    public IActionResult SepayReturn(string orderCode, string result)
    {
        var frontendBase = ResolveFrontendBase();
        if (frontendBase == null)
        {
            _logger.LogError("SePay Return: thiếu cấu hình Frontend:Url / FRONTEND_URL");
            return StatusCode(500, new { message = "Thiếu cấu hình Frontend URL." });
        }

        var status = result switch
        {
            "success" => "pending_confirm",
            "cancel" => "cancelled",
            _ => "failed"
        };

        var redirectUrl =
            $"{frontendBase.TrimEnd('/')}/payment/result?status={status}&orderCode={Uri.EscapeDataString(orderCode)}";

        _logger.LogInformation("SePay Return ({Result}) → redirecting to {Url}", result, redirectUrl);
        return Redirect(redirectUrl);
    }

    // ── Query ─────────────────────────────────────────────────────────────────

    /// <summary>GET /api/payments/history</summary>
    [Authorize]
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var history = await _paymentService.GetPaymentHistoryAsync(userId.Value);
        return Ok(history);
    }

    /// <summary>GET /api/payments/subscription-status — kèm planId/planName của gói đang dùng (null nếu admin cấp tay)</summary>
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
    /// Body: { "userId": "...", "planId": "&lt;mã gói bất kỳ trong SubscriptionPlans&gt;", "expiredAt": "2026-12-31T00:00:00Z" }
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

    /// <summary>[Admin] DELETE /api/payments/admin/subscription/{userId}</summary>
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

    // ── User: Tự hủy subscription ─────────────────────────────────────────────

    /// <summary>User tự hủy gói Premium của mình. DELETE /api/payments/subscription/me</summary>
    [Authorize]
    [HttpDelete("subscription/me")]
    public async Task<IActionResult> CancelMySubscription()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        try
        {
            await _paymentService.CancelMySubscriptionAsync(userId.Value);
            return Ok(new { message = "Hủy gói Premium thành công." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CancelMySubscription failed for user {UserId}", userId);
            return StatusCode(500, new { message = "Không thể hủy gói. Vui lòng thử lại." });
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Toàn bộ tham số vnp_* trên query string gốc, dùng để xác thực chữ ký
    /// (không phụ thuộc DTO nên VNPay thêm tham số mới vẫn đúng).
    /// </summary>
    private Dictionary<string, string> GetVnpayQuery() =>
        Request.Query
            .Where(kv => kv.Key.StartsWith("vnp_", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.Ordinal);

    /// <summary>
    /// Frontend base URL từ Frontend:Url (config) hoặc biến môi trường FRONTEND_URL;
    /// chỉ fallback localhost khi chạy Development. Trả null nếu thiếu cấu hình ở môi trường khác.
    /// </summary>
    private string? ResolveFrontendBase()
    {
        var frontendBase = _config["Frontend:Url"]
                           ?? Environment.GetEnvironmentVariable("FRONTEND_URL");

        if (!string.IsNullOrWhiteSpace(frontendBase)) return frontendBase;
        return _env.IsDevelopment() ? "http://localhost:5173" : null;
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirstValue("sub");

        return Guid.TryParse(claim, out var id) ? id : null;
    }
}