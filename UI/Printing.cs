using System.Drawing.Printing;
using PharmacyDesktop.Core;

namespace PharmacyDesktop.UI;

/// <summary>Prints the visible columns of a grid as a paginated A4 report.</summary>
public sealed class GridPrinter
{
    private readonly DataGridView _grid;
    private readonly string _title;
    private readonly string? _subtitle;
    private int _rowIndex;
    private int _page;

    public GridPrinter(DataGridView grid, string title, string? subtitle)
    {
        _grid = grid;
        _title = title;
        _subtitle = subtitle;
    }

    public void Preview()
    {
        using var doc = new PrintDocument { DocumentName = _title };
        var cols = _grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible && c is not DataGridViewButtonColumn).OrderBy(c => c.DisplayIndex).ToList();
        doc.DefaultPageSettings.Landscape = cols.Sum(c => c.Width) > 900;
        doc.DefaultPageSettings.Margins = new Margins(40, 40, 40, 40);
        doc.BeginPrint += (_, _) => { _rowIndex = 0; _page = 0; };
        doc.PrintPage += (_, e) => PrintPage(e, cols);
        using var dlg = new PrintPreviewDialog { Document = doc, Width = 1100, Height = 800, UseAntiAlias = true };
        dlg.ShowDialog();
    }

    private void PrintPage(PrintPageEventArgs e, List<DataGridViewColumn> cols)
    {
        var g = e.Graphics!;
        var area = e.MarginBounds;
        _page++;
        float y = area.Top;
        using var titleFont = new Font("Segoe UI Semibold", 15);
        using var subFont = new Font("Segoe UI", 9);
        using var headFont = new Font("Segoe UI Semibold", 8.5f);
        using var cellFont = new Font("Segoe UI", 8.5f);
        using var headBrush = new SolidBrush(Theme.Primary);
        using var linePen = new Pen(Theme.Border);

        g.DrawString(Session.Tenant?.Name ?? "PharmaDesk", subFont, Brushes.Gray, area.Left, y);
        y += 16;
        g.DrawString(_title, titleFont, Brushes.Black, area.Left, y);
        y += 30;
        var sub = (_subtitle ?? "") + $"   Printed {DateTime.Now:dd-MMM-yyyy hh:mm tt}";
        g.DrawString(sub.Trim(), subFont, Brushes.Gray, area.Left, y);
        y += 24;

        var totalW = cols.Sum(c => c.Width);
        var scale = area.Width / (float)Math.Max(1, totalW);
        var widths = cols.Select(c => c.Width * scale).ToArray();

        g.FillRectangle(headBrush, area.Left, y, area.Width, 24);
        float x = area.Left;
        var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < cols.Count; i++)
        {
            g.DrawString(cols[i].HeaderText, headFont, Brushes.White, new RectangleF(x + 3, y, widths[i] - 6, 24), fmt);
            x += widths[i];
        }
        y += 26;

        while (_rowIndex < _grid.Rows.Count)
        {
            if (y + 20 > area.Bottom - 20) break;
            var row = _grid.Rows[_rowIndex];
            x = area.Left;
            for (int i = 0; i < cols.Count; i++)
            {
                var text = row.Cells[cols[i].Index].FormattedValue?.ToString() ?? "";
                var right = cols[i].DefaultCellStyle.Alignment == DataGridViewContentAlignment.MiddleRight;
                var f = new StringFormat(fmt) { Alignment = right ? StringAlignment.Far : StringAlignment.Near };
                g.DrawString(text, cellFont, Brushes.Black, new RectangleF(x + 3, y, widths[i] - 6, 20), f);
                x += widths[i];
            }
            y += 20;
            g.DrawLine(linePen, area.Left, y, area.Right, y);
            _rowIndex++;
        }

        g.DrawString($"Page {_page}", subFont, Brushes.Gray, area.Right - 60, area.Bottom);
        e.HasMorePages = _rowIndex < _grid.Rows.Count;
    }
}

/// <summary>80mm thermal-style sales receipt.</summary>
public static class ReceiptPrinter
{
    public static void Print(Sale sale, List<SaleLine> lines, bool preview = true)
    {
        var db = Session.Db;
        var name = db.GetSetting(SettingKeys.PharmacyName, Session.Tenant.Name);
        var address = db.GetSetting(SettingKeys.Address);
        var phone = db.GetSetting(SettingKeys.Phone);
        var license = db.GetSetting(SettingKeys.LicenseNo);
        var footer = db.GetSetting(SettingKeys.ReceiptFooter, "Thank you for your visit! Get well soon.");

        // Merge lines split across batches back into one per item for the customer copy.
        var printLines = lines.GroupBy(l => new { l.ItemId, l.Rate })
                              .Select(g => (Name: g.First().ItemName, Qty: g.Sum(x => x.Qty), Rate: g.Key.Rate, Amount: g.Sum(x => x.Amount)))
                              .ToList();

        using var doc = new PrintDocument { DocumentName = sale.InvoiceNo };
        var height = 420 + printLines.Count * 34;
        doc.DefaultPageSettings.PaperSize = new PaperSize("Receipt 80mm", 315, height);
        doc.DefaultPageSettings.Margins = new Margins(10, 10, 10, 10);
        doc.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            float w = 295, x = 10, y = 10;
            using var fTitle = new Font("Segoe UI", 13, FontStyle.Bold);
            using var f = new Font("Consolas", 8.5f);
            using var fb = new Font("Consolas", 8.5f, FontStyle.Bold);
            using var fBig = new Font("Consolas", 11f, FontStyle.Bold);
            var center = new StringFormat { Alignment = StringAlignment.Center };
            var right = new StringFormat { Alignment = StringAlignment.Far };

            void Center(string text, Font font) { var h = g.MeasureString(text, font, (int)w).Height; g.DrawString(text, font, Brushes.Black, new RectangleF(x, y, w, h), center); y += h; }
            void Row(string left, string rightText, Font font) { g.DrawString(left, font, Brushes.Black, x, y); g.DrawString(rightText, font, Brushes.Black, new RectangleF(x, y, w, 16), right); y += 15; }
            void Line() { g.DrawString(new string('-', 48), f, Brushes.Black, x, y); y += 13; }

            Center(name, fTitle);
            if (!string.IsNullOrWhiteSpace(address)) Center(address, f);
            if (!string.IsNullOrWhiteSpace(phone)) Center("Ph: " + phone, f);
            if (!string.IsNullOrWhiteSpace(license)) Center("License: " + license, f);
            y += 4; Line();
            Row($"Invoice: {sale.InvoiceNo}", sale.Date.ToString("dd-MM-yy hh:mm tt"), f);
            Row($"Customer: {Trim(sale.CustomerName, 30)}", "", f);
            Row($"Cashier: {Trim(sale.CreatedBy, 30)}", "", f);
            Line();
            Row("Item", "Qty    Rate    Amount", fb);
            Line();
            foreach (var l in printLines)
            {
                g.DrawString(Trim(l.Name, 46), f, Brushes.Black, x, y); y += 14;
                Row("", $"{l.Qty,4} x {l.Rate,7:N2}  {l.Amount,9:N2}", f);
            }
            Line();
            Row("Sub Total", sale.SubTotal.ToString("N2"), f);
            if (sale.Discount > 0) Row("Discount", "-" + sale.Discount.ToString("N2"), f);
            y += 2;
            g.DrawString("TOTAL", fBig, Brushes.Black, x, y);
            g.DrawString($"{Session.Cur} {sale.GrandTotal:N2}", fBig, Brushes.Black, new RectangleF(x, y, w, 20), right);
            y += 22;
            Row($"Paid ({sale.PaymentMethod})", sale.CashReceived.ToString("N2"), f);
            if (sale.Change > 0) Row("Change", sale.Change.ToString("N2"), f);
            if (sale.Due > 0) Row("Due", sale.Due.ToString("N2"), fb);
            Line();
            y += 4;
            Center(footer, f);
            y += 6;
            Center("Powered by PharmaDesk", f);
        };

        if (preview)
        {
            using var dlg = new PrintPreviewDialog { Document = doc, Width = 480, Height = 760, UseAntiAlias = true };
            dlg.ShowDialog();
        }
        else
        {
            try { doc.Print(); }
            catch (Exception ex) { Ui.Error("Printing failed: " + ex.Message); }
        }
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
