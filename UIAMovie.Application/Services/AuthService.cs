// UIAMovie.Application/Services/AuthService.cs

using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Constants;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Services;

public interface IAuthService
{
    Task<(bool Success, string Message)> RegisterAsync(string email, string username, string password);
    Task<(LoginResponseDTO? Response, Guid? PendingUserId)> LoginAsync(string email, string password);
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

    private const string OTP_PREFIX        = "otp:";
    private const string RESET_PREFIX      = "reset:";
    private const string USER_EMAIL_PREFIX = "user:email:";
    private const string USER_ID_PREFIX    = "user:id:";

    public AuthService(
        IRepository<User> userRepository,
        IRepository<UserSession> sessionRepository,
        IJwtTokenGenerator jwtTokenGenerator,
        IEmailService emailService,
        ICacheService cacheService)
    {
        _userRepository = userRepository;
        _sessionRepository = sessionRepository;
        _jwtTokenGenerator = jwtTokenGenerator;
        _emailService = emailService;
        _cacheService = cacheService;
    }

    public async Task<(bool Success, string Message)> RegisterAsync(
        string email, string username, string password)
    {
        var existing = await FindUserByEmailAsync(email);
        if (existing != null)
            return (false, "Email đã được đăng ký");

        var user = new User
        {
            Email        = email,
            Username     = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role         = Roles.User,
            IsActive     = true
        };

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();

        // Cache user ngay sau khi tạo
        await CacheUserAsync(user);

        return (true, "Đăng ký thành công");
    }

    public async Task<(LoginResponseDTO? Response, Guid? PendingUserId)> LoginAsync(
        string email, string password)
    {
        var user = await FindUserByEmailAsync(email);
        if (user == null) return (null, null);
        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) return (null, null);

        if (user.Is2FaEnabled)
        {
            await SendOtpAsync(user.Id);
            return (null, user.Id);
        }

        return (await CreateSessionAsync(user), null);
    }

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

        // Nếu chưa bật 2FA (flow enable): bật lên sau khi verify thành công
        if (!user.Is2FaEnabled)
        {
            user.Is2FaEnabled = true;
            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();
        }

        return await CreateSessionAsync(user);
    }

    public async Task<(bool Success, string Message)> Disable2FAAsync(Guid userId, string code)
    {
        // Xác thực OTP
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

    public async Task<bool> ForgotPasswordAsync(string email)
    {
        var user = await FindUserByEmailAsync(email);
        if (user == null) return true; // silent fail — không lộ email tồn tại

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
        // Invalidate user cache sau khi đổi password
        await _cacheService.RemoveAsync($"{USER_EMAIL_PREFIX}{email}");
        await _cacheService.RemoveAsync($"{USER_ID_PREFIX}{user.Id}");

        return true;
    }

    public async Task LogoutAsync(Guid userId)
    {
        var sessions = (await _sessionRepository.GetAllAsync())
            .Where(s => s.UserId == userId).ToList();

        foreach (var s in sessions)
            _sessionRepository.Remove(s);

        await _sessionRepository.SaveChangesAsync();
    }

    private static string GenerateOtp() =>
        new Random().Next(100000, 999999).ToString();

    // ── Cache helpers ────────────────────────────────────────────────────────

    /// <summary>Tìm user theo email — ưu tiên cache, fallback DB + cache lại.</summary>
    private async Task<User?> FindUserByEmailAsync(string email)
    {
        var cacheKey = $"{USER_EMAIL_PREFIX}{email.ToLower()}";
        var cached   = await _cacheService.GetAsync<User>(cacheKey);
        if (cached != null) return cached;

        // FindAsync theo email trực tiếp thay vì GetAllAsync
        var user = await _userRepository.FindOneAsync(u => u.Email == email);
        if (user != null)
            await CacheUserAsync(user);

        return user;
    }

    /// <summary>Cache user theo cả email lẫn id.</summary>
    private async Task CacheUserAsync(User user)
    {
        var expiry = TimeSpan.FromMinutes(30);
        await _cacheService.SetAsync($"{USER_EMAIL_PREFIX}{user.Email.ToLower()}", user, expiry);
        await _cacheService.SetAsync($"{USER_ID_PREFIX}{user.Id}",               user, expiry);
    }

    private async Task<LoginResponseDTO> CreateSessionAsync(User user)
    {
        // ← Truyền role vào token
        var accessToken = _jwtTokenGenerator.GenerateAccessToken(user.Id, user.Email, user.Role);
        var refreshToken = _jwtTokenGenerator.GenerateRefreshToken();

        var session = new UserSession
        {
            UserId = user.Id,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        await _sessionRepository.AddAsync(session);
        await _sessionRepository.SaveChangesAsync();

        user.LastLogin = DateTime.UtcNow;
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        return new LoginResponseDTO
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = DateTime.UtcNow.AddHours(1),
            User = new UserDTO
            {
                Id = user.Id,
                Email = user.Email,
                Username = user.Username,
                AvatarUrl = user.AvatarUrl,
                SubscriptionType = user.SubscriptionType,
                Role = user.Role,
                CreatedAt = user.CreatedAt
            }
        };
    }
    
    public async Task<LoginResponseDTO?> RefreshTokenAsync(string refreshToken)
    {
        var session = (await _sessionRepository.GetAllAsync())
            .FirstOrDefault(s => s.RefreshToken == refreshToken);

        if (session == null)
            return null;

        if (session.ExpiresAt < DateTime.UtcNow)
            return null;

        var user = await _userRepository.GetByIdAsync(session.UserId);
        if (user == null)
            return null;

        // tạo access token mới
        var newAccessToken = _jwtTokenGenerator.GenerateAccessToken(
            user.Id,
            user.Email,
            user.Role
        );

        // update session
        session.AccessToken = newAccessToken;
        _sessionRepository.Update(session);
        await _sessionRepository.SaveChangesAsync();

        return new LoginResponseDTO
        {
            AccessToken = newAccessToken,
            RefreshToken = session.RefreshToken,
            ExpiresIn = DateTime.UtcNow.AddHours(1),
            User = new UserDTO
            {
                Id = user.Id,
                Email = user.Email,
                Username = user.Username,
                AvatarUrl = user.AvatarUrl,
                SubscriptionType = user.SubscriptionType,
                Role = user.Role,
                CreatedAt = user.CreatedAt
            }
        };
    }
}