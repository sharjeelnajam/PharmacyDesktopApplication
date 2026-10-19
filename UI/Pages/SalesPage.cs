using PharmacyDesktop.Core;
using PharmacyDesktop.UI.Controls;
using PharmacyDesktop.UI.Forms;

namespace PharmacyDesktop.UI.Pages;

/// <summary>Invoice history with details, reprint and sale returns.</summary>
public class SalesPage : PageBase
{
    private readonly DateTimePicker _from, _to;
    private readonly TextBox _search;
    private readonly ComboBox _statusFilter;
    private readonly DataGridView _grid, _lines;
    private readonly StatCard _count, _total, _profit, _returns;

    public override string Title => "Sales History";
    public override string Subtitle => "Search invoices, reprint receipts and process returns";

    private sealed record SaleRow(Sale Sale, string Invoice, DateTime Date, string Customer, int Items, decimal Total, decimal Discount, decimal Returned, decimal Net, decimal Profit, string Payment, string Status, string User);
    private sealed record LineRow(string Item, string Batch, string Expiry, int Qty, decimal Rate, decimal Discount, decimal Amount, int Returned);

    public SalesPage()
    {
        var filters = new Card { Dock = DockStyle.Top, Height = 88, Padding = new Padding(18, 12, 18, 8) };
        var flow = Ui.Toolbar();
        flow.Dock = DockStyle.Fill;
        _from = Ui.Date(DateTime.Today.AddDays(-30));
        _to = Ui.Date(DateTime.Today);
        _search = Ui.TextBox("Invoice # or customer", 220);
        _statusFilter = Ui.Combo(new object[] { "All", "Paid", "Due", "Partial Return", "Returned" }, 150);
        _statusFilter.SelectedIndex = 0;
        var today = new ModernButton("Today", "", ButtonKind.Secondary) { Margin = new Padding(0, 20, 8, 0) };
        today.Click += (_, _) => { _from.Value = _to.Value = DateTime.Today; LoadData(); };
        var month = new ModernButton("This month", "", ButtonKind.Secondary) { Margin = new Padding(0, 20, 8, 0) };
        month.Click += (_, _) => { _from.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); _to.Value = DateTime.Today; LoadData(); };
        flow.Controls.Add(Ui.Field("FROM", _from));
        flow.Controls.Add(Ui.Field("TO", _to));
        flow.Controls.Add(Ui.Field("SEARCH", _search));
        flow.Controls.Add(Ui.Field("STATUS", _statusFilter));
        flow.Controls.Add(today);
        flow.Controls.Add(month);
        filters.Controls.Add(flow);
        _from.ValueChanged += (_, _) => LoadData();
        _to.ValueChanged += (_, _) => LoadData();
        _search.TextChanged += (_, _) => LoadData();
        _statusFilter.SelectedIndexChanged += (_, _) => LoadData();

        var stats = new TableLayoutPanel { Dock = DockStyle.Top, Height = 106, ColumnCount = 4, BackColor = Theme.Background, Padding = new Padding(0, 14, 0, 0) };
        for (int i = 0; i < 4; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        stats.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _count = new StatCard { Caption = "Invoices", Glyph = Theme.Glyph.Document, AccentColor = Theme.Info, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
        _total = new StatCard { Caption = "Net Sales", Glyph = Theme.Glyph.Money, AccentColor = Theme.Primary, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
        _profit = new StatCard { Caption = "Profit", Glyph = Theme.Glyph.Trending, AccentColor = Theme.Success, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
        _returns = new StatCard { Caption = "Returns", Glyph = Theme.Glyph.Undo, AccentColor = Theme.Danger, Dock = DockStyle.Fill, Margin = new Padding(0) };
        stats.Controls.Add(_count, 0, 0);
        stats.Controls.Add(_total, 1, 0);
        stats.Controls.Add(_profit, 2, 0);
        stats.Controls.Add(_returns, 3, 0);
        if (Session.IsCashier) _profit.Visible = false;

        _grid = Ui.Grid();
        _grid.Col("Invoice", "Invoice #", 110);
        _grid.DateCol("Date", "Date / Time", 150, time: true);
        _grid.Col("Customer", "Customer", 160, fill: true);
        _grid.Col("Items", "Lines", 60, "N0", right: true);
        _grid.Money("Total", "Total", 100);
        _grid.Money("Discount", "Discount", 90);
        _grid.Money("Returned", "Returned", 90);
        _grid.Money("Net", "Net", 100);
        if (!Session.IsCashier) _grid.Money("Profit", "Profit", 90);
        _grid.Col("Payment", "Payment", 100);
        _grid.Col("Status", "Status", 120);
        _grid.Col("User", "Cashier", 120);
        _grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Status" && e.Value is string s)
                Pill.Paint(e, s, s == "Paid" ? Theme.Success : s == "Due" ? Theme.Warning : Theme.Danger);
        };
        _grid.SelectionChanged += (_, _) => ShowLines();
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Reprint(); };

        var print = new ModernButton("Reprint", Theme.Glyph.Print, ButtonKind.Secondary);
        print.Click += (_, _) => Reprint();
        var ret = new ModernButton("Sale Return", Theme.Glyph.Undo, ButtonKind.Danger);
        ret.Click += (_, _) => Return();
        var excel = new ModernButton("Excel", Theme.Glyph.Excel, ButtonKind.Success);
        excel.Click += (_, _) => GridHelper.ExportToExcel(_grid, "Sales");
        var printList = new ModernButton("Print list", Theme.Glyph.Print, ButtonKind.Secondary);
        printList.Click += (_, _) => GridHelper.Print(_grid, "Sales", $"{_from.Value:dd-MMM-yyyy} to {_to.Value:dd-MMM-yyyy}");

        _lines = Ui.Grid();
        _lines.Col("Item", "Medicine", 160, fill: true);
        _lines.Col("Batch", "Batch", 100);
        _lines.Col("Expiry", "Expiry", 80);
        _lines.Col("Qty", "Qty", 60, "N0", right: true);
        _lines.Money("Rate", "Rate", 80);
        _lines.Money("Discount", "Disc.", 70);
        _lines.Money("Amount", "Amount", 90);
        _lines.Col("Returned", "Returned", 70, "N0", right: true);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Theme.Background, SplitterWidth = 14, FixedPanel = FixedPanel.Panel2 };
        split.Panel1.Controls.Add(Ui.GridCard("Invoices", _grid, excel, printList, ret, print));
        split.Panel2.Controls.Add(Ui.GridCard("Invoice items", _lines));
        split.SizeChanged += (_, _) => { try { split.SplitterDistance = Math.Max(120, (int)(split.Height * 0.58)); } catch { } };

        Controls.Add(split);
        Controls.Add(Ui.Spacer(14));
        Controls.Add(stats);
        Controls.Add(filters);
    }

    public override void OnPageShown() => LoadData();

    private void LoadData()
    {
        var db = Session.Db;
        var from = _from.Value.Date;
        var to = _to.Value.Date.AddDays(1);
        var q = _search.Text.Trim();
        var status = _statusFilter.SelectedItem as string ?? "All";
        var lineCounts = db.SaleLines.Rows.GroupBy(l => l.SaleId).ToDictionary(g => g.Key, g => g.Select(x => x.ItemId).Distinct().Count());

        var sales = db.Sales.Rows
            .Where(s => s.Date >= from && s.Date < to)
            .Where(s => q.Length == 0 || s.InvoiceNo.Contains(q, StringComparison.OrdinalIgnoreCase) || s.CustomerName.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Where(s => status == "All" || s.Status == status)
            .OrderByDescending(s => s.Date)
            .ToList();

        _grid.Bind(sales.Select(s => new SaleRow(s, s.InvoiceNo, s.Date, s.CustomerName, lineCounts.TryGetValue(s.Id, out var c) ? c : 0,
            s.SubTotal, s.Discount, s.Returned, s.GrandTotal - s.Returned, s.Profit, s.PaymentMethod, s.Status, s.CreatedBy)));

        _count.Value = sales.Count.ToString("N0");
        _count.Hint = $"{from:dd MMM} – {_to.Value:dd MMM yyyy}";
        _total.Value = Session.Money(sales.Sum(s => s.GrandTotal - s.Returned));
        _total.Hint = $"Discounts given {Session.Money(sales.Sum(s => s.Discount))}";
        _profit.Value = Session.Money(sales.Sum(s => s.Profit));
        var net = sales.Sum(s => s.GrandTotal - s.Returned);
        _profit.Hint = net > 0 ? $"Margin {sales.Sum(s => s.Profit) / net:P1}" : "";
        _returns.Value = Session.Money(sales.Sum(s => s.Returned));
        _returns.Hint = $"{sales.Count(s => s.Returned > 0)} invoice(s) with returns";
        ShowLines();
    }

    private void ShowLines()
    {
        var row = _grid.Selected<SaleRow>();
        if (row == null) { _lines.Bind(Array.Empty<LineRow>()); return; }
        _lines.Bind(Session.Svc.LinesOf(row.Sale).Select(l => new LineRow(l.ItemName, l.BatchNo, l.Expiry?.ToString("MM/yyyy") ?? "", l.Qty, l.Rate, l.Discount, l.Amount, l.ReturnedQty)));
    }

    private void Reprint()
    {
        var row = _grid.Selected<SaleRow>();
        if (row == null) return;
        ReceiptPrinter.Print(row.Sale, Session.Svc.LinesOf(row.Sale));
    }

    private void Return()
    {
        var row = _grid.Selected<SaleRow>();
        if (row == null) { Ui.Info("Select an invoice first."); return; }
        if (row.Sale.Status == "Returned") { Ui.Info("This invoice has already been fully returned."); return; }
        using var dlg = new SaleReturnDialog(row.Sale);
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
        {
            LoadData();
            MainForm.Current?.RefreshBadges();
        }
    }
}

/// <summary>Choose quantities to return for each line of an invoice.</summary>
public class SaleReturnDialog : Form
{
    private readonly Sale _sale;
    private readonly List<SaleLine> _lines;
    private readonly DataGridView _grid;
    private readonly TextBox _reason;
    private readonly Label _total;

    public SaleReturnDialog(Sale sale)
    {
        _sale = sale;
        _lines = Session.Svc.LinesOf(sale);
        Text = $"Sale Return — {sale.InvoiceNo}";
        Size = new Size(860, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        BackColor = Theme.Surface;
        Font = Theme.Body;
        ShowInTaskbar = false;

        var head = new Panel { Dock = DockStyle.Top, Height = 74, Padding = new Padding(22, 14, 22, 0), BackColor = Theme.Surface };
        var t = Ui.Label($"Return items from {sale.InvoiceNo}", Theme.H2);
        t.Location = new Point(22, 12);
        var s = Ui.Label($"{sale.CustomerName} · {sale.Date:dd-MMM-yyyy hh:mm tt} · Total {Session.Money(sale.GrandTotal)}", Theme.Small, Theme.TextMuted);
        s.Location = new Point(23, 44);
        head.Controls.Add(t);
        head.Controls.Add(s);

        _grid = Ui.Grid();
        _grid.ReadOnly = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Item", HeaderText = "Medicine", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Batch", HeaderText = "Batch", ReadOnly = true, Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Sold", HeaderText = "Sold", ReadOnly = true, Width = 70 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Prev", HeaderText = "Returned", ReadOnly = true, Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Rate", HeaderText = "Rate", ReadOnly = true, Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Return", HeaderText = "Return Qty", Width = 100 });
        _grid.Columns["Return"]!.DefaultCellStyle.BackColor = Color.FromArgb(240, 253, 250);
        _grid.Columns["Return"]!.DefaultCellStyle.Font = Theme.BodyBold;
        foreach (var l in _lines)
        {
            var i = _grid.Rows.Add(l.ItemName, l.BatchNo, l.Qty, l.ReturnedQty, l.Rate.ToString("N2"), 0);
            _grid.Rows[i].Tag = l;
            if (l.ReturnedQty >= l.Qty) _grid.Rows[i].Cells["Return"].ReadOnly = true;
        }
        _grid.CellValueChanged += (_, _) => UpdateTotal();

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 130, Padding = new Padding(22, 10, 22, 16), BackColor = Theme.SurfaceAlt };
        var rl = Ui.FieldLabel("REASON");
        rl.Location = new Point(22, 10);
        _reason = Ui.TextBox("e.g. Wrong medicine, patient returned", 420);
        _reason.Location = new Point(22, 30);
        _total = Ui.Label("Refund: " + Session.Money(0), Theme.H2, Theme.Danger);
        _total.Location = new Point(480, 28);
        var all = new ModernButton("Return all", "", ButtonKind.Secondary) { Location = new Point(22, 76) };
        all.Click += (_, _) =>
        {
            foreach (DataGridViewRow r in _grid.Rows) { var l = (SaleLine)r.Tag!; r.Cells["Return"].Value = l.Qty - l.ReturnedQty; }
        };
        var ok = new ModernButton("Process Return", Theme.Glyph.Undo, ButtonKind.Danger) { Location = new Point(640, 76), Width = 180 };
        ok.Click += (_, _) => Process();
        var cancel = new ModernButton("Cancel", "", ButtonKind.Secondary) { Location = new Point(530, 76), Width = 100 };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        bottom.Controls.AddRange(new Control[] { rl, _reason, _total, all, ok, cancel });

        var mid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 0, 22, 10), BackColor = Theme.Surface };
        _grid.Dock = DockStyle.Fill;
        mid.Controls.Add(_grid);
        Controls.Add(mid);
        Controls.Add(bottom);
        Controls.Add(head);
    }

    private Dictionary<int, int> Quantities()
    {
        var result = new Dictionary<int, int>();
        foreach (DataGridViewRow r in _grid.Rows)
        {
            var l = (SaleLine)r.Tag!;
            if (int.TryParse(r.Cells["Return"].Value?.ToString(), out var q) && q > 0) result[l.Id] = q;
        }
        return result;
    }

    private void UpdateTotal()
    {
        var ratio = _sale.SubTotal == 0 ? 0 : _sale.Discount / _sale.SubTotal;
        var total = Quantities().Sum(kv =>
        {
            var l = _lines.First(x => x.Id == kv.Key);
            return l.Qty == 0 ? 0 : l.Amount / l.Qty * Math.Min(kv.Value, l.Qty - l.ReturnedQty) * (1 - ratio);
        });
        _total.Text = "Refund: " + Session.Money(Math.Round(total, 2));
    }

    private void Process()
    {
        _grid.EndEdit();
        var qty = Quantities();
        if (qty.Count == 0) { Ui.Error("Enter a return quantity for at least one item."); return; }
        SaleReturn? ret = null;
        if (!Ui.Try(() => ret = Session.Svc.ReturnSale(_sale, qty, _reason.Text.Trim()))) return;
        Ui.Info($"Return {ret!.ReturnNo} saved.\nRefund {Session.Money(ret.Amount)} to the customer.\nStock has been added back.");
        DialogResult = DialogResult.OK;
    }
}
