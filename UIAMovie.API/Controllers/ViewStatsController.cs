// UIAMovie.API/Controllers/ViewStatsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Domain.Constants;

namespace UIAMovie.API.Controllers;

[ApiController]
[Route("api/admin/view-stats")]
[Authorize(Roles = Roles.Admin)]   // cùng kiểu với MoviesController
public class ViewStatsController : ControllerBase
{
    private readonly IViewStatsService _service;
    public ViewStatsController(IViewStatsService service) => _service = service;

    /// <summary>granularity: day|week|month|quarter|year · contentType: all|movie|tvshow</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(
        [FromQuery] string? granularity, [FromQuery] string? contentType,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? contentId)
        => Ok(new ApiResponseDTO<object> { Data = await _service.GetOverviewAsync(granularity, contentType, from, to, contentId), Message = "Thành công" });

    [HttpGet("top")]
    public async Task<IActionResult> Top(
        [FromQuery] string? contentType, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int take = 10)
        => Ok(new ApiResponseDTO<object> { Data = await _service.GetTopAsync(contentType, from, to, take), Message = "Thành công" });

    [HttpGet("genres")]
    public async Task<IActionResult> Genres(
        [FromQuery] string? contentType, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
        => Ok(new ApiResponseDTO<object> { Data = await _service.GetByGenreAsync(contentType, from, to), Message = "Thành công" });
}

// Program.cs:
// builder.Services.AddScoped<IViewStatsRepository, ViewStatsRepository>();
// builder.Services.AddScoped<IViewStatsService, ViewStatsService>();