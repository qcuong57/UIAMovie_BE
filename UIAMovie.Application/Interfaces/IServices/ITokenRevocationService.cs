namespace UIAMovie.Application.Interfaces.IServices;

/// <summary>
/// Thu hồi access token còn hạn của một user (khóa, xóa, đổi role, đổi/reset mật khẩu).
/// Token có iat nhỏ hơn hoặc bằng mốc thu hồi sẽ bị từ chối; token cấp SAU mốc vẫn hợp lệ.
/// </summary>
public interface ITokenRevocationService
{
    /// <summary>Đặt mốc thu hồi = bây giờ (chỉ vô hiệu access token, không đụng tới session).</summary>
    Task MarkAsync(Guid userId);

    /// <summary>Token phát hành (iat, Unix giây) tại <paramref name="issuedAt"/> có bị thu hồi không.</summary>
    Task<bool> IsRevokedAsync(Guid userId, long issuedAt);

    /// <summary>Đá user khỏi mọi thiết bị: đánh dấu thu hồi trước, rồi xóa toàn bộ session (refresh token).</summary>
    Task RevokeAllAsync(Guid userId);
}