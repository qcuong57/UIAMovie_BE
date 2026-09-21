// UIAMovie.Infrastructure/AI/Resilience/AiRateLimiter.cs
using System.Threading.RateLimiting;

namespace UIAMovie.Infrastructure.AI.Resilience;

public interface IAiRateLimiter
{
    ValueTask<RateLimitLease> AcquireAsync(int permits = 1, CancellationToken cancellationToken = default);
}

public class SlidingWindowAiRateLimiter : IAiRateLimiter
{
    private readonly SlidingWindowRateLimiter _limiter;

    public SlidingWindowAiRateLimiter(int permitsPerMinute = 28)
    {
        _limiter = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 15
        });
    }

    public ValueTask<RateLimitLease> AcquireAsync(int permits = 1, CancellationToken cancellationToken = default)
        => _limiter.AcquireAsync(permits, cancellationToken);
}