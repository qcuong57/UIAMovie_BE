namespace UIAMovie.Application.Caching;

/// <summary>Khóa cache liên quan tới user, dùng chung giữa các service để không lệch chuỗi.</summary>
internal static class UserCacheKeys
{
    public static string ById(Guid id) => $"user:id:{id}";

    // Không còn ghi key này (không cache entity User nữa); chỉ còn xóa cho tới khi key cũ hết TTL.
    public static string ByEmail(string email) => $"user:email:{email.Trim().ToLowerInvariant()}";

    /// <summary>Mốc thu hồi access token của user (Unix giây).</summary>
    public static string Revoked(Guid id) => $"user:revoked:{id}";
}