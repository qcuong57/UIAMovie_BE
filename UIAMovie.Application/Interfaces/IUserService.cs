// UIAMovie.Application/Services/IUserService.cs
using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Services;

public interface IUserService
{
    Task<PaginatedDTO<UserDTO>> GetUsersAsync(UserQueryDTO query);
    Task<UserDTO?> GetUserByIdAsync(Guid id);
    Task<(bool Success, string Message)> UpdateUserAsync(Guid id, UpdateUserDTO dto);
    Task<(bool Success, string Message)> UpdateUserRoleAsync(Guid id, string role); // ← Thêm dòng này
    Task<bool> DeleteUserAsync(Guid id);
    Task<(bool Success, string Message)> ChangePasswordAsync(Guid id, ChangePasswordDTO dto);
}