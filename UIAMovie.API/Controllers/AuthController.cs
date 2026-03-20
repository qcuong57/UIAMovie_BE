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

    /// <summary>Đăng ký tài khoản mới</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDTO dto)
    {
        var (success, message) = await _authService.RegisterAsync(
            dto.Email, dto.Username, dto.Password);

        return success ? Ok(new { message }) : BadRequest(new { message });
    }

    // ─── Login ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Đăng nhập.
    /// Nếu 2FA bật → OTP tự động gửi về email, response trả về userId để dùng cho /otp/verify.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDTO dto)
    {
        var (result, pendingUserId) = await _authService.LoginAsync(dto.Email, dto.Password);

        // Login thành công, không cần 2FA
        if (result != null)
            return Ok(result);

        // 2FA bật → OTP đã được gửi tự động
        if (pendingUserId.HasValue)
            return Ok(new
            {
                requiresOtp = true,
                userId      = pendingUserId,
                message     = "OTP đã được gửi đến email của bạn"
            });

        // Sai email/password
        return Unauthorized(new { message = "Email hoặc mật khẩu không đúng" });
    }

    // ─── OTP ─────────────────────────────────────────────────────────────────

    /// <summary>Gửi lại OTP (dùng khi OTP hết hạn hoặc không nhận được email)</summary>
    [HttpPost("otp/send")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpDTO dto)
    {
        var success = await _authService.SendOtpAsync(dto.UserId);
        return success
            ? Ok(new { message = "OTP đã được gửi đến email của bạn" })
            : BadRequest(new { message = "Không tìm thấy user" });
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
            ? Ok(result)
            : BadRequest(new { message = "Mã OTP không đúng hoặc đã hết hạn" });
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
        var userId = GetUserId();
        var success = await _authService.SendOtpAsync(userId);

        return success
            ? Ok(new { message = "OTP đã gửi đến email, gọi /otp/verify để bật 2FA" })
            : BadRequest(new { message = "Không thể gửi OTP" });
    }

    /// <summary>
    /// Tắt 2FA — xác thực OTP rồi set Is2FaEnabled = false.
    /// Body: { userId, code }
    /// </summary>
    [HttpPost("2fa/disable")]
    [Authorize]
    public async Task<IActionResult> Disable2FA([FromBody] VerifyOtpDTO dto)
    {
        var userId = GetUserId();
        var (success, message) = await _authService.Disable2FAAsync(userId, dto.Code);

        return success ? Ok(new { message }) : BadRequest(new { message });
    }

    // ─── Forgot / Reset Password ─────────────────────────────────────────────

    /// <summary>Quên mật khẩu — gửi OTP về email</summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDTO dto)
    {
        await _authService.ForgotPasswordAsync(dto.Email);
        // Luôn trả OK để không tiết lộ email có tồn tại hay không
        return Ok(new { message = "Nếu email tồn tại, mã OTP đã được gửi" });
    }

    /// <summary>Đặt lại mật khẩu bằng OTP nhận từ email</summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDTO dto)
    {
        if (dto.NewPassword != dto.ConfirmPassword)
            return BadRequest(new { message = "Mật khẩu xác nhận không khớp" });

        var success = await _authService.ResetPasswordAsync(
            dto.Email, dto.Code, dto.NewPassword);

        return success
            ? Ok(new { message = "Đặt lại mật khẩu thành công" })
            : BadRequest(new { message = "Mã OTP không đúng hoặc đã hết hạn" });
    }
    
    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenDTO dto)
    {
        var result = await _authService.RefreshTokenAsync(dto.RefreshToken);

        return result != null
            ? Ok(result)
            : Unauthorized(new { message = "Refresh token không hợp lệ hoặc đã hết hạn" });
    }

    // ─── Logout ──────────────────────────────────────────────────────────────

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _authService.LogoutAsync(GetUserId());
        return Ok(new { message = "Đăng xuất thành công" });
    }

    // ─── Helper ──────────────────────────────────────────────────────────────

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Guid.Empty.ToString());
}