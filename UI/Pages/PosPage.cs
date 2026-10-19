using System.Globalization;
using PharmacyDesktop.Core;
using PharmacyDesktop.UI.Controls;
using PharmacyDesktop.UI.Forms;

namespace PharmacyDesktop.UI.Pages;

/// <summary>Point of sale: pick medicines, choose batches, take payment, print receipt.</summary>
public class PosPage : PageBase
{
    private const string AutoBatch = "Auto (first expiry)";

    private sealed class CartLine
    {
        public Item Item = null!;
        public List<Batch> Batches = new();
        public int BatchId;
        public decimal Rate;
        public int Qty = 1;
        public decimal Discount;
        public int Available => Batches.Sum(b => b.QtyRemaining);
        public decimal Amount => Math.Max(0, Math.Round(Rate * Qty - Discount, 2));
        public Batch? ShownBatch => Batches.FirstOrDefault(b => b.Id == BatchId) ?? Batches.FirstOrDefault();
    }

    private readonly List<CartLine> _cart = new();
    private readonly ComboBox _customer;
    private readonly ItemSearchBox _search;
    private readonly DataGridView _grid;
    private readonly Label _invoiceNo, _status, _count, _subTotal, _grandTotal, _change, _changeCaption, _profit;
    private readonly NumericUpDown _discount, _cash;
    private readonly ComboBox _discountType, _payment;
    private readonly TextBox _notes;
    private bool _updating;

    public override string Title => "New Sale";
    public override string Subtitle => "F4 search · F6 cash · F9 save · F10 save & print · Ctrl+N new sale";
    public bool HasItems => _cart.Count > 0;

    public PosPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Background };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));

        // ───── Left: customer + search + cart ─────
        var left = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Margin = new Padding(0, 0, 16, 0) };
        var topCard = new Card { Dock = DockStyle.Top, Height = 150, Padding = new Padding(20, 14, 20, 14) };

        var custLbl = Ui.FieldLabel("PATIENT / CUSTOMER");
        custLbl.Location = new Point(20, 14);
        _customer = Ui.Combo(width: 380, editable: true);
        _customer.Location = new Point(20, 36);
        _customer.AutoCompleteSource = AutoCompleteSource.ListItems;
        var addCust = new ModernButton("", Theme.Glyph.Add) { Location = new Point(406, 34), Width = 40, Height = 32 };
        addCust.Click += (_, _) => AddCustomer();
        new ToolTip().SetToolTip(addCust, "Add new patient");

        var invLbl = Ui.FieldLabel("INVOICE NO.");
        invLbl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _invoiceNo = Ui.Label("", Theme.H2, Theme.PrimaryDark);
        _invoiceNo.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        _search = new ItemSearchBox { Location = new Point(20, 86), Height = 44, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
        _search.ItemPicked += AddItem;

        topCard.Controls.AddRange(new Control[] { custLbl, _customer, addCust, invLbl, _invoiceNo, _search });
        topCard.Resize += (_, _) =>
        {
            _search.Width = topCard.Width - 40;
            _customer.Width = Math.Max(160, Math.Min(380, topCard.Width - 20 - 200 - 56));
            addCust.Left = _customer.Right + 6;
            invLbl.Location = new Point(topCard.Width - 180, 14);
            _invoiceNo.Location = new Point(topCard.Width - 182, 34);
        };

        _grid = Ui.Grid();
        _grid.ReadOnly = false;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "No", HeaderText = "#", Width = 36, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Item", HeaderText = "Medicine", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 110 });
        _grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Batch", HeaderText = "Batch", Width = 130, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Expiry", HeaderText = "Expiry", Width = 72, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Stock", HeaderText = "Stock", Width = 62, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Rate", HeaderText = "Rate", Width = 78 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Qty", HeaderText = "Qty", Width = 56 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Disc", HeaderText = "Disc.", Width = 62 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Amount", HeaderText = "Amount", Width = 92, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Del", HeaderText = "", Width = 36, Text = "✕", UseColumnTextForButtonValue = true, FlatStyle = FlatStyle.Flat });
        // Hide the less important columns when the cart is narrow (e.g. 1366x768 screens).
        _grid.Resize += (_, _) =>
        {
            _grid.Columns["No"]!.Visible = _grid.Width > 860;
            _grid.Columns["Expiry"]!.Visible = _grid.Width > 760;
            _grid.Columns["Stock"]!.Visible = _grid.Width > 600;
        };
        foreach (var n in new[] { "Stock", "Rate", "Qty", "Disc", "Amount" })
        {
            _grid.Columns[n]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns[n]!.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        foreach (var n in new[] { "Rate", "Qty", "Disc" })
            _grid.Columns[n]!.DefaultCellStyle.BackColor = Color.FromArgb(240, 253, 250);
        _grid.Columns["Amount"]!.DefaultCellStyle.Font = Theme.BodyBold;
        _grid.Columns["Del"]!.DefaultCellStyle.ForeColor = Theme.Danger;
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

        _grid.DataError += (_, e) => e.ThrowException = false;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.CurrentCell is DataGridViewComboBoxCell && _grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += GridCellChanged;
        _grid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Del") RemoveLine(e.RowIndex);
        };
        _grid.CellEndEdit += (_, e) =>
        {
            if (_grid.Columns[e.ColumnIndex].Name == "Qty") BeginInvoke(_search.FocusSearch);
        };
        _grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is TextBox tb) BeginInvoke(tb.SelectAll);
        };

        var empty = Ui.Label("Cart is empty — search a medicine above or scan its barcode.", Theme.Body, Theme.TextMuted);
        empty.Anchor = AnchorStyles.None;
        var cartCard = Ui.GridCard("Cart", _grid);
        _status = (Label)cartCard.Tag!;
        _status.Font = Theme.SmallBold;
        _status.ForeColor = Theme.Success;
        _grid.Controls.Add(empty);
        _grid.Resize += (_, _) => empty.Location = new Point((_grid.Width - empty.Width) / 2, 120);
        _grid.RowsAdded += (_, _) => empty.Visible = false;
        _grid.RowsRemoved += (_, _) => empty.Visible = _grid.Rows.Count == 0;

        left.Controls.Add(cartCard);
        left.Controls.Add(Ui.Spacer(16));
        left.Controls.Add(topCard);
        layout.Controls.Add(left, 0, 0);

        // ───── Right: totals + payment ─────
        var sum = new Card { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 16), AutoScroll = true };
        int y = 16;
        const int x = 22, w = 300;

        var title = Ui.Label("Order Summary", Theme.H2);
        title.Location = new Point(x, y);
        sum.Controls.Add(title);
        y += 36;

        Label Row(string caption, Font? valueFont = null, Color? color = null)
        {
            var c = Ui.Label(caption, Theme.Body, Theme.TextMuted);
            c.Location = new Point(x, y + 2);
            var v = new Label { Font = valueFont ?? Theme.BodyBold, ForeColor = color ?? Theme.Text, TextAlign = ContentAlignment.MiddleRight, Location = new Point(x + 120, y), Size = new Size(w - 120, 24), BackColor = Color.Transparent };
            sum.Controls.Add(c);
            sum.Controls.Add(v);
            y += 28;
            return v;
        }

        _count = Row("Items / Qty");
        _subTotal = Row("Sub Total");

        var discLbl = Ui.Label("Discount", Theme.Body, Theme.TextMuted);
        discLbl.Location = new Point(x, y + 6);
        _discount = Ui.Number(0, 2, 10_000_000, 120);
        _discount.Location = new Point(x + w - 196, y);
        _discountType = Ui.Combo(new object[] { Session.Cur, "%" }, 70);
        _discountType.SelectedIndex = 0;
        _discountType.Location = new Point(x + w - 70, y);
        _discount.ValueChanged += (_, _) => RecalcTotals();
        _discountType.SelectedIndexChanged += (_, _) => RecalcTotals();
        sum.Controls.AddRange(new Control[] { discLbl, _discount, _discountType });
        y += 40;

        var totalBox = new Card { Location = new Point(x, y), Size = new Size(w, 72), FillColor = Theme.Primary, BorderColor = Theme.Primary, BackColor = Theme.Primary, Radius = 12 };
        var totalCap = Ui.Label("GRAND TOTAL", Theme.SmallBold, Color.FromArgb(204, 251, 241));
        totalCap.Location = new Point(16, 10);
        _grandTotal = new Label { Font = new Font("Segoe UI", 22F, FontStyle.Bold), ForeColor = Color.White, Location = new Point(12, 26), Size = new Size(w - 24, 40), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
        totalBox.Controls.Add(totalCap);
        totalBox.Controls.Add(_grandTotal);
        sum.Controls.Add(totalBox);
        y += 82;

        var payLbl = Ui.FieldLabel("PAYMENT METHOD");
        payLbl.Location = new Point(x, y);
        _payment = Ui.Combo(new object[] { "Cash", "Card", "Online Transfer", "Credit" }, 160);
        _payment.SelectedIndex = 0;
        _payment.Location = new Point(x, y + 20);
        _payment.SelectedIndexChanged += (_, _) => RecalcTotals();
        var cashLbl = Ui.FieldLabel("AMOUNT RECEIVED (F6)");
        cashLbl.Location = new Point(x + 172, y);
        _cash = Ui.Number(0, 2, 100_000_000, w - 172);
        _cash.Location = new Point(x + 172, y + 20);
        _cash.ValueChanged += (_, _) => RecalcTotals();
        _cash.Enter += (_, _) => _cash.Select(0, _cash.Text.Length);
        sum.Controls.AddRange(new Control[] { payLbl, _payment, cashLbl, _cash });
        y += 58;

        _changeCaption = Ui.Label("Change", Theme.Body, Theme.TextMuted);
        _changeCaption.Location = new Point(x, y + 4);
        _change = new Label { Font = Theme.H2, ForeColor = Theme.Success, TextAlign = ContentAlignment.MiddleRight, Location = new Point(x + 120, y), Size = new Size(w - 120, 28), BackColor = Color.Transparent };
        sum.Controls.Add(_changeCaption);
        sum.Controls.Add(_change);
        y += 30;

        _profit = Row("Profit / Loss", Theme.BodyBold, Theme.Success);
        if (Session.IsCashier) { _profit.Visible = false; sum.Controls[sum.Controls.Count - 2].Visible = false; }

        var notesLbl = Ui.FieldLabel("NOTE");
        notesLbl.Location = new Point(x, y);
        _notes = Ui.TextBox("Optional note / doctor / prescription ref.", w);
        _notes.Location = new Point(x, y + 20);
        sum.Controls.Add(notesLbl);
        sum.Controls.Add(_notes);
        y += 58;

        var savePrint = new ModernButton("Save & Print  (F10)", Theme.Glyph.Print) { Location = new Point(x, y), Width = w, Height = 46, Font = new Font("Segoe UI Semibold", 11F) };
        savePrint.Click += (_, _) => Save(true);
        y += 52;
        var save = new ModernButton("Save  (F9)", Theme.Glyph.Save, ButtonKind.Success) { Location = new Point(x, y), Width = (w - 10) / 2 };
        save.Click += (_, _) => Save(false);
        var clear = new ModernButton("New Sale", Theme.Glyph.Clear, ButtonKind.Secondary) { Location = new Point(x + (w + 10) / 2, y), Width = (w - 10) / 2 };
        clear.Click += (_, _) => { if (_cart.Count == 0 || Ui.Confirm("Clear the current cart?")) ClearSale(); };
        y += 44;
        var missed = new ModernButton("Log missed sale", Theme.Glyph.Warning, ButtonKind.Ghost) { Location = new Point(x, y), Width = w, Height = 30 };
        missed.Click += (_, _) => MissedSalesPage.AddMissedSale(FindForm());
        sum.Controls.AddRange(new Control[] { savePrint, save, clear, missed });

        layout.Controls.Add(sum, 1, 0);
        Controls.Add(layout);
    }

    public override void OnPageShown()
    {
        LoadCustomers();
        _invoiceNo.Text = Session.Db.NextInvoiceNo();
        // Refresh batch info in case stock changed elsewhere.
        foreach (var line in _cart) line.Batches = Session.Svc.AvailableBatches(line.Item.Id, AllowExpired);
        RenderCart();
        BeginInvoke(_search.FocusSearch);
    }

    public override bool HandleKey(Keys key)
    {
        switch (key)
        {
            case Keys.F4: _search.FocusSearch(); return true;
            case Keys.F6: _cash.Focus(); return true;
            case Keys.F9: Save(false); return true;
            case Keys.F10: Save(true); return true;
            case Keys.Control | Keys.N: if (_cart.Count == 0 || Ui.Confirm("Clear the current cart?")) ClearSale(); return true;
            case Keys.Control | Keys.Delete when _grid.CurrentCell != null && !_grid.IsCurrentCellInEditMode:
                RemoveLine(_grid.CurrentCell.RowIndex); return true;
        }
        return false;
    }

    private static bool AllowExpired => Session.Db.GetSettingBool(SettingKeys.AllowExpiredSale, false);

    private void LoadCustomers()
    {
        var selected = _customer.SelectedItem;
        _customer.BeginUpdate();
        _customer.Items.Clear();
        _customer.Items.Add("Walk-in Customer");
        foreach (var c in Session.Db.Customers.Rows.OrderBy(c => c.Name)) _customer.Items.Add(c);
        _customer.EndUpdate();
        if (selected is Customer sc && _customer.Items.Cast<object>().OfType<Customer>().FirstOrDefault(c => c.Id == sc.Id) is { } again)
            _customer.SelectedItem = again;
        else
            _customer.SelectedIndex = 0;
    }

    private void AddCustomer()
    {
        var c = PatientsPage.EditCustomer(FindForm(), null);
        if (c == null) return;
        LoadCustomers();
        _customer.SelectedItem = _customer.Items.Cast<object>().OfType<Customer>().First(x => x.Id == c.Id);
    }

    private void AddItem(Item item)
    {
        _status.Text = "";
        var existing = _cart.FirstOrDefault(l => l.Item.Id == item.Id);
        if (existing != null)
        {
            existing.Qty++;
        }
        else
        {
            var batches = Session.Svc.AvailableBatches(item.Id, AllowExpired);
            if (batches.Count == 0)
            {
                var expiredOnly = Session.Svc.AvailableBatches(item.Id, true).Count > 0;
                Ui.Error(expiredOnly ? $"'{item.Name}' has only expired stock.\nRemove it via Stock Adjustment or enable expired sales in Settings."
                                     : $"'{item.Name}' is out of stock.\nAdd stock first or log it as a missed sale.");
                return;
            }
            existing = new CartLine { Item = item, Batches = batches, Rate = item.UnitPrice > 0 ? item.UnitPrice : Math.Round(batches[0].RetailPerUnit, 2) };
            _cart.Add(existing);
        }
        RenderCart();

        var row = _cart.IndexOf(existing);
        if (!_search.LastPickWasBarcode && row >= 0)
        {
            _grid.CurrentCell = _grid.Rows[row].Cells["Qty"];
            _grid.Focus();
            _grid.BeginEdit(true);
        }
    }

    private void RemoveLine(int index)
    {
        if (index < 0 || index >= _cart.Count) return;
        _cart.RemoveAt(index);
        RenderCart();
        _search.FocusSearch();
    }

    private void RenderCart()
    {
        _updating = true;
        _grid.Rows.Clear();
        for (int i = 0; i < _cart.Count; i++)
        {
            var l = _cart[i];
            var idx = _grid.Rows.Add();
            var row = _grid.Rows[idx];
            var combo = (DataGridViewComboBoxCell)row.Cells["Batch"];
            combo.Items.Add(AutoBatch);
            foreach (var b in l.Batches) combo.Items.Add(BatchText(b));
            var chosen = l.Batches.FirstOrDefault(b => b.Id == l.BatchId);
            combo.Value = chosen == null ? AutoBatch : BatchText(chosen);
            FillRow(row, l, i);
        }
        _updating = false;
        RecalcTotals();
    }

    private static string BatchText(Batch b) =>
        $"{(string.IsNullOrWhiteSpace(b.BatchNo) ? "—" : b.BatchNo)} · {(b.Expiry.HasValue ? b.Expiry.Value.ToString("MM/yy") : "no exp")} · {b.QtyRemaining}";

    private void FillRow(DataGridViewRow row, CartLine l, int index)
    {
        var shown = l.ShownBatch;
        row.Cells["No"].Value = index + 1;
        row.Cells["Item"].Value = l.Item.Name;
        row.Cells["Expiry"].Value = shown?.Expiry?.ToString("MM/yyyy") ?? "—";
        row.Cells["Stock"].Value = l.Available;
        row.Cells["Rate"].Value = l.Rate.ToString("N2");
        row.Cells["Qty"].Value = l.Qty;
        row.Cells["Disc"].Value = l.Discount.ToString("N2");
        row.Cells["Amount"].Value = l.Amount.ToString("N2");

        var over = l.Qty > l.Available;
        row.Cells["Qty"].Style.BackColor = over ? Theme.DangerSoft : Color.Empty;
        row.Cells["Qty"].Style.ForeColor = over ? Theme.Danger : Color.Empty;
        var exp = shown?.Expiry;
        row.Cells["Expiry"].Style.ForeColor = exp.HasValue && exp.Value < DateTime.Today ? Theme.Danger
            : exp.HasValue && exp.Value < DateTime.Today.AddDays(90) ? Theme.Warning : Color.Empty;
    }

    private void GridCellChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_updating || e.RowIndex < 0 || e.RowIndex >= _cart.Count) return;
        var l = _cart[e.RowIndex];
        var row = _grid.Rows[e.RowIndex];
        var raw = row.Cells[e.ColumnIndex].Value?.ToString() ?? "";
        var name = _grid.Columns[e.ColumnIndex].Name;

        switch (name)
        {
            case "Batch":
                l.BatchId = l.Batches.FirstOrDefault(b => BatchText(b) == raw)?.Id ?? 0;
                break;
            case "Qty":
                l.Qty = int.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out var q) && q > 0 ? q : 1;
                break;
            case "Rate":
                l.Rate = decimal.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out var r) && r >= 0 ? Math.Round(r, 2) : l.Rate;
                break;
            case "Disc":
                var d = decimal.TryParse(raw.TrimEnd('%'), NumberStyles.Any, CultureInfo.CurrentCulture, out var dv) && dv >= 0 ? dv : 0;
                if (raw.EndsWith('%')) d = Math.Round(l.Rate * l.Qty * Math.Min(d, 100) / 100, 2);
                l.Discount = Math.Min(d, l.Rate * l.Qty);
                break;
            default:
                return;
        }

        _updating = true;
        FillRow(row, l, e.RowIndex);
        _updating = false;
        RecalcTotals();
    }

    private decimal SubTotal => _cart.Sum(l => l.Amount);

    private decimal InvoiceDiscount
    {
        get
        {
            var sub = SubTotal;
            var d = _discountType.SelectedIndex == 1 ? Math.Round(sub * Math.Min(_discount.Value, 100) / 100, 2) : _discount.Value;
            return Math.Min(d, sub);
        }
    }

    private void RecalcTotals()
    {
        if (_updating) return;
        var sub = SubTotal;
        var total = sub - InvoiceDiscount;
        _count.Text = $"{_cart.Count} / {_cart.Sum(l => l.Qty)}";
        _subTotal.Text = Session.Money(sub);
        _grandTotal.Text = Session.Money(total);

        var credit = (_payment.SelectedItem as string) == "Credit";
        var diff = _cash.Value - total;
        if (credit || diff < 0)
        {
            _changeCaption.Text = "Due";
            _change.ForeColor = _cash.Value == 0 && !credit ? Theme.TextMuted : Theme.Danger;
            _change.Text = Session.Money(Math.Max(0, total - _cash.Value));
            if (!credit && _cash.Value == 0) { _changeCaption.Text = "Change"; _change.Text = Session.Money(0); }
        }
        else
        {
            _changeCaption.Text = "Change";
            _change.ForeColor = Theme.Success;
            _change.Text = Session.Money(diff);
        }

        // Estimated profit using FEFO cost of the batches that will be consumed.
        decimal cost = 0;
        foreach (var l in _cart)
        {
            var remaining = l.Qty;
            var ordered = l.Batches.OrderBy(b => b.Id == l.BatchId ? 0 : 1).ThenBy(b => b.Expiry ?? DateTime.MaxValue);
            foreach (var b in ordered)
            {
                var take = Math.Min(remaining, b.QtyRemaining);
                cost += take * b.CostPerUnit;
                remaining -= take;
                if (remaining == 0) break;
            }
        }
        var profit = total - cost;
        _profit.Text = Session.Money(profit);
        _profit.ForeColor = profit >= 0 ? Theme.Success : Theme.Danger;
    }

    private void Save(bool print)
    {
        if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();
        if (_cart.Count == 0) { Ui.Error("Add at least one medicine to the cart."); _search.FocusSearch(); return; }

        var customer = _customer.SelectedItem as Customer
                       ?? _customer.Items.Cast<object>().OfType<Customer>().FirstOrDefault(c => c.ToString() == _customer.Text);
        var payment = _payment.SelectedItem as string ?? "Cash";
        if (payment == "Credit" && customer == null)
        {
            Ui.Error("Select a registered patient for a credit sale so the due amount can be tracked.");
            _customer.Focus();
            return;
        }

        var typedName = _customer.Text.Trim();
        var sale = new Sale
        {
            Date = DateTime.Now,
            CustomerId = customer?.Id ?? 0,
            CustomerName = customer?.Name ?? (string.IsNullOrWhiteSpace(typedName) ? "Walk-in Customer" : typedName),
            Discount = InvoiceDiscount,
            PaymentMethod = payment,
            CashReceived = _cash.Value,
            Notes = _notes.Text.Trim()
        };
        var request = _cart.Select(l => new SaleRequestLine { ItemId = l.Item.Id, PreferredBatchId = l.BatchId, Qty = l.Qty, Rate = l.Rate, Discount = l.Discount }).ToList();

        (Sale sale, List<SaleLine> lines) result = default;
        if (!Ui.Try(() => result = Session.Svc.SaveSale(sale, request))) return;

        var printNow = print || Session.Db.GetSettingBool(SettingKeys.PrintAfterSave, false);
        ClearSale();
        _status.Text = $"✓ {result.sale.InvoiceNo} saved — {Session.Money(result.sale.GrandTotal)}" + (result.sale.Change > 0 ? $"  ·  change {Session.Money(result.sale.Change)}" : "");
        MainForm.Current?.RefreshBadges();
        if (printNow) ReceiptPrinter.Print(result.sale, result.lines);
    }

    private void ClearSale()
    {
        _cart.Clear();
        RenderCart();
        _discount.Value = 0;
        _cash.Value = 0;
        _payment.SelectedIndex = 0;
        _notes.Clear();
        _customer.SelectedIndex = 0;
        _status.Text = "";
        _invoiceNo.Text = Session.Db.NextInvoiceNo();
        _search.FocusSearch();
    }
}
