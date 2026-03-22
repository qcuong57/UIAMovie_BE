// UIAMovie.Application/Services/UserService.cs
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Constants;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Services;

// public interface IUserService
// {
//     Task<PaginatedDTO<UserDTO>> GetUsersAsync(UserQueryDTO query);
//     Task<UserDTO?> GetUserByIdAsync(Guid id);
//     Task<(bool Success, string Message)> UpdateUserAsync(Guid id, UpdateUserDTO dto);
//     Task<(bool Success, string Message)> UpdateUserRoleAsync(Guid id, string role); // ← Admin
//     Task<bool> DeleteUserAsync(Guid id);
//     Task<(bool Success, string Message)> ChangePasswordAsync(Guid id, ChangePasswordDTO dto);
// }

public class UserService : IUserService
{
    private readonly IRepository<User> _userRepository;
    private readonly ICacheService     _cacheService;

    private const string USER_CACHE_KEY  = "user:id:{0}";
    private const string USERS_LIST_KEY  = "users:list:{0}:{1}:{2}:{3}"; // page,size,search,role

    public UserService(
        IRepository<User> userRepository,
        ICacheService     cacheService)
    {
        _userRepository = userRepository;
        _cacheService   = cacheService;
    }

    public async Task<PaginatedDTO<UserDTO>> GetUsersAsync(UserQueryDTO query)
    {
        var users = await _userRepository.GetAllAsync();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.ToLower();
            users = users.Where(u =>
                u.Email.ToLower().Contains(search) ||
                u.Username.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(query.SubscriptionType))
            users = users.Where(u => u.SubscriptionType == query.SubscriptionType);

        if (!string.IsNullOrWhiteSpace(query.Role))
            users = users.Where(u => u.Role == query.Role);

        if (query.IsActive.HasValue)
            users = users.Where(u => u.IsActive == query.IsActive.Value);

        users = query.SortBy?.ToLower() switch
        {
            "email"     => query.SortDesc ? users.OrderByDescending(u => u.Email)
                                          : users.OrderBy(u => u.Email),
            "username"  => query.SortDesc ? users.OrderByDescending(u => u.Username)
                                          : users.OrderBy(u => u.Username),
            "role"      => query.SortDesc ? users.OrderByDescending(u => u.Role)
                                          : users.OrderBy(u => u.Role),
            "createdat" => query.SortDesc ? users.OrderByDescending(u => u.CreatedAt)
                                          : users.OrderBy(u => u.CreatedAt),
            _           => users.OrderByDescending(u => u.CreatedAt)
        };

        var totalCount = users.Count();
        var pageSize   = query.PageSize > 0 ? query.PageSize : 10;
        var pageNumber = query.PageNumber > 0 ? query.PageNumber : 1;

        var items = users
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(MapToDTO)
            .ToList();

        return new PaginatedDTO<UserDTO>
        {
            Items      = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize   = pageSize
        };
    }

    public async Task<UserDTO?> GetUserByIdAsync(Guid id)
    {
        var cacheKey = string.Format(USER_CACHE_KEY, id);
        return await _cacheService.GetOrSetAsync(cacheKey,
            async () =>
            {
                var user = await _userRepository.GetByIdAsync(id);
                return user == null ? null : MapToDTO(user);
            },
            TimeSpan.FromMinutes(30));
    }

    public async Task<(bool Success, string Message)> UpdateUserAsync(Guid id, UpdateUserDTO dto)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null) return (false, "Không tìm thấy user");

        user.Username         = dto.Username         ?? user.Username;
        user.AvatarUrl        = dto.AvatarUrl        ?? user.AvatarUrl;
        user.SubscriptionType = dto.SubscriptionType ?? user.SubscriptionType;
        user.UpdatedAt        = DateTime.UtcNow;

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(USER_CACHE_KEY, id));
        return (true, "Cập nhật thành công");
    }

    public async Task<(bool Success, string Message)> UpdateUserRoleAsync(Guid id, string role)
    {
        if (role != Roles.Admin && role != Roles.User)
            return (false, "Role không hợp lệ. Chỉ chấp nhận 'Admin' hoặc 'User'");

        var user = await _userRepository.GetByIdAsync(id);
        if (user == null) return (false, "Không tìm thấy user");

        user.Role      = role;
        user.UpdatedAt = DateTime.UtcNow;

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(USER_CACHE_KEY, id));
        return (true, $"Đã cập nhật role thành {role}");
    }

    public async Task<bool> DeleteUserAsync(Guid id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null) return false;

        _userRepository.Remove(user);
        await _userRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(USER_CACHE_KEY, id));
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

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.UpdatedAt    = DateTime.UtcNow;

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        await _cacheService.RemoveAsync(string.Format(USER_CACHE_KEY, id));
        return (true, "Đổi mật khẩu thành công");
    }

    private static UserDTO MapToDTO(User u) => new()
    {
        Id               = u.Id,
        Email            = u.Email,
        Username         = u.Username,
        AvatarUrl        = u.AvatarUrl,
        SubscriptionType = u.SubscriptionType,
        Role             = u.Role,
        Is2FaEnabled     = u.Is2FaEnabled, // ← map trạng thái 2FA
        CreatedAt        = u.CreatedAt
    };
}