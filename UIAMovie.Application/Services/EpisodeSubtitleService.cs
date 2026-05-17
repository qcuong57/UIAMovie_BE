// UIAMovie.Application/Services/EpisodeSubtitleService.cs
//
// [FIX 1] Fire-and-forget dùng IServiceScopeFactory để tạo scope mới trong Task.Run.
//         DbContext (scoped) của request gốc sẽ bị dispose ngay sau khi response trả về.
//         Background task cần scope riêng → repo riêng → DbContext riêng.
//
// [FIX 2] Không giữ tham chiếu entity qua scope boundary.
//         Sau khi SaveChangesAsync() trong scope request, chỉ truyền entity.Id vào Task.Run.
//         Background task tự load lại entity từ DB bằng Id đó.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UIAMovie.Application.DTOs;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Services;

public class EpisodeSubtitleService : IEpisodeSubtitleService
{
    private readonly IEpisodeSubtitleRepository _repo;
    private readonly IConfiguration             _config;
    private readonly HttpClient                 _httpClient;
    private readonly IServiceScopeFactory       _scopeFactory;   // [FIX 1]

    private string GroqApiKey  => _config["Groq:ApiKey"]  ?? "";
    private string GroqBaseUrl => _config["Groq:BaseUrl"] ?? "https://api.groq.com/openai/v1/chat/completions";
    private string GroqModel   => _config["Groq:Model"]   ?? "llama-3.1-8b-instant";

    private static readonly Dictionary<string, string> _languageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vi"] = "Tiếng Việt",
        ["en"] = "English",
        ["ko"] = "한국어",
        ["ja"] = "日本語",
        ["zh"] = "中文",
        ["fr"] = "Français",
        ["es"] = "Español",
        ["de"] = "Deutsch",
        ["th"] = "ภาษาไทย",
        ["id"] = "Bahasa Indonesia",
        ["pt"] = "Português",
        ["ar"] = "العربية",
        ["ru"] = "Русский",
        ["it"] = "Italiano",
    };

    public EpisodeSubtitleService(
        IEpisodeSubtitleRepository repo,
        IConfiguration             config,
        IHttpClientFactory         httpClientFactory,
        IServiceScopeFactory       scopeFactory)       // [FIX 1] inject thêm
    {
        _repo         = repo;
        _config       = config;
        _httpClient   = httpClientFactory.CreateClient("groq");
        _scopeFactory = scopeFactory;
    }

    // ── GET list (meta only) ──────────────────────────────────────────────────

    public async Task<IEnumerable<EpisodeSubtitleDTO>> GetSubtitlesAsync(Guid episodeId)
    {
        var entities = await _repo.GetByEpisodeIdAsync(episodeId);
        return entities.Select(ToDto);
    }

    // ── GET single (meta, no content) ────────────────────────────────────────

    public async Task<EpisodeSubtitleDTO?> GetSubtitleAsync(Guid subtitleId)
    {
        var entity = await _repo.GetByIdAsync(subtitleId);
        return entity == null ? null : ToDto(entity);
    }

    // ── GET content (full) ────────────────────────────────────────────────────

    public async Task<EpisodeSubtitleContentDTO?> GetSubtitleContentAsync(Guid subtitleId)
    {
        var entity = await _repo.GetByIdAsync(subtitleId);
        if (entity == null) return null;

        return new EpisodeSubtitleContentDTO
        {
            Id           = entity.Id,
            EpisodeId    = entity.EpisodeId,
            LanguageCode = entity.LanguageCode,
            LanguageName = entity.LanguageName,
            Content      = entity.Content
        };
    }

    // ── Upload thủ công ───────────────────────────────────────────────────────

    public async Task<EpisodeSubtitleDTO> UploadSubtitleAsync(
        Guid episodeId, UploadSubtitleDTO dto, Guid uploadedBy)
    {
        ValidateFile(dto.File);

        var rawText    = await ReadFileTextAsync(dto.File);
        var ext        = Path.GetExtension(dto.File.FileName).ToLowerInvariant();
        var vttContent = ext == ".srt" ? ConvertSrtToVtt(rawText) : NormalizeVtt(rawText);
        var langName   = ResolveLanguageName(dto.LanguageCode, dto.LanguageName);

        var existing = await _repo.GetByEpisodeAndLanguageAsync(episodeId, dto.LanguageCode);
        if (existing != null)
        {
            existing.Content      = vttContent;
            existing.LanguageName = langName;
            existing.Source       = SubtitleSource.Manual;
            existing.Status       = SubtitleStatus.Ready;
            existing.UpdatedAt    = DateTime.UtcNow;

            if (dto.IsDefault && !existing.IsDefault)
            {
                await _repo.ClearDefaultAsync(episodeId);
                existing.IsDefault = true;
            }

            _repo.Update(existing);
            await _repo.SaveChangesAsync();
            return ToDto(existing);
        }

        if (dto.IsDefault)
            await _repo.ClearDefaultAsync(episodeId);

        var entity = new EpisodeSubtitle
        {
            EpisodeId    = episodeId,
            LanguageCode = dto.LanguageCode,
            LanguageName = langName,
            Content      = vttContent,
            Source       = SubtitleSource.Manual,
            Status       = SubtitleStatus.Ready,
            IsDefault    = dto.IsDefault,
            UploadedBy   = uploadedBy
        };

        await _repo.AddAsync(entity);
        await _repo.SaveChangesAsync();
        return ToDto(entity);
    }

    // ── AI dịch từ subtitle đã có ─────────────────────────────────────────────

    public async Task<EpisodeSubtitleDTO> TranslateSubtitleAsync(
        Guid episodeId, TranslateSubtitleDTO dto, Guid requestedBy)
    {
        var source = await _repo.GetByIdAsync(dto.SourceSubtitleId)
            ?? throw new KeyNotFoundException("Không tìm thấy subtitle nguồn.");

        if (source.Status == SubtitleStatus.Processing)
            throw new InvalidOperationException("Subtitle nguồn đang được xử lý, vui lòng đợi.");

        var langName     = ResolveLanguageName(dto.TargetLanguageCode, dto.TargetLanguageName);
        // Capture giá trị cần dùng trong background — không capture entity hay repo
        var sourceContent    = source.Content;
        var sourceLangCode   = source.LanguageCode;
        var targetLangCode   = dto.TargetLanguageCode;

        var existing = await _repo.GetByEpisodeAndLanguageAsync(episodeId, targetLangCode);
        EpisodeSubtitle entity;

        if (existing != null)
        {
            existing.Status         = SubtitleStatus.Processing;
            existing.Content        = string.Empty;
            existing.TranslatedFrom = sourceLangCode;
            existing.Source         = SubtitleSource.AiTranslated;
            existing.UpdatedAt      = DateTime.UtcNow;
            _repo.Update(existing);
            await _repo.SaveChangesAsync();
            entity = existing;
        }
        else
        {
            entity = new EpisodeSubtitle
            {
                EpisodeId      = episodeId,
                LanguageCode   = targetLangCode,
                LanguageName   = langName,
                Content        = string.Empty,
                Source         = SubtitleSource.AiTranslated,
                TranslatedFrom = sourceLangCode,
                Status         = SubtitleStatus.Processing,
                UploadedBy     = requestedBy
            };
            await _repo.AddAsync(entity);
            await _repo.SaveChangesAsync();
        }

        // [FIX 1 + FIX 2] Chỉ truyền Id vào Task.Run — tạo scope mới bên trong
        var entityId = entity.Id;
        _ = Task.Run(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IEpisodeSubtitleRepository>();

            var bgEntity = await repo.GetByIdAsync(entityId);
            if (bgEntity == null) return;

            try
            {
                var translated = await TranslateVttWithGroqAsync(
                    sourceContent, sourceLangCode, targetLangCode);

                bgEntity.Content   = translated;
                bgEntity.Status    = SubtitleStatus.Ready;
                bgEntity.UpdatedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                bgEntity.Status       = SubtitleStatus.Failed;
                bgEntity.ErrorMessage = ex.Message;
                bgEntity.UpdatedAt    = DateTime.UtcNow;
            }

            repo.Update(bgEntity);
            await repo.SaveChangesAsync();
        });

        return ToDto(entity);
    }

    // ── AI dịch từ raw content ────────────────────────────────────────────────

    public async Task<EpisodeSubtitleDTO> AiGenerateSubtitleAsync(
        AiGenerateEpisodeSubtitleDTO dto, Guid requestedBy)
    {
        var langName  = ResolveLanguageName(dto.TargetLanguageCode, dto.TargetLanguageName);
        var fmt       = DetectFormat(dto.SourceContent);
        var vttSource = fmt == "srt"
            ? ConvertSrtToVtt(dto.SourceContent)
            : NormalizeVtt(dto.SourceContent);

        var sourceLangCode = dto.SourceLanguageCode;
        var targetLangCode = dto.TargetLanguageCode;

        var existing = await _repo.GetByEpisodeAndLanguageAsync(dto.EpisodeId, targetLangCode);
        EpisodeSubtitle entity;

        if (existing != null)
        {
            existing.Status         = SubtitleStatus.Processing;
            existing.Content        = string.Empty;
            existing.TranslatedFrom = sourceLangCode;
            existing.Source         = SubtitleSource.AiTranslated;
            existing.UpdatedAt      = DateTime.UtcNow;
            _repo.Update(existing);
            await _repo.SaveChangesAsync();
            entity = existing;
        }
        else
        {
            entity = new EpisodeSubtitle
            {
                EpisodeId      = dto.EpisodeId,
                LanguageCode   = targetLangCode,
                LanguageName   = langName,
                Content        = string.Empty,
                Source         = SubtitleSource.AiTranslated,
                TranslatedFrom = sourceLangCode,
                Status         = SubtitleStatus.Processing,
                UploadedBy     = requestedBy
            };
            await _repo.AddAsync(entity);
            await _repo.SaveChangesAsync();
        }

        // [FIX 1 + FIX 2]
        var entityId = entity.Id;
        _ = Task.Run(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IEpisodeSubtitleRepository>();

            var bgEntity = await repo.GetByIdAsync(entityId);
            if (bgEntity == null) return;

            try
            {
                var translated = await TranslateVttWithGroqAsync(
                    vttSource, sourceLangCode, targetLangCode);

                bgEntity.Content   = translated;
                bgEntity.Status    = SubtitleStatus.Ready;
                bgEntity.UpdatedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                bgEntity.Status       = SubtitleStatus.Failed;
                bgEntity.ErrorMessage = ex.Message;
                bgEntity.UpdatedAt    = DateTime.UtcNow;
            }

            repo.Update(bgEntity);
            await repo.SaveChangesAsync();
        });

        return ToDto(entity);
    }

    // ── Set Default ───────────────────────────────────────────────────────────

    public async Task<bool> SetDefaultAsync(Guid episodeId, Guid subtitleId)
    {
        var entity = await _repo.GetByIdAsync(subtitleId);
        if (entity == null || entity.EpisodeId != episodeId) return false;

        await _repo.ClearDefaultAsync(episodeId);

        entity.IsDefault = true;
        entity.UpdatedAt = DateTime.UtcNow;
        _repo.Update(entity);
        await _repo.SaveChangesAsync();

        return true;
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    public async Task<bool> DeleteSubtitleAsync(Guid subtitleId)
    {
        var entity = await _repo.GetByIdAsync(subtitleId);
        if (entity == null) return false;

        _repo.Remove(entity);
        await _repo.SaveChangesAsync();
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CORE: Groq AI Translation
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<string> TranslateVttWithGroqAsync(
        string vttContent, string sourceLang, string targetLang)
    {
        var cues = ParseVttCues(vttContent);
        if (cues.Count == 0)
            throw new InvalidOperationException("Không đọc được cue nào từ subtitle.");

        const int batchSize      = 30;
        var       translatedTexts = new List<string>(cues.Count);

        for (int i = 0; i < cues.Count; i += batchSize)
        {
            var batch      = cues.Skip(i).Take(batchSize).ToList();
            var batchTexts = batch.Select(c => c.Text).ToList();
            var result     = await TranslateBatchAsync(batchTexts, sourceLang, targetLang);
            translatedTexts.AddRange(result);

            if (i + batchSize < cues.Count)
                await Task.Delay(1200);
        }

        return BuildVtt(cues, translatedTexts);
    }

    private async Task<List<string>> TranslateBatchAsync(
        List<string> texts, string sourceLang, string targetLang)
    {
        var targetLangName = _languageNames.GetValueOrDefault(targetLang, targetLang);
        var sourceLangName = _languageNames.GetValueOrDefault(sourceLang, sourceLang);

        var numberedTexts = texts.Select((t, idx) => $"{idx + 1}. {t}").ToList();

        var prompt = $"""
            Translate the following subtitle lines from {sourceLangName} to {targetLangName}.
            Rules:
            - Keep the same line count: output exactly {texts.Count} lines.
            - Preserve line breaks within each subtitle block (use \\n).
            - Keep proper names, technical terms, and sound effects (like [Music], [Laughter]) as-is.
            - Output ONLY a JSON array of translated strings, no explanation.
            - Example output: ["Line 1 translated", "Line 2 translated"]

            Lines to translate:
            {string.Join("\n", numberedTexts)}
            """;

        var requestBody = new
        {
            model       = GroqModel,
            max_tokens  = 2000,
            temperature = 0.1,
            messages    = new[] { new { role = "user", content = prompt } }
        };

        var json    = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, GroqBaseUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", $"Bearer {GroqApiKey}");

        var response = await _httpClient.SendAsync(request);
        var body     = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Groq API lỗi {response.StatusCode}: {body}");

        return ParseGroqResponse(body, texts);
    }

    private static List<string> ParseGroqResponse(string responseBody, List<string> originalTexts)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var content   = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            content = Regex.Replace(content, @"```json\s*|\s*```", "").Trim();

            var array = JsonSerializer.Deserialize<List<string>>(content);
            if (array != null && array.Count == originalTexts.Count)
                return array;

            return Enumerable.Range(0, originalTexts.Count)
                .Select(i => array != null && i < array.Count ? array[i] : originalTexts[i])
                .ToList();
        }
        catch
        {
            return originalTexts;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SRT ↔ VTT Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    private static string ConvertSrtToVtt(string srtContent)
    {
        var text = srtContent.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        var vtt  = Regex.Replace(text, @"(\d{2}:\d{2}:\d{2}),(\d{3})", "$1.$2");
        return "WEBVTT\n\n" + vtt;
    }

    private static string NormalizeVtt(string vttContent)
    {
        var text = vttContent.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        if (!text.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
            text = "WEBVTT\n\n" + text;
        return text;
    }

    private static string DetectFormat(string content)
    {
        var trimmed = content.TrimStart();
        return trimmed.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) ? "vtt" : "srt";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // VTT Parser
    // ═══════════════════════════════════════════════════════════════════════════

    private record VttCue(string? Id, string Timing, string Text);

    private static List<VttCue> ParseVttCues(string vttContent)
    {
        var cues  = new List<VttCue>();
        var lines = vttContent
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n');

        int i = 0;
        while (i < lines.Length && !lines[i].Contains("-->")) i++;

        while (i < lines.Length)
        {
            var line = lines[i].Trim();

            if (string.IsNullOrEmpty(line) ||
                line.StartsWith("NOTE")   ||
                line.StartsWith("STYLE")  ||
                line.StartsWith("REGION"))
            {
                i++;
                continue;
            }

            string? cueId = null;
            if (!line.Contains("-->"))
            {
                cueId = line;
                i++;
                if (i >= lines.Length) break;
                line = lines[i].Trim();
            }

            if (!line.Contains("-->")) { i++; continue; }

            var timing = line;
            i++;

            var textBuilder = new StringBuilder();
            while (i < lines.Length && !string.IsNullOrEmpty(lines[i].Trim()))
            {
                if (textBuilder.Length > 0) textBuilder.Append('\n');
                textBuilder.Append(lines[i].Trim());
                i++;
            }

            var text = textBuilder.ToString();
            if (!string.IsNullOrEmpty(text))
                cues.Add(new VttCue(cueId, timing, text));
        }

        return cues;
    }

    private static string BuildVtt(List<VttCue> cues, List<string> translatedTexts)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        for (int i = 0; i < cues.Count; i++)
        {
            var cue  = cues[i];
            var text = i < translatedTexts.Count ? translatedTexts[i] : cue.Text;

            if (cue.Id != null) sb.AppendLine(cue.Id);
            sb.AppendLine(cue.Timing);
            sb.AppendLine(text.Replace("\\n", "\n"));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ── Misc Helpers ──────────────────────────────────────────────────────────

    private static void ValidateFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File không hợp lệ.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".srt" && ext != ".vtt")
            throw new ArgumentException("Chỉ chấp nhận file .srt hoặc .vtt.");

        if (file.Length > 5 * 1024 * 1024)
            throw new ArgumentException("File subtitle không được vượt quá 5MB.");
    }

    private static string ResolveLanguageName(string code, string? provided)
    {
        if (!string.IsNullOrWhiteSpace(provided)) return provided!;
        return _languageNames.GetValueOrDefault(code, code.ToUpperInvariant());
    }

    private static async Task<string> ReadFileTextAsync(IFormFile file)
    {
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    // ── Mapper ────────────────────────────────────────────────────────────────

    private static EpisodeSubtitleDTO ToDto(EpisodeSubtitle e) => new()
    {
        Id             = e.Id,
        EpisodeId      = e.EpisodeId,
        LanguageCode   = e.LanguageCode,
        LanguageName   = e.LanguageName,
        Source         = e.Source,
        TranslatedFrom = e.TranslatedFrom,
        Status         = e.Status,
        ErrorMessage   = e.ErrorMessage,
        IsDefault      = e.IsDefault,
        CreatedAt      = e.CreatedAt,
        UpdatedAt      = e.UpdatedAt
    };
}