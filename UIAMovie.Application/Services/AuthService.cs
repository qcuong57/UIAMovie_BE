using System.Security.Cryptography;
using System.Text;
using UIAMovie.Application.Caching;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Application.Mappings;
using UIAMovie.Application.Security;
using UIAMovie.Domain.Constants;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Services;

/// <summary>Kết quả yêu cầu gửi OTP.</summary>
public enum OtpSendResult
{
    Sent,
    TooSoon,
    NotFound
}

public interface IAuthService
{
    Task<(bool Success, string Message)> RegisterAsync(string email, string username, string password);
    Task<(bool Success, string Message)> VerifyRegisterOtpAsync(string email, string code);
    Task<(LoginResponseDTO? Response, Guid? PendingUserId, string? ErrorMessage, string? BanReason)> LoginAsync(string email, string password);

    /// <summary>Gửi lại OTP đăng nhập. Chỉ hợp lệ khi phiên đã qua bước nhập đúng mật khẩu.</summary>
    Task<OtpSendResult> SendOtpAsync(Guid userId);

    /// <summary>Gửi OTP cho user đã đăng nhập (bật/tắt 2FA).</summary>
    Task<OtpSendResult> SendTwoFactorOtpAsync(Guid userId);
    Task<LoginResponseDTO?> VerifyOtpAsync(Guid userId, string code);
    Task<(bool Success, string Message)> ConfirmEnable2FAAsync(Guid userId, string code);
    Task<(bool Success, string Message)> Disable2FAAsync(Guid userId, string code);

    /// <summary>Thu hồi đúng phiên ứng với refresh token (chỉ thiết bị hiện tại).</summary>
    Task LogoutAsync(string? refreshToken);
    Task<bool> ForgotPasswordAsync(string email);
    Task<bool> ResetPasswordAsync(string email, string code, string newPassword);
    Task<LoginResponseDTO?> RefreshTokenAsync(string refreshToken);
}

public class AuthService : IAuthService
{
    // ─── Cache key prefixes ──────────────────────────────────────────────────

    private const string OtpPrefix             = "otp:";
    private const string ResetPrefix           = "reset:";
    private const string RegisterOtpPrefix     = "register:otp:";
    private const string RegisterPendingPrefix = "register:pending:";
    private const string RefreshGracePrefix    = "refresh:grace:";
    private const string LoginPendingPrefix    = "login:pending:";
    private const string OtpCooldownPrefix     = "otp-cooldown:";
    private const string OtpAttemptPrefix      = "otp-attempt:";

    // ─── Messages ────────────────────────────────────────────────────────────

    private const string TooManyAttemptsMessage = "Bạn đã nhập sai quá nhiều lần. Vui lòng thử lại sau ít phút.";
    private const string InvalidOtpMessage      = "Mã OTP không đúng hoặc đã hết hạn";
    private const string UserNotFoundMessage    = "Không tìm thấy user";

    // ─── Policy ──────────────────────────────────────────────────────────────

    private const int BcryptWorkFactor = 12;
    private const int OtpMaxAttempts   = 5;

    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan RegisterOtpLifetime  = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LoginOtpLifetime     = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResetOtpLifetime     = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan OtpAttemptWindow     = TimeSpan.FromMinutes(15);

    // Từ lúc đúng mật khẩu đến lúc nhập xong OTP đăng nhập (kể cả thời gian bấm "gửi lại").
    private static readonly TimeSpan LoginPendingLifetime = TimeSpan.FromMinutes(10);

    // Khoảng tối thiểu giữa hai lần gửi OTP / email đặt lại mật khẩu tới cùng một đích.
    private static readonly TimeSpan OtpResendCooldown = TimeSpan.FromSeconds(60);

    // Sau khi xoay refresh token, token CŨ vẫn được chấp nhận trong khoảng ngắn này.
    // Giải quyết: nhiều tab khởi động cùng lúc dùng chung một cookie, hoặc response refresh bị mất giữa đường.
    private static readonly TimeSpan RefreshGraceWindow = TimeSpan.FromSeconds(30);

    // Hash dummy chống Timing Attack + User Enumeration khi login.
    private static readonly string DummyPasswordHash = HashPassword(Guid.NewGuid().ToString());

    private readonly IRepository<User> _userRepository;
    private readonly IRepository<UserSession> _sessionRepository;
    private readonly IRepository<UserSubscription> _subRepository;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly ICacheService _cacheService;
    private readonly ITokenRevocationService _tokenRevocation;
    private readonly IEmailQueue _emailQueue;

    public AuthService(
        IRepository<User> userRepository,
        IRepository<UserSession> sessionRepository,
        IRepository<UserSubscription> subRepository,
        IJwtTokenGenerator jwtTokenGenerator,
        ICacheService cacheService,
        ITokenRevocationService tokenRevocation,
        IEmailQueue emailQueue)
    {
        _userRepository    = userRepository;
        _sessionRepository = sessionRepository;
        _subRepository     = subRepository;
        _jwtTokenGenerator = jwtTokenGenerator;
        _cacheService      = cacheService;
        _tokenRevocation   = tokenRevocation;
        _emailQueue        = emailQueue;
    }

    // ─── Register ────────────────────────────────────────────────────────────

    public async Task<(bool Success, string Message)> RegisterAsync(
        string email, string username, string password)
    {
        if (PasswordPolicy.Validate(password) is { } policyError)
            return (false, policyError);

        var name = username?.Trim() ?? string.Empty;
        if (name.Length is < 3 or > 50)
            return (false, "Tên người dùng phải từ 3 đến 50 ký tự");

        var emailKey = NormalizeEmail(email);

        if (await FindUserByEmailAsync(emailKey) != null)
            return (false, "Email đã được đăng ký");

        // Username có unique index: kiểm tra sớm để không bắt user nhập OTP rồi mới gặp lỗi 500.
        if (await UsernameTakenAsync(name))
            return (false, "Tên người dùng đã được sử dụng");

        var pendingKey = RegisterPendingKey(emailKey);
        if (await _cacheService.ExistsAsync(pendingKey))
            return (false, "Email này đang chờ xác nhận OTP. Vui lòng kiểm tra hộp thư hoặc chờ mã hết hạn.");

        var pending = new PendingRegistration
        {
            Email        = emailKey,
            Username     = name,
            PasswordHash = HashPassword(password)
        };
        var otp = GenerateOtp();

        await _cacheService.SetAsync(pendingKey, pending, RegisterOtpLifetime);
        await _cacheService.SetAsync(RegisterOtpKey(emailKey), otp, RegisterOtpLifetime);

        // Gửi email ở nền để request trả về ngay, tránh timeout.
        _emailQueue.Enqueue(svc => svc.SendRegisterOtpEmailAsync(emailKey, otp));

        return (true, "Mã xác nhận đã được gửi đến email của bạn. Vui lòng nhập OTP để hoàn tất đăng ký.");
    }

    public async Task<(bool Success, string Message)> VerifyRegisterOtpAsync(string email, string code)
    {
        var emailKey = NormalizeEmail(email);

        var check = await CheckOtpAsync(AttemptKey("register", emailKey), RegisterOtpKey(emailKey), code);
        if (check != OtpCheck.Ok)
            return (false, OtpFailureMessage(check));

        var pending = await _cacheService.GetAsync<PendingRegistration>(RegisterPendingKey(emailKey));
        if (pending == null)
            return (false, "Phiên đăng ký đã hết hạn. Vui lòng đăng ký lại.");

        if (await FindUserByEmailAsync(pending.Email) != null)
        {
            await CleanupRegisterCacheAsync(emailKey);
            return (false, "Email đã được đăng ký bởi người khác.");
        }

        // Kiểm tra lại lúc tạo: tên có thể bị người khác chiếm trong lúc chờ nhập OTP.
        if (await UsernameTakenAsync(pending.Username))
        {
            await CleanupRegisterCacheAsync(emailKey);
            return (false, "Tên người dùng đã được người khác sử dụng. Vui lòng đăng ký lại với tên khác.");
        }

        await _userRepository.AddAsync(new User
        {
            Email        = pending.Email,
            Username     = pending.Username,
            PasswordHash = pending.PasswordHash,
            Role         = Roles.User,
            IsActive     = true
        });
        await _userRepository.SaveChangesAsync();

        await CleanupRegisterCacheAsync(emailKey);

        return (true, "Đăng ký thành công! Bạn có thể đăng nhập ngay bây giờ.");
    }

    // ─── Login ───────────────────────────────────────────────────────────────

    public async Task<(LoginResponseDTO? Response, Guid? PendingUserId, string? ErrorMessage, string? BanReason)> LoginAsync(
        string email, string password)
    {
        var user = await FindUserByEmailAsync(email);

        // Luôn verify BCrypt kể cả khi user không tồn tại để cân bằng timing.
        var passwordOk = BCrypt.Net.BCrypt.Verify(password, user?.PasswordHash ?? DummyPasswordHash);

        if (user == null || !passwordOk)
            return (null, null, "Email hoặc mật khẩu không đúng", null);

        if (!user.IsActive)
            return (null, null, "Tài khoản đã bị khóa", user.BanReason);

        if (user.Is2FaEnabled)
        {
            // Đánh dấu "đã qua bước mật khẩu": chỉ khi có dấu này mới được gửi lại / nhập OTP đăng nhập.
            await _cacheService.SetAsync(LoginPendingKey(user.Id), "1", LoginPendingLifetime);
            await TrySendOtpAsync(user); // đang trong thời gian chờ gửi lại thì mã trước đó vẫn còn hiệu lực
            return (null, user.Id, null, null);
        }

        return (await CreateSessionAsync(user), null, null, null);
    }

    // ─── OTP (2FA / Login) ───────────────────────────────────────────────────

    public async Task<OtpSendResult> SendOtpAsync(Guid userId)
    {
        // Kiểm tra dấu "đã đúng mật khẩu" trước khi tra user: người ngoài không dò được userId nào tồn tại.
        if (!await IsLoginPendingAsync(userId))
            return OtpSendResult.NotFound;

        return await SendOtpToUserAsync(userId);
    }

    public Task<OtpSendResult> SendTwoFactorOtpAsync(Guid userId) =>
        SendOtpToUserAsync(userId);

    public async Task<LoginResponseDTO?> VerifyOtpAsync(Guid userId, string code)
    {
        // Không có dấu "đã đúng mật khẩu" thì không được đổi OTP lấy phiên đăng nhập.
        if (!await IsLoginPendingAsync(userId))
            return null;

        var check = await CheckOtpAsync(AttemptKey("login", userId), OtpKey(userId), code);
        if (check != OtpCheck.Ok)
            return null;

        await _cacheService.RemoveAsync(LoginPendingKey(userId));

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null || !user.IsActive)
            return null; // có thể bị khóa trong lúc chờ nhập OTP

        // Chỉ tạo phiên đăng nhập, không tự ý kích hoạt cờ Is2FaEnabled.
        return await CreateSessionAsync(user);
    }

    public Task<(bool Success, string Message)> ConfirmEnable2FAAsync(Guid userId, string code) =>
        SetTwoFactorAsync(userId, code, enabled: true,
            attemptScope: "2fa-enable", successMessage: "Đã kích hoạt xác thực 2 lớp thành công");

    public Task<(bool Success, string Message)> Disable2FAAsync(Guid userId, string code) =>
        SetTwoFactorAsync(userId, code, enabled: false,
            attemptScope: "2fa-disable", successMessage: "Đã tắt xác thực 2 lớp");

    private async Task<(bool Success, string Message)> SetTwoFactorAsync(
        Guid userId, string code, bool enabled, string attemptScope, string successMessage)
    {
        var check = await CheckOtpAsync(AttemptKey(attemptScope, userId), OtpKey(userId), code);
        if (check != OtpCheck.Ok)
            return (false, OtpFailureMessage(check));

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            return (false, UserNotFoundMessage);

        user.Is2FaEnabled = enabled;
        user.UpdatedAt    = DateTime.UtcNow;
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        await InvalidateUserCacheAsync(user);

        return (true, successMessage);
    }

    // ─── Forgot / Reset Password ─────────────────────────────────────────────

    public async Task<bool> ForgotPasswordAsync(string email)
    {
        var emailKey    = NormalizeEmail(email);
        var cooldownKey = $"{OtpCooldownPrefix}reset:{emailKey}";

        // Luôn trả cùng một kết quả dù email có tồn tại hay đang bị giới hạn (chống dò email, chống spam hộp thư).
        // INCR nguyên tử: chỉ request đầu tiên trong cửa sổ cooldown (giá trị == 1) được đi tiếp.
        if (await _cacheService.IncrementAsync(cooldownKey, OtpResendCooldown) > 1) return true;

        if (await FindUserByEmailAsync(emailKey) == null) return true;

        var otp = GenerateOtp();
        await _cacheService.SetAsync(ResetKey(emailKey), otp, ResetOtpLifetime);
        _emailQueue.Enqueue(svc => svc.SendResetPasswordEmailAsync(emailKey, otp));

        return true;
    }

    public async Task<bool> ResetPasswordAsync(string email, string code, string newPassword)
    {
        // Validate mật khẩu mới trước khi tiêu thụ OTP: mật khẩu yếu không được làm mất OTP.
        if (PasswordPolicy.Validate(newPassword) != null)
            return false;

        var emailKey = NormalizeEmail(email);

        var check = await CheckOtpAsync(AttemptKey("reset", emailKey), ResetKey(emailKey), code);
        if (check != OtpCheck.Ok)
            return false;

        var user = await FindUserByEmailAsync(emailKey);
        if (user == null) return false;

        user.PasswordHash = HashPassword(newPassword);
        user.UpdatedAt    = DateTime.UtcNow;
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        // Mật khẩu có thể đã bị lộ: thu hồi cả phiên (refresh token) lẫn access token đang sống.
        await _tokenRevocation.RevokeAllAsync(user.Id);
        await InvalidateUserCacheAsync(user);

        return true;
    }

    // ─── Logout ──────────────────────────────────────────────────────────────

    public async Task LogoutAsync(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var session = await FindSessionByRefreshTokenAsync(refreshToken);
        if (session != null)
            await RemoveSessionAsync(session);
    }

    // ─── Refresh Token ───────────────────────────────────────────────────────

    public async Task<LoginResponseDTO?> RefreshTokenAsync(string refreshToken)
    {
        // DB chỉ lưu băm của refresh token: lộ DB cũng không dùng được token.
        var tokenHash = HashToken(refreshToken);
        var session   = await _sessionRepository.FindOneAsync(s => s.RefreshToken == tokenHash);

        if (session == null)
            return await RefreshWithinGraceRetryAsync(tokenHash);

        if (session.ExpiresAt < DateTime.UtcNow)
        {
            await RemoveSessionAsync(session);
            return null;
        }

        var user = await _userRepository.GetByIdAsync(session.UserId);
        if (user == null || !user.IsActive)
        {
            // Tài khoản bị xóa/khóa thì không cấp thêm token.
            await RemoveSessionAsync(session);
            return null;
        }

        var accessToken     = _jwtTokenGenerator.GenerateAccessToken(user.Id, user.Email, user.Role);
        var newRefreshToken = _jwtTokenGenerator.GenerateRefreshToken();

        try
        {
            _sessionRepository.Remove(session);
            await _sessionRepository.AddAsync(NewSession(user.Id, newRefreshToken));
            await _sessionRepository.SaveChangesAsync();
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            // Request song song cùng token đã xoay trước: dùng phiên mới của nó thay vì lỗi 500.
            return await RefreshWithinGraceRetryAsync(tokenHash);
        }

        // Nhớ token vừa được thay thế để xử lý request đến muộn trong khoảng grace.
        await _cacheService.SetAsync(
            $"{RefreshGracePrefix}{tokenHash}",
            new RotatedRefreshToken { NewRefreshToken = newRefreshToken },
            RefreshGraceWindow);

        return await BuildLoginResponseAsync(accessToken, newRefreshToken, user);
    }

    /// <summary>
    /// Request tới sát nhau: request thắng có thể chưa kịp ghi grace cache, nên thử lại ngắn.
    /// </summary>
    private async Task<LoginResponseDTO?> RefreshWithinGraceRetryAsync(string oldTokenHash)
    {
        const int maxAttempts = 3;
        var retryDelay = TimeSpan.FromMilliseconds(100);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var result = await RefreshWithinGraceAsync(oldTokenHash);
            if (result != null) return result;
            if (attempt < maxAttempts) await Task.Delay(retryDelay);
        }

        return null;
    }

    /// <summary>
    /// Token không còn trong DB: nếu nó vừa được xoay cách đây chưa lâu thì trả lại phiên MỚI
    /// (cùng refresh token mới, access token mới) thay vì buộc đăng nhập lại.
    /// </summary>
    private async Task<LoginResponseDTO?> RefreshWithinGraceAsync(string oldTokenHash)
    {
        var rotated = await _cacheService.GetAsync<RotatedRefreshToken>($"{RefreshGracePrefix}{oldTokenHash}");
        if (rotated == null)
            return null;

        var currentHash = HashToken(rotated.NewRefreshToken);
        var current     = await _sessionRepository.FindOneAsync(s => s.RefreshToken == currentHash);
        if (current == null || current.ExpiresAt < DateTime.UtcNow)
            return null; // đã đăng xuất / hết hạn trong lúc đó

        var user = await _userRepository.GetByIdAsync(current.UserId);
        if (user == null || !user.IsActive)
            return null;

        var accessToken = _jwtTokenGenerator.GenerateAccessToken(user.Id, user.Email, user.Role);
        return await BuildLoginResponseAsync(accessToken, rotated.NewRefreshToken, user);
    }

    // ─── OTP helpers ─────────────────────────────────────────────────────────

    private enum OtpCheck
    {
        Ok,
        TooManyAttempts,
        Invalid
    }

    /// <summary>Đếm lượt thử, kiểm tra và tiêu thụ OTP; thành công thì xóa bộ đếm.</summary>
    private async Task<OtpCheck> CheckOtpAsync(string attemptKey, string otpKey, string code)
    {
        if (!await RegisterOtpAttemptAsync(attemptKey))
            return OtpCheck.TooManyAttempts;

        if (!await ConsumeOtpAsync(otpKey, code))
            return OtpCheck.Invalid;

        await _cacheService.RemoveAsync(attemptKey);
        return OtpCheck.Ok;
    }

    private static string OtpFailureMessage(OtpCheck check) =>
        check == OtpCheck.TooManyAttempts ? TooManyAttemptsMessage : InvalidOtpMessage;

    // INCR nguyên tử: request song song không lọt qua giới hạn. TTL chỉ đặt ở lần đầu (cửa sổ cố định).
    private async Task<bool> RegisterOtpAttemptAsync(string attemptKey) =>
        await _cacheService.IncrementAsync(attemptKey, OtpAttemptWindow) <= OtpMaxAttempts;

    /// <summary>
    /// Kiểm tra mã rồi xóa nguyên tử: nếu nhiều request cùng gửi mã đúng thì chỉ MỘT request thắng.
    /// Mã sai thì không xóa (không cho người ngoài đốt mất OTP của user).
    /// </summary>
    private async Task<bool> ConsumeOtpAsync(string otpKey, string code)
    {
        var stored = await _cacheService.GetAsync<string>(otpKey);
        if (stored == null || !FixedTimeEquals(stored, code))
            return false;

        return await _cacheService.TryRemoveAsync(otpKey);
    }

    private async Task<OtpSendResult> SendOtpToUserAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        return user == null ? OtpSendResult.NotFound : await TrySendOtpAsync(user);
    }

    /// <summary>Gửi OTP nếu đã qua thời gian chờ gửi lại; ngược lại giữ nguyên mã cũ.</summary>
    private async Task<OtpSendResult> TrySendOtpAsync(User user)
    {
        var cooldownKey = $"{OtpCooldownPrefix}{user.Id}";
        // INCR nguyên tử: hai request song song không thể cùng qua cooldown (Redis lỗi → từ chối).
        if (await _cacheService.IncrementAsync(cooldownKey, OtpResendCooldown) > 1)
            return OtpSendResult.TooSoon;

        await IssueOtpAsync(user);
        return OtpSendResult.Sent;
    }

    /// <summary>Lưu OTP vào cache rồi đẩy việc gửi email vào hàng đợi nền (không chờ SMTP).</summary>
    private async Task IssueOtpAsync(User user)
    {
        var otp   = GenerateOtp();
        var email = user.Email;

        await _cacheService.SetAsync(OtpKey(user.Id), otp, LoginOtpLifetime);
        _emailQueue.Enqueue(svc => svc.SendOtpEmailAsync(email, otp));
    }

    private async Task<bool> IsLoginPendingAsync(Guid userId) =>
        await _cacheService.ExistsAsync(LoginPendingKey(userId));

    private static string GenerateOtp() =>
        RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    // ─── Cache keys ──────────────────────────────────────────────────────────

    private static string OtpKey(Guid userId)             => $"{OtpPrefix}{userId}";
    private static string ResetKey(string emailKey)       => $"{ResetPrefix}{emailKey}";
    private static string RegisterOtpKey(string emailKey) => $"{RegisterOtpPrefix}{emailKey}";
    private static string RegisterPendingKey(string emailKey) => $"{RegisterPendingPrefix}{emailKey}";
    private static string LoginPendingKey(Guid userId)    => $"{LoginPendingPrefix}{userId}";
    private static string AttemptKey(string scope, object id) => $"{OtpAttemptPrefix}{scope}:{id}";

    // ─── User / session helpers ──────────────────────────────────────────────

    /// <summary>Email so khớp không phân biệt hoa thường: "A@x.com" và "a@x.com" là cùng một tài khoản.</summary>
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    // Luôn đọc DB: không cache entity User (có PasswordHash, TwoFaSecret) trong Redis, và
    // IsActive/SubscriptionType luôn mới. Cache cũ từng ghi ngược lại DB qua Update(user) trong CreateSessionAsync.
    private async Task<User?> FindUserByEmailAsync(string email)
    {
        var emailKey = NormalizeEmail(email);
        return await _userRepository.FindOneAsync(u => u.Email.ToLower() == emailKey);
    }

    private async Task<bool> UsernameTakenAsync(string username)
    {
        var lowered = username.ToLowerInvariant();
        return await _userRepository.FindOneAsync(u => u.Username.ToLower() == lowered) != null;
    }

    private async Task InvalidateUserCacheAsync(User user) =>
        await _cacheService.RemoveManyAsync(UserCacheKeys.ById(user.Id), UserCacheKeys.ByEmail(user.Email));

    private async Task CleanupRegisterCacheAsync(string emailKey) =>
        await _cacheService.RemoveManyAsync(RegisterOtpKey(emailKey), RegisterPendingKey(emailKey));

    private static string HashPassword(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor);

    private static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static UserSession NewSession(Guid userId, string refreshToken) =>
        new()
        {
            UserId       = userId,
            RefreshToken = HashToken(refreshToken), // chỉ lưu bản băm
            ExpiresAt    = DateTime.UtcNow.Add(RefreshTokenLifetime)
        };

    /// <summary>Tìm phiên theo refresh token; nếu token vừa bị xoay (trong grace) thì trả phiên hiện tại.</summary>
    private async Task<UserSession?> FindSessionByRefreshTokenAsync(string refreshToken)
    {
        var tokenHash = HashToken(refreshToken);
        var session   = await _sessionRepository.FindOneAsync(s => s.RefreshToken == tokenHash);
        if (session != null) return session;

        var rotated = await _cacheService.GetAsync<RotatedRefreshToken>($"{RefreshGracePrefix}{tokenHash}");
        if (rotated == null) return null;

        var currentHash = HashToken(rotated.NewRefreshToken);
        return await _sessionRepository.FindOneAsync(s => s.RefreshToken == currentHash);
    }

    private async Task RemoveSessionAsync(UserSession session)
    {
        try
        {
            _sessionRepository.Remove(session);
            await _sessionRepository.SaveChangesAsync();
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            // Session đã bị request khác xóa (logout/refresh song song): mục tiêu đã đạt.
        }
    }

    // Application không tham chiếu EF Core nên nhận diện theo tên kiểu (kể cả khi bị bọc).
    private static bool IsConcurrencyConflict(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
            if (e.GetType().Name == "DbUpdateConcurrencyException") return true;
        return false;
    }

    private async Task<LoginResponseDTO> CreateSessionAsync(User user)
    {
        var accessToken  = _jwtTokenGenerator.GenerateAccessToken(user.Id, user.Email, user.Role);
        var refreshToken = _jwtTokenGenerator.GenerateRefreshToken();

        await _sessionRepository.AddAsync(NewSession(user.Id, refreshToken));
        await _sessionRepository.SaveChangesAsync();

        // user đã được theo dõi (tracked) trong scope này, nên KHÔNG gọi Update(user): Update đánh dấu
        // mọi cột là đã sửa và sẽ ghi đè IsActive/PasswordHash... nếu admin vừa khóa user giữa chừng.
        // Chỉ để EF phát hiện thay đổi, câu UPDATE sẽ chỉ chứa LastLogin.
        user.LastLogin = DateTime.UtcNow;
        await _userRepository.SaveChangesAsync();

        return await BuildLoginResponseAsync(accessToken, refreshToken, user);
    }

    private async Task<LoginResponseDTO> BuildLoginResponseAsync(string accessToken, string refreshToken, User user)
    {
        // Dùng chung UserMapper với /me để FE nhận cùng một dạng SubscriptionType ở mọi luồng.
        var subs = await _subRepository.FindAsync(s => s.UserId == user.Id);
        var sub  = subs.OrderByDescending(s => s.ExpiredAt).FirstOrDefault();

        return new LoginResponseDTO
        {
            AccessToken  = accessToken,
            RefreshToken = refreshToken,
            // Cùng nguồn cấu hình với thời hạn thật của token, để FE không tin sai hạn.
            ExpiresIn    = DateTime.UtcNow.Add(_jwtTokenGenerator.AccessTokenLifetime),
            User         = UserMapper.ToDto(user, sub)
        };
    }
}

internal sealed class PendingRegistration
{
    public string Email        { get; init; } = string.Empty;
    public string Username     { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
}

/// <summary>Kết quả xoay refresh token, giữ ngắn hạn trong cache cho khoảng grace.</summary>
internal sealed class RotatedRefreshToken
{
    public string NewRefreshToken { get; init; } = string.Empty;
}