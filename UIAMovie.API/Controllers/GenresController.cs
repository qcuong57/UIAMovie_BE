// UIAMovie.API/Controllers/GenresController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;
using UIAMovie.Domain.Constants;
using UIAMovie.Infrastructure.Configuration;

namespace UIAMovie.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GenresController : ControllerBase
{
    private readonly IGenreService _genreService;
    private readonly ITmdbService  _tmdbService;

    public GenresController(IGenreService genreService, ITmdbService tmdbService)
    {
        _genreService = genreService;
        _tmdbService  = tmdbService;
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
            return Conflict(new { message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // TMDB Sync
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// [Admin] Đồng bộ genre từ TMDB vào database.
    /// Gọi endpoint này trước khi import phim để đảm bảo genre đã có trong DB.
    /// - Genre chưa có → thêm mới.
    /// - Genre đã có nhưng TMDB đổi tên → cập nhật tên.
    /// </summary>
    [HttpPost("sync-tmdb")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SyncFromTmdb()
    {
        var tmdbGenres = await _tmdbService.GetGenresAsync();
        var created    = await _genreService.SyncFromTmdbAsync(tmdbGenres);
        return Ok(new
        {
            message      = "Đồng bộ genre từ TMDB thành công",
            createdCount = created
        });
    }
}