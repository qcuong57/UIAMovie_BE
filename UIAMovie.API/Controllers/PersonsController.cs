using Microsoft.AspNetCore.Authorization; // FIX: thiếu using này -> [Authorize] không compile
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Services;       // FIX: IPersonService nằm ở đây
using UIAMovie.Domain.Constants;

namespace UIAMovie.Controllers;

[ApiController]
[Route("api/persons")]
public class PersonsController : ControllerBase
{
    private readonly IPersonService _personService;
    public PersonsController(IPersonService personService) => _personService = personService;

    /// <summary>Autocomplete cho dropdown "chọn diễn viên có sẵn" khi thêm phim.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? query, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _personService.SearchPersonsAsync(query, page, pageSize);
        return Ok(new ApiResponseDTO<object> { Data = result, Message = "Thành công" });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var person = await _personService.GetPersonByIdAsync(id);
        return person == null
            ? NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy", StatusCode = 404 })
            : Ok(new ApiResponseDTO<object> { Data = person, Message = "Thành công" });
    }

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create([FromBody] CreatePersonDTO dto)
    {
        var id = await _personService.CreatePersonAsync(dto);
        return Ok(new ApiResponseDTO<object> { Data = new { id }, Message = "Đã tạo diễn viên/đạo diễn" });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePersonDTO dto)
    {
        var success = await _personService.UpdatePersonAsync(id, dto);
        return success ? Ok(new ApiResponseDTO<object> { Message = "Đã cập nhật" })
                        : NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy", StatusCode = 404 });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var success = await _personService.DeletePersonAsync(id);
        return success ? Ok(new ApiResponseDTO<object> { Message = "Đã xóa" })
                        : BadRequest(new ApiErrorResponseDTO { Message = "Đang gắn với phim, không thể xóa", StatusCode = 400 });
    }

    /// <summary>Liệt kê các nhóm Person nghi trùng tên — dùng để admin xét gộp.</summary>
    [HttpGet("duplicates")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> GetDuplicates()
    {
        var groups = await _personService.FindDuplicatesByNameAsync();
        return Ok(new ApiResponseDTO<object> { Data = groups, Message = "Thành công" });
    }

    /// <summary>Gộp các Person trùng vào 1 Person chính — chuyển cast/director/images rồi xóa bản trùng.</summary>
    [HttpPost("merge")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Merge([FromBody] MergePersonsDTO dto)
    {
        var success = await _personService.MergePersonsAsync(dto);
        return success ? Ok(new ApiResponseDTO<object> { Message = "Đã gộp" })
                        : BadRequest(new ApiErrorResponseDTO { Message = "Gộp thất bại", StatusCode = 400 });
    }
}