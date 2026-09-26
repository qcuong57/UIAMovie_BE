using System.Security.Cryptography;
using System.Text;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Constants;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Services;

public interface IAuthService
{
    Task<(bool Success, string Message)> RegisterAsync(string email, string username, string password);
    Task<(bool Success, string Message)> VerifyRegisterOtpAsync(string email, string code);
    Task<(LoginResponseDTO? Response, Guid? PendingUserId, string? ErrorMessage, string? BanReason)> LoginAsync(string email, string password);
    Task<bool> SendOtpAsync(Guid userId);
    Task<LoginResponseDTO?> VerifyOtpAsync(Guid userId, string code);
    Task<(bool Success, string Message)> ConfirmEnable2FAAsync(Guid userId, string code);
    Task<(bool Success, string Message)> Disable2FAAsync(Guid userId, string code);
    Task LogoutAsync(Guid userId);
    Task<bool> ForgotPasswordAsync(string email);
    Task<bool> ResetPasswordAsync(string email, string code, string newPassword);
    Task<LoginResponseDTO?> RefreshTokenAsync(string refreshToken);
}

public class AuthService : IAuthService
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<UserSession> _sessionRepository;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IEmailService _emailService;
    private readonly ICacheService _cacheService;

    private const string OTP_PREFIX              = "otp:";
    private const string RESET_PREFIX            = "reset:";
    private const string USER_EMAIL_PREFIX       = "user:email:";
    private const string USER_ID_PREFIX          = "user:id:";
    private const string REGISTER_OTP_PREFIX     = "register:otp:";
    private const string REGISTER_PENDING_PREFIX = "register:pending:";

    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan RegisterOtpLifetime  = TimeSpan.FromMinutes(10);

    // Hash dummy chống Timing Attack + User Enumeration khi login
    private static readonly string DummyPasswordHash =
        BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), workFactor: 12);

    private const int OtpMaxAttempts = 5;
    private static readonly TimeSpan OtpAttemptWindow = TimeSpan.FromMinutes(15);

    public AuthService(
        IRepository<User> userRepository,
        IRepository<UserSession> sessionRepository,
        IJwtTokenGenerator jwtTokenGenerator,
        IEmailService emailService,
        ICacheService cacheService)
    {
        _userRepository    = userRepository;
        _sessionRepository = sessionRepository;
        _jwtTokenGenerator = jwtTokenGenerator;
        _emailService      = emailService;
        _cacheService      = cacheService;
    }

    // ─── Register ────────────────────────────────────────────────────────────

    public async Task<(bool Success, string Message)> RegisterAsync(
        string email, string username, string password)
    {
        var existing = await FindUserByEmailAsync(email);
        if (existing != null)
            return (false, "Email đã được đăng ký");

        var alreadyPending = await _cacheService.GetAsync<PendingRegistration>(
            $"{REGISTER_PENDING_PREFIX}{email.ToLower()}");
        if (alreadyPending != null)
            return (false, "Email này đang chờ xác nhận OTP. Vui lòng kiểm tra hộp thư hoặc chờ mã hết hạn.");

        var pending = new PendingRegistration
        {
            Email        = email,
            Username     = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12)
        };

        var otp = GenerateOtp();

        await _cacheService.SetAsync(
            $"{REGISTER_PENDING_PREFIX}{email.ToLower()}", pending, RegisterOtpLifetime);
        await _cacheService.SetAsync(
            $"{REGISTER_OTP_PREFIX}{email.ToLower()}", otp, RegisterOtpLifetime);

        await _emailService.SendRegisterOtpEmailAsync(email, otp);

        return (true, "Mã xác nhận đã được gửi đến email của bạn. Vui lòng nhập OTP để hoàn tất đăng ký.");
    }

    public async Task<(bool Success, string Message)> VerifyRegisterOtpAsync(string email, string code)
    {
        var emailKey   = email.ToLower();
        var attemptKey = $"otp-attempt:register:{emailKey}";

        if (!await RegisterOtpAttemptAsync(attemptKey))
            return (false, "Bạn đã nhập sai quá nhiều lần. Vui lòng thử lại sau ít phút.");

        var storedOtp = await _cacheService.GetAsync<string>($"{REGISTER_OTP_PREFIX}{emailKey}");
        if (storedOtp == null || !FixedTimeEquals(storedOtp, code))
            return (false, "Mã OTP không đúng hoặc đã hết hạn");

        await ResetOtpAttemptsAsync(attemptKey);

        var pending = await _cacheService.GetAsync<PendingRegistration>($"{REGISTER_PENDING_PREFIX}{emailKey}");
        if (pending == null)
            return (false, "Phiên đăng ký đã hết hạn. Vui lòng đăng ký lại.");

        var existing = await FindUserByEmailAsync(pending.Email);
        if (existing != null)
        {
            await CleanupRegisterCacheAsync(emailKey);
            return (false, "Email đã được đăng ký bởi người khác.");
        }

        var user = new User
        {
            Email        = pending.Email,
            Username     = pending.Username,
            PasswordHash = pending.PasswordHash,
            Role         = Roles.User,
            IsActive     = true
        };

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();
        await CacheUserAsync(user);

        await CleanupRegisterCacheAsync(emailKey);

        return (true, "Đăng ký thành công! Bạn có thể đăng nhập ngay bây giờ.");
    }

    // ─── Login ───────────────────────────────────────────────────────────────

    public async Task<(LoginResponseDTO? Response, Guid? PendingUserId, string? ErrorMessage, string? BanReason)> LoginAsync(
        string email, string password)
    {
        var user = await FindUserByEmailAsync(email);

        // Luôn verify BCrypt kể cả khi user không tồn tại để cân bằng timing
        var passwordOk = user != null
            ? BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)
            : BCrypt.Net.BCrypt.Verify(password, DummyPasswordHash);

        if (user == null || !passwordOk)
            return (null, null, "Email hoặc mật khẩu không đúng", null);

        if (!user.IsActive)
            return (null, null, "Tài khoản đã bị khóa", user.BanReason);

        if (user.Is2FaEnabled)
        {
            await SendOtpAsync(user.Id);
            return (null, user.Id, null, null);
        }

        return (await CreateSessionAsync(user), null, null, null);
    }

    // ─── OTP (2FA / Login) ───────────────────────────────────────────────────

    public async Task<bool> SendOtpAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null) return false;

        var otp = GenerateOtp();
        await _cacheService.SetAsync($"{OTP_PREFIX}{userId}", otp, TimeSpan.FromMinutes(5));
        await _emailService.SendOtpEmailAsync(user.Email, otp);

        return true;
    }

    public async Task<LoginResponseDTO?> VerifyOtpAsync(Guid userId, string code)
    {
        var attemptKey = $"otp-attempt:{userId}";
        if (!await RegisterOtpAttemptAsync(attemptKey))
            return null;

        var stored = await _cacheService.GetAsync<string>($"{OTP_PREFIX}{userId}");
        if (stored == null || !FixedTimeEquals(stored, code)) 
            return null;

        await ResetOtpAttemptsAsync(attemptKey);
        await _cacheService.RemoveAsync($"{OTP_PREFIX}{userId}");

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null) return null;

        // Chỉ tạo phiên đăng nhập, không tự ý kích hoạt cờ Is2FaEnabled
        return await CreateSessionAsync(user);
    }

    public async Task<(bool Success, string Message)> ConfirmEnable2FAAsync(Guid userId, string code)
    {
        var attemptKey = $"otp-attempt:2fa-enable:{userId}";
        if (!await RegisterOtpAttemptAsync(attemptKey))
            return (false, "Bạn đã nhập sai quá nhiều lần. Vui lòng thử lại sau ít phút.");

        var stored = await _cacheService.GetAsync<string>($"{OTP_PREFIX}{userId}");
        if (stored == null || !FixedTimeEquals(stored, code))
            return (false, "Mã OTP không đúng hoặc đã hết hạn");

        await ResetOtpAttemptsAsync(attemptKey);
        await _cacheService.RemoveAsync($"{OTP_PREFIX}{userId}");

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null) return (false, "Không tìm thấy user");

        user.Is2FaEnabled = true;
        user.UpdatedAt    = DateTime.UtcNow;
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        await InvalidateUserCacheAsync(user);

        return (true, "Đã kích hoạt xác thực 2 lớp thành công");
    }

    public async Task<(bool Success, string Message)> Disable2FAAsync(Guid userId, string code)
    {
        var attemptKey = $"otp-attempt:2fa-disable:{userId}";
        if (!await RegisterOtpAttemptAsync(attemptKey))
            return (false, "Bạn đã nhập sai quá nhiều lần. Vui lòng thử lại sau ít phút.");

        var stored = await _cacheService.GetAsync<string>($"{OTP_PREFIX}{userId}");
        if (stored == null || !FixedTimeEquals(stored, code))
            return (false, "Mã OTP không đúng hoặc đã hết hạn");

        await ResetOtpAttemptsAsync(attemptKey);
        await _cacheService.RemoveAsync($"{OTP_PREFIX}{userId}");

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null) return (false, "Không tìm thấy user");

        user.Is2FaEnabled = false;
        user.UpdatedAt    = DateTime.UtcNow;
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        await InvalidateUserCacheAsync(user);

        return (true, "Đã tắt xác thực 2 lớp");
    }

    // ─── Forgot / Reset Password ─────────────────────────────────────────────

    public async Task<bool> ForgotPasswordAsync(string email)
    {
        var user = await FindUserByEmailAsync(email);
        if (user == null) return true;

        var otp = GenerateOtp();
        await _cacheService.SetAsync($"{RESET_PREFIX}{email.ToLower()}", otp, TimeSpan.FromMinutes(10));
        await _emailService.SendResetPasswordEmailAsync(email, otp);

        return true;
    }

    public async Task<bool> ResetPasswordAsync(string email, string code, string newPassword)
    {
        var emailKey   = email.ToLower();
        var attemptKey = $"otp-attempt:reset:{emailKey}";

        if (!await RegisterOtpAttemptAsync(attemptKey))
            return false;

        var stored = await _cacheService.GetAsync<string>($"{RESET_PREFIX}{emailKey}");
        if (stored == null || !FixedTimeEquals(stored, code)) 
            return false;

        var user = await FindUserByEmailAsync(email);
        if (user == null) return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 12);
        user.UpdatedAt    = DateTime.UtcNow;
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        // Thu hồi toàn bộ sessions cũ
        var sessions = await _sessionRepository.FindAsync(s => s.UserId == user.Id);
        foreach (var s in sessions)
            _sessionRepository.Remove(s);
        await _sessionRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync($"{RESET_PREFIX}{emailKey}");
        await InvalidateUserCacheAsync(user);
        await ResetOtpAttemptsAsync(attemptKey);

        return true;
    }

    // ─── Logout ──────────────────────────────────────────────────────────────

    public async Task LogoutAsync(Guid userId)
    {
        var sessions = await _sessionRepository.FindAsync(s => s.UserId == userId);
        foreach (var s in sessions)
            _sessionRepository.Remove(s);

        await _sessionRepository.SaveChangesAsync();
    }

    // ─── Refresh Token ───────────────────────────────────────────────────────

    public async Task<LoginResponseDTO?> RefreshTokenAsync(string refreshToken)
    {
        var session = await _sessionRepository.FindOneAsync(
            s => s.RefreshToken == refreshToken);

        if (session == null)
            return null;

        if (session.ExpiresAt < DateTime.UtcNow)
        {
            _sessionRepository.Remove(session);
            await _sessionRepository.SaveChangesAsync();
            return null;
        }

        var user = await _userRepository.GetByIdAsync(session.UserId);
        if (user == null)
            return null;

        var newAccessToken  = _jwtTokenGenerator.GenerateAccessToken(user.Id, user.Email, user.Role);
        var newRefreshToken = _jwtTokenGenerator.GenerateRefreshToken();

        _sessionRepository.Remove(session);

        var newSession = new UserSession
        {
            UserId       = user.Id,
            AccessToken  = newAccessToken,
            RefreshToken = newRefreshToken,
            ExpiresAt    = DateTime.UtcNow.Add(RefreshTokenLifetime)
        };

        await _sessionRepository.AddAsync(newSession);
        await _sessionRepository.SaveChangesAsync();

        return BuildLoginResponse(newAccessToken, newRefreshToken, user);
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────

    private static string GenerateOtp() =>
        RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

    private static bool FixedTimeEquals(string? a, string? b)
    {
        if (a == null || b == null) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
    }

    private async Task<bool> RegisterOtpAttemptAsync(string attemptKey)
    {
        var attempts = await _cacheService.GetAsync<int?>(attemptKey) ?? 0;
        if (attempts >= OtpMaxAttempts)
            return false;

        await _cacheService.SetAsync(attemptKey, attempts + 1, OtpAttemptWindow);
        return true;
    }

    private async Task ResetOtpAttemptsAsync(string attemptKey) =>
        await _cacheService.RemoveAsync(attemptKey);

    private async Task CleanupRegisterCacheAsync(string emailKey)
    {
        await _cacheService.RemoveAsync($"{REGISTER_OTP_PREFIX}{emailKey}");
        await _cacheService.RemoveAsync($"{REGISTER_PENDING_PREFIX}{emailKey}");
    }

    private async Task<User?> FindUserByEmailAsync(string email)
    {
        var cacheKey = $"{USER_EMAIL_PREFIX}{email.ToLower()}";
        var cached   = await _cacheService.GetAsync<User>(cacheKey);
        if (cached != null) return cached;

        var user = await _userRepository.FindOneAsync(u => u.Email == email);
        if (user != null)
            await CacheUserAsync(user);

        return user;
    }

    private async Task CacheUserAsync(User user)
    {
        var expiry = TimeSpan.FromMinutes(30);
        await _cacheService.SetAsync($"{USER_EMAIL_PREFIX}{user.Email.ToLower()}", user, expiry);
        await _cacheService.SetAsync($"{USER_ID_PREFIX}{user.Id}",                 user, expiry);
    }

    private async Task InvalidateUserCacheAsync(User user)
    {
        await _cacheService.RemoveAsync($"{USER_EMAIL_PREFIX}{user.Email.ToLower()}");
        await _cacheService.RemoveAsync($"{USER_ID_PREFIX}{user.Id}");
    }

    private async Task<LoginResponseDTO> CreateSessionAsync(User user)
    {
        var accessToken  = _jwtTokenGenerator.GenerateAccessToken(user.Id, user.Email, user.Role);
        var refreshToken = _jwtTokenGenerator.GenerateRefreshToken();

        var session = new UserSession
        {
            UserId       = user.Id,
            AccessToken  = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt    = DateTime.UtcNow.Add(RefreshTokenLifetime)
        };

        await _sessionRepository.AddAsync(session);
        await _sessionRepository.SaveChangesAsync();

        user.LastLogin = DateTime.UtcNow;
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        return BuildLoginResponse(accessToken, refreshToken, user);
    }

    private static LoginResponseDTO BuildLoginResponse(string accessToken, string refreshToken, User user) =>
        new()
        {
            AccessToken  = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn    = DateTime.UtcNow.AddMinutes(30),
            User = new UserDTO
            {
                Id               = user.Id,
                Email            = user.Email,
                Username         = user.Username,
                AvatarUrl        = user.AvatarUrl,
                SubscriptionType = user.SubscriptionType,
                Role             = user.Role,
                CreatedAt        = user.CreatedAt
            }
        };
}

internal sealed class PendingRegistration
{
    public string Email        { get; init; } = string.Empty;
    public string Username     { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
}