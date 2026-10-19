using PharmacyDesktop.Core;

namespace PharmacyDesktop.UI.Controls;

/// <summary>
/// Search-as-you-type medicine picker. Shows a rich drop-down with stock and price,
/// supports arrow keys / Enter and barcode scanners (exact barcode + Enter picks instantly).
/// </summary>
public class ItemSearchBox : UserControl
{
    private readonly TextBox _box;
    private readonly ListBox _list;
    private Dictionary<int, int> _stock = new();

    public event Action<Item>? ItemPicked;

    /// <summary>True when the last pick came from an exact barcode match (scanner), not the list.</summary>
    public bool LastPickWasBarcode { get; private set; }

    public bool ShowStock { get; set; } = true;
    public bool PriceAsPurchase { get; set; }

    public ItemSearchBox()
    {
        Height = 40;
        BackColor = Color.White;
        Padding = new Padding(36, 9, 8, 6);

        _box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 11.5F),
            Dock = DockStyle.Fill,
            PlaceholderText = "Search medicine by name, generic or scan barcode…  (F4)"
        };
        Controls.Add(_box);

        _list = new ListBox
        {
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 48,
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false,
            IntegralHeight = false,
            Font = Theme.Body
        };
        _list.DrawItem += DrawListItem;
        _list.MouseClick += (_, _) => PickSelected();
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { PickSelected(); e.Handled = true; } else if (e.KeyCode == Keys.Escape) HideList(); };
        _list.Leave += (_, _) => BeginInvoke(() => { if (!_box.Focused) HideList(); });

        _box.TextChanged += (_, _) => Filter();
        _box.KeyDown += BoxKeyDown;
        _box.Enter += (_, _) => { if (ShowStock) _stock = Session.Svc.StockMap(); };
        _box.Leave += (_, _) => BeginInvoke(() => { if (!_list.Focused && !_box.Focused) HideList(); });
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text { get => _box.Text; set => _box.Text = value ?? ""; }
    public string Placeholder { get => _box.PlaceholderText; set => _box.PlaceholderText = value; }

    public void FocusSearch()
    {
        _box.Focus();
        _box.SelectAll();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var focused = _box.Focused;
        using var pen = new Pen(focused ? Theme.Primary : Theme.Border, focused ? 2 : 1);
        using var path = Theme.RoundedRect(new RectangleF(1, 1, Width - 2.5f, Height - 2.5f), 8);
        g.DrawPath(pen, path);
        using var f = Theme.Icon(12);
        Txt.Draw(g, Theme.Glyph.Search, f, new Rectangle(10, 0, 22, Height), Theme.TextMuted, TextFormatFlags.VerticalCenter);
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        _box.GotFocus += (_, _) => Invalidate();
        _box.LostFocus += (_, _) => Invalidate();
    }

    private void BoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down when _list.Visible && _list.Items.Count > 0:
                _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Up when _list.Visible && _list.Items.Count > 0:
                _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Enter:
                e.Handled = e.SuppressKeyPress = true;
                var exact = Session.Svc.FindByBarcode(_box.Text);
                if (exact != null) Pick(exact, true);
                else if (_list.Visible) PickSelected();
                break;
            case Keys.Escape:
                HideList();
                _box.Clear();
                e.Handled = e.SuppressKeyPress = true;
                break;
        }
    }

    private void Filter()
    {
        var q = _box.Text.Trim();
        if (q.Length == 0) { HideList(); return; }

        var matches = Session.Db.Items.Rows
            .Where(i => i.IsActive)
            .Select(i => (Item: i, Score: Score(i, q)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Item.Name)
            .Take(30)
            .Select(x => x.Item)
            .ToArray();

        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Items.AddRange(matches.Cast<object>().ToArray());
        _list.EndUpdate();

        if (matches.Length == 0) { HideList(); return; }
        ShowList();
        _list.SelectedIndex = 0;
    }

    private static int Score(Item i, string q)
    {
        if (i.Barcode.Length > 0 && i.Barcode.Equals(q, StringComparison.OrdinalIgnoreCase)) return 100;
        if (i.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 80;
        if (i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) return 60;
        if (i.GenericName.Contains(q, StringComparison.OrdinalIgnoreCase)) return 40;
        if (i.Manufacturer.Contains(q, StringComparison.OrdinalIgnoreCase)) return 10;
        return 0;
    }

    private void ShowList()
    {
        var form = FindForm();
        if (form == null) return;
        if (_list.Parent != form) form.Controls.Add(_list);
        var pt = form.PointToClient(PointToScreen(new Point(0, Height + 2)));
        _list.Location = pt;
        _list.Width = Math.Max(Width, 620);
        _list.Height = Math.Min(_list.Items.Count, 8) * _list.ItemHeight + 4;
        if (_list.Right > form.ClientSize.Width - 8) _list.Left = Math.Max(8, form.ClientSize.Width - 8 - _list.Width);
        _list.Visible = true;
        _list.BringToFront();
    }

    private void HideList()
    {
        _list.Visible = false;
    }

    private void PickSelected()
    {
        if (_list.SelectedItem is Item item) Pick(item);
    }

    private void Pick(Item item, bool fromBarcode = false)
    {
        LastPickWasBarcode = fromBarcode;
        HideList();
        _box.Clear();
        _box.Focus();
        ItemPicked?.Invoke(item);
    }

    private void DrawListItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var item = (Item)_list.Items[e.Index];
        var g = e.Graphics;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(selected ? Theme.PrimarySoft : e.Index % 2 == 0 ? Color.White : Theme.SurfaceAlt))
            g.FillRectangle(b, e.Bounds);

        var r = e.Bounds;
        var stock = _stock.TryGetValue(item.Id, out var s) ? s : 0;
        var rightW = 210;
        Txt.Draw(g, item.Name, Theme.BodyBold, new Rectangle(r.X + 12, r.Y + 5, r.Width - rightW - 12, 20), Theme.Text, TextFormatFlags.EndEllipsis);
        var sub = string.Join("  ·  ", new[] { item.GenericName, item.Manufacturer, item.Rack.Length > 0 ? "Rack " + item.Rack : "" }.Where(x => !string.IsNullOrWhiteSpace(x)));
        Txt.Draw(g, sub, Theme.Small, new Rectangle(r.X + 12, r.Y + 25, r.Width - rightW - 12, 18), Theme.TextMuted, TextFormatFlags.EndEllipsis);

        var price = PriceAsPurchase ? $"Cost {Session.Cur} {item.PurchasePrice:N2}/pack" : $"{Session.Cur} {item.UnitPrice:N2}/{item.Unit.ToLower()}";
        Txt.Draw(g, price, Theme.BodyBold, new Rectangle(r.Right - rightW, r.Y + 5, rightW - 12, 20), Theme.PrimaryDark, TextFormatFlags.Right);
        if (ShowStock)
        {
            var color = stock <= 0 ? Theme.Danger : stock <= Session.Svc.ReorderLevelFor(item) ? Theme.Warning : Theme.Success;
            Txt.Draw(g, stock <= 0 ? "Out of stock" : $"Stock: {PharmacyService.FormatQty(stock, item.PackSize)}", Theme.SmallBold,
                new Rectangle(r.Right - rightW, r.Y + 25, rightW - 12, 18), color, TextFormatFlags.Right);
        }
    }
}
