// UIAMovie.Application/Services/UserService.cs

using System.Text.RegularExpressions;
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

public class UserService : IUserService
{
    private const int MaxPageSize = 100;

    // Giá trị SubscriptionType hợp lệ khi admin sửa trực tiếp.
    // KHÔNG có giá trị premium: Premium phải đi qua PaymentService (thanh toán hoặc admin
    // grant-subscription) để có bản ghi UserSubscription tương ứng. Nếu chỉ đặt
    // User.SubscriptionType = "premium" thì /user/me báo Premium nhưng SubscriptionChecker coi là Free.
    private static readonly HashSet<string> AllowedSubscriptionTypes =
        new(StringComparer.OrdinalIgnoreCase) { "free", "standard" };

    // Cùng luật với RegisterDTO.Username và FE (utils/validators.js).
    private static readonly Regex UsernamePattern = new("^[a-zA-Z0-9_]+$", RegexOptions.Compiled);

    private readonly IRepository<User> _userRepository;
    private readonly IUserRepository _userQueries;
    private readonly IRepository<UserSubscription> _subRepository;
    private readonly ICacheService _cacheService;
    private readonly ITokenRevocationService _tokenRevocation;

    public UserService(
        IRepository<User> userRepository,
        IUserRepository userQueries,
        IRepository<UserSubscription> subRepository,
        ICacheService cacheService,
        ITokenRevocationService tokenRevocation)
    {
        _userRepository = userRepository;
        _userQueries = userQueries;
        _subRepository = subRepository;
        _cacheService = cacheService;
        _tokenRevocation = tokenRevocation;
    }

    public async Task<PaginatedDTO<UserDTO>> GetUsersAsync(UserQueryDTO query)
    {
        var pageSize   = query.PageSize > 0 ? Math.Min(query.PageSize, MaxPageSize) : 10;
        var pageNumber = query.PageNumber > 0 ? query.PageNumber : 1;

        // Lọc, đếm, sắp xếp, phân trang trong DB; chỉ tải các cột hiển thị của đúng trang này.
        var page = await _userQueries.SearchAsync(new UserSearchCriteria(
            query.Search, query.SubscriptionType, query.Role, query.IsActive,
            query.SortBy, query.SortDesc, pageNumber, pageSize));

        var pagedUsers = page.Items;
        var totalCount = page.TotalCount;

        // Load subscription records cho batch này
        var userIds = pagedUsers.Select(u => u.Id).ToHashSet();
        var allSubs = await _subRepository.FindAsync(s => userIds.Contains(s.UserId));
        var subMap = allSubs.GroupBy(s => s.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.ExpiredAt).First());

        var items = pagedUsers.Select(u => UserMapper.ToDto(u, subMap.GetValueOrDefault(u.Id))).ToList();

        return new PaginatedDTO<UserDTO>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<UserDTO?> GetUserByIdAsync(Guid id)
    {
        var cacheKey = UserCacheKeys.ById(id);
        return await _cacheService.GetOrSetAsync(cacheKey,
            async () =>
            {
                var user = await _userRepository.GetByIdAsync(id);
                if (user == null) return null;
                var subs = await _subRepository.FindAsync(s => s.UserId == id);
                var sub = subs.OrderByDescending(s => s.ExpiredAt).FirstOrDefault();
                return UserMapper.ToDto(user, sub);
            },
            TimeSpan.FromMinutes(5)); // giảm xuống 5 phút vì có sub data thay đổi thường xuyên
    }

    public async Task<(bool Success, string Message)> UpdateUserAsync(Guid id, UpdateUserDTO dto)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null) return (false, "Không tìm thấy user");

        if (dto.Username != null)
        {
            var username = dto.Username.Trim();
            if (username.Length is < 3 or > 50)
                return (false, "Tên người dùng phải từ 3 đến 50 ký tự");

            if (!UsernamePattern.IsMatch(username))
                return (false, "Tên người dùng chỉ được chứa chữ, số, dấu gạch dưới");

            // Username có unique index: báo lỗi rõ ràng thay vì để DB ném lỗi (500).
            var lowered = username.ToLower();
            var taken = await _userRepository.FindOneAsync(u => u.Id != id && u.Username.ToLower() == lowered);
            if (taken != null)
                return (false, "Tên người dùng đã được sử dụng");

            user.Username = username;
        }

        if (dto.SubscriptionType != null && !AllowedSubscriptionTypes.Contains(dto.SubscriptionType))
        {
            return dto.SubscriptionType.Contains("premium", StringComparison.OrdinalIgnoreCase)
                ? (false, "Không đặt Premium trực tiếp được. Hãy dùng chức năng cấp Premium (grant-subscription).")
                : (false, "Loại gói không hợp lệ");
        }

        user.AvatarUrl = dto.AvatarUrl ?? user.AvatarUrl;
        user.SubscriptionType = dto.SubscriptionType ?? user.SubscriptionType;
        user.UpdatedAt = DateTime.UtcNow;

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(UserCacheKeys.ById(id));
        return (true, "Cập nhật thành công");
    }

    public async Task<(bool Success, string Message)> UpdateUserRoleAsync(Guid id, string role)
    {
        if (role != Roles.Admin && role != Roles.User)
            return (false, "Role không hợp lệ. Chỉ chấp nhận 'Admin' hoặc 'User'");

        var user = await _userRepository.GetByIdAsync(id);
        if (user == null) return (false, "Không tìm thấy user");

        user.Role = role;
        user.UpdatedAt = DateTime.UtcNow;

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(UserCacheKeys.ById(id));
        // Token cũ còn mang role cũ: buộc client refresh để nhận role mới.
        await _tokenRevocation.MarkAsync(id);
        return (true, $"Đã cập nhật role thành {role}");
    }

    public async Task<bool> DeleteUserAsync(Guid id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null) return false;

        _userRepository.Remove(user);
        await _userRepository.SaveChangesAsync();

        await InvalidateUserCacheAsync(user);
        await _tokenRevocation.MarkAsync(id); // access token cũ hết hiệu lực ngay
        return true;
    }

    public async Task<(bool Success, string Message)> ChangePasswordAsync(
        Guid id, ChangePasswordDTO dto)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null) return (false, "Không tìm thấy user");

        if (!BCrypt.Net.BCrypt.Verify(dto.OldPassword, user.PasswordHash))
            return (false, "Mật khẩu cũ không đúng");

        if (dto.NewPassword != dto.ConfirmPassword)
            return (false, "Mật khẩu xác nhận không khớp");

        if (PasswordPolicy.Validate(dto.NewPassword) is { } policyError)
            return (false, policyError);

        if (dto.NewPassword == dto.OldPassword)
            return (false, "Mật khẩu mới phải khác mật khẩu cũ");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(UserCacheKeys.ById(id));
        // Đổi mật khẩu = đá mọi thiết bị: thu hồi access token đang sống và xóa toàn bộ refresh token.
        // Client hiện tại cũng bị đăng xuất, FE cần chuyển về trang đăng nhập sau khi nhận kết quả này.
        await _tokenRevocation.RevokeAllAsync(id);
        return (true, "Đổi mật khẩu thành công. Vui lòng đăng nhập lại.");
    }

    public async Task<(bool Success, string Message)> BanUserAsync(Guid id, BanUserDTO dto)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null) return (false, "Không tìm thấy user");

        if (!user.IsActive)
            return (false, "Tài khoản đã bị khóa trước đó");

        user.IsActive = false;
        user.BanReason = dto.Reason?.Trim();
        user.BannedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        await InvalidateUserCacheAsync(user);
        // Thu hồi access token đang sống + xóa toàn bộ session → kick khỏi hệ thống ngay lập tức.
        await _tokenRevocation.RevokeAllAsync(id);
        return (true, $"Đã khóa tài khoản {user.Email}");
    }

    public async Task<(bool Success, string Message)> UnbanUserAsync(Guid id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null) return (false, "Không tìm thấy user");

        if (user.IsActive)
            return (false, "Tài khoản chưa bị khóa");

        user.IsActive = true;
        user.BanReason = null;
        user.BannedAt = null;
        user.UpdatedAt = DateTime.UtcNow;

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        await InvalidateUserCacheAsync(user);
        return (true, $"Đã mở khóa tài khoản {user.Email}");
    }

    private async Task InvalidateUserCacheAsync(User user) =>
        await _cacheService.RemoveManyAsync(UserCacheKeys.ById(user.Id), UserCacheKeys.ByEmail(user.Email));
}