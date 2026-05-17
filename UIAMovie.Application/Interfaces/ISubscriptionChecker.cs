// UIAMovie.Application/Interfaces/ISubscriptionChecker.cs
namespace UIAMovie.Application.Interfaces;

/// <summary>
/// Dùng thay cho user.SubscriptionType để check quyền truy cập.
/// Luôn kiểm tra ExpiredAt thực tế — tránh trường hợp DB còn "Premium"
/// nhưng đã hết hạn.
/// </summary>
public interface ISubscriptionChecker
{
    /// <summary>
    /// Trả về true nếu user đang có Premium HỢP LỆ (chưa hết hạn).
    /// </summary>
    Task<bool> IsPremiumAsync(Guid userId);

    /// <summary>
    /// Trả về true nếu user có thể xem content Premium.
    /// Kết hợp kiểm tra: tài khoản active + subscription còn hạn.
    /// </summary>
    Task<bool> CanWatchPremiumContentAsync(Guid userId);
}