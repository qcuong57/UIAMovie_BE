using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UIAMovie.Application.Interfaces;
using UIAMovie.Infrastructure.AI.Resilience;

namespace UIAMovie.Infrastructure.AI.Providers;

public class GroqProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly IAiRateLimiter _rateLimiter;
    private readonly ILogger<GroqProvider> _logger;
    private readonly GroqOptions _options;

    public GroqProvider(
        HttpClient http,
        IAiRateLimiter rateLimiter,
        IOptions<GroqOptions> options,
        ILogger<GroqProvider> logger)
    {
        _http = http;
        _rateLimiter = rateLimiter;
        _logger = logger;
        _options = options.Value;

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public async Task<string> GenerateChatResponseAsync(
        IEnumerable<AiProviderMessage> messages,
        AiProviderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var lease = await _rateLimiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
        {
            _logger.LogWarning("[GroqProvider] Rate limit queue đầy — bỏ qua request.");
            return string.Empty;
        }

        var model = options?.Model ?? _options.Model;
        var maxTokens = options?.MaxTokens ?? 500;
        var temperature = options?.Temperature ?? 0.2;
        var enforceJson = options?.EnforceJsonObject ?? false;

        // Xử lý riêng cho các model reasoning (gpt-oss...) để không bị cạn token suy luận
        var isReasoningModel = model.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase);
        var currentMaxTokens = isReasoningModel ? Math.Max(maxTokens, 1200) : maxTokens;

        var payload = new
        {
            model = model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            max_tokens = currentMaxTokens,
            temperature = temperature,
            reasoning_effort = isReasoningModel ? "low" : null,
            response_format = enforceJson ? new { type = "json_object" } : null
        };

        try
        {
            var response = await AiRetryPolicy.ExecuteWithRetryAsync(
                async attempt =>
                {
                    var content = new StringContent(
                        JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    return await _http.PostAsync(_options.BaseUrl, content, cancellationToken);
                },
                _options.MaxRetries,
                _logger);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[GroqProvider] HTTP {Code}: {Body}", response.StatusCode, body);
                return string.Empty;
            }

            using var doc = JsonDocument.Parse(body);
            var choice = doc.RootElement.GetProperty("choices")[0];

            // Đọc nội dung tin nhắn phản hồi
            var messageObj = choice.GetProperty("message");
            var contentStr = messageObj.TryGetProperty("content", out var cEl) ? cEl.GetString() : null;

            return contentStr ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GroqProvider] Exception khi gọi Groq API");
            return string.Empty;
        }
    }
}