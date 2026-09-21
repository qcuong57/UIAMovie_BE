// UIAMovie.Application/AI/Orchestration/IAiAssistantService.cs
using UIAMovie.Application.DTOs.AI;

namespace UIAMovie.Application.AI.Orchestration;

public interface IAiAssistantService
{
    Task<AiChatResponseDto> ProcessChatAsync(
        AiChatRequestDto request, 
        Guid? userId = null, 
        CancellationToken cancellationToken = default);
}