// UIAMovie.Infrastructure/AI/Parsing/AiJsonParser.cs
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace UIAMovie.Infrastructure.AI.Parsing;

public static class AiJsonParser
{
    public static string ExtractJsonContent(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var text = raw.Trim();

        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            text = text[7..];
        if (text.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            text = text[3..];
        if (text.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            text = text[..^3];

        var startArr = text.IndexOf('[');
        var endArr = text.LastIndexOf(']');
        if (startArr != -1 && endArr != -1 && endArr > startArr)
            return text.Substring(startArr, endArr - startArr + 1);

        var startObj = text.IndexOf('{');
        var endObj = text.LastIndexOf('}');
        if (startObj != -1 && endObj != -1 && endObj > startObj)
            return text.Substring(startObj, endObj - startObj + 1);

        return text.Trim();
    }

    public static List<Guid> ParseGuidArray(string raw, ILogger? logger = null)
    {
        try
        {
            var clean = ExtractJsonContent(raw);
            if (string.IsNullOrWhiteSpace(clean)) return new();

            using var doc = JsonDocument.Parse(clean);
            JsonElement arrayElement;

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                arrayElement = doc.RootElement;
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("ids", out var idsProp))
            {
                arrayElement = idsProp;
            }
            else
            {
                return new();
            }

            return arrayElement.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => Guid.TryParse(s, out _))
                .Select(s => Guid.Parse(s!))
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[AiJsonParser] Parse JSON sang Guid Array thất bại");
            return new();
        }
    }
}