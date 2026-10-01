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
    private readonly Channel<Func<IEmailService, Task>> _channel =
        Channel.CreateBounded<Func<IEmailService, Task>>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

    public ChannelReader<Func<IEmailService, Task>> Reader => _channel.Reader;

    public void Enqueue(Func<IEmailService, Task> job) => _channel.Writer.TryWrite(job);
}

public sealed class EmailQueueWorker : BackgroundService
{
    private const int MaxAttempts = 3;

    private readonly EmailQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailQueueWorker> _logger;

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
            // Chạy song song để 1 email chậm không chặn các email khác
            _ = Task.Run(() => RunWithRetryAsync(job, stoppingToken), stoppingToken);
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
}