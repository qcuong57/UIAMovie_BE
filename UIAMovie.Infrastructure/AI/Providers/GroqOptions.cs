// UIAMovie.Infrastructure/AI/Providers/GroqOptions.cs
namespace UIAMovie.Infrastructure.AI.Providers;

public class GroqOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/chat/completions";
    public string Model { get; set; } = "llama-3.1-8b-instant";
    public int MaxRetries { get; set; } = 3;
    public int RateLimitPermitsPerMinute { get; set; } = 28;
}