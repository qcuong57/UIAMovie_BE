// UIAMovie.Controllers/AiController.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.AI.Orchestration;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.DTOs.AI;

namespace UIAMovie.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AiController : ControllerBase
{
    private readonly IAiAssistantService _assistantService;
    private readonly ILogger<AiController> _logger;

    private const int MaxChatMessageLength = 500;

    public AiController(
        IAiAssistantService assistantService,
        ILogger<AiController> logger)
    {
        _assistantService = assistantService;
        _logger = logger;
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] AiChatRequestDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Message))
            return BadRequest(ApiResponse.Fail("Message không được để trống."));

        if (dto.Message.Length > MaxChatMessageLength)
            return BadRequest(ApiResponse.Fail($"Message không được vượt quá {MaxChatMessageLength} ký tự."));

        try
        {
            Guid? userId = TryGetUserId();
            var response = await _assistantService.ProcessChatAsync(dto, userId, cancellationToken);
            return Ok(ApiResponse.Ok(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI] Chat failed: {Msg}", dto.Message);
            return StatusCode(503, ApiResponse.Fail("Dịch vụ AI tạm thời không khả dụng. Vui lòng thử lại."));
        }
    }

    private Guid? TryGetUserId()
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }
}

file static class ApiResponse
{
    internal static ApiResponseDTO<T> Ok<T>(T data, string message = "Thành công")
        => new() { Data = data, Message = message, Success = true };

    internal static ApiResponseDTO<object> Fail(string message)
        => new() { Message = message, Success = false };
}