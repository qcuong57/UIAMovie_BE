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
    public string SubscriptionType { get; set; }
    public string Role { get; set; } // ← Thêm Role
    public DateTime CreatedAt { get; set; }
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