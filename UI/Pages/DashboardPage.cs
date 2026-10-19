using PharmacyDesktop.Core;
using PharmacyDesktop.UI.Controls;
using PharmacyDesktop.UI.Forms;

namespace PharmacyDesktop.UI.Pages;

public class DashboardPage : PageBase
{
    private readonly Label _greeting;
    private readonly StatCard _today, _profit, _month, _stockValue, _items, _low, _expiring, _expired;
    private readonly BarChart _chart;
    private readonly DataGridView _top, _recent, _lowGrid;

    public override string Title => "Dashboard";
    public override string Subtitle => $"Overview of {Session.Tenant.Name}";

    private sealed record TopRow(string Item, int Qty, decimal Sales);
    private sealed record RecentRow(string Invoice, DateTime Date, string Customer, decimal Total, string Status);
    private sealed record LowRow(string Item, string Stock, int Reorder);

    public DashboardPage()
    {
        // Scrolls on short screens so the chart and lists keep a usable height.
        AutoScroll = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 860, ColumnCount = 1, RowCount = 5, BackColor = Theme.Background };
        Resize += (_, _) => layout.Height = Math.Max(ClientSize.Height, 860);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        // Greeting + quick actions
        var top = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        _greeting = Ui.Label("", Theme.H2);
        _greeting.Location = new Point(2, 12);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Theme.Background, Padding = new Padding(0, 6, 0, 0) };
        var bSale = new ModernButton("New Sale  (F2)", Theme.Glyph.Cart);
        bSale.Click += (_, _) => MainForm.Current?.Navigate("pos");
        actions.Controls.Add(bSale);
        if (!Session.IsCashier)
        {
            var bStock = new ModernButton("Add Stock", Theme.Glyph.Add, ButtonKind.Secondary) { Margin = new Padding(0, 0, 10, 0) };
            bStock.Click += (_, _) => MainForm.Current?.Navigate("addstock");
            var bReport = new ModernButton("Reports", Theme.Glyph.Chart, ButtonKind.Secondary) { Margin = new Padding(0, 0, 10, 0) };
            bReport.Click += (_, _) => MainForm.Current?.Navigate("reports");
            actions.Controls.Add(bStock);
            actions.Controls.Add(bReport);
        }
        top.Controls.Add(_greeting);
        top.Controls.Add(actions);
        layout.Controls.Add(top, 0, 0);

        StatCard Stat(string caption, string glyph, Color color) => new() { Caption = caption, Glyph = glyph, AccentColor = color, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 14) };
        TableLayoutPanel Row(params StatCard[] cards)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = cards.Length, RowCount = 1, BackColor = Theme.Background, Margin = new Padding(0) };
            for (int i = 0; i < cards.Length; i++)
            {
                t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cards.Length));
                if (i == cards.Length - 1) cards[i].Margin = new Padding(0, 0, 0, 14);
                t.Controls.Add(cards[i], i, 0);
            }
            return t;
        }

        _today = Stat("Today's Sales", Theme.Glyph.Money, Theme.Primary);
        _profit = Stat("Today's Profit", Theme.Glyph.Trending, Theme.Success);
        _month = Stat("This Month", Theme.Glyph.Calendar, Theme.Accent);
        _stockValue = Stat("Stock Value (cost)", Theme.Glyph.Package, Theme.Info);
        _items = Stat("Medicines", Theme.Glyph.Tag, Theme.Purple);
        _low = Stat("Low Stock", Theme.Glyph.Warning, Theme.Warning);
        _expiring = Stat("Expiring Soon", Theme.Glyph.Clock, Theme.Pink);
        _expired = Stat("Expired Stock", Theme.Glyph.Delete, Theme.Danger);
        layout.Controls.Add(Row(_today, _profit, _month, _stockValue), 0, 1);
        layout.Controls.Add(Row(_items, _low, _expiring, _expired), 0, 2);

        // Chart + top sellers
        var mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Background, Margin = new Padding(0) };
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        var chartCard = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 14), Padding = new Padding(18, 14, 18, 12) };
        var chartTitle = Ui.Label("Sales — last 14 days", Theme.H3);
        chartTitle.Dock = DockStyle.Top;
        _chart = new BarChart { Dock = DockStyle.Fill, Format = v => Session.Money(v) };
        chartCard.Controls.Add(_chart);
        chartCard.Controls.Add(chartTitle);
        mid.Controls.Add(chartCard, 0, 0);

        _top = Ui.Grid();
        _top.Col("Item", "Medicine", 150, fill: true);
        _top.Col("Qty", "Qty", 60, "N0", right: true);
        _top.Money("Sales", "Sales", 100);
        var topCard = Ui.GridCard("Top sellers (30 days)", _top);
        topCard.Margin = new Padding(0, 0, 0, 14);
        mid.Controls.Add(topCard, 1, 0);
        layout.Controls.Add(mid, 0, 3);

        // Recent sales + low stock
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Background, Margin = new Padding(0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        _recent = Ui.Grid();
        _recent.Col("Invoice", "Invoice #", 110);
        _recent.DateCol("Date", "Date / Time", 150, time: true);
        _recent.Col("Customer", "Customer", 150, fill: true);
        _recent.Money("Total", "Total", 110);
        _recent.Col("Status", "Status", 110);
        _recent.CellPainting += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == _recent.Columns["Status"]!.Index && e.Value is string s)
                Pill.Paint(e, s, s == "Paid" ? Theme.Success : s == "Due" ? Theme.Warning : Theme.Danger);
        };
        var recentCard = Ui.GridCard("Recent sales", _recent);
        recentCard.Margin = new Padding(0, 0, 14, 0);
        bottom.Controls.Add(recentCard, 0, 0);

        _lowGrid = Ui.Grid();
        _lowGrid.Col("Item", "Medicine", 150, fill: true);
        _lowGrid.Col("Stock", "Stock", 100);
        _lowGrid.Col("Reorder", "Re-order", 70, "N0", right: true);
        _lowGrid.DefaultCellStyle.ForeColor = Theme.Darken(Theme.Warning, 0.35f);
        bottom.Controls.Add(Ui.GridCard("Low stock alert", _lowGrid), 1, 0);
        layout.Controls.Add(bottom, 0, 4);

        Controls.Add(layout);
    }

    public override void OnPageShown()
    {
        var db = Session.Db;
        var svc = Session.Svc;
        var now = DateTime.Now;
        var hour = now.Hour;
        _greeting.Text = $"{(hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening")}, {Session.User.FullName.Split(' ')[0]}!";

        decimal Net(Sale s) => s.GrandTotal - s.Returned;
        var todaySales = db.Sales.Rows.Where(s => s.Date.Date == DateTime.Today).ToList();
        var monthSales = db.Sales.Rows.Where(s => s.Date.Year == now.Year && s.Date.Month == now.Month).ToList();
        var todayTotal = todaySales.Sum(Net);
        var todayProfit = todaySales.Sum(s => s.Profit);

        _today.Value = Session.Money(todayTotal);
        _today.Hint = $"{todaySales.Count} invoice(s) today";
        _profit.Value = Session.Money(todayProfit);
        _profit.Hint = todayTotal > 0 ? $"Margin {todayProfit / todayTotal:P1}" : "No sales yet today";
        _month.Value = Session.Money(monthSales.Sum(Net));
        _month.Hint = $"{monthSales.Count} invoice(s) · profit {Session.Money(monthSales.Sum(s => s.Profit))}";
        _stockValue.Value = Session.Money(svc.StockCostValue());
        _stockValue.Hint = $"Retail value {Session.Money(svc.StockRetailValue())}";

        var stock = svc.StockMap();
        var active = db.Items.Rows.Where(i => i.IsActive).ToList();
        var lowItems = active.Where(i => (stock.TryGetValue(i.Id, out var s) ? s : 0) <= svc.ReorderLevelFor(i)).ToList();
        _items.Value = active.Count.ToString("N0");
        _items.Hint = $"{stock.Count:N0} in stock";
        _low.Value = lowItems.Count.ToString("N0");
        _low.Hint = "At or below re-order level";

        var alertDays = db.GetSettingInt(SettingKeys.ExpiryAlertDays, 90);
        var live = db.Batches.Rows.Where(b => b.QtyRemaining > 0 && b.Expiry.HasValue).ToList();
        var expired = live.Where(PharmacyService.IsExpired).ToList();
        var expiring = live.Where(b => !PharmacyService.IsExpired(b) && b.Expiry!.Value <= DateTime.Today.AddDays(alertDays)).ToList();
        _expiring.Value = expiring.Count.ToString("N0");
        _expiring.Hint = $"Batches expiring within {alertDays} days";
        _expired.Value = expired.Count.ToString("N0");
        _expired.Hint = $"Value {Session.Money(expired.Sum(b => b.QtyRemaining * b.CostPerUnit))}";

        var days = Enumerable.Range(0, 14).Select(i => DateTime.Today.AddDays(i - 13)).ToList();
        var byDay = db.Sales.Rows.Where(s => s.Date >= days[0]).GroupBy(s => s.Date.Date).ToDictionary(g => g.Key, g => g.Sum(Net));
        _chart.SetData(days.Select(d => (d.ToString("dd MMM"), byDay.TryGetValue(d, out var v) ? v : 0m)));

        var since = DateTime.Today.AddDays(-30);
        var recentIds = db.Sales.Rows.Where(s => s.Date >= since).Select(s => s.Id).ToHashSet();
        _top.Bind(db.SaleLines.Rows.Where(l => recentIds.Contains(l.SaleId))
            .GroupBy(l => l.ItemName)
            .Select(g => new TopRow(g.Key, g.Sum(x => x.Qty - x.ReturnedQty), g.Sum(x => x.Amount)))
            .OrderByDescending(r => r.Qty).Take(10));

        _recent.Bind(db.Sales.Rows.OrderByDescending(s => s.Date).Take(15)
            .Select(s => new RecentRow(s.InvoiceNo, s.Date, s.CustomerName, s.GrandTotal - s.Returned, s.Status)));

        _lowGrid.Bind(lowItems.Select(i =>
        {
            var s = stock.TryGetValue(i.Id, out var q) ? q : 0;
            return (Row: new LowRow(i.Name, s <= 0 ? "Out of stock" : PharmacyService.FormatQty(s, i.PackSize), svc.ReorderLevelFor(i)), Qty: s);
        }).OrderBy(x => x.Qty).Take(30).Select(x => x.Row));
    }
}
