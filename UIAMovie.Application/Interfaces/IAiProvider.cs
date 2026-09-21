// UIAMovie.Infrastructure/AI/Providers/IAiProvider.cs
namespace UIAMovie.Infrastructure.AI.Providers;

public class AiProviderMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}

public class AiProviderOptions
{
    public string? Model { get; set; }
    public int MaxTokens { get; set; } = 500;
    public double Temperature { get; set; } = 0.2;
    public bool EnforceJsonObject { get; set; } = false;
}

public interface IAiProvider
{
    Task<string> GenerateChatResponseAsync(
        IEnumerable<AiProviderMessage> messages,
        AiProviderOptions? options = null,
        CancellationToken cancellationToken = default);
}