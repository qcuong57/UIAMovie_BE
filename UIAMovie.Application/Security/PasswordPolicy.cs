namespace UIAMovie.Application.Services;

/// <summary>Quy tắc mật khẩu dùng chung cho đăng ký, đặt lại và đổi mật khẩu.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    // BCrypt chỉ băm 72 byte đầu; dài hơn sẽ bị cắt âm thầm.
    public const int MaxBytes = 72;

    /// <summary>Trả về thông báo lỗi, hoặc null nếu mật khẩu hợp lệ.</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return "Mật khẩu không được để trống";
        if (password.Length < MinLength)
            return $"Mật khẩu phải có ít nhất {MinLength} ký tự";
        if (System.Text.Encoding.UTF8.GetByteCount(password) > MaxBytes)
            return $"Mật khẩu quá dài (tối đa {MaxBytes} byte)";
        return null;
    }
}