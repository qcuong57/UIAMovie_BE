// UIAMovie.Infrastructure/Email/Templates/PaymentReceiptEmailTemplate.cs

using System.Globalization;
using System.Net;
using System.Text;
using UIAMovie.Application.DTOs;

namespace UIAMovie.Infrastructure.Email;

/// <summary>
/// Email xác nhận thanh toán (HTML + bản text). Thuần hàm, không I/O → dễ unit test.
/// Quy ước email: layout bằng table, CSS inline, rộng 600px, có preheader, nút bulletproof,
/// hỗ trợ dark mode (client không hỗ trợ sẽ rơi về giao diện sáng), luôn kèm bản text.
/// Mọi giá trị động đều HTML-encode (tên/email do người dùng nhập → chống HTML injection).
/// </summary>
internal static class PaymentReceiptEmailTemplate
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private const string Brand = "UIAMovie";
    private const string BrandColor = "#E50914";

    private static bool IsFree(PaymentReceiptEmailModel m) =>
        m.AmountVnd == 0 || string.Equals(m.Provider, "free", StringComparison.OrdinalIgnoreCase);

    public static string Subject(PaymentReceiptEmailModel m) =>
        IsFree(m)
            ? $"[{Brand}] Đã kích hoạt {m.PlanName} – {m.OrderCode}"
        : m.IsRenewal
            ? $"[{Brand}] Đã gia hạn {m.PlanName} – {m.OrderCode}"
            : $"[{Brand}] Xác nhận thanh toán {m.PlanName} – {m.OrderCode}";

    // ── HTML ──────────────────────────────────────────────────────────────────

    public static string RenderHtml(PaymentReceiptEmailModel m, string? supportEmail)
    {
        var free      = IsFree(m);
        var headline  = free ? "Kích hoạt thành công" : m.IsRenewal ? "Gia hạn thành công" : "Thanh toán thành công";
        var label     = free ? "Xác nhận kích hoạt" : "Xác nhận thanh toán";
        var intro     = free
            ? $"Gói {E(m.PlanName)} (miễn phí / dùng thử) đã được kích hoạt cho tài khoản của bạn và sẵn sàng sử dụng ngay."
            : m.IsRenewal
            ? $"Cảm ơn bạn đã gia hạn. Thời hạn {E(m.PlanName)} của bạn đã được cộng thêm {m.DurationDays} ngày."
            : $"Cảm ơn bạn đã nâng cấp. Tài khoản của bạn đã được kích hoạt {E(m.PlanName)} và sẵn sàng sử dụng ngay.";
        var preheader = $"Đơn {m.OrderCode} · {Money(m.AmountVnd)} · hiệu lực đến {Date(m.ExpiresAtUtc, false)}";
        var subject   = E(Subject(m));
        var expires   = Date(m.ExpiresAtUtc, false);
        var year      = DateTime.UtcNow.Year;

        var support = string.IsNullOrWhiteSpace(supportEmail)
            ? "hãy liên hệ bộ phận hỗ trợ"
            : $"hãy liên hệ <a href=\"mailto:{E(supportEmail)}\" style=\"color:{BrandColor};\">{E(supportEmail)}</a>";

        var rows = new StringBuilder()
            .Append(Row("Mã đơn hàng", m.OrderCode, mono: true))
            .Append(Row("Gói", m.PlanName))
            .Append(Row(free ? "Thời gian kích hoạt" : "Thời gian thanh toán", Date(m.PaidAtUtc, true)))
            .Append(Row("Phương thức", ProviderLabel(m.Provider)));
        if (!string.IsNullOrWhiteSpace(m.GatewayTransId))
            rows.Append(Row("Mã giao dịch cổng", m.GatewayTransId!, mono: true));

        var featuresBlock = BuildFeatures(m.Features);

        var footerNote = free
            ? $"Đây là email tự động, vui lòng giữ lại để tham khảo. Nếu bạn <strong>không thực hiện</strong> yêu cầu này, {support} ngay."
            : $"Đây là email tự động, vui lòng giữ lại làm biên nhận. Nếu bạn <strong>không thực hiện</strong> giao dịch này, {support} ngay. " +
              "Số tiền là số đã thanh toán qua cổng, chưa bao gồm các khoản phí do ngân hàng/cổng thanh toán áp dụng (nếu có).";
        var footerWhy = free ? "vì bạn vừa kích hoạt gói Premium" : "vì bạn vừa thanh toán gói Premium";

        return $$"""
<!DOCTYPE html>
<html lang="vi" xmlns="http://www.w3.org/1999/xhtml">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="color-scheme" content="light dark">
<meta name="supported-color-schemes" content="light dark">
<title>{{subject}}</title>
<style>
  body,table,td,a{-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%;}
  img{border:0;outline:none;text-decoration:none;}
  @media (max-width:620px){ .container{width:100% !important;} .px{padding-left:20px !important;padding-right:20px !important;} }
  @media (prefers-color-scheme:dark){
    .bg{background-color:#141414 !important;}
    .card{background-color:#1f1f1f !important;}
    .text{color:#f2f2f2 !important;}
    .muted{color:#b3b3b3 !important;}
    .line{border-color:#333333 !important;}
    .box{background-color:#2a2a2a !important;}
  }
</style>
</head>
<body class="bg" style="margin:0;padding:0;background-color:#f4f5f7;">
<div style="display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;">{{E(preheader)}}&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;</div>

<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="bg" style="background-color:#f4f5f7;">
<tr><td align="center" style="padding:24px 12px;">

  <table role="presentation" class="container" width="600" cellpadding="0" cellspacing="0" border="0" style="width:600px;max-width:600px;">

    <tr><td style="padding:0 0 16px 4px;font-family:Arial,Helvetica,sans-serif;font-size:26px;font-weight:bold;letter-spacing:1px;color:{{BrandColor}};">{{Brand}}</td></tr>

    <tr><td class="card" style="background-color:#ffffff;border-radius:8px;border-top:4px solid {{BrandColor}};">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">

        <tr><td class="px" style="padding:32px 40px 8px 40px;font-family:Arial,Helvetica,sans-serif;">
          <div class="muted" style="font-size:13px;color:#666666;text-transform:uppercase;letter-spacing:1px;">{{label}}</div>
          <h1 class="text" style="margin:8px 0 12px 0;font-size:26px;line-height:32px;color:#111111;">{{headline}}</h1>
          <p class="text" style="margin:0;font-size:15px;line-height:24px;color:#333333;">Xin chào <strong>{{E(m.RecipientName)}}</strong>,<br>{{intro}}</p>
        </td></tr>

        <tr><td class="px" style="padding:20px 40px 0 40px;font-family:Arial,Helvetica,sans-serif;">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="box" style="background-color:#fafafa;border-radius:6px;">
            <tr><td style="padding:16px 20px;">
              <div class="muted" style="font-size:12px;color:#666666;">Gói có hiệu lực đến</div>
              <div class="text" style="font-size:20px;font-weight:bold;color:#111111;padding-top:4px;">{{expires}}</div>
            </td></tr>
          </table>
        </td></tr>


        <tr><td class="px" style="padding:24px 40px 0 40px;font-family:Arial,Helvetica,sans-serif;">
          <div class="text" style="font-size:16px;font-weight:bold;color:#111111;padding-bottom:8px;">Chi tiết đơn hàng</div>
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
            {{rows}}
            <tr><td class="line" colspan="2" style="border-top:1px solid #e5e5e5;padding-top:12px;"></td></tr>
            <tr>
              <td class="text" style="font-size:16px;font-weight:bold;color:#111111;padding:4px 0 8px 0;">{{(free ? "Chi phí" : "Tổng thanh toán")}}</td>
              <td class="text" align="right" style="font-size:18px;font-weight:bold;color:#111111;padding:4px 0 8px 0;">{{(free ? "Miễn phí" : Money(m.AmountVnd))}}</td>
            </tr>
          </table>
        </td></tr>

        {{featuresBlock}}

        <tr><td class="px" style="padding:28px 40px 32px 40px;font-family:Arial,Helvetica,sans-serif;">
          <div class="line" style="border-top:1px solid #e5e5e5;padding-top:16px;"></div>
          <p class="muted" style="margin:0;font-size:13px;line-height:20px;color:#666666;">
            {{footerNote}}
          </p>
        </td></tr>

      </table>
    </td></tr>

    <tr><td align="center" class="muted" style="padding:20px 8px 0 8px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#888888;">
      &copy; {{year}} {{Brand}}. Email gửi tới {{E(m.ToEmail)}} {{footerWhy}}.
    </td></tr>

  </table>
</td></tr>
</table>
</body>
</html>
""";
    }

    // ── Plain text (multipart/alternative: tăng khả năng vào inbox, hỗ trợ client chỉ đọc text) ──

    public static string RenderText(PaymentReceiptEmailModel m)
    {
        var sb = new StringBuilder();
        var free = IsFree(m);
        sb.AppendLine(free ? "KÍCH HOẠT THÀNH CÔNG" : m.IsRenewal ? "GIA HẠN THÀNH CÔNG" : "THANH TOÁN THÀNH CÔNG");
        sb.AppendLine();
        sb.AppendLine($"Xin chào {m.RecipientName},");
        sb.AppendLine($"Gói {m.PlanName} của bạn có hiệu lực đến {Date(m.ExpiresAtUtc, false)}.");
        sb.AppendLine();
        sb.AppendLine("CHI TIẾT ĐƠN HÀNG");
        sb.AppendLine($"- Mã đơn hàng: {m.OrderCode}");
        sb.AppendLine($"- Gói: {m.PlanName}");
        sb.AppendLine($"- {(free ? "Thời gian kích hoạt" : "Thời gian thanh toán")}: {Date(m.PaidAtUtc, true)}");
        sb.AppendLine($"- Phương thức: {ProviderLabel(m.Provider)}");
        if (!string.IsNullOrWhiteSpace(m.GatewayTransId))
            sb.AppendLine($"- Mã giao dịch cổng: {m.GatewayTransId}");
        sb.AppendLine($"- {(free ? "Chi phí" : "Tổng thanh toán")}: {(free ? "Miễn phí" : Money(m.AmountVnd))}");
        sb.AppendLine();
        sb.AppendLine(free ? "Nếu bạn không thực hiện yêu cầu này, vui lòng liên hệ hỗ trợ ngay." : "Nếu bạn không thực hiện giao dịch này, vui lòng liên hệ hỗ trợ ngay.");
        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    private static string Money(long vnd) => vnd.ToString("N0", Vi) + " ₫";

    /// <summary>VN không có DST nên cộng cố định +7h (thống nhất với VnpayPaymentService / RevenueExcelExporter).</summary>
    private static string Date(DateTime utc, bool withTime)
    {
        var vn = utc.AddHours(7);
        return withTime
            ? vn.ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture) + " (GMT+7)"
            : vn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    private static string ProviderLabel(string provider) => provider.ToLowerInvariant() switch
    {
        "vnpay" => "VNPay",
        "sepay" => "SePay",
        "free"  => "Miễn phí / dùng thử",
        _       => provider.ToUpperInvariant()
    };

    private static string Row(string label, string value, bool mono = false)
    {
        var font = mono ? "font-family:Consolas,Menlo,monospace;" : "";
        return
            "<tr>" +
            $"<td class=\"muted\" style=\"font-size:14px;color:#666666;padding:6px 12px 6px 0;vertical-align:top;\">{E(label)}</td>" +
            $"<td class=\"text\" align=\"right\" style=\"font-size:14px;color:#111111;padding:6px 0;{font}\">{E(value)}</td>" +
            "</tr>";
    }

    private static string BuildFeatures(IReadOnlyList<string> features)
    {
        if (features.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.Append("<tr><td class=\"px\" style=\"padding:20px 40px 0 40px;font-family:Arial,Helvetica,sans-serif;\">");
        sb.Append("<div class=\"text\" style=\"font-size:16px;font-weight:bold;color:#111111;padding-bottom:6px;\">Quyền lợi của bạn</div>");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\">");
        foreach (var f in features)
            sb.Append("<tr><td class=\"muted\" style=\"padding:3px 0;font-size:14px;line-height:20px;color:#444444;\">")
              .Append($"<span style=\"color:{BrandColor};font-weight:bold;\">&#10003;</span>&nbsp;&nbsp;{E(f)}</td></tr>");
        sb.Append("</table></td></tr>");
        return sb.ToString();
    }
}