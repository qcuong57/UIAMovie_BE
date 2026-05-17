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
    Task LogoutAsync(Guid userId);
    Task<bool> ForgotPasswordAsync(string email);
    Task<bool> ResetPasswordAsync(string email, string code, string newPassword);
    Task<LoginResponseDTO?> RefreshTokenAsync(string refreshToken);
    Task<(bool Success, string Message)> Disable2FAAsync(Guid userId, string code);
}

public class AuthService : IAuthService
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<UserSession> _sessionRepository;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IEmailService _emailService;
    private readonly ICacheService _cacheService;

    private const string OTP_PREFIX             = "otp:";
    private const string RESET_PREFIX           = "reset:";
    private const string USER_EMAIL_PREFIX      = "user:email:";
    private const string USER_ID_PREFIX         = "user:id:";
    private const string REGISTER_OTP_PREFIX    = "register:otp:";
    private const string REGISTER_PENDING_PREFIX = "register:pending:";

    private static readonly TimeSpan RefreshTokenLifetime  = TimeSpan.FromDays(7);
    private static readonly TimeSpan RegisterOtpLifetime   = TimeSpan.FromMinutes(10);

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

    // ─── Register (Step 1): Lưu pending vào cache, gửi OTP ──────────────────

    public async Task<(bool Success, string Message)> RegisterAsync(
        string email, string username, string password)
    {
        // Kiểm tra email đã tồn tại trong DB chưa
        var existing = await FindUserByEmailAsync(email);
        if (existing != null)
            return (false, "Email đã được đăng ký");

        // Kiểm tra xem email đã có pending registration chưa (tránh spam)
        var alreadyPending = await _cacheService.GetAsync<PendingRegistration>(
            $"{REGISTER_PENDING_PREFIX}{email.ToLower()}");
        if (alreadyPending != null)
            return (false, "Email này đang chờ xác nhận OTP. Vui lòng kiểm tra hộp thư hoặc chờ mã hết hạn.");

        // Lưu thông tin đăng ký tạm vào cache
        var pending = new PendingRegistration
        {
            Email        = email,
            Username     = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        };

        var otp = GenerateOtp();

        await _cacheService.SetAsync(
            $"{REGISTER_PENDING_PREFIX}{email.ToLower()}", pending, RegisterOtpLifetime);
        await _cacheService.SetAsync(
            $"{REGISTER_OTP_PREFIX}{email.ToLower()}", otp, RegisterOtpLifetime);

        await _emailService.SendRegisterOtpEmailAsync(email, otp);

        return (true, "Mã xác nhận đã được gửi đến email của bạn. Vui lòng nhập OTP để hoàn tất đăng ký.");
    }

    // ─── Register (Step 2): Xác nhận OTP → lưu User vào DB ─────────────────

    public async Task<(bool Success, string Message)> VerifyRegisterOtpAsync(string email, string code)
    {
        var emailKey = email.ToLower();

        var storedOtp = await _cacheService.GetAsync<string>($"{REGISTER_OTP_PREFIX}{emailKey}");
        if (storedOtp == null || storedOtp != code)
            return (false, "Mã OTP không đúng hoặc đã hết hạn");

        var pending = await _cacheService.GetAsync<PendingRegistration>($"{REGISTER_PENDING_PREFIX}{emailKey}");
        if (pending == null)
            return (false, "Phiên đăng ký đã hết hạn. Vui lòng đăng ký lại.");

        // Kiểm tra lại lần cuối phòng race condition
        var existing = await FindUserByEmailAsync(pending.Email);
        if (existing != null)
        {
            await CleanupRegisterCacheAsync(emailKey);
            return (false, "Email đã được đăng ký bởi người khác.");
        }

        // Tạo user và lưu vào DB
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

        // Dọn cache pending
        await CleanupRegisterCacheAsync(emailKey);

        return (true, "Đăng ký thành công! Bạn có thể đăng nhập ngay bây giờ.");
    }

    // ─── Login ───────────────────────────────────────────────────────────────

    public async Task<(LoginResponseDTO? Response, Guid? PendingUserId, string? ErrorMessage, string? BanReason)> LoginAsync(
        string email, string password)
    {
        var user = await FindUserByEmailAsync(email);
        if (user == null)
            return (null, null, "Email không tồn tại trong hệ thống", null);

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return (null, null, "Mật khẩu không đúng", null);

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
        var stored = await _cacheService.GetAsync<string>($"{OTP_PREFIX}{userId}");
        if (stored == null || stored != code) return null;

        await _cacheService.RemoveAsync($"{OTP_PREFIX}{userId}");

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null) return null;

        if (!user.Is2FaEnabled)
        {
            user.Is2FaEnabled = true;
            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();
        }

        return await CreateSessionAsync(user);
    }

    // ─── 2FA Disable ─────────────────────────────────────────────────────────

    public async Task<(bool Success, string Message)> Disable2FAAsync(Guid userId, string code)
    {
        var stored = await _cacheService.GetAsync<string>($"{OTP_PREFIX}{userId}");
        if (stored == null || stored != code)
            return (false, "Mã OTP không đúng hoặc đã hết hạn");

        await _cacheService.RemoveAsync($"{OTP_PREFIX}{userId}");

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null) return (false, "Không tìm thấy user");

        user.Is2FaEnabled = false;
        user.UpdatedAt    = DateTime.UtcNow;
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        return (true, "Đã tắt xác thực 2 lớp");
    }

    // ─── Forgot / Reset Password ─────────────────────────────────────────────

    public async Task<bool> ForgotPasswordAsync(string email)
    {
        var user = await FindUserByEmailAsync(email);
        if (user == null) return true;

        var otp = GenerateOtp();
        await _cacheService.SetAsync($"{RESET_PREFIX}{email}", otp, TimeSpan.FromMinutes(10));
        await _emailService.SendResetPasswordEmailAsync(email, otp);

        return true;
    }

    public async Task<bool> ResetPasswordAsync(string email, string code, string newPassword)
    {
        var stored = await _cacheService.GetAsync<string>($"{RESET_PREFIX}{email}");
        if (stored == null || stored != code) return false;

        var user = await FindUserByEmailAsync(email);
        if (user == null) return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.UpdatedAt    = DateTime.UtcNow;
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync($"{RESET_PREFIX}{email}");
        await _cacheService.RemoveAsync($"{USER_EMAIL_PREFIX}{email}");
        await _cacheService.RemoveAsync($"{USER_ID_PREFIX}{user.Id}");

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
        Random.Shared.Next(100000, 999999).ToString();

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
            ExpiresIn    = DateTime.UtcNow.AddHours(1),
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

// ─── Internal DTO (không expose ra ngoài) ────────────────────────────────────

/// <summary>
/// Dữ liệu đăng ký tạm thời được lưu trong cache, chờ xác nhận OTP.
/// </summary>
internal sealed class PendingRegistration
{
    public string Email        { get; init; } = string.Empty;
    public string Username     { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
}