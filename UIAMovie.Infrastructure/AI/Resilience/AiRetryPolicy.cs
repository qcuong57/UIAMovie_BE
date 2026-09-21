// UIAMovie.Infrastructure/AI/Resilience/AiRetryPolicy.cs
using Microsoft.Extensions.Logging;

namespace UIAMovie.Infrastructure.AI.Resilience;

public static class AiRetryPolicy
{
    public static async Task<HttpResponseMessage> ExecuteWithRetryAsync(
        Func<int, Task<HttpResponseMessage>> action,
        int maxRetries,
        ILogger logger)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var response = await action(attempt);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    var delay = attempt * 1500;
                    logger.LogWarning("[AI Retry] HTTP {Code} — attempt {Attempt}/{Max}, waiting {Delay}ms",
                        (int)response.StatusCode, attempt, maxRetries, delay);

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(delay);
                        continue;
                    }
                }

                return response;
            }
            catch (HttpRequestException ex)
            {
                if (attempt == maxRetries)
                {
                    logger.LogError(ex, "[AI Retry] Mất kết nối tới máy chủ sau {Max} lần thử.", maxRetries);
                    throw;
                }
                await Task.Delay(1000 * attempt);
            }
        }

        throw new HttpRequestException("Max retries reached without response.");
    }
}