using PharmacyDesktop.Core;
using PharmacyDesktop.UI.Controls;
using PharmacyDesktop.UI.Forms;

namespace PharmacyDesktop.UI.Pages;

/// <summary>List of purchases (GRNs) with their lines — the "Manage Stock" screen.</summary>
public class PurchasesPage : PageBase
{
    private readonly DateTimePicker _from, _to;
    private readonly TextBox _search;
    private readonly ComboBox _supplier;
    private readonly DataGridView _grid, _lines;
    private readonly StatCard _count, _total, _discount, _units;
    private bool _loading;

    public override string Title => "Purchases";
    public override string Subtitle => "Stock received from suppliers";

    private sealed record PurchaseRow(Purchase Purchase, string Document, DateTime Date, string Supplier, string Invoice, DateTime InvoiceDate, int Items, decimal SubTotal, decimal Discount, decimal Tax, decimal Total, string User);
    private sealed record LineRow(string Item, string Batch, DateTime? Expiry, int PackSize, int Packs, int Bonus, int Units, decimal Cost, decimal Disc, decimal Tax, decimal Net, decimal Retail, decimal Total);

    public PurchasesPage()
    {
        var filters = new Card { Dock = DockStyle.Top, Height = 88, Padding = new Padding(18, 12, 18, 8) };
        var flow = Ui.Toolbar();
        flow.Dock = DockStyle.Fill;
        _from = Ui.Date(DateTime.Today.AddMonths(-1));
        _to = Ui.Date(DateTime.Today);
        _search = Ui.TextBox("Document #, invoice # or item", 230);
        _supplier = Ui.Combo(width: 200);
        flow.Controls.Add(Ui.Field("FROM", _from));
        flow.Controls.Add(Ui.Field("TO", _to));
        flow.Controls.Add(Ui.Field("SEARCH", _search));
        flow.Controls.Add(Ui.Field("SUPPLIER", _supplier));
        var add = new ModernButton("Add Stock", Theme.Glyph.Add) { Margin = new Padding(0, 20, 8, 0) };
        add.Click += (_, _) => MainForm.Current?.Navigate("addstock");
        flow.Controls.Add(add);
        filters.Controls.Add(flow);
        _from.ValueChanged += (_, _) => LoadData();
        _to.ValueChanged += (_, _) => LoadData();
        _search.TextChanged += (_, _) => LoadData();
        _supplier.SelectedIndexChanged += (_, _) => { if (!_loading) LoadData(); };

        var stats = new TableLayoutPanel { Dock = DockStyle.Top, Height = 106, ColumnCount = 4, BackColor = Theme.Background, Padding = new Padding(0, 14, 0, 0) };
        for (int i = 0; i < 4; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        stats.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _count = new StatCard { Caption = "Purchases", Glyph = Theme.Glyph.Document, AccentColor = Theme.Info, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
        _total = new StatCard { Caption = "Net Purchase", Glyph = Theme.Glyph.Money, AccentColor = Theme.Primary, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
        _discount = new StatCard { Caption = "Discounts received", Glyph = Theme.Glyph.Tag, AccentColor = Theme.Success, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
        _units = new StatCard { Caption = "Units received", Glyph = Theme.Glyph.Package, AccentColor = Theme.Purple, Dock = DockStyle.Fill, Margin = new Padding(0) };
        stats.Controls.Add(_count, 0, 0);
        stats.Controls.Add(_total, 1, 0);
        stats.Controls.Add(_discount, 2, 0);
        stats.Controls.Add(_units, 3, 0);

        _grid = Ui.Grid();
        _grid.Col("Document", "Document #", 110);
        _grid.DateCol("Date", "Received", 140, time: true);
        _grid.Col("Supplier", "Supplier", 160, fill: true);
        _grid.Col("Invoice", "Supplier Inv #", 110);
        _grid.DateCol("InvoiceDate", "Inv. Date", 100);
        _grid.Col("Items", "SKUs", 55, "N0", right: true);
        _grid.Money("SubTotal", "Sub Total", 100);
        _grid.Money("Discount", "Discount", 90);
        _grid.Money("Tax", "Tax", 80);
        _grid.Money("Total", "Total", 110);
        _grid.Col("User", "By", 110);
        _grid.SelectionChanged += (_, _) => ShowLines();

        var del = new ModernButton("Delete", Theme.Glyph.Delete, ButtonKind.Secondary);
        del.Click += (_, _) => Delete();
        var excel = new ModernButton("Excel", Theme.Glyph.Excel, ButtonKind.Success);
        excel.Click += (_, _) => GridHelper.ExportToExcel(_grid, "Purchases");
        var print = new ModernButton("Print", Theme.Glyph.Print, ButtonKind.Secondary);
        print.Click += (_, _) => GridHelper.Print(_grid, "Purchases", $"{_from.Value:dd-MMM-yyyy} to {_to.Value:dd-MMM-yyyy}");

        _lines = Ui.Grid();
        _lines.Col("Item", "Medicine", 160, fill: true);
        _lines.Col("Batch", "Batch", 90);
        _lines.DateCol("Expiry", "Expiry", 95);
        _lines.Col("PackSize", "Units/Pack", 75, "N0", right: true);
        _lines.Col("Packs", "Packs", 60, "N0", right: true);
        _lines.Col("Bonus", "Bonus", 60, "N0", right: true);
        _lines.Col("Units", "Total Units", 80, "N0", right: true);
        _lines.Money("Cost", "Cost/Pack", 85);
        _lines.Col("Disc", "Disc %", 60, "0.##", right: true);
        _lines.Col("Tax", "Tax %", 60, "0.##", right: true);
        _lines.Money("Net", "Net Cost", 85);
        _lines.Money("Retail", "Retail/Pack", 90);
        _lines.Money("Total", "Total", 95);
        var printLines = new ModernButton("Print GRN", Theme.Glyph.Print, ButtonKind.Secondary);
        printLines.Click += (_, _) =>
        {
            var p = _grid.Selected<PurchaseRow>();
            if (p != null) GridHelper.Print(_lines, $"GRN {p.Document}", $"{p.Supplier} · Inv {p.Invoice} · Total {Session.Money(p.Total)}");
        };

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Theme.Background, SplitterWidth = 14, FixedPanel = FixedPanel.Panel2 };
        split.Panel1.Controls.Add(Ui.GridCard("Purchase documents", _grid, excel, print, del));
        split.Panel2.Controls.Add(Ui.GridCard("Items in document", _lines, printLines));
        split.SizeChanged += (_, _) => { try { split.SplitterDistance = Math.Max(120, (int)(split.Height * 0.58)); } catch { } };

        Controls.Add(split);
        Controls.Add(Ui.Spacer(14));
        Controls.Add(stats);
        Controls.Add(filters);
    }

    public override void OnPageShown()
    {
        _loading = true;
        var current = _supplier.SelectedItem as string;
        _supplier.Items.Clear();
        _supplier.Items.Add("All");
        foreach (var n in Session.Db.Purchases.Rows.Select(p => p.SupplierName).Distinct().OrderBy(n => n)) _supplier.Items.Add(n);
        _supplier.SelectedItem = current != null && _supplier.Items.Contains(current) ? current : "All";
        _loading = false;
        LoadData();
    }

    private void LoadData()
    {
        var db = Session.Db;
        var from = _from.Value.Date;
        var to = _to.Value.Date.AddDays(1);
        var q = _search.Text.Trim();
        var sup = _supplier.SelectedItem as string ?? "All";
        var itemMatches = q.Length == 0 ? new HashSet<int>()
            : db.PurchaseLines.Rows.Where(l => l.ItemName.Contains(q, StringComparison.OrdinalIgnoreCase)).Select(l => l.PurchaseId).ToHashSet();

        var list = db.Purchases.Rows
            .Where(p => p.Date >= from && p.Date < to)
            .Where(p => sup == "All" || p.SupplierName == sup)
            .Where(p => q.Length == 0 || p.DocumentNo.Contains(q, StringComparison.OrdinalIgnoreCase) || p.SupplierInvoiceNo.Contains(q, StringComparison.OrdinalIgnoreCase) || itemMatches.Contains(p.Id))
            .OrderByDescending(p => p.Date)
            .ToList();
        _grid.Bind(list.Select(p => new PurchaseRow(p, p.DocumentNo, p.Date, p.SupplierName, p.SupplierInvoiceNo, p.SupplierInvoiceDate, p.ItemCount, p.SubTotal, p.Discount, p.SalesTax, p.GrandTotal, p.CreatedBy)));

        var ids = list.Select(p => p.Id).ToHashSet();
        _count.Value = list.Count.ToString("N0");
        _count.Hint = $"{list.Select(p => p.SupplierName).Distinct().Count()} supplier(s)";
        _total.Value = Session.Money(list.Sum(p => p.GrandTotal));
        _total.Hint = $"Tax {Session.Money(list.Sum(p => p.SalesTax))}";
        var lineDisc = db.PurchaseLines.Rows.Where(l => ids.Contains(l.PurchaseId)).Sum(l => (l.UnitCost * l.Packs) * l.DiscountPct / 100);
        _discount.Value = Session.Money(list.Sum(p => p.Discount) + lineDisc);
        _discount.Hint = "Invoice + line discounts";
        _units.Value = db.PurchaseLines.Rows.Where(l => ids.Contains(l.PurchaseId)).Sum(l => l.QtyUnits).ToString("N0");
        _units.Hint = "Including bonus";
        ShowLines();
    }

    private void ShowLines()
    {
        var p = _grid.Selected<PurchaseRow>();
        if (p == null) { _lines.Bind(Array.Empty<LineRow>()); return; }
        _lines.Bind(Session.Db.PurchaseLines.Rows.Where(l => l.PurchaseId == p.Purchase.Id)
            .Select(l => new LineRow(l.ItemName, l.BatchNo, l.Expiry, l.PackSize, l.Packs, l.BonusPacks, l.QtyUnits, l.UnitCost, l.DiscountPct, l.TaxPct, l.NetUnitCost, l.RetailPrice, l.Total)));
    }

    private void Delete()
    {
        var p = _grid.Selected<PurchaseRow>();
        if (p == null) return;
        if (!Session.IsAdmin) { Ui.Error("Only an Admin can delete purchases."); return; }
        if (!Ui.Confirm($"Delete purchase {p.Document}? Its stock will be removed.")) return;
        if (Ui.Try(() => Session.Svc.DeletePurchase(p.Purchase))) { LoadData(); MainForm.Current?.RefreshBadges(); }
    }
}
