using PharmacyDesktop.Core;
using PharmacyDesktop.UI.Controls;

namespace PharmacyDesktop.UI.Pages;

/// <summary>Batch-level stock with expiry tracking.</summary>
public class StockExpiryPage : PageBase
{
    private readonly TextBox _search;
    private readonly ComboBox _filter;
    private readonly DataGridView _grid;
    private readonly StatCard _batches, _expired, _soon, _value;

    public override string Title => "Stock & Expiry";
    public override string Subtitle => "Every batch in stock — first expiry is sold first";

    private sealed record BatchRow(Batch Batch, Item? ItemRef, string Item, string BatchNo, DateTime? Expiry, int DaysLeft, int Qty, string Stock, decimal CostUnit, decimal Value, decimal RetailValue, string Supplier, DateTime Received, string State);

    public StockExpiryPage()
    {
        var filters = new Card { Dock = DockStyle.Top, Height = 88, Padding = new Padding(18, 12, 18, 8) };
        var flow = Ui.Toolbar();
        flow.Dock = DockStyle.Fill;
        _search = Ui.TextBox("Medicine or batch #", 240);
        _filter = Ui.Combo(new object[] { "All batches in stock", "Expired", "Expiring in 30 days", "Expiring in 60 days", "Expiring in 90 days", "Expiring in 180 days", "No expiry entered" }, 220);
        _filter.SelectedIndex = 0;
        flow.Controls.Add(Ui.Field("SEARCH", _search));
        flow.Controls.Add(Ui.Field("SHOW", _filter));
        filters.Controls.Add(flow);
        _search.TextChanged += (_, _) => LoadData();
        _filter.SelectedIndexChanged += (_, _) => LoadData();

        var stats = new TableLayoutPanel { Dock = DockStyle.Top, Height = 106, ColumnCount = 4, BackColor = Theme.Background, Padding = new Padding(0, 14, 0, 0) };
        for (int i = 0; i < 4; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        stats.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _batches = new StatCard { Caption = "Batches in stock", Glyph = Theme.Glyph.Package, AccentColor = Theme.Info, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
        _expired = new StatCard { Caption = "Expired", Glyph = Theme.Glyph.Delete, AccentColor = Theme.Danger, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
        _soon = new StatCard { Caption = "Expiring soon", Glyph = Theme.Glyph.Clock, AccentColor = Theme.Warning, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
        _value = new StatCard { Caption = "Stock value (cost)", Glyph = Theme.Glyph.Money, AccentColor = Theme.Primary, Dock = DockStyle.Fill, Margin = new Padding(0) };
        stats.Controls.Add(_batches, 0, 0);
        stats.Controls.Add(_expired, 1, 0);
        stats.Controls.Add(_soon, 2, 0);
        stats.Controls.Add(_value, 3, 0);

        _grid = Ui.Grid();
        _grid.Col("Item", "Medicine", 200, fill: true);
        _grid.Col("BatchNo", "Batch #", 100);
        _grid.DateCol("Expiry", "Expiry", 105);
        _grid.Col("DaysLeft", "Days left", 80, "N0", right: true);
        _grid.Col("Stock", "Qty (units)", 130);
        _grid.Money("CostUnit", "Cost/unit", 85);
        _grid.Money("Value", "Cost value", 100);
        _grid.Money("RetailValue", "Retail value", 105);
        _grid.Col("Supplier", "Supplier", 140);
        _grid.DateCol("Received", "Received", 100);
        _grid.Col("State", "Status", 110);
        _grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "State" && e.Value is string s)
                Pill.Paint(e, s, s == "Expired" ? Theme.Danger : s == "Near expiry" ? Theme.Warning : s == "No expiry" ? Theme.TextMuted : Theme.Success);
        };
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "DaysLeft") return;
            if (_grid.Rows[e.RowIndex].DataBoundItem is BatchRow r)
            {
                if (!r.Expiry.HasValue) { e.Value = "—"; e.FormattingApplied = true; }
                else e.CellStyle!.ForeColor = r.DaysLeft < 0 ? Theme.Danger : r.DaysLeft <= 90 ? Theme.Darken(Theme.Warning, 0.2f) : Theme.Text;
            }
        };

        var adjust = new ModernButton("Adjust / Remove", Theme.Glyph.Sync, ButtonKind.Secondary);
        adjust.Click += (_, _) =>
        {
            var r = _grid.Selected<BatchRow>();
            if (r?.ItemRef == null) return;
            if (AdjustmentPage.AdjustDialog(FindForm(), r.ItemRef, r.Batch)) { LoadData(); Forms.MainForm.Current?.RefreshBadges(); }
        };
        var excel = new ModernButton("Excel", Theme.Glyph.Excel, ButtonKind.Success);
        excel.Click += (_, _) => GridHelper.ExportToExcel(_grid, "Stock & Expiry");
        var print = new ModernButton("Print", Theme.Glyph.Print, ButtonKind.Secondary);
        print.Click += (_, _) => GridHelper.Print(_grid, "Stock & Expiry", _filter.SelectedItem as string);

        Controls.Add(Ui.GridCard("Batches", _grid, excel, print, adjust));
        Controls.Add(Ui.Spacer(14));
        Controls.Add(stats);
        Controls.Add(filters);
    }

    public override void OnPageShown() => LoadData();

    private void LoadData()
    {
        var db = Session.Db;
        var items = db.Items.Rows.ToDictionary(i => i.Id);
        var suppliers = db.Purchases.Rows.ToDictionary(p => p.Id, p => p.SupplierName);
        var alertDays = db.GetSettingInt(SettingKeys.ExpiryAlertDays, 90);
        var q = _search.Text.Trim();

        var all = db.Batches.Rows.Where(b => b.QtyRemaining > 0).Select(b =>
        {
            items.TryGetValue(b.ItemId, out var it);
            var days = b.Expiry.HasValue ? (int)(b.Expiry.Value.Date - DateTime.Today).TotalDays : int.MaxValue;
            var state = !b.Expiry.HasValue ? "No expiry" : days < 0 ? "Expired" : days <= alertDays ? "Near expiry" : "Good";
            return new BatchRow(b, it, it?.Name ?? "(deleted item)", b.BatchNo, b.Expiry, days, b.QtyRemaining,
                PharmacyService.FormatQty(b.QtyRemaining, it?.PackSize ?? 1), Math.Round(b.CostPerUnit, 2), Math.Round(b.QtyRemaining * b.CostPerUnit, 2),
                Math.Round(b.QtyRemaining * (it?.UnitPrice ?? b.RetailPerUnit), 2), suppliers.TryGetValue(b.PurchaseId, out var s) ? s : "Adjustment", b.ReceivedAt, state);
        }).ToList();

        _batches.Value = all.Count.ToString("N0");
        _batches.Hint = $"{all.Select(r => r.Batch.ItemId).Distinct().Count()} medicine(s)";
        var expired = all.Where(r => r.State == "Expired").ToList();
        _expired.Value = expired.Count.ToString("N0");
        _expired.Hint = $"Loss value {Session.Money(expired.Sum(r => r.Value))}";
        var soon = all.Where(r => r.State == "Near expiry").ToList();
        _soon.Value = soon.Count.ToString("N0");
        _soon.Hint = $"Within {alertDays} days · {Session.Money(soon.Sum(r => r.Value))}";
        _value.Value = Session.Money(all.Sum(r => r.Value));
        _value.Hint = $"Retail {Session.Money(all.Sum(r => r.RetailValue))}";

        var f = _filter.SelectedIndex;
        int[] windows = { 0, 0, 30, 60, 90, 180 };
        var rows = all
            .Where(r => q.Length == 0 || r.Item.Contains(q, StringComparison.OrdinalIgnoreCase) || r.BatchNo.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Where(r => f switch
            {
                1 => r.State == "Expired",
                2 or 3 or 4 or 5 => r.Expiry.HasValue && r.DaysLeft >= 0 && r.DaysLeft <= windows[f],
                6 => !r.Expiry.HasValue,
                _ => true
            })
            .OrderBy(r => r.Expiry ?? DateTime.MaxValue).ThenBy(r => r.Item)
            .ToList();
        _grid.Bind(rows);
    }
}
