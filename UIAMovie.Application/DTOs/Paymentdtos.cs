// UIAMovie.Application/DTOs/PaymentDTOs.cs

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace UIAMovie.Application.DTOs;

// ── Subscription Plans ────────────────────────────────────────────────────────

/// <summary>Gói subscription hiển thị cho user chọn</summary>
public class SubscriptionPlanDTO
{
    public string PlanId { get; set; } = string.Empty; // "monthly_premium", "yearly_premium"
    public string Name { get; set; } = string.Empty; // "Premium Tháng", "Premium Năm"
    public string Description { get; set; } = string.Empty;
    public long PriceVnd { get; set; } // 69000, 599000
    public string PriceDisplay { get; set; } = string.Empty; // "69.000đ/tháng"
    public int DurationDays { get; set; } // 30, 365
    public bool IsMostPopular { get; set; }
    public List<string> Features { get; set; } = new();
}

// ── Payment Order ─────────────────────────────────────────────────────────────

/// <summary>Request tạo payment order</summary>
public class CreatePaymentOrderDTO
{
    [Required] public string PlanId { get; set; } = string.Empty; // "monthly_premium" | "yearly_premium"

    /// <summary>"vnpay" | "sepay"</summary>
    [Required]
    public string PaymentProvider { get; set; } = "vnpay";

    /// <summary>URL frontend redirect sau thanh toán (optional — dùng default nếu null)</summary>
    public string? ReturnUrl { get; set; }
}

/// <summary>Response sau khi tạo order — frontend chuyển user sang cổng thanh toán (xem PaymentMethod)</summary>
public class PaymentOrderResponseDTO
{
    public Guid OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty; // "ORD-20240506-XXXX"
    /// <summary>
    /// VNPay: URL redirect (location.href).
    /// SePay: URL action của form (POST) — FE phải submit form với <see cref="FormFields"/>.
    /// </summary>
    public string PaymentUrl { get; set; } = string.Empty;

    /// <summary>"redirect" (VNPay) | "form_post" (SePay)</summary>
    public string PaymentMethod { get; set; } = "redirect";

    /// <summary>Chỉ có khi PaymentMethod = "form_post". FE tạo input hidden theo ĐÚNG thứ tự này.</summary>
    public Dictionary<string, string>? FormFields { get; set; }
    public long Amount { get; set; } // VND
    public string Provider { get; set; } = string.Empty;
    public DateTime ExpiredAt { get; set; } // Hết hạn sau 15 phút
}

// ── Payment History ───────────────────────────────────────────────────────────

public class PaymentHistoryDTO
{
    public Guid Id { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string Provider { get; set; } = string.Empty; // "vnpay" | "sepay"
    public string Status { get; set; } = string.Empty; // "pending" | "success" | "failed" | "expired"
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
}

// ── Subscription Status ───────────────────────────────────────────────────────

public class SubscriptionStatusDTO
{
    public string SubscriptionType { get; set; } = string.Empty; // "Free" | "Premium"
    public bool IsPremium { get; set; }
    public DateTime? ExpiredAt { get; set; }
    public int? DaysRemaining { get; set; }
    public bool IsExpiringSoon { get; set; } // true nếu còn <= 7 ngày
}

// ── VNPay IPN / Return ────────────────────────────────────────────────────────

/// <summary>
/// Query params VNPay gửi về IPN endpoint (server-to-server) và ReturnUrl.
///
/// QUAN TRỌNG:
///   - GET /api/payments/vnpay/ipn    → HandleVnpayIpnAsync → upgrade subscription (VNPay dùng GET)
///   - GET /api/payments/vnpay/return → cũng gọi HandleVnpayIpnAsync (idempotent, có khóa) rồi redirect về frontend
///
/// DTO này chỉ để đọc giá trị. Chữ ký được xác thực từ query string gốc, KHÔNG từ DTO,
/// nên VNPay có gửi thêm tham số ngoài DTO thì chữ ký vẫn đúng.
///
/// vnp_Amount đã được nhân 100 so với VND thật (69.000đ → 6.900.000).
/// Tham khảo: https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/
/// </summary>
public class VnpayIpnDTO
{
    public string vnp_TmnCode { get; set; } = string.Empty;
    public long vnp_Amount { get; set; } // * 100 so với VND thật
    public string vnp_BankCode { get; set; } = string.Empty;
    public string vnp_BankTranNo { get; set; } = string.Empty;
    public string vnp_CardType { get; set; } = string.Empty;
    public string vnp_PayDate { get; set; } = string.Empty;
    public string vnp_OrderInfo { get; set; } = string.Empty;
    public string vnp_TransactionNo { get; set; } = string.Empty;
    public string vnp_ResponseCode { get; set; } = string.Empty; // "00" = success
    public string vnp_TransactionStatus { get; set; } = string.Empty; // "00" = success
    public string vnp_TxnRef { get; set; } = string.Empty; // = OrderCode của mình
    public string vnp_SecureHash { get; set; } = string.Empty;
}

// ── SePay IPN ─────────────────────────────────────────────────────────────────

/// <summary>
/// Body JSON SePay gửi tới POST /api/payments/sepay/ipn (chỉ khai báo các field đang dùng).
/// IPN của SePay KHÔNG có chữ ký, chỉ xác thực bằng header X-Secret-Key,
/// nên mọi giá trị ở đây phải được đối chiếu với đơn trong DB trước khi tin.
/// Tham khảo: https://developer.sepay.vn/vi/cong-thanh-toan/IPN
/// </summary>
public class SepayIpnDTO
{
    /// <summary>"ORDER_PAID" | "TRANSACTION_VOID"</summary>
    [JsonPropertyName("notification_type")] public string NotificationType { get; set; } = string.Empty;
    [JsonPropertyName("order")]             public SepayIpnOrder? Order { get; set; }
    [JsonPropertyName("transaction")]       public SepayIpnTransaction? Transaction { get; set; }
}

public class SepayIpnOrder
{
    [JsonPropertyName("order_id")]             public string OrderId { get; set; } = string.Empty;
    [JsonPropertyName("order_status")]         public string Status { get; set; } = string.Empty;        // "CAPTURED"
    [JsonPropertyName("order_invoice_number")] public string InvoiceNumber { get; set; } = string.Empty; // = OrderCode của mình

    /// <summary>VND thật, không nhân 100. SePay gửi dạng chuỗi "50000.00".</summary>
    [JsonPropertyName("order_amount")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal Amount { get; set; }
}

public class SepayIpnTransaction
{
    [JsonPropertyName("transaction_id")]     public string TransactionId { get; set; } = string.Empty;
    [JsonPropertyName("transaction_status")] public string Status { get; set; } = string.Empty;          // "APPROVED"
}

// ── Content Access ────────────────────────────────────────────────────────────

/// <summary>Gắn vào MovieDTO / TvShowDTO để frontend biết user có thể xem không</summary>
public class ContentAccessDTO
{
    public bool CanWatch { get; set; }
    public bool RequiresPremium { get; set; }
    public string? BlockReason { get; set; } // "Cần gói Premium để xem phim này"
}