// UIAMovie.Application/Services/Payment/SepayPaymentService.cs

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace UIAMovie.Application.Services;

public class SepayOptions
{
    /// <summary>MERCHANT ID lấy ở my.sepay.vn (Cổng thanh toán → Sandbox).</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>SECRET KEY của merchant — dùng để ký form checkout (HMAC-SHA256).</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Giá trị header X-Secret-Key mà SePay gửi kèm IPN. Nhập đúng chuỗi này ở
    /// my.sepay.vn → Cổng thanh toán → Cấu hình → IPN (kiểu xác thực SECRET_KEY).
    /// </summary>
    public string IpnSecret { get; set; } = string.Empty;

    /// <summary>Sandbox: https://pay-sandbox.sepay.vn/v1/checkout/init — Production: https://pay.sepay.vn/v1/checkout/init</summary>
    public string CheckoutUrl { get; set; } = "https://pay-sandbox.sepay.vn/v1/checkout/init";
}

/// <summary>Form checkout SePay: FE phải POST <see cref="Fields"/> tới <see cref="Action"/>.</summary>
public record SepayCheckoutForm(string Action, Dictionary<string, string> Fields);

public interface ISepayPaymentService
{
    /// <summary>
    /// Tạo các field (đã ký) của form checkout. SePay dùng POST form chứ không phải URL redirect như VNPay.
    /// </summary>
    /// <param name="orderCode">Dùng làm order_invoice_number (SePay yêu cầu duy nhất).</param>
    /// <param name="amount">VND thật (không nhân 100).</param>
    SepayCheckoutForm CreateCheckoutForm(string orderCode, long amount, string description,
        string successUrl, string errorUrl, string cancelUrl);

    /// <summary>So sánh header X-Secret-Key của IPN với cấu hình (so sánh thời gian cố định).</summary>
    bool VerifyIpnSecret(string? headerValue);
}

public class SepayPaymentService : ISepayPaymentService
{
    // Thứ tự các field được ký do SePay quy định — KHÔNG sắp xếp lại.
    private static readonly string[] SignedFieldOrder =
    {
        "order_amount", "merchant", "currency", "operation", "order_description",
        "order_invoice_number", "customer_id", "payment_method",
        "success_url", "error_url", "cancel_url"
    };

    private readonly SepayOptions _opts;

    public SepayPaymentService(IOptions<SepayOptions> opts) => _opts = opts.Value;

    public SepayCheckoutForm CreateCheckoutForm(string orderCode, long amount, string description,
        string successUrl, string errorUrl, string cancelUrl)
    {
        // Dictionary giữ thứ tự thêm vào; thứ tự này trùng thứ tự ký và thứ tự input trong form mẫu của SePay.
        // Không gửi payment_method → SePay tự hiện màn hình chọn phương thức (hoặc tự chọn nếu merchant chỉ bật một).
        var fields = new Dictionary<string, string>
        {
            ["order_amount"]         = amount.ToString(CultureInfo.InvariantCulture),
            ["merchant"]             = _opts.MerchantId,
            ["currency"]             = "VND",
            ["operation"]            = "PURCHASE",
            ["order_description"]    = description,
            ["order_invoice_number"] = orderCode,
            ["success_url"]          = successUrl,
            ["error_url"]            = errorUrl,
            ["cancel_url"]           = cancelUrl,
        };

        fields["signature"] = Sign(fields);
        return new SepayCheckoutForm(_opts.CheckoutUrl, fields);
    }

    public bool VerifyIpnSecret(string? headerValue)
    {
        if (string.IsNullOrEmpty(headerValue) || string.IsNullOrEmpty(_opts.IpnSecret))
            return false;

        var a = Encoding.UTF8.GetBytes(headerValue);
        var b = Encoding.UTF8.GetBytes(_opts.IpnSecret);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// signature = Base64(HMAC-SHA256(secretKey, "field1=value1,field2=value2,..."))
    /// Giá trị để RAW (không URL-encode), chỉ gồm các field có trong <see cref="SignedFieldOrder"/> và có mặt trong form.
    /// </summary>
    private string Sign(IReadOnlyDictionary<string, string> fields)
    {
        var data = string.Join(",",
            SignedFieldOrder.Where(fields.ContainsKey).Select(k => $"{k}={fields[k]}"));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_opts.SecretKey));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }
}