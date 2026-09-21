// UIAMovie.Application/AI/Tools/IAiTool.cs
using UIAMovie.Application.AI.Models;

namespace UIAMovie.Application.AI.Tools;

public interface IAiTool
{
    string Name { get; }
    Task<AiToolResult> ExecuteAsync(string input, IDictionary<string, object>? parameters = null);
}