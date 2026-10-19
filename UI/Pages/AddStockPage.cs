using System.Globalization;
using PharmacyDesktop.Core;
using PharmacyDesktop.UI.Controls;
using PharmacyDesktop.UI.Forms;

namespace PharmacyDesktop.UI.Pages;

/// <summary>Goods received note: record a supplier invoice and add batches to stock.</summary>
public class AddStockPage : PageBase
{
    private sealed class Line
    {
        public Item Item = null!;
        public string BatchNo = "";
        public DateTime? Expiry;
        public int PackSize = 1;
        public int Packs = 1;
        public int Bonus;
        public decimal UnitCost;
        public decimal DiscPct;
        public decimal TaxPct;
        public decimal Retail;
        public decimal NetCost => Math.Round(UnitCost * (1 - DiscPct / 100) * (1 + TaxPct / 100), 2);
        public decimal Total => Math.Round(NetCost * Packs, 2);
        public decimal MarginPct => Retail <= 0 ? 0 : Math.Round((Retail - NetCost * Packs / Math.Max(1, Packs + Bonus)) / Retail * 100, 1);
    }

    private readonly List<Line> _lines = new();
    private readonly Label _docNo;
    private readonly ComboBox _supplier;
    private readonly TextBox _invoiceNo, _notes;
    private readonly DateTimePicker _date, _invoiceDate;
    private readonly ItemSearchBox _search;
    private readonly DataGridView _grid;
    private readonly NumericUpDown _discount, _tax;
    private readonly Label _totalQty, _linesTotal, _grand;
    private bool _updating;

    public override string Title => "Add Stock";
    public override string Subtitle => "Record a purchase / GRN — prices are per pack, expiry as MM/YY";

    public AddStockPage()
    {
        // ───── Header card ─────
        var head = new Card { Dock = DockStyle.Top, Height = 168, Padding = new Padding(18, 12, 18, 12) };
        var flow = Ui.Toolbar();
        flow.Dock = DockStyle.None;
        flow.Location = new Point(18, 12);
        _docNo = Ui.Label("", Theme.H2, Theme.PrimaryDark);
        var docPanel = new Panel { Width = 120, Height = 58, Margin = new Padding(0, 0, 14, 6), BackColor = Theme.Surface };
        var docCap = Ui.FieldLabel("DOCUMENT NO.");
        docPanel.Controls.Add(docCap);
        _docNo.Location = new Point(0, 24);
        docPanel.Controls.Add(_docNo);

        _supplier = Ui.Combo(width: 210, editable: true);
        var addSup = new ModernButton("", Theme.Glyph.Add) { Width = 38, Height = 30, Margin = new Padding(0, 24, 14, 0) };
        addSup.Click += (_, _) =>
        {
            var s = SuppliersPage.EditSupplier(FindForm(), null);
            if (s != null) { LoadSuppliers(); _supplier.SelectedItem = _supplier.Items.Cast<object>().OfType<Supplier>().First(x => x.Id == s.Id); }
        };
        _invoiceNo = Ui.TextBox("Supplier bill #", 130);
        _date = Ui.Date(null, 135);
        _invoiceDate = Ui.Date(null, 135);
        flow.Controls.Add(docPanel);
        flow.Controls.Add(Ui.Field("SUPPLIER", _supplier));
        flow.Controls.Add(addSup);
        flow.Controls.Add(Ui.Field("SUPPLIER INVOICE #", _invoiceNo));
        flow.Controls.Add(Ui.Field("RECEIVED DATE", _date));
        flow.Controls.Add(Ui.Field("INVOICE DATE", _invoiceDate));

        var searchRow = new Panel { Height = 46, BackColor = Theme.Surface };
        _search = new ItemSearchBox { Dock = DockStyle.Fill, PriceAsPurchase = true, Placeholder = "Search medicine to add to this purchase…  (F4)" };
        _search.ItemPicked += AddLine;
        var newItem = new ModernButton("New Item", Theme.Glyph.Add, ButtonKind.Secondary) { Dock = DockStyle.Right, Height = 44 };
        newItem.Click += (_, _) =>
        {
            var it = ItemsPage.EditItem(FindForm(), null);
            if (it != null) AddLine(it);
        };
        searchRow.Controls.Add(_search);
        searchRow.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 10, BackColor = Theme.Surface });
        searchRow.Controls.Add(newItem);
        head.Controls.Add(flow);
        head.Controls.Add(searchRow);
        // Manual layout: the fields wrap onto a second line on narrow screens and the header grows to fit.
        void LayoutHead()
        {
            var w = Math.Max(200, head.Width - 36);
            flow.MaximumSize = new Size(w, 0);
            flow.MinimumSize = new Size(w, 0);
            searchRow.SetBounds(18, flow.Bottom + 6, w, 46);
            var h = searchRow.Bottom + 14;
            if (head.Height != h) head.Height = h;
        }
        head.Resize += (_, _) => LayoutHead();
        flow.SizeChanged += (_, _) => LayoutHead();

        // ───── Lines grid ─────
        _grid = Ui.Grid();
        _grid.ReadOnly = false;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        void C(string name, string header, int width, bool ro = false, bool right = true)
        {
            var c = new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = width, ReadOnly = ro, SortMode = DataGridViewColumnSortMode.NotSortable };
            if (right) { c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight; }
            if (!ro) c.DefaultCellStyle.BackColor = Color.FromArgb(240, 253, 250);
            _grid.Columns.Add(c);
        }
        C("No", "#", 40, true, false);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Item", HeaderText = "Medicine", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 170, SortMode = DataGridViewColumnSortMode.NotSortable });
        C("Batch", "Batch #", 95, false, false);
        C("Expiry", "Expiry (MM/YY)", 100, false, false);
        C("Pack", "Units/Pack", 80);
        C("Packs", "Packs", 65);
        C("Bonus", "Bonus", 60);
        C("Cost", "Cost/Pack", 85);
        C("Disc", "Disc %", 65);
        C("Tax", "Tax %", 60);
        C("Net", "Net Cost", 85, true);
        C("Retail", "Retail/Pack", 90);
        C("Margin", "Margin", 70, true);
        C("Total", "Total", 100, true);
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Del", HeaderText = "", Width = 40, Text = "✕", UseColumnTextForButtonValue = true, FlatStyle = FlatStyle.Flat });
        _grid.Columns["Total"]!.DefaultCellStyle.Font = Theme.BodyBold;
        _grid.Columns["Del"]!.DefaultCellStyle.ForeColor = Theme.Danger;
        _grid.DataError += (_, e) => e.ThrowException = false;
        _grid.CellValueChanged += CellChanged;
        _grid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Del") { _lines.RemoveAt(e.RowIndex); Render(); }
        };
        _grid.EditingControlShowing += (_, e) => { if (e.Control is TextBox tb) BeginInvoke(tb.SelectAll); };

        // ───── Footer ─────
        var foot = new Card { Dock = DockStyle.Bottom, Height = 112, Padding = new Padding(18, 12, 18, 12) };
        var notesLbl = Ui.FieldLabel("GRN NOTES");
        notesLbl.Location = new Point(18, 12);
        _notes = Ui.TextBox("Optional remarks", 230);
        _notes.Multiline = true;
        _notes.Height = 58;
        _notes.Location = new Point(18, 32);
        _discount = Ui.Number(0, 2, 100_000_000, 140);
        _tax = Ui.Number(0, 2, 100_000_000, 140);
        var dPanel = Ui.Field($"INV. DISCOUNT ({Session.Cur})", _discount, 140);
        dPanel.Location = new Point(266, 12);
        var tPanel = Ui.Field($"SALES / ADV. TAX ({Session.Cur})", _tax, 140);
        tPanel.Location = new Point(420, 12);
        _discount.ValueChanged += (_, _) => Totals();
        _tax.ValueChanged += (_, _) => Totals();

        _totalQty = Ui.Label("", Theme.Small, Theme.TextMuted);
        _linesTotal = Ui.Label("", Theme.Small, Theme.TextMuted);
        var grandCap = Ui.FieldLabel("GRAND TOTAL");
        _grand = Ui.Label("", new Font("Segoe UI Semibold", 17F), Theme.PrimaryDark);
        var save = new ModernButton("Save Purchase  (Ctrl+S)", Theme.Glyph.Save) { Width = 210, Height = 42 };
        save.Click += (_, _) => Save();
        var clear = new ModernButton("Clear", Theme.Glyph.Clear, ButtonKind.Secondary) { Width = 210, Height = 34 };
        clear.Click += (_, _) => { if (_lines.Count == 0 || Ui.Confirm("Discard this purchase?")) ClearAll(); };
        foot.Controls.AddRange(new Control[] { notesLbl, _notes, dPanel, tPanel, _totalQty, _linesTotal, grandCap, _grand, save, clear });
        foot.Resize += (_, _) =>
        {
            save.Location = new Point(foot.Width - 18 - save.Width, 14);
            clear.Location = new Point(save.Left, 62);
            var x = Math.Max(580, save.Left - 230);
            grandCap.Location = new Point(x, 12);
            _grand.Location = new Point(x - 2, 30);
            _totalQty.Location = new Point(x, 66);
            _linesTotal.Location = new Point(x, 84);
        };

        // Fill first, outermost edges last (WinForms docks in reverse order of addition).
        var gap = Ui.Spacer(14);
        gap.Dock = DockStyle.Bottom;
        Controls.Add(Ui.GridCard("Purchase items", _grid));
        Controls.Add(Ui.Spacer(14));
        Controls.Add(gap);
        Controls.Add(head);
        Controls.Add(foot);
    }

    public override void OnPageShown()
    {
        LoadSuppliers();
        _docNo.Text = Session.Db.NextDocumentNo();
        Totals();
    }

    public override bool HandleKey(Keys key)
    {
        if (key == Keys.F4) { _search.FocusSearch(); return true; }
        if (key == (Keys.Control | Keys.S)) { Save(); return true; }
        return false;
    }

    private void LoadSuppliers()
    {
        var current = _supplier.SelectedItem as Supplier;
        _supplier.Items.Clear();
        foreach (var s in Session.Db.Suppliers.Rows.Where(s => s.IsActive).OrderBy(s => s.Name)) _supplier.Items.Add(s);
        if (current != null) _supplier.SelectedItem = _supplier.Items.Cast<Supplier>().FirstOrDefault(s => s.Id == current.Id);
    }

    public static DateTime? ParseExpiry(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        var monthYear = new[] { "MM/yy", "M/yy", "MM/yyyy", "M/yyyy", "MM-yy", "MM-yyyy", "MMyy", "MMM-yy", "MMM yy", "MMM-yyyy", "MMM yyyy" };
        if (DateTime.TryParseExact(text, monthYear, CultureInfo.InvariantCulture, DateTimeStyles.None, out var my))
            return new DateTime(my.Year, my.Month, DateTime.DaysInMonth(my.Year, my.Month));
        var full = new[] { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "yyyy-MM-dd", "dd/MM/yy", "dd-MMM-yyyy" };
        if (DateTime.TryParseExact(text, full, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        return DateTime.TryParse(text, out var any) ? any : null;
    }

    private void AddLine(Item item)
    {
        var line = new Line
        {
            Item = item,
            PackSize = Math.Max(1, item.PackSize),
            UnitCost = item.PurchasePrice,
            Retail = item.RetailPrice
        };
        _lines.Add(line);
        Render();
        var row = _grid.Rows[_lines.Count - 1];
        _grid.CurrentCell = row.Cells["Batch"];
        _grid.Focus();
        _grid.BeginEdit(true);
    }

    private void Render()
    {
        _updating = true;
        _grid.Rows.Clear();
        for (int i = 0; i < _lines.Count; i++)
        {
            var idx = _grid.Rows.Add();
            Fill(_grid.Rows[idx], _lines[i], i);
        }
        _updating = false;
        Totals();
    }

    private void Fill(DataGridViewRow row, Line l, int i)
    {
        row.Cells["No"].Value = i + 1;
        row.Cells["Item"].Value = l.Item.Name;
        row.Cells["Batch"].Value = l.BatchNo;
        row.Cells["Expiry"].Value = l.Expiry?.ToString("MM/yy") ?? "";
        row.Cells["Pack"].Value = l.PackSize;
        row.Cells["Packs"].Value = l.Packs;
        row.Cells["Bonus"].Value = l.Bonus;
        row.Cells["Cost"].Value = l.UnitCost.ToString("N2");
        row.Cells["Disc"].Value = l.DiscPct.ToString("0.##");
        row.Cells["Tax"].Value = l.TaxPct.ToString("0.##");
        row.Cells["Net"].Value = l.NetCost.ToString("N2");
        row.Cells["Retail"].Value = l.Retail.ToString("N2");
        row.Cells["Margin"].Value = l.MarginPct.ToString("0.#") + "%";
        row.Cells["Total"].Value = l.Total.ToString("N2");
        row.Cells["Margin"].Style.ForeColor = l.MarginPct < 0 ? Theme.Danger : l.MarginPct < 8 ? Theme.Darken(Theme.Warning, 0.2f) : Theme.Success;
        var expired = l.Expiry.HasValue && l.Expiry.Value < DateTime.Today.AddDays(30);
        row.Cells["Expiry"].Style.ForeColor = expired ? Theme.Danger : Color.Empty;
    }

    private void CellChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_updating || e.RowIndex < 0 || e.RowIndex >= _lines.Count) return;
        var l = _lines[e.RowIndex];
        var raw = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
        int I(int fallback, int min = 0) => int.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out var v) && v >= min ? v : fallback;
        decimal D(decimal fallback) => decimal.TryParse(raw.TrimEnd('%'), NumberStyles.Any, CultureInfo.CurrentCulture, out var v) && v >= 0 ? v : fallback;

        switch (_grid.Columns[e.ColumnIndex].Name)
        {
            case "Batch": l.BatchNo = raw; break;
            case "Expiry":
                var exp = ParseExpiry(raw);
                if (raw.Length > 0 && exp == null) Ui.Error("Enter expiry like 06/27, 06/2027 or 30/06/2027.");
                l.Expiry = exp;
                break;
            case "Pack": l.PackSize = I(l.PackSize, 1); break;
            case "Packs": l.Packs = I(l.Packs); break;
            case "Bonus": l.Bonus = I(l.Bonus); break;
            case "Cost": l.UnitCost = D(l.UnitCost); break;
            case "Disc": l.DiscPct = Math.Min(100, D(l.DiscPct)); break;
            case "Tax": l.TaxPct = Math.Min(100, D(l.TaxPct)); break;
            case "Retail": l.Retail = D(l.Retail); break;
            default: return;
        }
        _updating = true;
        Fill(_grid.Rows[e.RowIndex], l, e.RowIndex);
        _updating = false;
        Totals();
    }

    private void Totals()
    {
        var lines = _lines.Sum(l => l.Total);
        var grand = Math.Max(0, lines - _discount.Value + _tax.Value);
        _totalQty.Text = $"{_lines.Count} item(s) · {_lines.Sum(l => l.Packs + l.Bonus):N0} pack(s)";
        _linesTotal.Text = $"Lines total {Session.Money(lines)}";
        _grand.Text = Session.Money(grand);
    }

    private void Save()
    {
        if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();
        if (_lines.Count == 0) { Ui.Error("Add at least one medicine."); return; }
        var missingExpiry = _lines.Where(l => !l.Expiry.HasValue).Select(l => l.Item.Name).ToList();
        if (missingExpiry.Count > 0 && !Ui.Confirm($"Expiry is missing for:\n• {string.Join("\n• ", missingExpiry.Take(8))}\n\nSave anyway?")) return;
        var noRetail = _lines.FirstOrDefault(l => l.Retail <= 0);
        if (noRetail != null) { Ui.Error($"Enter the retail price for '{noRetail.Item.Name}'."); return; }

        var supplier = _supplier.SelectedItem as Supplier
                       ?? _supplier.Items.Cast<Supplier>().FirstOrDefault(s => s.Name.Equals(_supplier.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        if (supplier == null && _supplier.Text.Trim().Length > 0)
        {
            if (!Ui.Confirm($"'{_supplier.Text.Trim()}' is not in your suppliers list. Add it now?")) return;
            var s = new Supplier { Name = _supplier.Text.Trim() };
            if (!Ui.Try(() => Session.Db.Commit(() => Session.Db.Suppliers.Add(s)))) return;
            supplier = s;
        }

        var lines = _lines.Select(l => new PurchaseLine
        {
            ItemId = l.Item.Id, ItemName = l.Item.Name, BatchNo = l.BatchNo, Expiry = l.Expiry, PackSize = l.PackSize, Packs = l.Packs,
            BonusPacks = l.Bonus, UnitCost = l.UnitCost, DiscountPct = l.DiscPct, TaxPct = l.TaxPct, NetUnitCost = l.NetCost,
            RetailPrice = l.Retail, Total = l.Total
        }).ToList();
        var linesTotal = lines.Sum(l => l.Total);
        var purchase = new Purchase
        {
            DocumentNo = _docNo.Text,
            SupplierId = supplier?.Id ?? 0,
            SupplierName = supplier?.Name ?? "Cash Purchase",
            SupplierInvoiceNo = _invoiceNo.Text.Trim(),
            Date = _date.Value.Date + DateTime.Now.TimeOfDay,
            SupplierInvoiceDate = _invoiceDate.Value.Date,
            SubTotal = linesTotal,
            Discount = _discount.Value,
            SalesTax = _tax.Value,
            GrandTotal = Math.Max(0, linesTotal - _discount.Value + _tax.Value),
            Notes = _notes.Text.Trim()
        };

        if (!Ui.Try(() => Session.Svc.SavePurchase(purchase, lines))) return;
        Ui.Info($"Purchase {purchase.DocumentNo} saved.\n{lines.Count} item(s) added to stock — total {Session.Money(purchase.GrandTotal)}.");
        ClearAll();
        MainForm.Current?.RefreshBadges();
    }

    private void ClearAll()
    {
        _lines.Clear();
        Render();
        _invoiceNo.Clear();
        _notes.Clear();
        _discount.Value = 0;
        _tax.Value = 0;
        _supplier.SelectedIndex = -1;
        _supplier.Text = "";
        _date.Value = _invoiceDate.Value = DateTime.Today;
        _docNo.Text = Session.Db.NextDocumentNo();
    }
}
