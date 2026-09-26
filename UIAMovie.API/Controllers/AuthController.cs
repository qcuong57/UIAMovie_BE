using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;

namespace UIAMovie.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    // ─── Register ────────────────────────────────────────────────────────────

    [HttpPost("register")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> Register([FromBody] RegisterDTO dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new ApiErrorResponseDTO
            {
                Message = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault() ?? "Dữ liệu không hợp lệ",
                StatusCode = 400
            });

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
        if (!ModelState.IsValid)
            return BadRequest(new ApiErrorResponseDTO
            {
                Message = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault() ?? "Dữ liệu không hợp lệ",
                StatusCode = 400
            });

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
        {
            SetRefreshTokenCookie(result.RefreshToken);
            return Ok(new ApiResponseDTO<LoginResponseDTO>
            {
                Data    = result,
                Message = "Đăng nhập thành công"
            });
        }

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
        var success = await _authService.SendOtpAsync(dto.UserId);
        return success
            ? Ok(new ApiResponseDTO<object> { Message = "OTP đã được gửi đến email của bạn" })
            : BadRequest(new ApiErrorResponseDTO { Message = "Không tìm thấy user", StatusCode = 400 });
    }

    [HttpPost("otp/verify")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDTO dto)
    {
        var result = await _authService.VerifyOtpAsync(dto.UserId, dto.Code);

        if (result != null)
        {
            SetRefreshTokenCookie(result.RefreshToken);
            return Ok(new ApiResponseDTO<LoginResponseDTO>
            {
                Data    = result,
                Message = "Xác thực OTP thành công"
            });
        }

        return BadRequest(new ApiErrorResponseDTO
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
        var userId  = GetUserId();
        var success = await _authService.SendOtpAsync(userId);

        return success
            ? Ok(new ApiResponseDTO<object>
            {
                Message = "OTP đã gửi đến email, gọi /api/auth/2fa/confirm để hoàn tất bật 2FA"
            })
            : BadRequest(new ApiErrorResponseDTO
            {
                Message    = "Không thể gửi OTP",
                StatusCode = 400
            });
    }

    [HttpPost("2fa/confirm")]
    [Authorize]
    public async Task<IActionResult> ConfirmEnable2FA([FromBody] VerifyOtpDTO dto)
    {
        var userId = GetUserId();
        var (success, message) = await _authService.ConfirmEnable2FAAsync(userId, dto.Code);

        return success
            ? Ok(new ApiResponseDTO<object> { Message = message })
            : BadRequest(new ApiErrorResponseDTO { Message = message, StatusCode = 400 });
    }

    [HttpPost("2fa/disable")]
    [Authorize]
    public async Task<IActionResult> Disable2FA([FromBody] VerifyOtpDTO dto)
    {
        var userId             = GetUserId();
        var (success, message) = await _authService.Disable2FAAsync(userId, dto.Code);

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
        if (!ModelState.IsValid)
            return BadRequest(new ApiErrorResponseDTO
            {
                Message = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault() ?? "Dữ liệu không hợp lệ",
                StatusCode = 400
            });

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
    public async Task<IActionResult> RefreshToken()
    {
        // Tự động đọc Refresh Token từ HttpOnly Cookie an toàn
        var refreshToken = Request.Cookies["refreshToken"];

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

        SetRefreshTokenCookie(result.RefreshToken);

        return Ok(new ApiResponseDTO<LoginResponseDTO>
        {
            Data    = result,
            Message = "Refresh token thành công"
        });
    }

    // ─── Logout ──────────────────────────────────────────────────────────────

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _authService.LogoutAsync(GetUserId());
        DeleteRefreshTokenCookie();
        return Ok(new ApiResponseDTO<object> { Message = "Đăng xuất thành công" });
    }

    // ─── Helper Cookies & Claims ─────────────────────────────────────────────

    private void SetRefreshTokenCookie(string token)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,                                     // Chống đọc trộm bằng JS / XSS
            Secure = true,                                       // Luôn đi qua HTTPS
            SameSite = SameSiteMode.None,                        // Hỗ trợ gọi chéo domain/port giữa FE và BE
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/"
        };

        Response.Cookies.Append("refreshToken", token, cookieOptions);
    }

    private void DeleteRefreshTokenCookie()
    {
        Response.Cookies.Delete("refreshToken", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/"
        });
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(claim, out var userId))
            throw new UnauthorizedAccessException("Không xác định được người dùng từ token");

        return userId;
    }
}