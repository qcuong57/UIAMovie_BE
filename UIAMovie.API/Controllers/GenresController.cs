// UIAMovie.API/Controllers/GenresController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;
using UIAMovie.Domain.Constants;

namespace UIAMovie.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GenresController : ControllerBase
{
    private readonly IGenreService _genreService;

    public GenresController(IGenreService genreService)
    {
        _genreService = genreService;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PUBLIC — Không cần đăng nhập
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Lấy danh sách tất cả genre</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var genres = await _genreService.GetAllAsync();
        return Ok(genres);
    }

    /// <summary>Lấy chi tiết genre theo ID</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var genre = await _genreService.GetByIdAsync(id);
        return genre == null
            ? NotFound(new { message = "Không tìm thấy genre" })
            : Ok(genre);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ADMIN — CRUD genre
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>[Admin] Tạo genre mới</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create([FromBody] CreateGenreDTO dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Tên genre không được để trống" });

        try
        {
            var id = await _genreService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id },
                new { message = "Tạo genre thành công", id });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>[Admin] Cập nhật genre</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGenreDTO dto)
    {
        try
        {
            var success = await _genreService.UpdateAsync(id, dto);
            return success
                ? Ok(new { message = "Cập nhật genre thành công" })
                : NotFound(new { message = "Không tìm thấy genre" });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>[Admin] Xóa genre</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var success = await _genreService.DeleteAsync(id);
            return success
                ? Ok(new { message = "Xóa genre thành công" })
                : NotFound(new { message = "Không tìm thấy genre" });
        }
        catch (InvalidOperationException ex)
        {
            // Genre đang được dùng bởi phim — không cho xóa
            return Conflict(new { message = ex.Message });
        }
    }
}