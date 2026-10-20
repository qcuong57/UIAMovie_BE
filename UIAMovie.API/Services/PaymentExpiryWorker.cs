
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UIAMovie.Application.Services;

namespace UIAMovie.API.Services;

/// <summary>
/// Định kỳ xử lý các đơn thanh toán Pending đã quá hạn.
/// Việc chuyển trạng thái phải được thực hiện nguyên tử ở database.
/// </summary>
public sealed class PaymentExpiryWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PaymentExpiryWorker> _logger;

    public PaymentExpiryWorker(
        IServiceScopeFactory scopes,
        ILogger<PaymentExpiryWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var startedAt = DateTimeOffset.UtcNow;

            try
            {
                await using var scope = _scopes.CreateAsyncScope();

                var payments = scope.ServiceProvider
                    .GetRequiredService<IPaymentService>();

                await payments.ExpireStaleOrdersAsync(stoppingToken);

                _logger.LogDebug(
                    "Payment expiry scan completed in {ElapsedMs} ms",
                    (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Payment expiry scan failed at {StartedAtUtc}",
                    startedAt);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
