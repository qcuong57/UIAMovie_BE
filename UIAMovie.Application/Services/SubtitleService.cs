// UIAMovie.Application/Services/SubtitleService.cs
//
// [FIX 1] Fire-and-forget dùng IServiceScopeFactory để tạo scope mới trong Task.Run.
//         DbContext (scoped) của request gốc bị dispose sau khi response trả về.
//         Background task cần scope riêng → repo riêng → DbContext riêng.
//
// [FIX 2] Không giữ tham chiếu entity qua scope boundary.
//         Background task chỉ nhận entity.Id, tự load lại từ DB trong scope mới.

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

// ── Interface ─────────────────────────────────────────────────────────────────

public interface ISubtitleService
{
    /// <summary>Lấy danh sách subtitle (meta, không kèm content) của phim.</summary>
    Task<IEnumerable<SubtitleInfoDTO>> GetSubtitlesAsync(Guid movieId);

    /// <summary>Lấy nội dung WebVTT của một subtitle để player dùng.</summary>
    Task<SubtitleContentDTO?> GetSubtitleContentAsync(Guid subtitleId);

    /// <summary>Import file .srt hoặc .vtt thủ công. Auto-convert SRT → VTT.</summary>
    Task<SubtitleInfoDTO> UploadSubtitleAsync(Guid movieId, UploadSubtitleDTO dto, Guid uploadedBy);

    /// <summary>AI dịch subtitle đã có trong DB sang ngôn ngữ khác.</summary>
    Task<SubtitleInfoDTO> TranslateSubtitleAsync(Guid movieId, TranslateSubtitleDTO dto, Guid requestedBy);

    /// <summary>AI dịch raw content SRT/VTT được paste trực tiếp.</summary>
    Task<SubtitleInfoDTO> AiGenerateSubtitleAsync(AiGenerateSubtitleDTO dto, Guid requestedBy);

    /// <summary>Xóa một subtitle.</summary>
    Task<bool> DeleteSubtitleAsync(Guid subtitleId);

    /// <summary>Đặt subtitle là mặc định khi phim load.</summary>
    Task<bool> SetDefaultAsync(Guid movieId, Guid subtitleId);
}

// ── Implementation ────────────────────────────────────────────────────────────

public class SubtitleService : ISubtitleService
{
    private readonly ISubtitleRepository  _subtitleRepo;
    private readonly IConfiguration       _config;
    private readonly HttpClient           _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;   // [FIX 1]

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

    public SubtitleService(
        ISubtitleRepository   subtitleRepo,
        IConfiguration        config,
        IHttpClientFactory    httpClientFactory,
        IServiceScopeFactory  scopeFactory)        // [FIX 1] inject thêm
    {
        _subtitleRepo = subtitleRepo;
        _config       = config;
        _httpClient   = httpClientFactory.CreateClient("groq");
        _scopeFactory = scopeFactory;
    }

    // ─── Get ──────────────────────────────────────────────────────────────────

    public async Task<IEnumerable<SubtitleInfoDTO>> GetSubtitlesAsync(Guid movieId)
    {
        var subtitles = await _subtitleRepo.GetByMovieIdAsync(movieId);
        return subtitles.Select(MapToInfo);
    }

    public async Task<SubtitleContentDTO?> GetSubtitleContentAsync(Guid subtitleId)
    {
        var subtitle = await _subtitleRepo.GetByIdAsync(subtitleId);
        if (subtitle == null) return null;

        return new SubtitleContentDTO
        {
            Id           = subtitle.Id,
            LanguageCode = subtitle.LanguageCode,
            LanguageName = subtitle.LanguageName,
            Content      = subtitle.Content,
            Format       = "vtt"
        };
    }

    // ─── Upload thủ công ──────────────────────────────────────────────────────

    public async Task<SubtitleInfoDTO> UploadSubtitleAsync(
        Guid movieId, UploadSubtitleDTO dto, Guid uploadedBy)
    {
        ValidateFile(dto.File);

        var rawText    = await ReadFileTextAsync(dto.File);
        var ext        = Path.GetExtension(dto.File.FileName).ToLowerInvariant();
        var vttContent = ext == ".srt" ? ConvertSrtToVtt(rawText) : NormalizeVtt(rawText);
        var langName   = ResolveLanguageName(dto.LanguageCode, dto.LanguageName);

        var existing = await _subtitleRepo.GetByMovieAndLanguageAsync(movieId, dto.LanguageCode);
        if (existing != null)
        {
            existing.Content      = vttContent;
            existing.LanguageName = langName;
            existing.Source       = SubtitleSource.Manual;
            existing.Status       = SubtitleStatus.Ready;
            existing.UpdatedAt    = DateTime.UtcNow;
            _subtitleRepo.Update(existing);
            await _subtitleRepo.SaveChangesAsync();
            return MapToInfo(existing);
        }

        if (dto.IsDefault)
            await _subtitleRepo.ClearDefaultAsync(movieId);

        var subtitle = new MovieSubtitle
        {
            MovieId      = movieId,
            LanguageCode = dto.LanguageCode,
            LanguageName = langName,
            Content      = vttContent,
            Source       = SubtitleSource.Manual,
            Status       = SubtitleStatus.Ready,
            IsDefault    = dto.IsDefault,
            UploadedBy   = uploadedBy,
        };

        await _subtitleRepo.AddAsync(subtitle);
        await _subtitleRepo.SaveChangesAsync();
        return MapToInfo(subtitle);
    }

    // ─── AI dịch từ subtitle đã có trong DB ──────────────────────────────────

    public async Task<SubtitleInfoDTO> TranslateSubtitleAsync(
        Guid movieId, TranslateSubtitleDTO dto, Guid requestedBy)
    {
        var source = await _subtitleRepo.GetByIdAsync(dto.SourceSubtitleId)
            ?? throw new KeyNotFoundException("Không tìm thấy subtitle gốc");

        if (source.Status == SubtitleStatus.Processing)
            throw new InvalidOperationException("Subtitle gốc đang được xử lý, vui lòng đợi");

        var targetLangName = ResolveLanguageName(dto.TargetLanguageCode, dto.TargetLanguageName);
        // Capture primitive values — không capture entity hay _subtitleRepo
        var sourceContent  = source.Content;
        var sourceLangCode = source.LanguageCode;
        var targetLangCode = dto.TargetLanguageCode;

        var existing = await _subtitleRepo.GetByMovieAndLanguageAsync(movieId, targetLangCode);
        MovieSubtitle target;

        if (existing != null)
        {
            existing.Status         = SubtitleStatus.Processing;
            existing.Content        = "";
            existing.TranslatedFrom = sourceLangCode;
            existing.Source         = SubtitleSource.AiTranslated;
            existing.UpdatedAt      = DateTime.UtcNow;
            _subtitleRepo.Update(existing);
            await _subtitleRepo.SaveChangesAsync();
            target = existing;
        }
        else
        {
            target = new MovieSubtitle
            {
                MovieId        = movieId,
                LanguageCode   = targetLangCode,
                LanguageName   = targetLangName,
                Content        = "",
                Source         = SubtitleSource.AiTranslated,
                TranslatedFrom = sourceLangCode,
                Status         = SubtitleStatus.Processing,
                UploadedBy     = requestedBy,
            };
            await _subtitleRepo.AddAsync(target);
            await _subtitleRepo.SaveChangesAsync();
        }

        // [FIX 1 + FIX 2] Chỉ truyền Id vào Task.Run — tạo scope mới bên trong
        var targetId = target.Id;
        _ = Task.Run(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISubtitleRepository>();

            var bgTarget = await repo.GetByIdAsync(targetId);
            if (bgTarget == null) return;

            try
            {
                var translated = await TranslateVttWithGroqAsync(
                    sourceContent, sourceLangCode, targetLangCode);

                bgTarget.Content   = translated;
                bgTarget.Status    = SubtitleStatus.Ready;
                bgTarget.UpdatedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                bgTarget.Status       = SubtitleStatus.Failed;
                bgTarget.ErrorMessage = ex.Message;
                bgTarget.UpdatedAt    = DateTime.UtcNow;
            }

            repo.Update(bgTarget);
            await repo.SaveChangesAsync();
        });

        return MapToInfo(target);
    }

    // ─── AI dịch từ raw content paste vào ────────────────────────────────────

    public async Task<SubtitleInfoDTO> AiGenerateSubtitleAsync(
        AiGenerateSubtitleDTO dto, Guid requestedBy)
    {
        var langName       = ResolveLanguageName(dto.TargetLanguageCode, dto.TargetLanguageName);
        var ext            = DetectFormat(dto.SourceContent);
        var vttSource      = ext == "srt" ? ConvertSrtToVtt(dto.SourceContent) : NormalizeVtt(dto.SourceContent);
        var sourceLangCode = dto.SourceLanguageCode;
        var targetLangCode = dto.TargetLanguageCode;

        var existing = await _subtitleRepo.GetByMovieAndLanguageAsync(dto.MovieId, targetLangCode);
        MovieSubtitle subtitle;

        if (existing != null)
        {
            existing.Status         = SubtitleStatus.Processing;
            existing.Content        = "";
            existing.TranslatedFrom = sourceLangCode;
            existing.Source         = SubtitleSource.AiTranslated;
            existing.UpdatedAt      = DateTime.UtcNow;
            _subtitleRepo.Update(existing);
            await _subtitleRepo.SaveChangesAsync();
            subtitle = existing;
        }
        else
        {
            subtitle = new MovieSubtitle
            {
                MovieId        = dto.MovieId,
                LanguageCode   = targetLangCode,
                LanguageName   = langName,
                Content        = "",
                Source         = SubtitleSource.AiTranslated,
                TranslatedFrom = sourceLangCode,
                Status         = SubtitleStatus.Processing,
                UploadedBy     = requestedBy,
            };
            await _subtitleRepo.AddAsync(subtitle);
            await _subtitleRepo.SaveChangesAsync();
        }

        // [FIX 1 + FIX 2]
        var subtitleId = subtitle.Id;
        _ = Task.Run(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISubtitleRepository>();

            var bgSubtitle = await repo.GetByIdAsync(subtitleId);
            if (bgSubtitle == null) return;

            try
            {
                var translated = await TranslateVttWithGroqAsync(
                    vttSource, sourceLangCode, targetLangCode);

                bgSubtitle.Content   = translated;
                bgSubtitle.Status    = SubtitleStatus.Ready;
                bgSubtitle.UpdatedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                bgSubtitle.Status       = SubtitleStatus.Failed;
                bgSubtitle.ErrorMessage = ex.Message;
                bgSubtitle.UpdatedAt    = DateTime.UtcNow;
            }

            repo.Update(bgSubtitle);
            await repo.SaveChangesAsync();
        });

        return MapToInfo(subtitle);
    }

    // ─── Delete / Default ─────────────────────────────────────────────────────

    public async Task<bool> DeleteSubtitleAsync(Guid subtitleId)
    {
        var subtitle = await _subtitleRepo.GetByIdAsync(subtitleId);
        if (subtitle == null) return false;

        _subtitleRepo.Remove(subtitle);
        await _subtitleRepo.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetDefaultAsync(Guid movieId, Guid subtitleId)
    {
        var subtitle = await _subtitleRepo.GetByIdAsync(subtitleId);
        if (subtitle == null || subtitle.MovieId != movieId) return false;

        await _subtitleRepo.ClearDefaultAsync(movieId);

        subtitle.IsDefault = true;
        subtitle.UpdatedAt = DateTime.UtcNow;
        _subtitleRepo.Update(subtitle);
        await _subtitleRepo.SaveChangesAsync();
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
            throw new InvalidOperationException("Không đọc được cue nào từ subtitle");

        const int batchSize     = 30;
        var translatedTexts     = new List<string>(cues.Count);

        for (int i = 0; i < cues.Count; i += batchSize)
        {
            var batch      = cues.Skip(i).Take(batchSize).ToList();
            var batchTexts = batch.Select(c => c.Text).ToList();
            var translated = await TranslateBatchAsync(batchTexts, sourceLang, targetLang);
            translatedTexts.AddRange(translated);

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

        var json     = JsonSerializer.Serialize(requestBody);
        var request  = new HttpRequestMessage(HttpMethod.Post, GroqBaseUrl)
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
            using var doc   = JsonDocument.Parse(responseBody);
            var content     = doc.RootElement
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

    // ─── Misc Helpers ─────────────────────────────────────────────────────────

    private static string DetectFormat(string content)
    {
        var trimmed = content.TrimStart();
        return trimmed.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) ? "vtt" : "srt";
    }

    private static string ResolveLanguageName(string code, string? provided)
    {
        if (!string.IsNullOrWhiteSpace(provided)) return provided!;
        return _languageNames.GetValueOrDefault(code, code.ToUpperInvariant());
    }

    private static void ValidateFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File không hợp lệ");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".srt" && ext != ".vtt")
            throw new ArgumentException("Chỉ chấp nhận file .srt hoặc .vtt");

        if (file.Length > 5 * 1024 * 1024)
            throw new ArgumentException("File subtitle không được vượt quá 5MB");
    }

    private static async Task<string> ReadFileTextAsync(IFormFile file)
    {
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static SubtitleInfoDTO MapToInfo(MovieSubtitle s) => new()
    {
        Id           = s.Id,
        LanguageCode = s.LanguageCode,
        LanguageName = s.LanguageName,
        Source       = s.Source,
        Status       = s.Status,
        ErrorMessage = s.ErrorMessage,
        IsDefault    = s.IsDefault,
        CreatedAt    = s.CreatedAt,
    };
}