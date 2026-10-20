using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IServices;

namespace UIAMovie.Infrastructure.Email;

/// <summary>
/// Hàng đợi gửi email chạy nền. Controller/Service chỉ việc Enqueue rồi trả response ngay,
/// không phải chờ kết nối SMTP.
/// </summary>
public sealed class EmailQueue : IEmailQueue
{
    private const int Capacity = 1000;

    private readonly Channel<Func<IEmailService, Task>> _channel;

    public EmailQueue(ILogger<EmailQueue> logger)
    {
        _channel = Channel.CreateBounded<Func<IEmailService, Task>>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode     = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            },
            // Đầy hàng đợi thì bỏ email cũ nhất (OTP cũ gần như đã hết hạn), nhưng phải để lại dấu vết.
            itemDropped: _ => logger.LogError(
                "Hàng đợi email đầy ({Capacity}), đã bỏ email cũ nhất. Kiểm tra SMTP hoặc tải bất thường.",
                Capacity));
    }

    public ChannelReader<Func<IEmailService, Task>> Reader => _channel.Reader;

    public void Enqueue(Func<IEmailService, Task> job) => _channel.Writer.TryWrite(job);
}

public sealed class EmailQueueWorker : BackgroundService
{
    private const int MaxAttempts = 3;

    // Giới hạn số email gửi đồng thời để SMTP chậm không làm phình số Task/kết nối.
    private const int MaxConcurrency = 8;

    private readonly EmailQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailQueueWorker> _logger;
    private readonly SemaphoreSlim _gate = new(MaxConcurrency, MaxConcurrency);

    public EmailQueueWorker(
        EmailQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailQueueWorker> logger)
    {
        _queue        = queue;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            // Chờ có chỗ trống rồi mới chạy song song: 1 email chậm không chặn các email khác,
            // nhưng cũng không để số lượng đồng thời tăng không giới hạn.
            await _gate.WaitAsync(stoppingToken);
            _ = RunAndReleaseAsync(job, stoppingToken);
        }
    }

    private async Task RunAndReleaseAsync(Func<IEmailService, Task> job, CancellationToken ct)
    {
        try
        {
            await RunWithRetryAsync(job, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunWithRetryAsync(Func<IEmailService, Task> job, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                await job(emailService);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gửi email thất bại (lần {Attempt}/{Max})", attempt, MaxAttempts);

                if (attempt < MaxAttempts)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }

        _logger.LogError("Gửi email thất bại sau {Max} lần thử, bỏ qua.", MaxAttempts);
    }

    public override void Dispose()
    {
        _gate.Dispose();
        base.Dispose();
    }
}