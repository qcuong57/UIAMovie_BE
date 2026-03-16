namespace UIAMovie.Application.DTOs;

// Kết quả API thành công
public class ApiResponseDTO<T>
{
    public bool Success { get; set; } = true;
    public T Data { get; set; }
    public string Message { get; set; } = "Success";
}

// Kết quả API lỗi
public class ApiErrorResponseDTO
{
    public bool Success { get; set; } = false;
    public string Message { get; set; }
    public Dictionary<string, List<string>> Errors { get; set; }
    public int StatusCode { get; set; }
}

// Login Response
public class LoginResponseDTO
{
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public UserDTO User { get; set; }
    public DateTime ExpiresIn { get; set; }
}

// 2FA Response
public class TwoFactorResponseDTO
{
    public string Secret { get; set; }
    public string QrCodeUri { get; set; }
    public List<string> BackupCodes { get; set; }
}