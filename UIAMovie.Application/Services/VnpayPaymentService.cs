// UIAMovie.Application/Services/Payment/VnpayPaymentService.cs

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace UIAMovie.Application.Services;

public class VnpayOptions
{
    public string TmnCode { get; set; } = string.Empty;
    public string HashSecret { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
    public string ReturnUrl { get; set; } = string.Empty;

    /// <summary>
    /// Không còn gửi sang VNPay: IPN URL được cấu hình trong merchant portal, không nhận qua tham số.
    /// Giữ lại để không làm vỡ appsettings hiện có.
    /// </summary>
    public string IpnUrl { get; set; } = string.Empty;
}

public interface IVnpayPaymentService
{
    /// <param name="returnUrl">URL backend nhận redirect từ VNPay. Nếu null → dùng ReturnUrl trong config.</param>
    string CreatePaymentUrl(string orderCode, long amount, string orderInfo, string ipAddress,
        string? returnUrl = null);

    /// <summary>
    /// Xác thực chữ ký từ TOÀN BỘ query string gốc VNPay gửi về (không qua DTO),
    /// nên VNPay có thêm tham số mới (vnp_PromotionCode...) thì chữ ký vẫn đúng.
    /// </summary>
    bool VerifySignature(IReadOnlyDictionary<string, string> rawQuery);
}

public class VnpayPaymentService : IVnpayPaymentService
{
    private readonly VnpayOptions _opts;

    public VnpayPaymentService(IOptions<VnpayOptions> opts) => _opts = opts.Value;

    /// <summary>VNPay yêu cầu giờ GMT+7. Việt Nam không có DST nên cộng cố định 7 giờ, chạy đúng trên mọi OS/múi giờ server.</summary>
    private static DateTime VietnamNow() => DateTime.UtcNow.AddHours(7);

    public string CreatePaymentUrl(string orderCode, long amount, string orderInfo, string ipAddress,
        string? returnUrl = null)
    {
        var effectiveReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) ? returnUrl : _opts.ReturnUrl;

        var now = VietnamNow();

        var vnpParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = _opts.TmnCode,
            ["vnp_Amount"] = (amount * 100).ToString(),
            ["vnp_CreateDate"] = now.ToString("yyyyMMddHHmmss"),
            ["vnp_CurrCode"] = "VND",
            ["vnp_IpAddr"] = ipAddress,
            ["vnp_Locale"] = "vn",
            ["vnp_OrderInfo"] = orderInfo,
            ["vnp_OrderType"] = "other",
            ["vnp_ReturnUrl"] = effectiveReturnUrl,
            ["vnp_TxnRef"] = orderCode,
            ["vnp_ExpireDate"] = now.AddMinutes(15).ToString("yyyyMMddHHmmss"),
        };

        // VNPay chuẩn: hash dùng WebUtility.UrlEncode (space → '+', giống PHP urlencode)
        var hashData = BuildHashString(vnpParams);
        var secureHash = ComputeHmacSha512(hashData, _opts.HashSecret);

        // URL dùng Uri.EscapeDataString (space → '%20') để browser hiểu đúng
        var urlQuery = BuildUrlString(vnpParams);
        return $"{_opts.BaseUrl}?{urlQuery}&vnp_SecureHash={secureHash}";
    }

    public bool VerifySignature(IReadOnlyDictionary<string, string> rawQuery)
    {
        if (!rawQuery.TryGetValue("vnp_SecureHash", out var receivedHash)
            || string.IsNullOrWhiteSpace(receivedHash))
            return false;

        var vnpParams = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in rawQuery)
        {
            if (!key.StartsWith("vnp_", StringComparison.Ordinal)) continue;
            if (key is "vnp_SecureHash" or "vnp_SecureHashType") continue;
            if (string.IsNullOrEmpty(value)) continue;
            vnpParams[key] = value;
        }

        var computed = ComputeHmacSha512(BuildHashString(vnpParams), _opts.HashSecret);

        var a = Encoding.ASCII.GetBytes(computed);
        var b = Encoding.ASCII.GetBytes(receivedHash.Trim().ToLowerInvariant());
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Chuỗi để tính HMAC — dùng WebUtility.UrlEncode giống PHP urlencode (chuẩn VNPay).
    /// </summary>
    private static string BuildHashString(SortedDictionary<string, string> dict)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in dict)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(key);
            sb.Append('=');
            sb.Append(WebUtility.UrlEncode(value));
        }

        return sb.ToString();
    }

    /// <summary>Chuỗi ghép vào URL redirect — dùng Uri.EscapeDataString (RFC 3986).</summary>
    private static string BuildUrlString(SortedDictionary<string, string> dict)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in dict)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(key);
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
        }

        return sb.ToString();
    }

    private static string ComputeHmacSha512(string data, string key)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}