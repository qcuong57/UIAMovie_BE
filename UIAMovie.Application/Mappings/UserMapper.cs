using UIAMovie.Application.DTOs;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Mappings;

/// <summary>Map User → UserDTO dùng chung cho /me, danh sách admin và response đăng nhập.</summary>
internal static class UserMapper
{
    public static UserDTO ToDto(User u, UserSubscription? sub = null)
    {
        // Ưu tiên bản ghi UserSubscription; fallback về field trên User khi không có.
        var rawType   = sub?.SubscriptionType ?? u.SubscriptionType;
        var expiredAt = sub?.ExpiredAt;
        var isExpired = expiredAt.HasValue && expiredAt.Value < DateTime.UtcNow;

        // Quy ước hiện có của FE (trang admin, hồ sơ): null = không có gói đang hiệu lực.
        // Gói free hoặc đã hết hạn đều trả null.
        var effective = isExpired || string.IsNullOrWhiteSpace(rawType)
                                  || rawType.Equals("free", StringComparison.OrdinalIgnoreCase)
            ? null
            : rawType;

        return new UserDTO
        {
            Id = u.Id,
            Email = u.Email,
            Username = u.Username,
            AvatarUrl = u.AvatarUrl,
            SubscriptionType = effective, // null nếu không có gói hiệu lực
            SubscriptionExpiredAt = expiredAt,
            SubscriptionStartedAt = sub?.StartedAt,
            Role = u.Role,
            Is2FaEnabled = u.Is2FaEnabled,
            CreatedAt = u.CreatedAt,
            IsActive = u.IsActive,
            BanReason = u.BanReason,
        };
    }
}