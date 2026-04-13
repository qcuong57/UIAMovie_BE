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
        return Ok(new ApiResponseDTO<IEnumerable<object>> { Data = genres, Message = "Thành công" });
    }

    /// <summary>Lấy chi tiết genre theo ID</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var genre = await _genreService.GetByIdAsync(id);
        return genre == null
            ? NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy genre", StatusCode = 404 })
            : Ok(new ApiResponseDTO<object> { Data = genre, Message = "Thành công" });
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
            return BadRequest(new ApiErrorResponseDTO { Message = "Tên genre không được để trống", StatusCode = 400 });

        try
        {
            var id = await _genreService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id },
                new ApiResponseDTO<object> { Data = new { id }, Message = "Tạo genre thành công" });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiErrorResponseDTO { Message = ex.Message, StatusCode = 409 });
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
                ? Ok(new ApiResponseDTO<object> { Message = "Cập nhật genre thành công" })
                : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy genre", StatusCode = 404 });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiErrorResponseDTO { Message = ex.Message, StatusCode = 409 });
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
                ? Ok(new ApiResponseDTO<object> { Message = "Xóa genre thành công" })
                : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy genre", StatusCode = 404 });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiErrorResponseDTO { Message = ex.Message, StatusCode = 409 });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // TMDB Sync
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// [Admin] Đồng bộ genre từ TMDB vào database.
    /// </summary>
    [HttpPost("sync-tmdb")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SyncFromTmdb()
    {
        var tmdbGenres = await _tmdbService.GetGenresAsync();
        var created    = await _genreService.SyncFromTmdbAsync(tmdbGenres);
        return Ok(new ApiResponseDTO<object>
        {
            Data = new { createdCount = created },
            Message = "Đồng bộ genre từ TMDB thành công"
        });
    }
}