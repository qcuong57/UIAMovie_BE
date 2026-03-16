using OtpNet;
using UIAMovie.Application.Interfaces;

namespace UIAMovie.Infrastructure.Security;


public class TwoFactorAuthProvider : ITwoFactorAuthProvider
{
    public string GenerateSecret()
    {
        var bytes = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(bytes);
    }

    public bool VerifyCode(string secret, string code)
    {
        try
        {
            var bytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(bytes);
            var result = totp.VerifyTotp(code, out long _);
            return result;
        }
        catch
        {
            return false;
        }
    }

    public string GenerateQrCodeUri(string email, string secret)
    {
        return $"otpauth://totp/NetflixClone:{email}?secret={secret}&issuer=NetflixClone";
    }
}