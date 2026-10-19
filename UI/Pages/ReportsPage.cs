using System.Data;
using PharmacyDesktop.Core;
using PharmacyDesktop.UI.Controls;

namespace PharmacyDesktop.UI.Pages;

/// <summary>Ready-made business reports with Excel export and printing.</summary>
public class ReportsPage : PageBase
{
    private sealed record Report(string Name, string Glyph, string Description, bool UsesDates, Func<DateTime, DateTime, (DataTable table, string summary)> Build);

    private readonly List<Report> _reports;
    private readonly ListBox _list;
    private readonly DateTimePicker _from, _to;
    private readonly DataGridView _grid;
    private readonly Label _title, _desc, _summary;
    private DataTable? _current;

    public override string Title => "Reports";
    public override string Subtitle => "Sales, profit, purchases and stock analysis";

    public ReportsPage()
    {
        _reports = new List<Report>
        {
            new("Profit & Loss", Theme.Glyph.Money, "Sales, cost of goods sold, stock losses and net profit for the period.", true, ProfitLoss),
            new("Daily Sales Summary", Theme.Glyph.Calendar, "Invoices, discounts, returns, net sales and profit per day.", true, DailySales),
            new("Item-wise Sales", Theme.Glyph.Package, "Quantity sold, revenue and profit per medicine.", true, ItemSales),
            new("Sales by Payment Method", Theme.Glyph.Money, "Cash, card, online and credit totals.", true, ByPayment),
            new("Sales by User", Theme.Glyph.Person, "Performance of each cashier / pharmacist.", true, ByUser),
            new("Credit / Due Invoices", Theme.Glyph.Warning, "Invoices with outstanding balances.", true, Dues),
            new("Sale Returns", Theme.Glyph.Undo, "All returns processed in the period.", true, Returns),
            new("Purchases by Supplier", Theme.Glyph.Work, "Purchase totals, discounts and taxes per supplier.", true, PurchasesBySupplier),
            new("Stock Valuation", Theme.Glyph.Chart, "Current stock at cost and retail with potential profit.", false, StockValuation),
            new("Re-order List", Theme.Glyph.Cart, "Items at or below re-order level with suggested order quantity.", false, Reorder),
            new("Expiry Report", Theme.Glyph.Clock, "Batches expiring between the selected dates (use future dates).", true, Expiry),
            new("Stock Adjustments", Theme.Glyph.Sync, "Write-offs and corrections, grouped by reason.", true, Adjustments),
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Background };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var listCard = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 0), Padding = new Padding(10, 14, 10, 10) };
        var listTitle = Ui.Label("  Choose a report", Theme.H3);
        listTitle.Dock = DockStyle.Top;
        listTitle.Height = 36;
        listTitle.AutoSize = false;
        _list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 44, IntegralHeight = false };
        _list.Items.AddRange(_reports.Cast<object>().ToArray());
        _list.DrawItem += DrawReportItem;
        _list.SelectedIndexChanged += (_, _) => Generate();
        listCard.Controls.Add(_list);
        listCard.Controls.Add(listTitle);
        layout.Controls.Add(listCard, 0, 0);

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        var head = new Card { Dock = DockStyle.Top, Height = 128, Padding = new Padding(18, 12, 18, 12) };
        _title = Ui.Label("", Theme.H2);
        _title.Location = new Point(18, 12);
        _desc = Ui.Label("", Theme.Small, Theme.TextMuted);
        _desc.Location = new Point(19, 42);
        _from = Ui.Date(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), 140);
        _to = Ui.Date(DateTime.Today, 140);
        var fromP = Ui.Field("FROM", _from);
        var toP = Ui.Field("TO", _to);
        _from.ValueChanged += (_, _) => Generate();
        _to.ValueChanged += (_, _) => Generate();

        var presets = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Surface, WrapContents = false, Location = new Point(18, 94) };
        void Preset(string text, Func<(DateTime, DateTime)> range)
        {
            var b = new LinkLabel { Text = text, AutoSize = true, LinkColor = Theme.Primary, Font = Theme.SmallBold, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 0, 14, 0) };
            b.LinkClicked += (_, _) => { var (f, t) = range(); _from.Value = f; _to.Value = t; };
            presets.Controls.Add(b);
        }
        var today = DateTime.Today;
        Preset("Today", () => (today, today));
        Preset("Yesterday", () => (today.AddDays(-1), today.AddDays(-1)));
        Preset("Last 7 days", () => (today.AddDays(-6), today));
        Preset("This month", () => (new DateTime(today.Year, today.Month, 1), today));
        Preset("Last month", () => (new DateTime(today.Year, today.Month, 1).AddMonths(-1), new DateTime(today.Year, today.Month, 1).AddDays(-1)));
        Preset("This year", () => (new DateTime(today.Year, 1, 1), today));
        Preset("Next 90 days", () => (today, today.AddDays(90)));

        head.Controls.AddRange(new Control[] { _title, _desc, fromP, toP, presets });
        head.Resize += (_, _) =>
        {
            toP.Location = new Point(head.Width - 18 - toP.Width, 14);
            fromP.Location = new Point(toP.Left - 10 - fromP.Width, 14);
            _desc.MaximumSize = new Size(Math.Max(200, fromP.Left - 30), 0);
            _title.MaximumSize = new Size(Math.Max(200, fromP.Left - 30), 0);
        };

        _grid = Ui.Grid();
        _grid.AutoGenerateColumns = true;
        _grid.DataBindingComplete += (_, _) => FormatColumns();
        var excel = new ModernButton("Excel", Theme.Glyph.Excel, ButtonKind.Success);
        excel.Click += (_, _) => { if (_current != null) GridHelper.ExportToExcel(_grid, _title.Text); };
        var print = new ModernButton("Print", Theme.Glyph.Print, ButtonKind.Secondary);
        print.Click += (_, _) => { if (_current != null) GridHelper.Print(_grid, _title.Text, RangeText + "   " + _summary.Text); };
        var card = Ui.GridCard("Result", _grid, excel, print);
        _summary = (Label)card.Tag!;
        _summary.Font = Theme.SmallBold;
        _summary.ForeColor = Theme.PrimaryDark;

        right.Controls.Add(card);
        right.Controls.Add(Ui.Spacer(14));
        right.Controls.Add(head);
        layout.Controls.Add(right, 1, 0);
        Controls.Add(layout);
    }

    private string RangeText => SelectedReport?.UsesDates == true ? $"{_from.Value:dd-MMM-yyyy} to {_to.Value:dd-MMM-yyyy}" : $"As of {DateTime.Now:dd-MMM-yyyy}";
    private Report? SelectedReport => _list.SelectedItem as Report;

    public override void OnPageShown()
    {
        if (_list.SelectedIndex < 0) _list.SelectedIndex = 0;
        else Generate();
    }

    private void Generate()
    {
        var r = SelectedReport;
        if (r == null) return;
        _title.Text = r.Name;
        _desc.Text = r.Description;
        _from.Enabled = _to.Enabled = r.UsesDates;
        Cursor = Cursors.WaitCursor;
        try
        {
            var (table, summary) = r.Build(_from.Value.Date, _to.Value.Date.AddDays(1));
            _current = table;
            _grid.DataSource = null;
            _grid.Columns.Clear();
            _grid.DataSource = table;
            _summary.Text = summary;
        }
        finally { Cursor = Cursors.Default; }
    }

    private void FormatColumns()
    {
        foreach (DataGridViewColumn c in _grid.Columns)
        {
            c.SortMode = DataGridViewColumnSortMode.Automatic;
            c.MinimumWidth = 70;
            var type = _current?.Columns[c.DataPropertyName]?.DataType;
            if (type == typeof(decimal)) { c.DefaultCellStyle.Format = "N2"; Right(c); c.Width = 115; }
            else if (type == typeof(int)) { c.DefaultCellStyle.Format = "N0"; Right(c); c.Width = 90; }
            else if (type == typeof(DateTime)) { c.DefaultCellStyle.Format = "dd-MMM-yyyy"; c.Width = 110; }
            else c.Width = 140;
        }
        var first = _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => _current?.Columns[c.DataPropertyName]?.DataType == typeof(string));
        if (first != null) { first.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; first.MinimumWidth = 180; }

        static void Right(DataGridViewColumn c)
        {
            c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
    }

    private void DrawReportItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var r = (Report)_list.Items[e.Index];
        var g = e.Graphics;
        var selected = (e.State & DrawItemState.Selected) != 0;
        g.FillRectangle(Brushes.White, e.Bounds);
        if (selected)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = Theme.RoundedRect(new RectangleF(e.Bounds.X + 2, e.Bounds.Y + 3, e.Bounds.Width - 4, e.Bounds.Height - 6), 8);
            using var b = new SolidBrush(Theme.PrimarySoft);
            g.FillPath(b, path);
        }
        var color = selected ? Theme.PrimaryDark : Theme.Text;
        using var f = Theme.Icon(12);
        Txt.Draw(g, r.Glyph, f, new Rectangle(e.Bounds.X + 10, e.Bounds.Y, 26, e.Bounds.Height), selected ? Theme.Primary : Theme.TextMuted, TextFormatFlags.VerticalCenter);
        Txt.Draw(g, r.Name, selected ? Theme.BodyBold : Theme.Body, new Rectangle(e.Bounds.X + 42, e.Bounds.Y, e.Bounds.Width - 46, e.Bounds.Height), color, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    // ───────────────────────────── Report builders ─────────────────────────────

    private static DataTable Table(params (string name, Type type)[] cols)
    {
        var t = new DataTable();
        foreach (var (n, ty) in cols) t.Columns.Add(n, ty);
        return t;
    }

    private static string M(decimal v) => Session.Money(v);
    private static TenantDb Db => Session.Db;
    private static List<Sale> SalesIn(DateTime from, DateTime to) => Db.Sales.Rows.Where(s => s.Date >= from && s.Date < to).ToList();

    private (DataTable, string) ProfitLoss(DateTime from, DateTime to)
    {
        var sales = SalesIn(from, to);
        var gross = sales.Sum(s => s.SubTotal);
        var discount = sales.Sum(s => s.Discount);
        var returns = Db.SaleReturns.Rows.Where(r => r.Date >= from && r.Date < to).Sum(r => r.Amount);
        var net = sales.Sum(s => s.GrandTotal) - returns;
        var ids = sales.Select(s => s.Id).ToHashSet();
        var cogs = Db.SaleLines.Rows.Where(l => ids.Contains(l.SaleId)).Sum(l => (l.Qty - l.ReturnedQty) * l.CostPerUnit);
        var adjustments = Db.Adjustments.Rows.Where(a => a.Date >= from && a.Date < to).ToList();
        var losses = -adjustments.Where(a => a.QtyChange < 0).Sum(a => a.CostValue);
        var gains = adjustments.Where(a => a.QtyChange > 0).Sum(a => a.CostValue);
        var purchases = Db.Purchases.Rows.Where(p => p.Date >= from && p.Date < to).Sum(p => p.GrandTotal);

        var t = Table(("Particulars", typeof(string)), ("Amount", typeof(decimal)));
        t.Rows.Add("Gross sales", gross);
        t.Rows.Add("Less: discounts", -discount);
        t.Rows.Add("Less: sale returns", -returns);
        t.Rows.Add("Net sales", net);
        t.Rows.Add("Less: cost of goods sold", -Math.Round(cogs, 2));
        t.Rows.Add("Gross profit", Math.Round(net - cogs, 2));
        t.Rows.Add("Less: stock losses (expired / damaged / lost)", -losses);
        t.Rows.Add("Add: stock gains (found / corrections)", gains);
        t.Rows.Add("NET PROFIT", Math.Round(net - cogs - losses + gains, 2));
        t.Rows.Add("", DBNull.Value);
        t.Rows.Add("Purchases in period (for reference)", purchases);
        var np = net - cogs - losses + gains;
        return (t, $"{sales.Count} invoices · net profit {M(Math.Round(np, 2))}" + (net > 0 ? $"  ·  margin {np / net:P1}" : ""));
    }

    private (DataTable, string) DailySales(DateTime from, DateTime to)
    {
        var t = Table(("Date", typeof(DateTime)), ("Invoices", typeof(int)), ("Gross", typeof(decimal)), ("Discount", typeof(decimal)),
            ("Returns", typeof(decimal)), ("Net Sales", typeof(decimal)), ("Cost", typeof(decimal)), ("Profit", typeof(decimal)));
        var rows = SalesIn(from, to).GroupBy(s => s.Date.Date).OrderBy(g => g.Key).ToList();
        foreach (var g in rows)
        {
            var net = g.Sum(s => s.GrandTotal - s.Returned);
            t.Rows.Add(g.Key, g.Count(), g.Sum(s => s.SubTotal), g.Sum(s => s.Discount), g.Sum(s => s.Returned), net, net - g.Sum(s => s.Profit), g.Sum(s => s.Profit));
        }
        var all = rows.SelectMany(g => g).ToList();
        return (t, $"{all.Count} invoices · net {M(all.Sum(s => s.GrandTotal - s.Returned))} · profit {M(all.Sum(s => s.Profit))}");
    }

    private (DataTable, string) ItemSales(DateTime from, DateTime to)
    {
        var ids = SalesIn(from, to).Select(s => s.Id).ToHashSet();
        var items = Db.Items.Rows.ToDictionary(i => i.Id);
        var t = Table(("Medicine", typeof(string)), ("Category", typeof(string)), ("Qty Sold", typeof(int)), ("Returned", typeof(int)),
            ("Net Qty", typeof(int)), ("Sales", typeof(decimal)), ("Cost", typeof(decimal)), ("Profit", typeof(decimal)), ("Margin %", typeof(decimal)));
        decimal totalSales = 0, totalProfit = 0;
        foreach (var g in Db.SaleLines.Rows.Where(l => ids.Contains(l.SaleId)).GroupBy(l => l.ItemId)
                     .Select(g => new
                     {
                         Name = g.First().ItemName,
                         Qty = g.Sum(x => x.Qty),
                         Ret = g.Sum(x => x.ReturnedQty),
                         Sales = g.Sum(x => x.Qty == 0 ? 0 : x.Amount * (x.Qty - x.ReturnedQty) / x.Qty),
                         Cost = g.Sum(x => (x.Qty - x.ReturnedQty) * x.CostPerUnit),
                         Cat = items.TryGetValue(g.Key, out var it) ? it.Category : ""
                     })
                     .OrderByDescending(x => x.Sales))
        {
            var profit = g.Sales - g.Cost;
            totalSales += g.Sales;
            totalProfit += profit;
            t.Rows.Add(g.Name, g.Cat, g.Qty, g.Ret, g.Qty - g.Ret, Math.Round(g.Sales, 2), Math.Round(g.Cost, 2), Math.Round(profit, 2), g.Sales == 0 ? 0 : Math.Round(profit / g.Sales * 100, 1));
        }
        return (t, $"{t.Rows.Count} medicines · sales {M(Math.Round(totalSales, 2))} · profit {M(Math.Round(totalProfit, 2))}");
    }

    private (DataTable, string) ByPayment(DateTime from, DateTime to)
    {
        var t = Table(("Payment Method", typeof(string)), ("Invoices", typeof(int)), ("Net Sales", typeof(decimal)), ("Received", typeof(decimal)), ("Due", typeof(decimal)));
        var sales = SalesIn(from, to);
        foreach (var g in sales.GroupBy(s => s.PaymentMethod).OrderByDescending(g => g.Sum(s => s.GrandTotal)))
            t.Rows.Add(g.Key, g.Count(), g.Sum(s => s.GrandTotal - s.Returned), g.Sum(s => s.CashReceived - s.Change), g.Sum(s => s.Due));
        return (t, $"Total {M(sales.Sum(s => s.GrandTotal - s.Returned))}");
    }

    private (DataTable, string) ByUser(DateTime from, DateTime to)
    {
        var t = Table(("User", typeof(string)), ("Invoices", typeof(int)), ("Net Sales", typeof(decimal)), ("Discount Given", typeof(decimal)), ("Profit", typeof(decimal)), ("Avg Invoice", typeof(decimal)));
        foreach (var g in SalesIn(from, to).GroupBy(s => s.CreatedBy).OrderByDescending(g => g.Sum(s => s.GrandTotal)))
        {
            var net = g.Sum(s => s.GrandTotal - s.Returned);
            t.Rows.Add(g.Key, g.Count(), net, g.Sum(s => s.Discount), g.Sum(s => s.Profit), Math.Round(net / g.Count(), 2));
        }
        return (t, $"{t.Rows.Count} user(s)");
    }

    private (DataTable, string) Dues(DateTime from, DateTime to)
    {
        var t = Table(("Customer", typeof(string)), ("Invoice", typeof(string)), ("Date", typeof(DateTime)), ("Total", typeof(decimal)), ("Paid", typeof(decimal)), ("Due", typeof(decimal)));
        var list = SalesIn(from, to).Where(s => s.Due > 0).OrderBy(s => s.CustomerName).ThenBy(s => s.Date).ToList();
        foreach (var s in list) t.Rows.Add(s.CustomerName, s.InvoiceNo, s.Date, s.GrandTotal, s.CashReceived, s.Due);
        return (t, $"{list.Count} invoice(s) · outstanding {M(list.Sum(s => s.Due))}");
    }

    private (DataTable, string) Returns(DateTime from, DateTime to)
    {
        var t = Table(("Reason", typeof(string)), ("Return #", typeof(string)), ("Invoice", typeof(string)), ("Date", typeof(DateTime)), ("Amount", typeof(decimal)), ("By", typeof(string)));
        var list = Db.SaleReturns.Rows.Where(r => r.Date >= from && r.Date < to).OrderByDescending(r => r.Date).ToList();
        foreach (var r in list) t.Rows.Add(r.Reason, r.ReturnNo, r.InvoiceNo, r.Date, r.Amount, r.CreatedBy);
        return (t, $"{list.Count} return(s) · refunded {M(list.Sum(r => r.Amount))}");
    }

    private (DataTable, string) PurchasesBySupplier(DateTime from, DateTime to)
    {
        var t = Table(("Supplier", typeof(string)), ("Documents", typeof(int)), ("Sub Total", typeof(decimal)), ("Discount", typeof(decimal)), ("Tax", typeof(decimal)), ("Net Purchase", typeof(decimal)), ("Last Purchase", typeof(DateTime)));
        var list = Db.Purchases.Rows.Where(p => p.Date >= from && p.Date < to).ToList();
        foreach (var g in list.GroupBy(p => p.SupplierName).OrderByDescending(g => g.Sum(p => p.GrandTotal)))
            t.Rows.Add(g.Key, g.Count(), g.Sum(p => p.SubTotal), g.Sum(p => p.Discount), g.Sum(p => p.SalesTax), g.Sum(p => p.GrandTotal), g.Max(p => p.Date));
        return (t, $"{list.Count} purchase(s) · {M(list.Sum(p => p.GrandTotal))}");
    }

    private (DataTable, string) StockValuation(DateTime _, DateTime __)
    {
        var t = Table(("Medicine", typeof(string)), ("Category", typeof(string)), ("Units", typeof(int)), ("Cost Value", typeof(decimal)), ("Retail Value", typeof(decimal)), ("Potential Profit", typeof(decimal)));
        var items = Db.Items.Rows.ToDictionary(i => i.Id);
        decimal cost = 0, retail = 0;
        foreach (var g in Db.Batches.Rows.Where(b => b.QtyRemaining > 0).GroupBy(b => b.ItemId))
        {
            if (!items.TryGetValue(g.Key, out var it)) continue;
            var units = g.Sum(b => b.QtyRemaining);
            var c = Math.Round(g.Sum(b => b.QtyRemaining * b.CostPerUnit), 2);
            var r = Math.Round(units * it.UnitPrice, 2);
            cost += c; retail += r;
            t.Rows.Add(it.Name, it.Category, units, c, r, r - c);
        }
        t.DefaultView.Sort = "[Cost Value] DESC";
        return (t.DefaultView.ToTable(), $"Cost {M(cost)} · retail {M(retail)} · potential profit {M(retail - cost)}");
    }

    private (DataTable, string) Reorder(DateTime _, DateTime __)
    {
        var t = Table(("Medicine", typeof(string)), ("Manufacturer", typeof(string)), ("Stock Units", typeof(int)), ("Re-order Level", typeof(int)), ("Suggested Packs", typeof(int)), ("Last Supplier", typeof(string)), ("Last Cost/Pack", typeof(decimal)));
        var stock = Session.Svc.StockMap();
        var lastSupplier = Db.PurchaseLines.Rows.OrderBy(l => l.Id).GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => Db.Purchases.Find(g.Last().PurchaseId)?.SupplierName ?? "");
        foreach (var it in Db.Items.Rows.Where(i => i.IsActive).OrderBy(i => i.Name))
        {
            var s = stock.TryGetValue(it.Id, out var v) ? v : 0;
            var level = Session.Svc.ReorderLevelFor(it);
            if (s > level) continue;
            var pack = Math.Max(1, it.PackSize);
            var suggest = (int)Math.Ceiling((level * 2 - s) / (double)pack);
            t.Rows.Add(it.Name, it.Manufacturer, s, level, Math.Max(1, suggest), lastSupplier.TryGetValue(it.Id, out var sup) ? sup : "", it.PurchasePrice);
        }
        return (t, $"{t.Rows.Count} item(s) need re-ordering");
    }

    private (DataTable, string) Expiry(DateTime from, DateTime to)
    {
        var t = Table(("Medicine", typeof(string)), ("Batch", typeof(string)), ("Expiry", typeof(DateTime)), ("Days Left", typeof(int)), ("Units", typeof(int)), ("Cost Value", typeof(decimal)));
        var items = Db.Items.Rows.ToDictionary(i => i.Id, i => i.Name);
        var list = Db.Batches.Rows.Where(b => b.QtyRemaining > 0 && b.Expiry.HasValue && b.Expiry.Value >= from && b.Expiry.Value < to).OrderBy(b => b.Expiry).ToList();
        foreach (var b in list)
            t.Rows.Add(items.TryGetValue(b.ItemId, out var n) ? n : "", b.BatchNo, b.Expiry, (int)(b.Expiry!.Value.Date - DateTime.Today).TotalDays, b.QtyRemaining, Math.Round(b.QtyRemaining * b.CostPerUnit, 2));
        return (t, $"{list.Count} batch(es) · value {M(list.Sum(b => b.QtyRemaining * b.CostPerUnit))}");
    }

    private (DataTable, string) Adjustments(DateTime from, DateTime to)
    {
        var t = Table(("Reason", typeof(string)), ("Entries", typeof(int)), ("Units Removed", typeof(int)), ("Units Added", typeof(int)), ("Net Value", typeof(decimal)));
        var list = Db.Adjustments.Rows.Where(a => a.Date >= from && a.Date < to).ToList();
        foreach (var g in list.GroupBy(a => a.Reason).OrderBy(g => g.Sum(a => a.CostValue)))
            t.Rows.Add(g.Key, g.Count(), -g.Where(a => a.QtyChange < 0).Sum(a => a.QtyChange), g.Where(a => a.QtyChange > 0).Sum(a => a.QtyChange), g.Sum(a => a.CostValue));
        return (t, $"Net value {M(list.Sum(a => a.CostValue))}");
    }
}
