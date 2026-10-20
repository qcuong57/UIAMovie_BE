// UIAMovie.Infrastructure/Email/PaymentReceiptEmailNotifier.cs
using System.Net;
using System.Net.Mail;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces.IServices;

namespace UIAMovie.Infrastructure.Email;

/// <summary>
/// Hàng đợi trong bộ nhớ + BackgroundService gửi mail qua SMTP, thử lại tối đa 3 lần.
/// Cấu hình (appsettings / user-secrets):
///   Dùng chung section "Email" của project: SenderName, SenderEmail, SmtpHost, SmtpPort, Username, Password.
///   Tùy chọn: Email:SupportEmail.
/// Lưu ý: hàng đợi nằm trong RAM nên app restart giữa chừng sẽ mất mail chưa gửi;
/// cần độ bền cao hơn thì thay bằng Hangfire/outbox mà vẫn giữ nguyên interface.
/// </summary>
public sealed class PaymentReceiptEmailNotifier : BackgroundService, IPaymentReceiptNotifier
{
    private const int MaxAttempts = 3;

    private readonly Channel<PaymentReceiptEmailModel> _queue =
        Channel.CreateBounded<PaymentReceiptEmailModel>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    private readonly IConfiguration _config;
    private readonly ILogger<PaymentReceiptEmailNotifier> _logger;

    public PaymentReceiptEmailNotifier(IConfiguration config, ILogger<PaymentReceiptEmailNotifier> logger)
    {
        _config = config;
        _logger = logger;
    }

    public void Enqueue(PaymentReceiptEmailModel model)
    {
        if (!_queue.Writer.TryWrite(model))
            _logger.LogError("Hàng đợi email đầy, bỏ qua biên nhận đơn {OrderCode}", model.OrderCode);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var model in _queue.Reader.ReadAllAsync(stoppingToken))
                await SendWithRetryAsync(model, stoppingToken);
        }
        catch (OperationCanceledException) { /* tắt app */ }
    }

    private async Task SendWithRetryAsync(PaymentReceiptEmailModel model, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await SendAsync(model, ct);
                _logger.LogInformation("Đã gửi biên nhận đơn {OrderCode} tới {Email}", model.OrderCode, model.ToEmail);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gửi biên nhận đơn {OrderCode} thất bại (lần {Attempt}/{Max})",
                    model.OrderCode, attempt, MaxAttempts);
                if (attempt < MaxAttempts)
                    await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
            }
        }
        _logger.LogError("Bỏ cuộc gửi biên nhận đơn {OrderCode} tới {Email}", model.OrderCode, model.ToEmail);
    }

    private async Task SendAsync(PaymentReceiptEmailModel model, CancellationToken ct)
    {
        var host = _config["Email:SmtpHost"];
        var from = _config["Email:SenderEmail"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("Thiếu cấu hình Email:SmtpHost hoặc Email:SenderEmail.");

        var support  = _config["Email:SupportEmail"];

        using var msg = new MailMessage
        {
            From       = new MailAddress(from, _config["Email:SenderName"] ?? "UIAMovie"),
            Subject    = PaymentReceiptEmailTemplate.Subject(model),
            SubjectEncoding = System.Text.Encoding.UTF8,
            BodyEncoding    = System.Text.Encoding.UTF8,
            // Phần text là body chính, HTML là alternate view → multipart/alternative
            Body       = PaymentReceiptEmailTemplate.RenderText(model),
            IsBodyHtml = false
        };
        msg.To.Add(model.ToEmail);
        msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            PaymentReceiptEmailTemplate.RenderHtml(model, support),
            System.Text.Encoding.UTF8, "text/html"));

        using var smtp = new SmtpClient(host, int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 587)
        {
            EnableSsl = true, // Gmail 587 dùng STARTTLS
            Credentials = new NetworkCredential(_config["Email:Username"], _config["Email:Password"])
        };

        await smtp.SendMailAsync(msg, ct);
    }
}