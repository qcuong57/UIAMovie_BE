// UIAMovie.Infrastructure/Services/EmailService.cs
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using UIAMovie.Application.Interfaces;

namespace UIAMovie.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;

    public EmailService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendOtpEmailAsync(string toEmail, string otp) =>
        await SendEmailAsync(toEmail, "Mã xác thực OTP - UIAMovie", BuildOtpTemplate(otp, "xác thực"));

    public async Task SendResetPasswordEmailAsync(string toEmail, string otp) =>
        await SendEmailAsync(toEmail, "Đặt lại mật khẩu - UIAMovie", BuildOtpTemplate(otp, "đặt lại mật khẩu"));

    private async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            _configuration["Email:SenderName"],
            _configuration["Email:SenderEmail"]));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();
        await client.ConnectAsync(
            _configuration["Email:SmtpHost"],
            int.Parse(_configuration["Email:SmtpPort"]!),
            SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(
            _configuration["Email:Username"],
            _configuration["Email:Password"]);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    private static string BuildOtpTemplate(string otp, string action) => $"""
        <div style="font-family:Arial,sans-serif;max-width:480px;margin:auto;
                    padding:32px;border:1px solid #eee;border-radius:8px;">
            <h2 style="color:#e50914;">UIAMovie</h2>
            <p>Mã OTP để <strong>{action}</strong> của bạn là:</p>
            <div style="font-size:36px;font-weight:bold;letter-spacing:8px;
                        color:#e50914;padding:16px 0;">{otp}</div>
            <p style="color:#888;">Mã có hiệu lực trong <strong>10 phút</strong>.
               Không chia sẻ mã này với bất kỳ ai.</p>
        </div>
    """;
}