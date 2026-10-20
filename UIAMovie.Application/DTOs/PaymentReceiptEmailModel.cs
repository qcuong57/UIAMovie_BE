// UIAMovie.Application/DTOs/PaymentReceiptEmailModel.cs
namespace UIAMovie.Application.DTOs;

/// <summary>
/// Dữ liệu bất biến để dựng email xác nhận thanh toán. Là một "snapshot" tại thời điểm thanh toán:
/// job chạy nền sau khi scope/DbContext của request đã bị hủy, nên KHÔNG được giữ entity.
/// Tất cả thời gian là UTC; việc đổi sang giờ VN thuộc về tầng trình bày (template).
/// </summary>
public sealed record PaymentReceiptEmailModel(
    string ToEmail,
    string RecipientName,
    string OrderCode,
    string PlanName,
    long AmountVnd,
    string Provider,
    string? GatewayTransId,
    DateTime PaidAtUtc,
    DateTime ExpiresAtUtc,
    int DurationDays,
    bool IsRenewal,
    IReadOnlyList<string> Features);