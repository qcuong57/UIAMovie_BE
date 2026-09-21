// UIAMovie.Application/AI/Models/AiToolResult.cs
namespace UIAMovie.Application.AI.Models;

public sealed record AiToolResult
{
    public bool Success { get; init; } = true;
    public string ToolName { get; init; } = string.Empty;
    public object? Data { get; init; }
    public string? FormattedContext { get; init; }
    public string? ErrorMessage { get; init; }
}