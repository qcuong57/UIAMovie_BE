using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Services;

/// <summary>
/// Thu hồi access token còn hạn của một user (khóa, xóa, đổi role, đổi/reset mật khẩu).
/// Lưu mốc thời gian (Unix giây) trong Redis; token có iat nhỏ hơn hoặc bằng mốc bị từ chối
/// (xem OnTokenValidated trong Program.cs). Token cấp SAU mốc (refresh, mở khóa) vẫn hợp lệ.
/// </summary>
public static class TokenRevocation
{
    /// <summary>Thời hạn access token tối đa được phép cấu hình; JwtTokenGenerator cắt về mức này.</summary>
    public static readonly TimeSpan MaxAccessTokenLifetime = TimeSpan.FromMinutes(60);

    // Phải lớn hơn thời hạn access token tối đa, nếu không mốc hết hạn trước khi token cũ chết.
    private static readonly TimeSpan Ttl = MaxAccessTokenLifetime + TimeSpan.FromHours(1);

    public static string Key(Guid userId) => $"user:revoked:{userId}";

    public static Task MarkAsync(ICacheService cache, Guid userId) =>
        cache.SetAsync(Key(userId), DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Ttl);

    /// <summary>
    /// Token phát hành trước hoặc đúng giây thu hồi đều bị loại (iat chỉ có độ phân giải giây,
    /// dùng "&lt;" sẽ để lọt token cấp cùng giây với lúc khóa).
    /// </summary>
    public static bool IsRevoked(long? revokedAt, long issuedAt) =>
        revokedAt is { } mark && issuedAt <= mark;

    /// <summary>
    /// Đá user ra khỏi mọi thiết bị: vô hiệu access token đang sống, rồi xóa toàn bộ phiên (refresh token).
    /// Đánh dấu thu hồi trước để lỗi khi xóa phiên không làm token cũ sống lại.
    /// </summary>
    public static async Task RevokeAllAsync(
        ICacheService cache, IRepository<UserSession> sessions, Guid userId)
    {
        await MarkAsync(cache, userId);

        foreach (var session in await sessions.FindAsync(s => s.UserId == userId))
            sessions.Remove(session);

        await sessions.SaveChangesAsync();
    }
}