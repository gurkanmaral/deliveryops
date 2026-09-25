using System.Globalization;
using ClosedXML.Excel;
using DeliveryOps.Core.Queries.Billing;
using MuPDF.Fonts;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace DeliveryOps.Core.Api.Billing;

public static class BillingDocumentExporter
{
    private const string BrandHex = "1E633F";
    private const string SoftGreenHex = "E8F3EC";
    private static readonly object FontLock = new();
    private static bool _fontConfigured;

    public static byte[] ToPdf(BillingSettlementResponse settlement)
    {
        EnsureFontResolver();
        using PdfDocument document = new();
        document.Info.Title = $"Mutabakat {settlement.DocumentNumber}";
        document.Info.Subject = $"{settlement.BusinessName} dönem mutabakatı";
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(210);
        page.Height = XUnit.FromMillimeter(297);
        using XGraphics graphics = XGraphics.FromPdfPage(page);

        XFont titleFont = new("Noto Sans", 20, XFontStyleEx.Bold);
        XFont headingFont = new("Noto Sans", 11, XFontStyleEx.Bold);
        XFont bodyFont = new("Noto Sans", 9, XFontStyleEx.Regular);
        XFont smallFont = new("Noto Sans", 7.5, XFontStyleEx.Regular);
        XFont totalFont = new("Noto Sans", 13, XFontStyleEx.Bold);
        XBrush dark = new XSolidBrush(XColor.FromArgb(31, 49, 40));
        XBrush muted = new XSolidBrush(XColor.FromArgb(105, 120, 112));
        XBrush brand = new XSolidBrush(XColor.FromArgb(30, 99, 63));
        XBrush softGreen = new XSolidBrush(XColor.FromArgb(232, 243, 236));
        XPen line = new(XColor.FromArgb(220, 229, 224), 0.7);

        const double left = 48;
        const double right = 547;
        graphics.DrawRectangle(brand, 0, 0, page.Width.Point, 12);
        graphics.DrawString("DeliveryOps", titleFont, brand, new XRect(left, 42, 260, 32), XStringFormats.TopLeft);
        graphics.DrawString("MUTABAKAT BELGESİ", headingFont, dark, new XRect(left, 78, 240, 20), XStringFormats.TopLeft);
        graphics.DrawString(settlement.DocumentNumber, headingFont, brand,
            new XRect(330, 78, right - 330, 20), XStringFormats.TopRight);
        graphics.DrawLine(line, left, 105, right, 105);

        DrawInfo(graphics, bodyFont, smallFont, dark, muted, "İşletme", settlement.BusinessName, left, 126);
        DrawInfo(graphics, bodyFont, smallFont, dark, muted, "Dönem",
            $"{settlement.PeriodFrom:dd.MM.yyyy} - {settlement.PeriodTo:dd.MM.yyyy}", 300, 126);
        DrawInfo(graphics, bodyFont, smallFont, dark, muted, "Kesinleşme tarihi",
            settlement.FinalizedAtUtc?.ToOffset(TimeSpan.FromHours(3)).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) ?? "-", left, 171);
        DrawInfo(graphics, bodyFont, smallFont, dark, muted, "Para birimi", settlement.Currency, 300, 171);

        DrawSectionTitle(graphics, headingFont, dark, "Operasyon özeti", left, 226);
        string[] operationHeaders = ["Kalem", "Adet", "Sipariş tutarı"];
        string[][] operationRows =
        [
            ["Teslim edilen paket", settlement.DeliveredOrderCount.ToString("N0", CultureInfo.GetCultureInfo("tr-TR")), Money(settlement.DeliveredOrderValue)],
            ["İade edilen paket", settlement.ReturnedOrderCount.ToString("N0", CultureInfo.GetCultureInfo("tr-TR")), "-"],
            ["İptal edilen sipariş", settlement.CancelledOrderCount.ToString("N0", CultureInfo.GetCultureInfo("tr-TR")), "Ücretlendirme dışı"]
        ];
        DrawTable(graphics, bodyFont, headingFont, dark, muted, softGreen, line, operationHeaders,
            operationRows, left, 250, [265d, 90d, 144d]);

        DrawSectionTitle(graphics, headingFont, dark, "Tarife ve hesaplama", left, 382);
        string[] pricingHeaders = ["Hesaplama", "Oran / birim", "Tutar"];
        string[][] pricingRows =
        [
            ["Teslimat hizmeti", $"{settlement.DeliveredOrderCount:N0} × {Money(settlement.FeePerDeliveredOrder)}", Money(settlement.DeliveryFeeAmount)],
            ["Sipariş komisyonu", $"{Money(settlement.DeliveredOrderValue)} × %{settlement.CommissionRatePercent:N2}", Money(settlement.CommissionAmount)],
            ["İade işlemi", $"{settlement.ReturnedOrderCount:N0} × {Money(settlement.FeePerReturnedOrder)}", Money(settlement.ReturnFeeAmount)]
        ];
        DrawTable(graphics, bodyFont, headingFont, dark, muted, softGreen, line, pricingHeaders,
            pricingRows, left, 406, [265d, 140d, 94d]);

        double totalsTop = 548;
        graphics.DrawRectangle(softGreen, left, totalsTop, right - left, 108);
        DrawTotalLine(graphics, bodyFont, dark, "Ara toplam", Money(settlement.SubtotalAmount), left + 18, right - 18, totalsTop + 20);
        DrawTotalLine(graphics, bodyFont, dark, $"KDV (%{settlement.TaxRatePercent:N2})", Money(settlement.TaxAmount), left + 18, right - 18, totalsTop + 47);
        graphics.DrawLine(new XPen(XColor.FromArgb(181, 210, 192), 0.8), left + 18, totalsTop + 64, right - 18, totalsTop + 64);
        graphics.DrawString("GENEL TOPLAM", headingFont, brand, new XRect(left + 18, totalsTop + 76, 250, 22), XStringFormats.TopLeft);
        graphics.DrawString(Money(settlement.TotalAmount), totalFont, brand, new XRect(320, totalsTop + 73, right - 338, 25), XStringFormats.TopRight);

        graphics.DrawString("Bu belge kesinleşmiş operasyon kayıtları ve tarife snapshot'ından üretilmiştir.",
            smallFont, muted, new XRect(left, 700, right - left, 18), XStringFormats.TopLeft);
        graphics.DrawString("Fatura değildir.", smallFont, muted,
            new XRect(left, 718, right - left, 18), XStringFormats.TopLeft);
        graphics.DrawLine(line, left, 758, right, 758);
        graphics.DrawString($"Belge no: {settlement.DocumentNumber}", smallFont, muted,
            new XRect(left, 770, 260, 18), XStringFormats.TopLeft);
        graphics.DrawString("DeliveryOps", smallFont, muted,
            new XRect(360, 770, right - 360, 18), XStringFormats.TopRight);

        using MemoryStream stream = new();
        document.Save(stream, false);
        return stream.ToArray();
    }

    public static byte[] ToExcel(BillingSettlementResponse settlement)
    {
        using XLWorkbook workbook = new();
        IXLWorksheet sheet = workbook.Worksheets.Add("Mutabakat");
        sheet.ShowGridLines = false;
        sheet.Cell("A2").Value = "DeliveryOps Mutabakat Belgesi";
        sheet.Cell("A2").Style.Font.Bold = true;
        sheet.Cell("A2").Style.Font.FontSize = 16;
        sheet.Cell("A3").Value = settlement.DocumentNumber;
        sheet.Cell("A3").Style.Font.FontColor = XLColor.FromHtml(BrandHex);
        sheet.Cell("A3").Style.Font.Bold = true;

        sheet.Cell("A5").Value = "İşletme";
        sheet.Cell("B5").Value = settlement.BusinessName;
        sheet.Cell("D5").Value = "Kesinleşme tarihi";
        sheet.Cell("E5").Value = settlement.FinalizedAtUtc?.ToOffset(TimeSpan.FromHours(3)).DateTime;
        sheet.Cell("A6").Value = "Dönem başlangıcı";
        sheet.Cell("B6").Value = settlement.PeriodFrom.ToDateTime(TimeOnly.MinValue);
        sheet.Cell("D6").Value = "Dönem sonu";
        sheet.Cell("E6").Value = settlement.PeriodTo.ToDateTime(TimeOnly.MinValue);
        sheet.Range("E5:E6").Style.DateFormat.Format = "dd mmm yyyy";
        sheet.Cell("B6").Style.DateFormat.Format = "dd mmm yyyy";

        sheet.Cell("A9").Value = "Operasyon özeti";
        StyleSection(sheet.Range("A9:E9"));
        WriteRow(sheet, 10, ["Kalem", "Adet", "Sipariş Tutarı", "Birim / Oran", "Hesaplanan Tutar"], header: true);
        WriteRow(sheet, 11, ["Teslim edilen paket", settlement.DeliveredOrderCount,
            settlement.DeliveredOrderValue, settlement.FeePerDeliveredOrder, settlement.DeliveryFeeAmount]);
        WriteRow(sheet, 12, ["Sipariş komisyonu", settlement.DeliveredOrderCount,
            settlement.DeliveredOrderValue, settlement.CommissionRatePercent / 100m, settlement.CommissionAmount]);
        WriteRow(sheet, 13, ["İade edilen paket", settlement.ReturnedOrderCount,
            null, settlement.FeePerReturnedOrder, settlement.ReturnFeeAmount]);
        WriteRow(sheet, 14, ["İptal edilen sipariş", settlement.CancelledOrderCount, null, null, 0m]);

        sheet.Cell("D12").Style.NumberFormat.Format = "0.00%";
        sheet.Range("C11:C14").Style.NumberFormat.Format = "#,##0.00 [$₺-tr-TR]";
        sheet.Range("D11:D14").Style.NumberFormat.Format = "#,##0.00 [$₺-tr-TR]";
        sheet.Cell("D12").Style.NumberFormat.Format = "0.00%";
        sheet.Range("E11:E14").Style.NumberFormat.Format = "#,##0.00 [$₺-tr-TR]";

        sheet.Cell("D17").Value = "Ara toplam";
        sheet.Cell("E17").Value = settlement.SubtotalAmount;
        sheet.Cell("D18").Value = $"KDV (%{settlement.TaxRatePercent:N2})";
        sheet.Cell("E18").Value = settlement.TaxAmount;
        sheet.Cell("D19").Value = "Genel toplam";
        sheet.Cell("E19").Value = settlement.TotalAmount;
        sheet.Range("D19:E19").Style.Fill.BackgroundColor = XLColor.FromHtml(SoftGreenHex);
        sheet.Range("D19:E19").Style.Font.Bold = true;
        sheet.Range("E17:E19").Style.NumberFormat.Format = "#,##0.00 [$₺-tr-TR]";

        sheet.Cell("A22").Value = "Bu belge kesinleşmiş operasyon kayıtları ve tarife snapshot'ından üretilmiştir. Fatura değildir.";
        sheet.Range("A22:E22").Merge();
        sheet.Cell("A22").Style.Font.Italic = true;
        sheet.Cell("A22").Style.Font.FontColor = XLColor.Gray;
        sheet.Columns("A:E").AdjustToContents();
        sheet.Column("A").Width = 25;
        sheet.Column("B").Width = 23;
        sheet.Column("C").Width = 20;
        sheet.Column("D").Width = 22;
        sheet.Column("E").Width = 20;
        sheet.SheetView.FreezeRows(10);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.FitToPages(1, 1);
        sheet.PageSetup.PrintAreas.Add("A1:E22");

        using MemoryStream stream = new();
        workbook.SaveAs(stream, validate: true);
        return stream.ToArray();
    }

    private static void EnsureFontResolver()
    {
        if (_fontConfigured) return;
        lock (FontLock)
        {
            if (_fontConfigured) return;
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = new NotoSansFontResolver();
            _fontConfigured = true;
        }
    }

    private static void DrawInfo(XGraphics graphics, XFont valueFont, XFont labelFont,
        XBrush dark, XBrush muted, string label, string value, double x, double y)
    {
        graphics.DrawString(label.ToUpperInvariant(), labelFont, muted, new XRect(x, y, 220, 14), XStringFormats.TopLeft);
        graphics.DrawString(value, valueFont, dark, new XRect(x, y + 16, 220, 20), XStringFormats.TopLeft);
    }

    private static void DrawSectionTitle(XGraphics graphics, XFont font, XBrush brush,
        string title, double x, double y) =>
        graphics.DrawString(title, font, brush, new XRect(x, y, 300, 20), XStringFormats.TopLeft);

    private static void DrawTable(XGraphics graphics, XFont bodyFont, XFont headerFont,
        XBrush dark, XBrush muted, XBrush headerFill, XPen line, IReadOnlyList<string> headers,
        IReadOnlyList<string[]> rows, double x, double y, IReadOnlyList<double> widths)
    {
        const double rowHeight = 31;
        double cursor = x;
        for (int column = 0; column < headers.Count; column++)
        {
            graphics.DrawRectangle(headerFill, cursor, y, widths[column], rowHeight);
            graphics.DrawString(headers[column], headerFont, dark,
                new XRect(cursor + 8, y + 9, widths[column] - 16, 16), column == 0 ? XStringFormats.TopLeft : XStringFormats.TopRight);
            cursor += widths[column];
        }
        for (int row = 0; row < rows.Count; row++)
        {
            double top = y + rowHeight * (row + 1);
            cursor = x;
            for (int column = 0; column < headers.Count; column++)
            {
                graphics.DrawLine(line, cursor, top + rowHeight, cursor + widths[column], top + rowHeight);
                graphics.DrawString(rows[row][column], bodyFont, column == 0 ? dark : muted,
                    new XRect(cursor + 8, top + 9, widths[column] - 16, 16), column == 0 ? XStringFormats.TopLeft : XStringFormats.TopRight);
                cursor += widths[column];
            }
        }
    }

    private static void DrawTotalLine(XGraphics graphics, XFont font, XBrush brush,
        string label, string value, double left, double right, double y)
    {
        graphics.DrawString(label, font, brush, new XRect(left, y, 240, 18), XStringFormats.TopLeft);
        graphics.DrawString(value, font, brush, new XRect(320, y, right - 320, 18), XStringFormats.TopRight);
    }

    private static void StyleSection(IXLRange range)
    {
        range.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandHex);
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Font.Bold = true;
    }

    private static void WriteRow(IXLWorksheet sheet, int row, object?[] values, bool header = false)
    {
        for (int column = 0; column < values.Length; column++)
        {
            IXLCell cell = sheet.Cell(row, column + 1);
            switch (values[column])
            {
                case int number: cell.Value = number; break;
                case decimal number: cell.Value = number; break;
                case string text: cell.Value = text; break;
                case null: cell.Value = string.Empty; break;
            }
        }
        if (!header) return;
        IXLRange range = sheet.Range(row, 1, row, values.Length);
        range.Style.Fill.BackgroundColor = XLColor.FromHtml(SoftGreenHex);
        range.Style.Font.Bold = true;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static string Money(decimal value) =>
        value.ToString("C2", CultureInfo.GetCultureInfo("tr-TR"));

    private sealed class NotoSansFontResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new(isBold && isItalic ? "notosbi" : isBold ? "notosbo" : isItalic ? "notosit" : "notos");

        public byte[] GetFont(string faceName) => FontRegistry.Descriptors.TryGetValue(faceName, out var font)
            ? font.Loader()
            : throw new InvalidOperationException($"Font bulunamadı: {faceName}");
    }
}
