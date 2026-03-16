// UIAMovie.API/Controllers/UserController.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;
using UIAMovie.Domain.Constants;

namespace UIAMovie.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]  // Tất cả endpoint đều cần đăng nhập
public class UserController : ControllerBase
{
    private readonly IUserService _userService;

    public UserController(IUserService userService)
    {
        _userService = userService;
    }

    // ═══════════════════════════════════════════════════════════════════
    // ADMIN ONLY
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>[Admin] Lấy danh sách user — tìm kiếm, lọc, phân trang</summary>
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> GetUsers([FromQuery] UserQueryDTO query)
    {
        var result = await _userService.GetUsersAsync(query);
        return Ok(result);
    }

    /// <summary>[Admin] Lấy thông tin user theo ID</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        return user == null
            ? NotFound(new { message = "Không tìm thấy user" })
            : Ok(user);
    }

    /// <summary>[Admin] Cập nhật thông tin user bất kỳ</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserDTO dto)
    {
        var (success, message) = await _userService.UpdateUserAsync(id, dto);
        return success ? Ok(new { message }) : NotFound(new { message });
    }

    /// <summary>[Admin] Thay đổi role của user (User ↔ Admin)</summary>
    [HttpPatch("{id:guid}/role")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UpdateRoleDTO dto)
    {
        var (success, message) = await _userService.UpdateUserRoleAsync(id, dto.Role);
        return success ? Ok(new { message }) : BadRequest(new { message });
    }

    /// <summary>[Admin] Xóa user</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(Guid id)
    {
        // Không cho phép admin tự xóa chính mình
        if (id == GetUserId())
            return BadRequest(new { message = "Không thể tự xóa tài khoản của mình" });

        var success = await _userService.DeleteUserAsync(id);
        return success
            ? Ok(new { message = "Xóa user thành công" })
            : NotFound(new { message = "Không tìm thấy user" });
    }

    // ═══════════════════════════════════════════════════════════════════
    // USER (chính mình)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Lấy thông tin user đang đăng nhập</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var user = await _userService.GetUserByIdAsync(GetUserId());
        return user == null ? NotFound() : Ok(user);
    }

    /// <summary>Cập nhật thông tin bản thân</summary>
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateUserDTO dto)
    {
        var (success, message) = await _userService.UpdateUserAsync(GetUserId(), dto);
        return success ? Ok(new { message }) : BadRequest(new { message });
    }

    /// <summary>Đổi mật khẩu</summary>
    [HttpPost("me/change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDTO dto)
    {
        var (success, message) = await _userService.ChangePasswordAsync(GetUserId(), dto);
        return success ? Ok(new { message }) : BadRequest(new { message });
    }

    // ─── Helper ──────────────────────────────────────────────────────────────

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Guid.Empty.ToString());
}