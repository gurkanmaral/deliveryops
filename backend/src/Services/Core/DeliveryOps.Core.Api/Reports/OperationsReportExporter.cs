using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Queries.Reports;

namespace DeliveryOps.Core.Api.Reports;

internal static class OperationsReportExporter
{
    private const string HeaderColor = "1E633F";
    private const string LightGreen = "E8F3EC";

    public static byte[] ToExcel(OperationsReportResponse report)
    {
        using XLWorkbook workbook = new();
        AddSummarySheet(workbook, report);
        AddDailySheet(workbook, report.Daily);
        AddCourierSheet(workbook, report.Couriers);
        AddBranchSheet(workbook, report.Branches);
        AddSourceSheet(workbook, report.Sources);
        using MemoryStream stream = new();
        workbook.SaveAs(stream, validate: true);
        return stream.ToArray();
    }

    public static byte[] ToCsv(OperationsReportResponse report)
    {
        StringBuilder csv = new();
        csv.AppendLine("Tarih;Toplam Sipariş;Teslim Edildi;İptal;Başarısız;Açık;Sipariş Tutarı;Teslim Edilen Tutar");
        foreach (DailyOrderMetric item in report.Daily)
        {
            csv.Append(item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(';')
                .Append(item.TotalOrders).Append(';').Append(item.DeliveredOrders).Append(';')
                .Append(item.CancelledOrders).Append(';').Append(item.FailedOrders).Append(';')
                .Append(item.OpenOrders).Append(';')
                .Append(item.TotalOrderValue.ToString("0.00", CultureInfo.InvariantCulture)).Append(';')
                .AppendLine(item.DeliveredValue.ToString("0.00", CultureInfo.InvariantCulture));
        }
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv.ToString());
    }

    private static void AddSummarySheet(XLWorkbook workbook, OperationsReportResponse report)
    {
        IXLWorksheet sheet = workbook.Worksheets.Add("Özet");
        sheet.Cell("A1").Value = "DeliveryOps Operasyon Raporu";
        sheet.Cell("A1").Style.Font.Bold = true;
        sheet.Cell("A1").Style.Font.FontSize = 18;
        sheet.Cell("A2").Value = $"Dönem: {report.From:dd.MM.yyyy} - {report.To:dd.MM.yyyy} · Saat dilimi: {report.TimeZone}";
        sheet.Cell("A2").Style.Font.FontColor = XLColor.Gray;

        string[] labels = ["Toplam sipariş", "Teslim edilen", "İptal", "Başarısız / iade", "Açık sipariş",
            "Teslimat başarı oranı", "Ortalama atama (dk)", "Ortalama teslim alma (dk)",
            "Ortalama teslimat (dk)", "Ortalama toplam süre (dk)", "Toplam sipariş tutarı", "Teslim edilen tutar"];
        object?[] values = [report.Summary.TotalOrders, report.Summary.DeliveredOrders,
            report.Summary.CancelledOrders, report.Summary.FailedOrders, report.Summary.OpenOrders,
            report.Summary.DeliverySuccessRate, report.Summary.AverageAssignmentMinutes,
            report.Summary.AveragePickupMinutes, report.Summary.AverageDeliveryMinutes,
            report.Summary.AverageTotalMinutes, report.Summary.TotalOrderValue, report.Summary.DeliveredValue];

        sheet.Cell("A4").Value = "Gösterge";
        sheet.Cell("B4").Value = "Değer";
        StyleHeader(sheet.Range("A4:B4"));
        for (int index = 0; index < labels.Length; index++)
        {
            int row = index + 5;
            sheet.Cell(row, 1).Value = labels[index];
            SetCellValue(sheet.Cell(row, 2), values[index]);
            if (index % 2 == 1) sheet.Range(row, 1, row, 2).Style.Fill.BackgroundColor = XLColor.FromHtml(LightGreen);
        }
        sheet.Cell("B10").Style.NumberFormat.Format = "0.0%";
        sheet.Range("B11:B14").Style.NumberFormat.Format = "0.0";
        sheet.Range("B15:B16").Style.NumberFormat.Format = "#,##0.00 [$₺-tr-TR]";
        sheet.Columns("A:B").AdjustToContents();
        sheet.Column("A").Width = Math.Max(sheet.Column("A").Width, 28);
        sheet.SheetView.FreezeRows(4);
    }

    private static void AddDailySheet(XLWorkbook workbook, IReadOnlyList<DailyOrderMetric> rows)
    {
        IXLWorksheet sheet = workbook.Worksheets.Add("Günlük");
        string[] headers = ["Tarih", "Toplam", "Teslim", "İptal", "Başarısız", "Açık", "Sipariş Tutarı", "Teslim Edilen Tutar"];
        WriteHeaders(sheet, headers);
        for (int index = 0; index < rows.Count; index++)
        {
            DailyOrderMetric item = rows[index];
            int row = index + 2;
            sheet.Cell(row, 1).Value = item.Date.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 2).Value = item.TotalOrders;
            sheet.Cell(row, 3).Value = item.DeliveredOrders;
            sheet.Cell(row, 4).Value = item.CancelledOrders;
            sheet.Cell(row, 5).Value = item.FailedOrders;
            sheet.Cell(row, 6).Value = item.OpenOrders;
            sheet.Cell(row, 7).Value = item.TotalOrderValue;
            sheet.Cell(row, 8).Value = item.DeliveredValue;
        }
        sheet.Column(1).Style.DateFormat.Format = "dd mmm yyyy";
        sheet.Columns(7, 8).Style.NumberFormat.Format = "#,##0.00 [$₺-tr-TR]";
        FinishTable(sheet, headers.Length, rows.Count);
    }

    private static void AddCourierSheet(XLWorkbook workbook, IReadOnlyList<CourierPerformanceMetric> rows)
    {
        IXLWorksheet sheet = workbook.Worksheets.Add("Kuryeler");
        string[] headers = ["Kurye", "Atanan", "Teslim", "Başarısız", "Açık", "Başarı Oranı", "Ort. Teslimat (dk)", "Ort. Toplam (dk)"];
        WriteHeaders(sheet, headers);
        for (int index = 0; index < rows.Count; index++)
        {
            CourierPerformanceMetric item = rows[index];
            int row = index + 2;
            sheet.Cell(row, 1).Value = item.CourierName;
            sheet.Cell(row, 2).Value = item.AssignedOrders;
            sheet.Cell(row, 3).Value = item.DeliveredOrders;
            sheet.Cell(row, 4).Value = item.FailedOrders;
            sheet.Cell(row, 5).Value = item.OpenOrders;
            sheet.Cell(row, 6).Value = item.DeliverySuccessRate;
            SetCellValue(sheet.Cell(row, 7), item.AverageDeliveryMinutes);
            SetCellValue(sheet.Cell(row, 8), item.AverageTotalMinutes);
        }
        sheet.Column(6).Style.NumberFormat.Format = "0.0%";
        sheet.Columns(7, 8).Style.NumberFormat.Format = "0.0";
        FinishTable(sheet, headers.Length, rows.Count);
    }

    private static void AddBranchSheet(XLWorkbook workbook, IReadOnlyList<BranchPerformanceMetric> rows)
    {
        IXLWorksheet sheet = workbook.Worksheets.Add("Şubeler");
        string[] headers = ["İşletme", "Şube", "Toplam", "Teslim", "İptal", "Başarısız", "Açık", "Başarı Oranı", "Ort. Toplam (dk)", "Sipariş Tutarı", "Teslim Edilen Tutar"];
        WriteHeaders(sheet, headers);
        for (int index = 0; index < rows.Count; index++)
        {
            BranchPerformanceMetric item = rows[index];
            int row = index + 2;
            sheet.Cell(row, 1).Value = item.BusinessName;
            sheet.Cell(row, 2).Value = item.BranchName;
            sheet.Cell(row, 3).Value = item.TotalOrders;
            sheet.Cell(row, 4).Value = item.DeliveredOrders;
            sheet.Cell(row, 5).Value = item.CancelledOrders;
            sheet.Cell(row, 6).Value = item.FailedOrders;
            sheet.Cell(row, 7).Value = item.OpenOrders;
            sheet.Cell(row, 8).Value = item.DeliverySuccessRate;
            SetCellValue(sheet.Cell(row, 9), item.AverageTotalMinutes);
            sheet.Cell(row, 10).Value = item.TotalOrderValue;
            sheet.Cell(row, 11).Value = item.DeliveredValue;
        }
        sheet.Column(8).Style.NumberFormat.Format = "0.0%";
        sheet.Column(9).Style.NumberFormat.Format = "0.0";
        sheet.Columns(10, 11).Style.NumberFormat.Format = "#,##0.00 [$₺-tr-TR]";
        FinishTable(sheet, headers.Length, rows.Count);
    }

    private static void AddSourceSheet(XLWorkbook workbook, IReadOnlyList<SourcePerformanceMetric> rows)
    {
        IXLWorksheet sheet = workbook.Worksheets.Add("Kaynaklar");
        string[] headers = ["Kaynak", "Toplam", "Teslim", "Başarısız", "Başarı Oranı", "Sipariş Tutarı"];
        WriteHeaders(sheet, headers);
        for (int index = 0; index < rows.Count; index++)
        {
            SourcePerformanceMetric item = rows[index];
            int row = index + 2;
            sheet.Cell(row, 1).Value = SourceLabel(item.Source);
            sheet.Cell(row, 2).Value = item.TotalOrders;
            sheet.Cell(row, 3).Value = item.DeliveredOrders;
            sheet.Cell(row, 4).Value = item.FailedOrders;
            sheet.Cell(row, 5).Value = item.DeliverySuccessRate;
            sheet.Cell(row, 6).Value = item.TotalOrderValue;
        }
        sheet.Column(5).Style.NumberFormat.Format = "0.0%";
        sheet.Column(6).Style.NumberFormat.Format = "#,##0.00 [$₺-tr-TR]";
        FinishTable(sheet, headers.Length, rows.Count);
    }

    private static void WriteHeaders(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (int index = 0; index < headers.Count; index++) sheet.Cell(1, index + 1).Value = headers[index];
        StyleHeader(sheet.Range(1, 1, 1, headers.Count));
    }

    private static void FinishTable(IXLWorksheet sheet, int columnCount, int rowCount)
    {
        int finalRow = Math.Max(1, rowCount + 1);
        sheet.Range(1, 1, finalRow, columnCount).SetAutoFilter();
        sheet.SheetView.FreezeRows(1);
        sheet.Columns(1, columnCount).AdjustToContents();
        foreach (IXLColumn column in sheet.Columns(1, columnCount))
            if (column.Width > 42) column.Width = 42;
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderColor);
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Font.Bold = true;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case int intValue: cell.Value = intValue; break;
            case double doubleValue: cell.Value = doubleValue; break;
            case decimal decimalValue: cell.Value = decimalValue; break;
            case string stringValue: cell.Value = stringValue; break;
            case null: cell.Value = string.Empty; break;
            default: cell.Value = value.ToString() ?? string.Empty; break;
        }
    }

    private static string SourceLabel(OrderSource source) => source switch
    {
        OrderSource.Phone => "Telefon",
        OrderSource.AdminPanel => "Admin paneli",
        OrderSource.BusinessPanel => "İşletme paneli",
        OrderSource.Yemeksepeti => "Yemeksepeti",
        OrderSource.Getir => "Getir",
        OrderSource.Pos => "POS",
        OrderSource.Trendyol => "Trendyol",
        _ => "Diğer"
    };
}
