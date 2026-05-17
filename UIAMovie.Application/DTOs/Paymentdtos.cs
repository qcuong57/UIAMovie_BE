// UIAMovie.Application/DTOs/PaymentDTOs.cs
using System.ComponentModel.DataAnnotations;

namespace UIAMovie.Application.DTOs;

// ── Subscription Plans ────────────────────────────────────────────────────────

/// <summary>Gói subscription hiển thị cho user chọn</summary>
public class SubscriptionPlanDTO
{
    public string       PlanId        { get; set; } = string.Empty; // "monthly_premium", "yearly_premium"
    public string       Name          { get; set; } = string.Empty; // "Premium Tháng", "Premium Năm"
    public string       Description   { get; set; } = string.Empty;
    public long         PriceVnd      { get; set; }                 // 59000, 590000
    public string       PriceDisplay  { get; set; } = string.Empty; // "59.000đ/tháng"
    public int          DurationDays  { get; set; }                 // 30, 365
    public bool         IsMostPopular { get; set; }
    public List<string> Features      { get; set; } = new();
}

// ── Payment Order ─────────────────────────────────────────────────────────────

/// <summary>Request tạo payment order</summary>
public class CreatePaymentOrderDTO
{
    [Required]
    public string PlanId { get; set; } = string.Empty; // "monthly_premium" | "yearly_premium"

    /// <summary>Hiện chỉ hỗ trợ "vnpay"</summary>
    [Required]
    public string PaymentProvider { get; set; } = "vnpay";

    /// <summary>URL frontend redirect sau thanh toán (optional — dùng default nếu null)</summary>
    public string? ReturnUrl { get; set; }
}

/// <summary>Response sau khi tạo order — frontend redirect đến PaymentUrl</summary>
public class PaymentOrderResponseDTO
{
    public Guid     OrderId    { get; set; }
    public string   OrderCode  { get; set; } = string.Empty; // "ORD-20240506-XXXX"
    public string   PaymentUrl { get; set; } = string.Empty; // URL redirect sang VNPay
    public long     Amount     { get; set; }                 // VND
    public string   Provider   { get; set; } = string.Empty;
    public DateTime ExpiredAt  { get; set; }                 // Hết hạn sau 15 phút
}

// ── Payment History ───────────────────────────────────────────────────────────

public class PaymentHistoryDTO
{
    public Guid      Id            { get; set; }
    public string    OrderCode     { get; set; } = string.Empty;
    public string    PlanName      { get; set; } = string.Empty;
    public long      Amount        { get; set; }
    public string    Provider      { get; set; } = string.Empty; // "vnpay"
    public string    Status        { get; set; } = string.Empty; // "pending" | "success" | "failed"
    public string?   FailureReason { get; set; }
    public DateTime  CreatedAt     { get; set; }
    public DateTime? PaidAt        { get; set; }
}

// ── Subscription Status ───────────────────────────────────────────────────────

public class SubscriptionStatusDTO
{
    public string    SubscriptionType { get; set; } = string.Empty; // "Free" | "Premium"
    public bool      IsPremium        { get; set; }
    public DateTime? ExpiredAt        { get; set; }
    public int?      DaysRemaining    { get; set; }
    public bool      IsExpiringSoon   { get; set; } // true nếu còn < 7 ngày
}

// ── VNPay IPN / Return ────────────────────────────────────────────────────────

/// <summary>
/// Query params VNPay gửi về IPN endpoint (server-to-server) và ReturnUrl.
///
/// QUAN TRỌNG:
///   - POST /api/payments/vnpay/ipn  → HandleVnpayIpnAsync → upgrade subscription
///   - GET  /api/payments/vnpay/return → chỉ redirect browser về frontend, KHÔNG upgrade
///
/// vnp_Amount đã được nhân 100 so với VND thật (59.000đ → 5.900.000).
/// Tham khảo: https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/
/// </summary>
public class VnpayIpnDTO
{
    public string vnp_TmnCode           { get; set; } = string.Empty;
    public long   vnp_Amount            { get; set; }  // * 100 so với VND thật
    public string vnp_BankCode          { get; set; } = string.Empty;
    public string vnp_BankTranNo        { get; set; } = string.Empty;
    public string vnp_CardType          { get; set; } = string.Empty;
    public string vnp_PayDate           { get; set; } = string.Empty;
    public string vnp_OrderInfo         { get; set; } = string.Empty;
    public string vnp_TransactionNo     { get; set; } = string.Empty;
    public string vnp_ResponseCode      { get; set; } = string.Empty; // "00" = success
    public string vnp_TransactionStatus { get; set; } = string.Empty;
    public string vnp_TxnRef            { get; set; } = string.Empty; // = OrderCode của mình
    public string vnp_SecureHash        { get; set; } = string.Empty;
}

// ── Content Access ────────────────────────────────────────────────────────────

/// <summary>Gắn vào MovieDTO / TvShowDTO để frontend biết user có thể xem không</summary>
public class ContentAccessDTO
{
    public bool    CanWatch        { get; set; }
    public bool    RequiresPremium { get; set; }
    public string? BlockReason     { get; set; } // "Cần gói Premium để xem phim này"
}