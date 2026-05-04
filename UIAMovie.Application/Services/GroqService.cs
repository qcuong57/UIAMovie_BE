using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UIAMovie.Application.AI;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;

namespace UIAMovie.Infrastructure.Services;

/// <summary>
/// GroqService — llama-3.1-8b-instant via Groq API (OpenAI-compatible format).
///
/// Free tier: 14,400 RPD / 30 RPM / 6,000 TPM.
///
/// Fixes so với phiên bản cũ:
///   [1] ChatAsync: nhận thêm List&lt;ChatMessageDTO&gt; history — truyền đa lượt vào messages[]
///   [2] BuildCacheKey: dùng SHA256 thay GetHashCode() — ổn định qua process restart
///   [3] SmartSearch cache key: normalize kỹ hơn (trim, collapse whitespace)
///   [4] CallGroqAsync: hỗ trợ messages[] nhiều turn thay vì chỉ system + 1 user message
///   [v2][5] MoodRecommendAsync: gợi ý phim theo tâm trạng
///
/// Fixes [v3]:
///   [FIX-2] BuildMovieCsv đã xóa — dùng AiMovieCsvBuilder.Build() chung với Controller
///   [FIX-6] SlidingWindowRateLimiter thay SemaphoreSlim — enforce đúng 30 RPM Groq free tier
///
/// Fixes [v4]:
///   [FIX-7] Bỏ IDisposable — _rateLimiter là static field có lifetime bằng process,
///           không được dispose theo instance. Dispose() cũ gây ObjectDisposedException
///           khi DI tạo nhiều instance (scoped/transient) rồi dispose từng cái.
///           Đăng ký Singleton trong DI để tránh tạo nhiều instance.
/// </summary>
public sealed class GroqService : IGroqService
{
    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly HttpClient           _http;
    private readonly ICacheService        _cache;
    private readonly ILogger<GroqService> _logger;
    private readonly string               _baseUrl;
    private readonly string               _model;

    // ── Constants ─────────────────────────────────────────────────────────────
    private const int MaxRetries          = 3;
    private const int RecommendMovieLimit = 20;
    private const int SearchMovieLimit    = 25;
    private const int MaxOutputTokens     = 256;

    /// <summary>
    /// [FIX-6] SlidingWindowRateLimiter: enforce đúng 30 RPM Groq free tier.
    ///
    /// SemaphoreSlim(5,5) cũ chỉ giới hạn concurrency — không ngăn được 30 request
    /// cùng bắn trong 1 giây. SlidingWindowRateLimiter đếm request trong cửa sổ 60s
    /// trượt liên tục → đúng với định nghĩa RPM.
    ///
    /// [FIX-7] static readonly — lifetime bằng process, KHÔNG dispose theo instance.
    /// Nếu dispose, các request sau sẽ throw ObjectDisposedException.
    /// </summary>
    private static readonly RateLimiter _rateLimiter = new SlidingWindowRateLimiter(
        new SlidingWindowRateLimiterOptions
        {
            PermitLimit          = 28,          // 28/30 — buffer 2 để tránh edge case
            Window               = TimeSpan.FromMinutes(1),
            SegmentsPerWindow    = 6,            // cửa sổ trượt 10s mỗi segment
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit           = 10,           // tối đa 10 request xếp hàng chờ
        });

    // ── Constructor ───────────────────────────────────────────────────────────
    public GroqService(
        HttpClient           http,
        ICacheService        cache,
        ILogger<GroqService> logger,
        IConfiguration       config)
    {
        _http   = http;
        _cache  = cache;
        _logger = logger;

        var apiKey = config["Groq:ApiKey"]
                     ?? throw new InvalidOperationException("Groq:ApiKey chưa được cấu hình.");

        _baseUrl = config["Groq:BaseUrl"]
                   ?? "https://api.groq.com/openai/v1/chat/completions";
        _model   = config["Groq:Model"]
                   ?? "llama-3.1-8b-instant";

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    // ─── Chat ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Nhận history để AI nhớ ngữ cảnh hội thoại đa lượt.
    ///
    /// messages[] gửi lên Groq:
    ///   [system]    → system prompt + movie context
    ///   [user]      → turn 1 của người dùng
    ///   [assistant] → reply turn 1
    ///   [user]      → turn 2 ...
    ///   [user]      → message hiện tại (mới nhất)
    ///
    /// Token estimate: 20 turns × ~80 tokens ≈ 1600 tokens history
    ///   + system (~200) + movie context (~800) + reply (300) ≈ ~2900 tokens/request
    ///   → an toàn với Groq 6000 TPM free tier.
    /// </summary>
    public async Task<string> ChatAsync(
        string                userMessage,
        string?               systemContext = null,
        List<ChatMessageDTO>? history       = null)
    {
        var system = systemContext ?? MoviePrompts.ChatSystem;

        var messages = new List<object>
        {
            new { role = "system", content = system }
        };

        if (history is { Count: > 0 })
        {
            foreach (var turn in history)
            {
                if (turn.Role is "user" or "assistant")
                    messages.Add(new { role = turn.Role, content = turn.Content });
            }
        }

        messages.Add(new { role = "user", content = userMessage });

        var result = await CallGroqWithMessagesAsync(messages, maxTokens: 300);

        return string.IsNullOrWhiteSpace(result)
            ? "Xin lỗi, tôi đang bận. Vui lòng thử lại sau ít phút."
            : result;
    }

    // ─── Recommend ────────────────────────────────────────────────────────────

    public async Task<List<Guid>> RecommendMoviesAsync(
        List<string>       watchedTitles,
        List<string>       favoriteGenres,
        List<MovieContext> availableMovies)
    {
        var cacheKey = BuildCacheKeyHash("rec", watchedTitles, favoriteGenres);
        var cached   = await _cache.GetAsync<List<Guid>>(cacheKey);
        if (cached != null) return cached;

        var subset      = SelectMoviesForRecommend(availableMovies, favoriteGenres, RecommendMovieLimit);
        var movieCsv    = AiMovieCsvBuilder.Build(subset);
        var userMessage = MoviePrompts.BuildRecommendUser(
            watched:  TruncateJoin(watchedTitles, 15),
            genres:   string.Join(", ", favoriteGenres),
            movieCsv: movieCsv);

        var result = await ParseJsonGuidArrayAsync(MoviePrompts.RecommendSystem, userMessage);

        if (result.Count > 0)
            await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(30));

        return result;
    }

    // ─── Mood Recommend ───────────────────────────────────────────────────────

    /// <summary>
    /// Gợi ý phim theo tâm trạng.
    /// Token estimate: ~20 movies × 80 chars ≈ 500 tokens. Output: 8 GUIDs ≈ 35 tokens.
    /// </summary>
    public async Task<List<Guid>> MoodRecommendAsync(
        string mood,
        string targetGenres,
        string movieCsv)
    {
        var userMessage = MoviePrompts.BuildMoodUser(mood, targetGenres, movieCsv);
        return await ParseJsonGuidArrayAsync(MoviePrompts.MoodSystem, userMessage);
    }

    // ─── Smart Search ─────────────────────────────────────────────────────────

    public async Task<List<Guid>> SmartSearchAsync(string query, List<MovieContext> availableMovies)
    {
        var normalizedQuery = NormalizeSearchQuery(query);
        var cacheKey        = $"ai:search:{normalizedQuery}";

        var cached = await _cache.GetAsync<List<Guid>>(cacheKey);
        if (cached != null) return cached;

        var subset      = SelectMoviesForSearch(availableMovies, query, SearchMovieLimit);
        var movieCsv    = AiMovieCsvBuilder.Build(subset);
        var userMessage = MoviePrompts.BuildSearchUser(query, movieCsv);

        var result = await ParseJsonGuidArrayAsync(MoviePrompts.SearchSystem, userMessage);

        if (result.Count > 0)
            await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(15));

        return result;
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    private static List<MovieContext> SelectMoviesForRecommend(
        List<MovieContext> all, List<string> genres, int limit)
    {
        var genreSet = genres.Select(g => g.ToLowerInvariant()).ToHashSet();

        return all
            .Where(m => !string.IsNullOrWhiteSpace(m.Description))
            .OrderByDescending(m =>
                m.Genres.Split(',').Any(g => genreSet.Contains(g.Trim().ToLowerInvariant())) ? 1 : 0)
            .ThenByDescending(m => m.Rating)
            .Take(limit)
            .Concat(all
                .Where(m => string.IsNullOrWhiteSpace(m.Description))
                .OrderByDescending(m => m.Rating))
            .Take(limit)
            .ToList();
    }

    private static List<MovieContext> SelectMoviesForSearch(
        List<MovieContext> all, string query, int limit)
    {
        var queryTokens = query
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .ToHashSet();

        return all
            .OrderByDescending(m =>
            {
                var hasDesc  = string.IsNullOrWhiteSpace(m.Description) ? 0 : 2;
                var titleHit = queryTokens.Any(t => m.Title.ToLowerInvariant().Contains(t)) ? 3 : 0;
                var genreHit = queryTokens.Any(t => m.Genres.ToLowerInvariant().Contains(t)) ? 1 : 0;
                var descHit  = !string.IsNullOrWhiteSpace(m.Description) &&
                               queryTokens.Any(t => m.Description.ToLowerInvariant().Contains(t)) ? 2 : 0;
                return hasDesc + titleHit + genreHit + descHit + m.Rating * 0.1;
            })
            .Take(limit)
            .ToList();
    }

    private static string TruncateJoin(IEnumerable<string> items, int max)
        => string.Join(", ", items.Take(max));

    /// <summary>
    /// SHA256 hash ổn định — không phụ thuộc vào .NET runtime version.
    /// </summary>
    private static string BuildCacheKeyHash(string prefix, List<string> a, List<string> b)
    {
        var raw   = string.Join(",", a.Take(10).OrderBy(x => x))
                  + "|"
                  + string.Join(",", b.OrderBy(x => x));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var hash  = Convert.ToHexString(bytes)[..16];
        return $"ai:{prefix}:{hash}";
    }

    /// <summary>
    /// Normalize search query để tăng cache hit rate.
    /// </summary>
    private static string NormalizeSearchQuery(string query)
        => System.Text.RegularExpressions.Regex
            .Replace(query.ToLowerInvariant().Trim(), @"\s+", " ");

    private static string ExtractJsonArray(string raw)
    {
        var start = raw.IndexOf('[');
        var end   = raw.LastIndexOf(']');
        return start != -1 && end != -1 && end >= start
            ? raw.Substring(start, end - start + 1)
            : raw;
    }

    private async Task<List<Guid>> ParseJsonGuidArrayAsync(string systemPrompt, string userMessage)
    {
        try
        {
            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage  }
            };

            var raw = await CallGroqWithMessagesAsync(messages, MaxOutputTokens);
            if (string.IsNullOrWhiteSpace(raw)) return [];

            var json = ExtractJsonArray(raw);

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray()
                      .Select(e => e.GetString())
                      .Where(s => Guid.TryParse(s, out _))
                      .Select(s => Guid.Parse(s!))
                      .Distinct()
                      .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Groq] ParseJsonGuidArray failed — response không phải JSON hợp lệ");
            return [];
        }
    }

    // ─── Core HTTP call với rate limiting + exponential backoff ───────────────

    /// <summary>
    /// [FIX-6] SlidingWindowRateLimiter — đảm bảo không vượt 30 RPM Groq free tier.
    /// Nếu hàng chờ đầy (QueueLimit = 10) → RateLimitLease.IsAcquired = false → trả empty.
    /// </summary>
    private async Task<string> CallGroqWithMessagesAsync(
        List<object> messages,
        int          maxTokens = MaxOutputTokens)
    {
        using var lease = await _rateLimiter.AcquireAsync(permitCount: 1);

        if (!lease.IsAcquired)
        {
            _logger.LogWarning("[Groq] Rate limit queue đầy — bỏ qua request.");
            return string.Empty;
        }

        return await CallGroqInternalAsync(messages, maxTokens);
    }

    private async Task<string> CallGroqInternalAsync(List<object> messages, int maxTokens)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var payload = new
                {
                    model       = _model,
                    messages    = messages,
                    max_tokens  = maxTokens,
                    temperature = 0.2
                };

                var content  = new StringContent(
                    JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await _http.PostAsync(_baseUrl, content);
                var body     = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                   (int)response.StatusCode >= 500)
                {
                    var delay = attempt * 2_000;
                    _logger.LogWarning(
                        "[Groq] HTTP {Code} — attempt {Attempt}/{Max}, waiting {Delay}ms",
                        (int)response.StatusCode, attempt, MaxRetries, delay);

                    if (attempt < MaxRetries)
                    {
                        await Task.Delay(delay);
                        continue;
                    }

                    _logger.LogError("[Groq] Quota exhausted hoặc Server down sau {Max} attempts.", MaxRetries);
                    return string.Empty;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("[Groq] HTTP {Code}: {Body}", response.StatusCode, body);
                    throw new InvalidOperationException($"Groq API error {response.StatusCode}: {body}");
                }

                using var doc = JsonDocument.Parse(body);
                var text = doc.RootElement
                              .GetProperty("choices")[0]
                              .GetProperty("message")
                              .GetProperty("content")
                              .GetString();

                if (string.IsNullOrWhiteSpace(text))
                    _logger.LogWarning("[Groq] HTTP 200 nhưng content rỗng — có thể bị safety filter.");

                return text ?? string.Empty;
            }
            catch (HttpRequestException ex)
            {
                var delay = 1_000 * attempt;
                _logger.LogWarning(ex,
                    "[Groq] Network error attempt {Attempt}/{Max}, retry in {Delay}ms",
                    attempt, MaxRetries, delay);

                if (attempt == MaxRetries)
                {
                    _logger.LogError("[Groq] Không thể kết nối sau {Max} attempts.", MaxRetries);
                    return string.Empty;
                }

                await Task.Delay(delay);
            }
        }

        return string.Empty;
    }

    // [FIX-7] KHÔNG implement IDisposable và KHÔNG dispose _rateLimiter.
    // _rateLimiter là static field — lifetime bằng process.
    // Dispose theo instance sẽ gây ObjectDisposedException cho toàn bộ request sau đó.
    // Đăng ký service này là Singleton trong DI (Program.cs):
    //   builder.Services.AddSingleton<IGroqService, GroqService>();
}