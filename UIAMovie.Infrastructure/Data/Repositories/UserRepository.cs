using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Infrastructure.Data.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly MovieDbContext _db;

    public UserRepository(MovieDbContext db) => _db = db;

    public async Task<bool> IsActiveAsync(Guid userId, CancellationToken ct = default)
    {
        // Ép sang bool? để phân biệt "không có user" (null) với "user bị khóa" (false).
        var active = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (bool?)u.IsActive)
            .FirstOrDefaultAsync(ct);

        return active == true;
    }

    public async Task<UserPage> SearchAsync(UserSearchCriteria c, CancellationToken ct = default)
    {
        var now  = DateTime.UtcNow;
        var subs = _db.UserSubscriptions.AsNoTracking();
        IQueryable<User> q = _db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(c.Search))
        {
            var s = c.Search.Trim().ToLower();
            q = q.Where(u => u.Email.ToLower().Contains(s) || u.Username.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(c.SubscriptionType))
        {
            var type = c.SubscriptionType.Trim();

            if (type.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("free", StringComparison.OrdinalIgnoreCase))
            {
                // Không có gói còn hạn; user chưa có bản ghi gói thì xét theo cột trên User (đúng như UserMapper).
                // So sánh "free" KHÔNG phân biệt hoa thường: hằng số SubscriptionTypes.Free là "Free",
                // và Postgres so sánh chuỗi có phân biệt hoa thường. Trước đây user bị thu hồi/hủy gói
                // (User.SubscriptionType = "Free", không còn dòng subscription) bị loại khỏi bộ lọc này.
                q = q.Where(u =>
                    !subs.Any(s => s.UserId == u.Id && (s.ExpiredAt == null || s.ExpiredAt > now)) &&
                    (u.SubscriptionType == null || u.SubscriptionType.ToLower() == "free" ||
                     subs.Any(s => s.UserId == u.Id)));
            }
            else
            {
                // Bản ghi gói lưu loại "Premium" (SubscriptionTypes.Premium), KHÔNG lưu monthly/yearly:
                // PlanId chỉ nằm ở PaymentOrder. So sánh không phân biệt hoa thường.
                var typeLower = type.ToLowerInvariant();
                q = q.Where(u =>
                    subs.Any(s => s.UserId == u.Id && s.SubscriptionType.ToLower() == typeLower &&
                                  (s.ExpiredAt == null || s.ExpiredAt > now)) ||
                    (!subs.Any(s => s.UserId == u.Id) && u.SubscriptionType != null &&
                     u.SubscriptionType.ToLower() == typeLower));
            }
        }

        if (!string.IsNullOrWhiteSpace(c.Role))
            q = q.Where(u => u.Role == c.Role);

        if (c.IsActive.HasValue)
            q = q.Where(u => u.IsActive == c.IsActive.Value);

        var total = await q.CountAsync(ct);

        var ordered = c.SortBy?.ToLowerInvariant() switch
        {
            "email"     => c.SortDesc ? q.OrderByDescending(u => u.Email)     : q.OrderBy(u => u.Email),
            "username"  => c.SortDesc ? q.OrderByDescending(u => u.Username)  : q.OrderBy(u => u.Username),
            "role"      => c.SortDesc ? q.OrderByDescending(u => u.Role)      : q.OrderBy(u => u.Role),
            "createdat" => c.SortDesc ? q.OrderByDescending(u => u.CreatedAt) : q.OrderBy(u => u.CreatedAt),
            _           => q.OrderByDescending(u => u.CreatedAt)
        };

        // Id làm khóa phụ để thứ tự ổn định giữa các trang khi giá trị sắp xếp trùng nhau.
        var items = await ordered
            .ThenBy(u => u.Id)
            .Skip((c.PageNumber - 1) * c.PageSize)
            .Take(c.PageSize)
            .Select(u => new User
            {
                Id               = u.Id,
                Email            = u.Email,
                Username         = u.Username,
                AvatarUrl        = u.AvatarUrl,
                SubscriptionType = u.SubscriptionType,
                Role             = u.Role,
                Is2FaEnabled     = u.Is2FaEnabled,
                CreatedAt        = u.CreatedAt,
                IsActive         = u.IsActive,
                BanReason        = u.BanReason
            })
            .ToListAsync(ct);

        return new UserPage(items, total);
    }
}