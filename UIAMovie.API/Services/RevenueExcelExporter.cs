// UIAMovie.API/Services/RevenueExcelExporter.cs
// Cần NuGet: ClosedXML  (dotnet add UIAMovie.API package ClosedXML)
// Đặt ở API (tầng presentation) vì định dạng file là chi tiết trình bày, Application không phụ thuộc thư viện Excel.

using ClosedXML.Excel;
using UIAMovie.Application.DTOs;

namespace UIAMovie.API.Services;

public static class RevenueExcelExporter
{
    private const string MoneyFormat = "#,##0";
    private const string DateFormat  = "dd/mm/yyyy hh:mm:ss";

    private static readonly Dictionary<string, string> StatusLabels = new()
    {
        ["success"] = "Thành công",
        ["pending"] = "Chờ thanh toán",
        ["failed"]  = "Thất bại",
        ["expired"] = "Hết hạn"
    };

    public static byte[] Build(RevenueExportDTO d)
    {
        using var wb = new XLWorkbook();
        BuildSummary(wb.AddWorksheet("Tổng quan"), d);
        BuildByPlan(wb.AddWorksheet("Theo gói"), d);
        BuildOrders(wb.AddWorksheet("Đơn hàng"), d);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void BuildSummary(IXLWorksheet ws, RevenueExportDTO d)
    {
        var period = d.From is null && d.To is null
            ? "Toàn thời gian"
            : $"{d.From:dd/MM/yyyy} – {d.To:dd/MM/yyyy}".Replace(" – ", d.From is null ? "đến " : d.To is null ? " trở đi" : " – ");

        var rows = new (string Label, object Value, string? Format)[]
        {
            ("Báo cáo doanh thu Premium", "", null),
            ("Kỳ báo cáo (giờ VN)", period, null),
            ("Xuất lúc (giờ VN)", d.GeneratedAtUtc.AddHours(7), DateFormat),
            ("Số đơn thành công", d.SuccessOrders, MoneyFormat),
            ("Tổng doanh thu (VND, đã gồm thuế nếu giá niêm yết đã gồm)", d.TotalRevenue, MoneyFormat),
            ("Ghi chú", "Số tiền là số khách đã thanh toán qua cổng, CHƯA trừ phí cổng thanh toán và hoàn/hủy giao dịch.", null)
        };

        for (var i = 0; i < rows.Length; i++)
        {
            var r = i + 1;
            SetText(ws.Cell(r, 1), rows[i].Label);
            ws.Cell(r, 1).Style.Font.Bold = true;

            switch (rows[i].Value)
            {
                case string s: SetText(ws.Cell(r, 2), s); break;
                case DateTime dt: ws.Cell(r, 2).Value = dt; break;
                case int n: ws.Cell(r, 2).Value = n; break;
                case long n: ws.Cell(r, 2).Value = n; break;
            }
            if (rows[i].Format != null) ws.Cell(r, 2).Style.NumberFormat.Format = rows[i].Format!;
            ws.Cell(r, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        }

        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Column(1).Width = 58;
        ws.Column(2).Width = 40;
    }

    private static void BuildByPlan(IXLWorksheet ws, RevenueExportDTO d)
    {
        Header(ws, "Gói", "Số đơn", "Doanh thu (VND)", "Tỷ trọng (%)");

        var r = 2;
        foreach (var p in d.ByPlan)
        {
            SetText(ws.Cell(r, 1), p.PlanName);
            ws.Cell(r, 2).Value = p.Orders;
            ws.Cell(r, 3).Value = p.Revenue;
            ws.Cell(r, 4).Value = p.RevenueShare;
            ws.Cell(r, 3).Style.NumberFormat.Format = MoneyFormat;
            ws.Cell(r, 4).Style.NumberFormat.Format = "0.0";
            r++;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
    }

    private static void BuildOrders(IXLWorksheet ws, RevenueExportDTO d)
    {
        Header(ws, "Mã đơn", "Email", "Gói", "Số tiền (VND)", "Cổng", "Trạng thái",
            "Mã giao dịch cổng", "Ngày tạo (giờ VN)", "Ngày thanh toán (giờ VN)", "Lý do lỗi");

        var r = 2;
        foreach (var o in d.Orders)
        {
            SetText(ws.Cell(r, 1), o.OrderCode);
            SetText(ws.Cell(r, 2), o.UserEmail);
            SetText(ws.Cell(r, 3), o.PlanName);
            ws.Cell(r, 4).Value = o.Amount;
            ws.Cell(r, 4).Style.NumberFormat.Format = MoneyFormat;
            SetText(ws.Cell(r, 5), o.Provider.ToUpperInvariant());
            SetText(ws.Cell(r, 6), StatusLabels.TryGetValue(o.Status, out var label) ? label : o.Status);
            SetText(ws.Cell(r, 7), o.GatewayTransId);

            ws.Cell(r, 8).Value = o.CreatedAt.AddHours(7);
            ws.Cell(r, 8).Style.NumberFormat.Format = DateFormat;
            if (o.PaidAt.HasValue)
            {
                ws.Cell(r, 9).Value = o.PaidAt.Value.AddHours(7);
                ws.Cell(r, 9).Style.NumberFormat.Format = DateFormat;
            }
            SetText(ws.Cell(r, 10), o.FailureReason);
            r++;
        }

        if (d.Orders.Count > 0)
            ws.Range(1, 1, r - 1, 10).SetAutoFilter();

        ws.Columns().AdjustToContents(1, Math.Min(r, 200)); // chỉ đo 200 dòng đầu cho nhanh
        ws.SheetView.FreezeRows(1);
    }

    private static void Header(IXLWorksheet ws, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = titles[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7");
        }
    }

    /// <summary>
    /// Chống CSV/Excel formula injection: email, lý do lỗi... là dữ liệu do người dùng/cổng quyết định;
    /// chuỗi bắt đầu bằng = + - @ sẽ bị Excel hiểu là công thức khi admin mở file.
    /// </summary>
    private static void SetText(IXLCell cell, string? value)
    {
        var s = value ?? string.Empty;
        if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
        cell.Value = s;
    }
}