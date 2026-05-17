// UIAMovie.Application/Services/Payment/VnpayPaymentService.cs
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Services.Payment;

public class VnpayOptions
{
    public string TmnCode    { get; set; } = string.Empty;
    public string HashSecret { get; set; } = string.Empty;
    public string BaseUrl    { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
    public string ReturnUrl  { get; set; } = string.Empty;
    public string IpnUrl     { get; set; } = string.Empty;
}

public interface IVnpayPaymentService
{
    /// <param name="returnUrl">URL backend nhận redirect từ VNPay. Nếu null → dùng ReturnUrl trong config.</param>
    string CreatePaymentUrl(string orderCode, long amount, string orderInfo, string ipAddress, string? returnUrl = null);
    bool   VerifySignature(VnpayIpnDTO ipn);
}

public class VnpayPaymentService : IVnpayPaymentService
{
    private readonly VnpayOptions _opts;

    public VnpayPaymentService(IOptions<VnpayOptions> opts) => _opts = opts.Value;

    public string CreatePaymentUrl(string orderCode, long amount, string orderInfo, string ipAddress, string? returnUrl = null)
    {
        // Ưu tiên returnUrl truyền vào (từ frontend), fallback về config
        var effectiveReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) ? returnUrl : _opts.ReturnUrl;

        var vnpParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Version"]    = "2.1.0",
            ["vnp_Command"]    = "pay",
            ["vnp_TmnCode"]    = _opts.TmnCode,
            ["vnp_Amount"]     = (amount * 100).ToString(),
            ["vnp_CreateDate"] = DateTime.Now.ToString("yyyyMMddHHmmss"),
            ["vnp_CurrCode"]   = "VND",
            ["vnp_IpAddr"]     = ipAddress,
            ["vnp_Locale"]     = "vn",
            ["vnp_OrderInfo"]  = orderInfo,
            ["vnp_OrderType"]  = "other",
            ["vnp_ReturnUrl"]  = effectiveReturnUrl,
            ["vnp_TxnRef"]     = orderCode,
            ["vnp_ExpireDate"] = DateTime.Now.AddMinutes(15).ToString("yyyyMMddHHmmss"),
        };

        // Chỉ thêm IpnUrl nếu là URL thật (không phải placeholder)
        if (!string.IsNullOrEmpty(_opts.IpnUrl)
            && !_opts.IpnUrl.Contains("your-domain.com"))
        {
            vnpParams["vnp_IpnUrl"] = _opts.IpnUrl;
        }

        // VNPay chuẩn: hash dùng WebUtility.UrlEncode (space → '+', giống PHP urlencode)
        var hashData   = BuildHashString(vnpParams);
        var secureHash = ComputeHmacSha512(hashData, _opts.HashSecret);

        // URL dùng Uri.EscapeDataString (space → '%20') để browser hiểu đúng
        var urlQuery = BuildUrlString(vnpParams);
        return $"{_opts.BaseUrl}?{urlQuery}&vnp_SecureHash={secureHash}";
    }

    public bool VerifySignature(VnpayIpnDTO ipn)
    {
        var vnpParams = new SortedDictionary<string, string>(StringComparer.Ordinal);

        void Add(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value)) vnpParams[key] = value;
        }

        Add("vnp_TmnCode",           ipn.vnp_TmnCode);
        Add("vnp_Amount",            ipn.vnp_Amount.ToString());
        Add("vnp_BankCode",          ipn.vnp_BankCode);
        Add("vnp_BankTranNo",        ipn.vnp_BankTranNo);
        Add("vnp_CardType",          ipn.vnp_CardType);
        Add("vnp_PayDate",           ipn.vnp_PayDate);
        Add("vnp_OrderInfo",         ipn.vnp_OrderInfo);
        Add("vnp_TransactionNo",     ipn.vnp_TransactionNo);
        Add("vnp_ResponseCode",      ipn.vnp_ResponseCode);
        Add("vnp_TransactionStatus", ipn.vnp_TransactionStatus);
        Add("vnp_TxnRef",            ipn.vnp_TxnRef);

        var hashData     = BuildHashString(vnpParams);
        var computedHash = ComputeHmacSha512(hashData, _opts.HashSecret);
        return string.Equals(computedHash, ipn.vnp_SecureHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Chuỗi để tính HMAC — dùng WebUtility.UrlEncode giống PHP urlencode (chuẩn VNPay).
    /// space → '+', ký tự đặc biệt → %XX
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

    /// <summary>
    /// Chuỗi để ghép vào URL redirect — dùng Uri.EscapeDataString (RFC 3986).
    /// </summary>
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
        var hash       = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLower();
    }
}