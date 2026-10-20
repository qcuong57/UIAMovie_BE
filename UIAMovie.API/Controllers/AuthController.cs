using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Security;
using UIAMovie.Application.Services;

namespace UIAMovie.Controllers;

/// <summary>Danh sách origin của FE được phép gọi API (dùng chung với CORS, đăng ký trong Program.cs).</summary>
public sealed record AllowedOriginList(string[] Values);

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private const string RefreshCookieName = "refreshToken";

    // Cookie chỉ được gửi kèm các request tới /api/auth/* (refresh, logout, ...),
    // không đi theo mọi request API.
    private const string RefreshCookiePath = "/api/auth";

    // Path cũ của phiên bản trước; còn dọn để user cũ không bị giữ 2 cookie trùng tên.
    private const string LegacyCookiePath = "/";

    private static readonly TimeSpan RefreshCookieLifetime = TimeSpan.FromDays(7);

    private readonly IAuthService _authService;
    private readonly AllowedOriginList _allowedOrigins;

    public AuthController(IAuthService authService, AllowedOriginList allowedOrigins)
    {
        _authService    = authService;
        _allowedOrigins = allowedOrigins;
    }

    // ─── Register ────────────────────────────────────────────────────────────

    [HttpPost("register")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> Register([FromBody] RegisterDTO dto)
    {
        if (!ModelState.IsValid) return InvalidModel();

        if (dto.Password != dto.ConfirmPassword)
            return BadRequest(new ApiErrorResponseDTO { Message = "Mật khẩu xác nhận không khớp", StatusCode = 400 });

        var (success, message) = await _authService.RegisterAsync(
            dto.Email, dto.Username, dto.Password);

        return success
            ? Ok(new ApiResponseDTO<object>
            {
                Message = message,
                Data    = new { email = dto.Email, requiresOtp = true }
            })
            : BadRequest(new ApiErrorResponseDTO { Message = message, StatusCode = 400 });
    }

    [HttpPost("register/verify-otp")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> VerifyRegisterOtp([FromBody] VerifyRegisterOtpDTO dto)
    {
        if (!ModelState.IsValid) return InvalidModel();

        var (success, message) = await _authService.VerifyRegisterOtpAsync(dto.Email, dto.Code);

        return success
            ? Ok(new ApiResponseDTO<object> { Message = message })
            : BadRequest(new ApiErrorResponseDTO { Message = message, StatusCode = 400 });
    }

    // ─── Login ───────────────────────────────────────────────────────────────

    [HttpPost("login")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> Login([FromBody] LoginDTO dto)
    {
        var (result, pendingUserId, errorMessage, banReason) =
            await _authService.LoginAsync(dto.Email, dto.Password);

        if (result != null)
            return SessionOk(result, "Đăng nhập thành công");

        if (pendingUserId.HasValue)
            return Ok(new ApiResponseDTO<object>
            {
                Data    = new { requiresOtp = true, userId = pendingUserId },
                Message = "OTP đã được gửi đến email của bạn"
            });

        if (banReason != null)
            return Unauthorized(new ApiErrorResponseDTO
            {
                Message    = errorMessage ?? "Tài khoản đã bị khóa",
                BanReason  = banReason,
                StatusCode = 401
            });

        return Unauthorized(new ApiErrorResponseDTO
        {
            Message    = errorMessage ?? "Email hoặc mật khẩu không đúng",
            StatusCode = 401
        });
    }

    // ─── OTP ─────────────────────────────────────────────────────────────────

    [HttpPost("otp/send")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpDTO dto)
    {
        var result = await _authService.SendOtpAsync(dto.UserId);

        return OtpSendResponse(
            result,
            sentMessage:     "OTP đã được gửi đến email của bạn",
            notFoundMessage: "Phiên đăng nhập không hợp lệ hoặc đã hết hạn. Vui lòng đăng nhập lại.");
    }

    [HttpPost("otp/verify")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDTO dto)
    {
        var result = await _authService.VerifyOtpAsync(dto.UserId, dto.Code);

        return result != null
            ? SessionOk(result, "Xác thực OTP thành công")
            : BadRequest(new ApiErrorResponseDTO
            {
                Message    = "Mã OTP không đúng hoặc đã hết hạn",
                StatusCode = 400
            });
    }

    // ─── 2FA ─────────────────────────────────────────────────────────────────

    [HttpPost("2fa/enable")]
    [Authorize]
    public async Task<IActionResult> Enable2FA()
    {
        var result = await _authService.SendTwoFactorOtpAsync(GetUserId());

        return OtpSendResponse(
            result,
            sentMessage:     "OTP đã gửi đến email, gọi /api/auth/2fa/confirm để hoàn tất bật 2FA",
            notFoundMessage: "Không thể gửi OTP");
    }

    [HttpPost("2fa/confirm")]
    [Authorize]
    public async Task<IActionResult> ConfirmEnable2FA([FromBody] VerifyOtpDTO dto)
    {
        var (success, message) = await _authService.ConfirmEnable2FAAsync(GetUserId(), dto.Code);

        return success
            ? Ok(new ApiResponseDTO<object> { Message = message })
            : BadRequest(new ApiErrorResponseDTO { Message = message, StatusCode = 400 });
    }

    [HttpPost("2fa/disable")]
    [Authorize]
    public async Task<IActionResult> Disable2FA([FromBody] VerifyOtpDTO dto)
    {
        var (success, message) = await _authService.Disable2FAAsync(GetUserId(), dto.Code);

        return success
            ? Ok(new ApiResponseDTO<object> { Message = message })
            : BadRequest(new ApiErrorResponseDTO { Message = message, StatusCode = 400 });
    }

    // ─── Forgot / Reset Password ─────────────────────────────────────────────

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDTO dto)
    {
        await _authService.ForgotPasswordAsync(dto.Email);
        return Ok(new ApiResponseDTO<object>
        {
            Message = "Nếu email tồn tại, mã OTP đã được gửi"
        });
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDTO dto)
    {
        if (!ModelState.IsValid) return InvalidModel();

        if (dto.NewPassword != dto.ConfirmPassword)
            return BadRequest(new ApiErrorResponseDTO { Message = "Mật khẩu xác nhận không khớp", StatusCode = 400 });

        // Kiểm tra mật khẩu mới TRƯỚC khi đụng tới OTP, để mật khẩu yếu không làm mất OTP / báo sai lý do.
        if (PasswordPolicy.Validate(dto.NewPassword) is { } policyError)
            return BadRequest(new ApiErrorResponseDTO { Message = policyError, StatusCode = 400 });

        var success = await _authService.ResetPasswordAsync(
            dto.Email, dto.Code, dto.NewPassword);

        if (success)
        {
            DeleteRefreshTokenCookie();
            return Ok(new ApiResponseDTO<object> { Message = "Đặt lại mật khẩu thành công" });
        }

        return BadRequest(new ApiErrorResponseDTO
        {
            Message    = "Mã OTP không đúng hoặc đã hết hạn",
            StatusCode = 400
        });
    }

    // ─── Refresh Token (HttpOnly Cookie) ──────────────────────────────────────

    [HttpPost("refresh-token")]
    [EnableRateLimiting("auth-refresh")]
    public async Task<IActionResult> RefreshToken()
    {
        // Cookie dùng SameSite=None nên trình duyệt gửi kèm cả request từ site khác.
        // Chặn tại đây: request có Origin lạ thì không được phép xoay refresh token.
        if (!IsAllowedOrigin())
            return OriginForbidden();

        var refreshToken = Request.Cookies[RefreshCookieName];

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(new ApiErrorResponseDTO
            {
                Message    = "Không tìm thấy refresh token trong cookie",
                StatusCode = 401
            });
        }

        var result = await _authService.RefreshTokenAsync(refreshToken);

        if (result == null)
        {
            DeleteRefreshTokenCookie();
            return Unauthorized(new ApiErrorResponseDTO
            {
                Message    = "Refresh token không hợp lệ hoặc đã hết hạn",
                StatusCode = 401
            });
        }

        return SessionOk(result, "Refresh token thành công");
    }

    // ─── Logout ──────────────────────────────────────────────────────────────

    // Không yêu cầu access token: access token hết hạn vẫn phải đăng xuất và xóa cookie được.
    // Phiên bị thu hồi là phiên của refresh token trong cookie, không ảnh hưởng thiết bị khác.
    [HttpPost("logout")]
    [EnableRateLimiting("auth-refresh")]
    public async Task<IActionResult> Logout()
    {
        // Cookie SameSite=None: chặn site lạ ép người dùng đăng xuất (CSRF).
        if (!IsAllowedOrigin())
            return OriginForbidden();

        await _authService.LogoutAsync(Request.Cookies[RefreshCookieName]);
        DeleteRefreshTokenCookie();
        return Ok(new ApiResponseDTO<object> { Message = "Đăng xuất thành công" });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Ghi refresh token vào cookie HttpOnly và trả response cho client.
    /// Body KHÔNG chứa RefreshToken: JS (kể cả khi bị XSS) không đọc được nó.
    /// </summary>
    private IActionResult SessionOk(LoginResponseDTO result, string message)
    {
        if (string.IsNullOrEmpty(result.RefreshToken))
            throw new InvalidOperationException("Phiên đăng nhập không có refresh token để đặt cookie");

        SetRefreshTokenCookie(result.RefreshToken);

        return Ok(new ApiResponseDTO<object>
        {
            Message = message,
            Data    = new { result.AccessToken, result.ExpiresIn, result.User }
        });
    }

    private IActionResult OtpSendResponse(OtpSendResult result, string sentMessage, string notFoundMessage) =>
        result switch
        {
            OtpSendResult.Sent => Ok(new ApiResponseDTO<object> { Message = sentMessage }),
            OtpSendResult.TooSoon => StatusCode(StatusCodes.Status429TooManyRequests, new ApiErrorResponseDTO
            {
                Message    = "Vui lòng đợi một lát trước khi yêu cầu gửi lại mã.",
                StatusCode = 429
            }),
            _ => BadRequest(new ApiErrorResponseDTO { Message = notFoundMessage, StatusCode = 400 })
        };

    private IActionResult OriginForbidden() =>
        StatusCode(StatusCodes.Status403Forbidden, new ApiErrorResponseDTO
        {
            Message    = "Origin không được phép",
            StatusCode = 403
        });

    private IActionResult InvalidModel() =>
        BadRequest(new ApiErrorResponseDTO
        {
            Message = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault() ?? "Dữ liệu không hợp lệ",
            StatusCode = 400
        });

    private bool IsAllowedOrigin()
    {
        var origin = Request.Headers.Origin.ToString();

        // Không có Origin (request cùng origin hoặc không phải từ trình duyệt) thì cho qua.
        return string.IsNullOrEmpty(origin)
            || _allowedOrigins.Values.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    private static CookieOptions RefreshCookieOptions(string path) => new()
    {
        HttpOnly = true,                 // Chống đọc trộm bằng JS / XSS
        Secure   = true,                 // Luôn đi qua HTTPS
        SameSite = SameSiteMode.None,    // FE và BE khác domain; đổi sang Lax/Strict khi đã có proxy cùng domain
        Path     = path
    };

    private void SetRefreshTokenCookie(string token)
    {
        // Dọn cookie cũ ở Path "/" (nếu có) để không bị gửi 2 cookie trùng tên.
        // Đặt TRƯỚC Append để dòng cấp cookie mới luôn là dòng cuối cùng.
        Response.Cookies.Delete(RefreshCookieName, RefreshCookieOptions(LegacyCookiePath));

        var options = RefreshCookieOptions(RefreshCookiePath);
        options.Expires = DateTimeOffset.UtcNow.Add(RefreshCookieLifetime);

        Response.Cookies.Append(RefreshCookieName, token, options);
    }

    private void DeleteRefreshTokenCookie()
    {
        Response.Cookies.Delete(RefreshCookieName, RefreshCookieOptions(RefreshCookiePath));
        Response.Cookies.Delete(RefreshCookieName, RefreshCookieOptions(LegacyCookiePath));
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(claim, out var userId))
            throw new UnauthorizedAccessException("Không xác định được người dùng từ token");

        return userId;
    }
}