using System.ComponentModel.DataAnnotations;

namespace UIAMovie.Application.DTOs;

public class RegisterDTO
{
    public string Email { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string ConfirmPassword { get; set; }
}

public class LoginDTO
{
    public string Email { get; set; }
    public string Password { get; set; }
}

public class VerifyRegisterOtpDTO
{
    [Required(ErrorMessage = "Email không được để trống")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string Email { get; set; } = string.Empty;
 
    [Required(ErrorMessage = "Mã OTP không được để trống")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "Mã OTP phải đúng 6 chữ số")]
    public string Code { get; set; } = string.Empty;
}


public class Verify2FADTO
{
    public string Code { get; set; }
}

public class SendOtpDTO
{
    public Guid UserId { get; set; }
}

public class VerifyOtpDTO
{
    public Guid UserId { get; set; }
    public string Code { get; set; }
}

public class ForgotPasswordDTO
{
    public string Email { get; set; }
}

public class ResetPasswordDTO
{
    public string Email { get; set; }
    public string Code { get; set; }
    public string NewPassword { get; set; }
    public string ConfirmPassword { get; set; }
}

// ─── User ────────────────────────────────────────────────────────────────────

public class UserDTO
{
    public Guid Id { get; set; }
    public string Email { get; set; }
    public string Username { get; set; }
    public string? AvatarUrl { get; set; }
    public string? SubscriptionType { get; set; }
    public DateTime? SubscriptionExpiredAt { get; set; }   // ← Ngày hết hạn Premium
    public DateTime? SubscriptionStartedAt { get; set; }   // ← Ngày kích hoạt Premium
    public string Role { get; set; }
    public bool Is2FaEnabled { get; set; } // ← Trạng thái 2FA
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public string? BanReason { get; set; }
}

public class UpdateUserDTO
{
    public string? Username { get; set; }
    public string? AvatarUrl { get; set; }
    public string? SubscriptionType { get; set; }
}

public class UpdateRoleDTO
{
    /// <summary>Admin hoặc User</summary>
    public string Role { get; set; }
}

public class ChangePasswordDTO
{
    public string OldPassword { get; set; }
    public string NewPassword { get; set; }
    public string ConfirmPassword { get; set; }
}

// ─── Query ───────────────────────────────────────────────────────────────────

public class UserQueryDTO
{
    public string? Search { get; set; }
    public string? SubscriptionType { get; set; }
    public string? Role { get; set; } // ← Lọc theo role
    public bool? IsActive { get; set; }
    public string? SortBy { get; set; } = "createdAt";
    public bool SortDesc { get; set; } = true;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
public class RefreshTokenDTO
{
    public string RefreshToken { get; set; }
}

public class BanUserDTO
{
    /// <summary>Lý do khóa — không bắt buộc nhưng nên điền</summary>
    [MaxLength(500, ErrorMessage = "Lý do tối đa 500 ký tự")]
    public string? Reason { get; set; }
}