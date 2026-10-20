// UIAMovie.API/Services/PaymentExpiryWorker.cs

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UIAMovie.Application.Services;

namespace UIAMovie.API.Services;

/// <summary>
/// Định kỳ chuyển đơn Pending quá hạn (+ thời gian ân hạn) sang Expired.
/// KHÔNG xóa dữ liệu. Dùng UPDATE có điều kiện nên chạy trùng giữa nhiều instance vẫn an toàn.
/// </summary>
public sealed class PaymentExpiryWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PaymentExpiryWorker> _logger;

    public PaymentExpiryWorker(IServiceScopeFactory scopes, ILogger<PaymentExpiryWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var payments = scope.ServiceProvider.GetRequiredService<IPaymentService>();
                await payments.ExpireStaleOrdersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PaymentExpiryWorker failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}