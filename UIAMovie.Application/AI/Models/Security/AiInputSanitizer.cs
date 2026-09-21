// UIAMovie.Application/AI/Security/AiInputSanitizer.cs
using System.Text.RegularExpressions;

namespace UIAMovie.Application.AI.Security;

public static class AiInputSanitizer
{
    private static readonly Regex SystemPromptInjectionRx = new(
        @"(system\s*:\s*|ignore\s+previous\s+instructions|bỏ\s+qua\s+chỉ\s+dẫn)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string SanitizeUserMessage(string? input, int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var clean = input.Trim();
        if (clean.Length > maxLength)
            clean = clean[..maxLength];

        return clean;
    }

    public static bool HasInjectionRisk(string message)
        => SystemPromptInjectionRx.IsMatch(message);
}