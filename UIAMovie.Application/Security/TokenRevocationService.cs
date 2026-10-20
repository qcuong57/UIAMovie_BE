using UIAMovie.Application.Caching;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Security;

public sealed class TokenRevocationService : ITokenRevocationService
{
    // Biên cộng thêm vào thời hạn access token để mốc không hết hạn trước khi token cũ chết.
    private static readonly TimeSpan TtlMargin = TimeSpan.FromHours(1);

    private readonly ICacheService _cache;
    private readonly IRepository<UserSession> _sessions;
    private readonly IJwtTokenGenerator _jwt;

    public TokenRevocationService(
        ICacheService cache, IRepository<UserSession> sessions, IJwtTokenGenerator jwt)
    {
        _cache    = cache;
        _sessions = sessions;
        _jwt      = jwt;
    }

    // Lấy từ cấu hình thật của token: đổi Jwt:AccessTokenMinutes thì TTL đổi theo, thu hồi không hỏng âm thầm.
    private TimeSpan Ttl => _jwt.AccessTokenLifetime + TtlMargin;

    public Task MarkAsync(Guid userId) =>
        _cache.SetAsync(UserCacheKeys.Revoked(userId), DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Ttl);

    /// <summary>
    /// Redis lỗi thì GetAsync trả null → cho qua (fail-open, không làm sập toàn bộ API).
    /// iat chỉ có độ phân giải giây nên dùng "&lt;=": token cấp cùng giây với lúc khóa cũng bị loại.
    /// </summary>
    public async Task<bool> IsRevokedAsync(Guid userId, long issuedAt)
    {
        var mark = await _cache.GetAsync<long?>(UserCacheKeys.Revoked(userId));
        return mark is { } m && issuedAt <= m;
    }

    public async Task RevokeAllAsync(Guid userId)
    {
        // Đánh dấu trước để lỗi khi xóa phiên không làm token cũ sống lại.
        await MarkAsync(userId);

        foreach (var session in await _sessions.FindAsync(s => s.UserId == userId))
            _sessions.Remove(session);

        await _sessions.SaveChangesAsync();
    }
}