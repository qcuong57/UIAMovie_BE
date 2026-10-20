using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data;

namespace UIAMovie.Infrastructure.Configuration;

/// <summary>Xóa định kỳ các UserSession đã hết hạn mà không ai dùng lại.</summary>
public sealed class SessionCleanupWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SessionCleanupWorker> _logger;

    public SessionCleanupWorker(IServiceScopeFactory scopes, ILogger<SessionCleanupWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Chờ app khởi động xong rồi mới chạy lần đầu.
        try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db  = scope.ServiceProvider.GetRequiredService<MovieDbContext>();
                    var now = DateTime.UtcNow;
                    var removed = await db.Set<UserSession>()
                        .Where(s => s.ExpiresAt < now)
                        .ExecuteDeleteAsync(ct);

                    if (removed > 0)
                        _logger.LogInformation("[SessionCleanup] Đã xóa {Count} session hết hạn", removed);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "[SessionCleanup] Thất bại, sẽ thử lại ở lượt sau");
                }
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }
}