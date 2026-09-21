// UIAMovie.Application/DTOs/AI/AiChatResponseDto.cs
namespace UIAMovie.Application.DTOs.AI;

public class AiChatResponseDto
{
    // Cấu trúc mới chuẩn RESTful
    public string Message { get; set; } = string.Empty;
    public string Intent { get; set; } = "movie";
    public List<AiRecommendationItemDto> Items { get; set; } = new();
    public List<AiActionDto> Actions { get; set; } = new();
    public AiResponseMetaDto Meta { get; set; } = new();

    // ── Giữ nguyên Backward Compatibility cho Frontend hiện tại ──
    public string Reply 
    { 
        get => Message; 
        set => Message = value; 
    }
    public List<MovieDTO> Movies { get; set; } = new();
    public List<TvShowSummaryDTO> TvShows { get; set; } = new();
    public string? CompareTable { get; set; }
    public IReadOnlyList<string> SuggestedActions { get; set; } = Array.Empty<string>();
}

public class AiRecommendationItemDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "movie"; // "movie" | "tv"
    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public decimal? Rating { get; set; }
    public int? Year { get; set; }
    public bool IsPremium { get; set; }
    public List<string> Genres { get; set; } = new();
}

public class AiActionDto
{
    public string Type { get; set; } = "chip"; // "chip" | "navigate" | "modal"
    public string Label { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public class AiResponseMetaDto
{
    public string Provider { get; set; } = "groq";
    public int ProcessingTimeMs { get; set; }
    public bool FallbackApplied { get; set; }
}