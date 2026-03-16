// UIAMovie.Application.Services/ITwoFactorAuthProvider.cs
namespace UIAMovie.Application.Interfaces;

public interface ITwoFactorAuthProvider
{
    string GenerateSecret();
    bool VerifyCode(string secret, string code);
    string GenerateQrCodeUri(string email, string secret);
}