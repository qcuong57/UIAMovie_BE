// UIAMovie.Domain/Entities/PaymentOrder.cs

namespace UIAMovie.Domain.Entities;

/// <summary>
/// Lưu trữ mỗi lần user khởi tạo thanh toán.
/// Không xóa record sau khi thanh toán — giữ làm lịch sử audit.
/// </summary>
public class PaymentOrder
{
    public Guid     Id            { get; set; } = Guid.NewGuid();

    /// <summary>Mã order duy nhất gửi sang gateway: "ORD-20240506-A3F7"</summary>
    public string   OrderCode     { get; set; } = string.Empty;

    public Guid     UserId        { get; set; }
    public User     User          { get; set; } = null!;

    /// <summary>"monthly_premium" | "yearly_premium"</summary>
    public string   PlanId        { get; set; } = string.Empty;

    /// <summary>"Premium Tháng" — snapshot tên gói lúc mua</summary>
    public string   PlanName      { get; set; } = string.Empty;

    /// <summary>Số ngày được cộng thêm khi thành công</summary>
    public int      DurationDays  { get; set; }

    /// <summary>Số tiền VND</summary>
    public long     Amount        { get; set; }

    /// <summary>"momo" | "vnpay"</summary>
    public string   Provider      { get; set; } = string.Empty;

    /// <summary>"pending" | "success" | "failed" | "expired"</summary>
    public string   Status        { get; set; } = PaymentStatus.Pending;

    public string?  FailureReason { get; set; }

    /// <summary>Transaction ID do gateway trả về (MoMo: TransId, VNPay: vnp_TransactionNo)</summary>
    public string?  GatewayTransId { get; set; }

    /// <summary>Raw callback JSON từ gateway — để debug</summary>
    public string?  RawCallback   { get; set; }

    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
    public DateTime ExpiredAt     { get; set; }           // Hết hạn sau 15 phút nếu chưa thanh toán
    public DateTime? PaidAt       { get; set; }
}

/// <summary>
/// Track trạng thái subscription của user.
/// Một user chỉ có một record, cập nhật khi thanh toán thành công.
/// </summary>
public class UserSubscription
{
    public Guid     Id               { get; set; } = Guid.NewGuid();
    public Guid     UserId           { get; set; }
    public User     User             { get; set; } = null!;

    /// <summary>"Free" | "Premium"</summary>
    public string   SubscriptionType { get; set; } = "Free";

    public DateTime? StartedAt      { get; set; }
    public DateTime? ExpiredAt      { get; set; }

    /// <summary>Order đã kích hoạt subscription này</summary>
    public Guid?    LastPaymentOrderId { get; set; }

    public DateTime UpdatedAt        { get; set; } = DateTime.UtcNow;
}

public static class PaymentStatus
{
    public const string Pending = "pending";
    public const string Success = "success";
    public const string Failed  = "failed";
    public const string Expired = "expired";
}

public static class SubscriptionTypes
{
    public const string Free    = "Free";
    public const string Premium = "Premium";
}