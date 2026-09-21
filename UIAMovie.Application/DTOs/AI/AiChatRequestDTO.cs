// UIAMovie.Application/DTOs/AI/AiChatRequestDto.cs
using System.ComponentModel.DataAnnotations;

namespace UIAMovie.Application.DTOs.AI;

public class AiChatRequestDto
{
    public const int MaxHistoryTurns = 12;
    public const int MaxTurnLength = 2000;

    private static readonly HashSet<string> AllowedRoles =
        new(StringComparer.OrdinalIgnoreCase) { "user", "assistant" };

    [Required(ErrorMessage = "Message không được để trống.")]
    [MaxLength(500, ErrorMessage = "Message không được vượt quá 500 ký tự.")]
    public string Message { get; set; } = string.Empty;

    private List<AiChatMessageDto> _history = new();

    public List<AiChatMessageDto> History
    {
        get => _history;
        set => _history = Sanitize(value);
    }

    /// <summary>
    /// Context phụ từ client (path, id đang xem). Client input được xem là untrusted.
    /// </summary>
    public AiClientContextDto? ClientContext { get; set; }

    private static List<AiChatMessageDto> Sanitize(List<AiChatMessageDto>? raw)
    {
        if (raw is null || raw.Count == 0) return new();

        return raw
            .Where(t => t is not null
                        && !string.IsNullOrWhiteSpace(t.Role)
                        && AllowedRoles.Contains(t.Role.Trim())
                        && !string.IsNullOrWhiteSpace(t.Content))
            .Select(t =>
            {
                var content = t.Content.Trim();
                if (content.Length > MaxTurnLength) content = content[..MaxTurnLength];
                return new AiChatMessageDto 
                { 
                    Role = t.Role.Trim().ToLowerInvariant(), 
                    Content = content 
                };
            })
            .TakeLast(MaxHistoryTurns)
            .ToList();
    }
}

public class AiChatMessageDto
{
    [Required]
    public string Role { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;
}

public class AiClientContextDto
{
    public string? CurrentPath { get; set; }
    public Guid? CurrentMovieId { get; set; }
    public Guid? CurrentTvShowId { get; set; }
    public string? SearchQuery { get; set; }
}