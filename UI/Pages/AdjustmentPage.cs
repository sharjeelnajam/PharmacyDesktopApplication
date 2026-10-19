using PharmacyDesktop.Core;
using PharmacyDesktop.UI.Controls;
using PharmacyDesktop.UI.Forms;

namespace PharmacyDesktop.UI.Pages;

/// <summary>Write off damaged/expired stock, return to supplier or correct counts.</summary>
public class AdjustmentPage : PageBase
{
    public static readonly string[] Reasons = { "Expired", "Damaged / Broken", "Return to Supplier", "Stock Count Correction", "Lost / Theft", "Internal Use", "Found Extra Stock" };

    private readonly ItemSearchBox _search;
    private readonly DateTimePicker _from, _to;
    private readonly DataGridView _grid;
    private readonly Label _summary;

    public override string Title => "Stock Adjustment";
    public override string Subtitle => "Expired, damaged, supplier returns and stock-count corrections";

    private sealed record AdjRow(DateTime Date, string Item, string Batch, int Qty, decimal Value, string Reason, string Notes, string User);

    public AdjustmentPage()
    {
        var top = new Card { Dock = DockStyle.Top, Height = 150, Padding = new Padding(18, 12, 18, 12) };
        var hint = Ui.Label("Pick a medicine to adjust its stock", Theme.H3);
        hint.Location = new Point(18, 14);
        var hint2 = Ui.Label("You'll choose the batch, quantity and reason in the next step.", Theme.Small, Theme.TextMuted);
        hint2.Location = new Point(19, 40);
        _search = new ItemSearchBox { Location = new Point(18, 72), Height = 44, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right, Placeholder = "Search medicine to adjust…" };
        _search.ItemPicked += item => { if (AdjustDialog(FindForm(), item, null)) { LoadData(); MainForm.Current?.RefreshBadges(); } };
        top.Controls.AddRange(new Control[] { hint, hint2, _search });
        top.Resize += (_, _) => _search.Width = Math.Min(760, top.Width - 36);

        var filters = Ui.Toolbar();
        filters.Padding = new Padding(0, 14, 0, 0);
        _from = Ui.Date(DateTime.Today.AddMonths(-1));
        _to = Ui.Date(DateTime.Today);
        filters.Controls.Add(Ui.Field("FROM", _from));
        filters.Controls.Add(Ui.Field("TO", _to));
        _from.ValueChanged += (_, _) => LoadData();
        _to.ValueChanged += (_, _) => LoadData();

        _grid = Ui.Grid();
        _grid.DateCol("Date", "Date", 150, time: true);
        _grid.Col("Item", "Medicine", 180, fill: true);
        _grid.Col("Batch", "Batch", 100);
        _grid.Col("Qty", "Qty change", 90, "+#,##0;-#,##0;0", right: true);
        _grid.Money("Value", "Cost value", 100);
        _grid.Col("Reason", "Reason", 160);
        _grid.Col("Notes", "Notes", 180);
        _grid.Col("User", "By", 110);
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Qty" && e.Value is int q)
                e.CellStyle!.ForeColor = q < 0 ? Theme.Danger : Theme.Success;
        };
        var excel = new ModernButton("Excel", Theme.Glyph.Excel, ButtonKind.Success);
        excel.Click += (_, _) => GridHelper.ExportToExcel(_grid, "Stock Adjustments");
        var card = Ui.GridCard("Adjustment history", _grid, excel);
        _summary = (Label)card.Tag!;

        Controls.Add(card);
        Controls.Add(filters);
        Controls.Add(top);
    }

    public override void OnPageShown() => LoadData();

    private void LoadData()
    {
        var from = _from.Value.Date;
        var to = _to.Value.Date.AddDays(1);
        var rows = Session.Db.Adjustments.Rows.Where(a => a.Date >= from && a.Date < to).OrderByDescending(a => a.Date).ToList();
        _grid.Bind(rows.Select(a => new AdjRow(a.Date, a.ItemName, a.BatchNo, a.QtyChange, a.CostValue, a.Reason, a.Notes, a.CreatedBy)));
        _summary.Text = $"{rows.Count} adjustment(s) · net value {Session.Money(rows.Sum(r => r.CostValue))}";
    }

    /// <summary>Dialog to add/remove stock for one item. Returns true if saved.</summary>
    public static bool AdjustDialog(IWin32Window? owner, Item item, Batch? preselect)
    {
        var batches = Session.Db.Batches.Rows.Where(b => b.ItemId == item.Id && b.QtyRemaining > 0)
                             .OrderBy(b => b.Expiry ?? DateTime.MaxValue).ToList();
        string Text(Batch b) => $"{(b.BatchNo.Length > 0 ? b.BatchNo : "—")}  ·  Exp {b.Expiry?.ToString("MM/yyyy") ?? "n/a"}  ·  {b.QtyRemaining} units";
        const string newBatch = "➕ Add as new stock (no batch)";
        var options = batches.Select(Text).Append(newBatch).ToArray();

        using var d = new FormDialog("Adjust stock", $"{item.Name} — current stock {Session.Svc.Stock(item.Id)} units", 2, 680);
        var batchCombo = d.AddComboFixed("batch", "BATCH", options, preselect != null ? Text(preselect) : options[0]);
        d.AddComboFixed("type", "ACTION", new[] { "Remove stock (−)", "Add stock (+)" }, "Remove stock (−)");
        d.AddNumber("qty", $"QUANTITY ({item.Unit.ToLower()}s)", 1, 0, 10_000_000, 1);
        d.AddComboFixed("reason", "REASON", Reasons, preselect != null && PharmacyService.IsExpired(preselect) ? "Expired" : Reasons[1]);
        d.AddText("notes", "NOTES", "", fullRow: true);
        d.Validator = x =>
        {
            var remove = x.Str("type").StartsWith("Remove");
            var b = batches.FirstOrDefault(bb => Text(bb) == x.Str("batch"));
            if (remove && b == null) return "Choose the batch to remove stock from.";
            if (remove && b != null && x.Int("qty") > b.QtyRemaining) return $"This batch only has {b.QtyRemaining} units.";
            return null;
        };
        batchCombo.SelectedIndexChanged += (_, _) =>
        {
            if (batchCombo.SelectedItem as string == newBatch) ((ComboBox)d["type"]).SelectedIndex = 1;
        };
        if (d.ShowDialog(owner) != DialogResult.OK) return false;

        var remove = d.Str("type").StartsWith("Remove");
        var batch = batches.FirstOrDefault(b => Text(b) == d.Str("batch"));
        var qty = d.Int("qty") * (remove ? -1 : 1);
        return Ui.Try(() => Session.Svc.Adjust(item, batch, qty, d.Str("reason"), d.Str("notes")));
    }
}
