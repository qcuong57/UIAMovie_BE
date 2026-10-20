using System.Collections.Concurrent;
using OtpNet;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IServices;

namespace UIAMovie.Infrastructure.Security;

public class TwoFactorAuthProvider : ITwoFactorAuthProvider
{
    private const string Issuer = "UIAMovie";

    // Chịu lệch đồng hồ ±1 bước (30 giây) giữa điện thoại và server.
    private static readonly VerificationWindow Window = new(previous: 1, future: 1);

    // Bước thời gian TOTP lớn nhất đã được chấp nhận cho từng secret, để một mã không dùng lại được (replay).
    // Lưu trong bộ nhớ của instance này; nếu chạy nhiều instance, chuyển sang Redis.
    private static readonly ConcurrentDictionary<string, long> LastAcceptedStep = new();

    public string GenerateSecret()
    {
        var bytes = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(bytes);
    }

    public bool VerifyCode(string secret, string code)
    {
        try
        {
            var totp = new Totp(Base32Encoding.ToBytes(secret));
            if (!totp.VerifyTotp(code, out var matchedStep, Window))
                return false;

            // Chỉ chấp nhận khi bước thời gian MỚI hơn bước đã dùng lần trước.
            var accepted = false;
            LastAcceptedStep.AddOrUpdate(
                secret,
                _ => { accepted = true; return matchedStep; },
                (_, last) =>
                {
                    accepted = matchedStep > last;
                    return accepted ? matchedStep : last;
                });

            return accepted;
        }
        catch
        {
            return false;
        }
    }

    public string GenerateQrCodeUri(string email, string secret)
    {
        var label = $"{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}";
        return $"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(Issuer)}";
    }
}