// UIAMovie.Application/Interfaces/IEmailService.cs
namespace UIAMovie.Application.Interfaces;

public interface IEmailService
{
    Task SendOtpEmailAsync(string toEmail, string otp);
    Task SendResetPasswordEmailAsync(string toEmail, string otp);
}