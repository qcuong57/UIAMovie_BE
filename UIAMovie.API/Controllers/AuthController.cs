// UIAMovie.API/Controllers/AuthController.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    /// <summary>
    /// Bước 1: Gửi thông tin đăng ký.
    /// Hệ thống gửi OTP về email, chưa lưu vào DB.
    /// Gọi /register/verify-otp để hoàn tất.
    /// </summary>
    [HttpPost("register")]
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

    /// <summary>
    /// Bước 2: Xác nhận OTP đăng ký.
    /// Đúng OTP → tạo User trong DB và trả về thành công.
    /// </summary>
    [HttpPost("register/verify-otp")]
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

    /// <summary>
    /// Đăng nhập.
    /// Nếu 2FA bật → OTP tự động gửi về email, response trả về userId để dùng cho /otp/verify.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDTO dto)
    {
        var (result, pendingUserId, errorMessage, banReason) =
            await _authService.LoginAsync(dto.Email, dto.Password);

        if (result != null)
            return Ok(new ApiResponseDTO<LoginResponseDTO>
            {
                Data    = result,
                Message = "Đăng nhập thành công"
            });

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

    /// <summary>Gửi lại OTP (dùng khi OTP hết hạn hoặc không nhận được email)</summary>
    [HttpPost("otp/send")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpDTO dto)
    {
        var success = await _authService.SendOtpAsync(dto.UserId);
        return success
            ? Ok(new ApiResponseDTO<object> { Message = "OTP đã được gửi đến email của bạn" })
            : BadRequest(new ApiErrorResponseDTO { Message = "Không tìm thấy user", StatusCode = 400 });
    }

    /// <summary>
    /// Xác thực OTP sau khi login (2FA) hoặc sau khi bật 2FA.
    /// Trả về accessToken + refreshToken nếu đúng.
    /// </summary>
    [HttpPost("otp/verify")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDTO dto)
    {
        var result = await _authService.VerifyOtpAsync(dto.UserId, dto.Code);

        return result != null
            ? Ok(new ApiResponseDTO<LoginResponseDTO>
            {
                Data    = result,
                Message = "Xác thực OTP thành công"
            })
            : BadRequest(new ApiErrorResponseDTO
            {
                Message    = "Mã OTP không đúng hoặc đã hết hạn",
                StatusCode = 400
            });
    }

    // ─── 2FA ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bật 2FA — gửi OTP về email để xác nhận.
    /// Sau đó gọi /otp/verify để hoàn tất.
    /// </summary>
    [HttpPost("2fa/enable")]
    [Authorize]
    public async Task<IActionResult> Enable2FA()
    {
        var userId  = GetUserId();
        var success = await _authService.SendOtpAsync(userId);

        return success
            ? Ok(new ApiResponseDTO<object>
            {
                Message = "OTP đã gửi đến email, gọi /otp/verify để bật 2FA"
            })
            : BadRequest(new ApiErrorResponseDTO
            {
                Message    = "Không thể gửi OTP",
                StatusCode = 400
            });
    }

    /// <summary>
    /// Tắt 2FA — xác thực OTP rồi set Is2FaEnabled = false.
    /// Body: { userId, code }
    /// </summary>
    [HttpPost("2fa/disable")]
    [Authorize]
    public async Task<IActionResult> Disable2FA([FromBody] VerifyOtpDTO dto)
    {
        var userId          = GetUserId();
        var (success, message) = await _authService.Disable2FAAsync(userId, dto.Code);

        return success
            ? Ok(new ApiResponseDTO<object> { Message = message })
            : BadRequest(new ApiErrorResponseDTO { Message = message, StatusCode = 400 });
    }

    // ─── Forgot / Reset Password ─────────────────────────────────────────────

    /// <summary>Quên mật khẩu — gửi OTP về email</summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDTO dto)
    {
        await _authService.ForgotPasswordAsync(dto.Email);
        // Luôn trả OK để không tiết lộ email có tồn tại hay không
        return Ok(new ApiResponseDTO<object>
        {
            Message = "Nếu email tồn tại, mã OTP đã được gửi"
        });
    }

    /// <summary>Đặt lại mật khẩu bằng OTP nhận từ email</summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDTO dto)
    {
        if (dto.NewPassword != dto.ConfirmPassword)
            return BadRequest(new ApiErrorResponseDTO
            {
                Message    = "Mật khẩu xác nhận không khớp",
                StatusCode = 400
            });

        var success = await _authService.ResetPasswordAsync(
            dto.Email, dto.Code, dto.NewPassword);

        return success
            ? Ok(new ApiResponseDTO<object> { Message = "Đặt lại mật khẩu thành công" })
            : BadRequest(new ApiErrorResponseDTO
            {
                Message    = "Mã OTP không đúng hoặc đã hết hạn",
                StatusCode = 400
            });
    }

    // ─── Refresh Token ───────────────────────────────────────────────────────

    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenDTO dto)
    {
        var result = await _authService.RefreshTokenAsync(dto.RefreshToken);

        return result != null
            ? Ok(new ApiResponseDTO<LoginResponseDTO>
            {
                Data    = result,
                Message = "Refresh token thành công"
            })
            : Unauthorized(new ApiErrorResponseDTO
            {
                Message    = "Refresh token không hợp lệ hoặc đã hết hạn",
                StatusCode = 401
            });
    }

    // ─── Logout ──────────────────────────────────────────────────────────────

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _authService.LogoutAsync(GetUserId());
        return Ok(new ApiResponseDTO<object> { Message = "Đăng xuất thành công" });
    }

    // ─── Helper ──────────────────────────────────────────────────────────────

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Guid.Empty.ToString());
}