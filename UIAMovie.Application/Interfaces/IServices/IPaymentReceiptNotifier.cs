// UIAMovie.Application/Interfaces/IServices/IPaymentReceiptNotifier.cs
using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Interfaces.IServices;

/// <summary>
/// Xếp hàng gửi email biên nhận. KHÔNG chặn và KHÔNG ném lỗi gửi mail ra ngoài:
/// việc gửi chạy nền, sau khi request/DbContext đã kết thúc.
/// </summary>
public interface IPaymentReceiptNotifier
{
    void Enqueue(PaymentReceiptEmailModel model);
}