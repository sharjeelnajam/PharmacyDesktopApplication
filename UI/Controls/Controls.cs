using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PharmacyDesktop.UI.Controls;

/// <summary>White rounded panel with a soft border — the basic building block of every page.</summary>
public class Card : Panel
{
    public int Radius { get; set; } = 12;
    public Color FillColor { get; set; } = Theme.Surface;
    public Color BorderColor { get; set; } = Theme.Border;

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Surface;
        Padding = new Padding(16);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.EffectiveBack(Parent));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundedRect(r, Radius);
        using var b = new SolidBrush(FillColor);
        e.Graphics.FillPath(b, path);
        using var p = new Pen(BorderColor);
        e.Graphics.DrawPath(p, path);
    }
}

public enum ButtonKind { Primary, Secondary, Danger, Success, Ghost, Warning, Info }

/// <summary>Flat rounded button with an optional MDL2 icon glyph.</summary>
public class ModernButton : Button
{
    private bool _hover, _down;
    private ButtonKind _kind = ButtonKind.Primary;

    public string Glyph { get; set; } = "";
    public int Radius { get; set; } = 8;

    public ButtonKind Kind
    {
        get => _kind;
        set { _kind = value; Invalidate(); }
    }

    public ModernButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = Theme.BodyBold;
        Height = 38;
        Padding = new Padding(14, 0, 14, 0);
    }

    public ModernButton(string text, string glyph = "", ButtonKind kind = ButtonKind.Primary) : this()
    {
        Text = text;
        Glyph = glyph;
        Kind = kind;
        AutoSizeToText();
    }

    public void AutoSizeToText()
    {
        using var g = CreateGraphics();
        var w = TextRenderer.MeasureText(g, Text, Font).Width + Padding.Horizontal;
        if (!string.IsNullOrEmpty(Glyph)) w += string.IsNullOrEmpty(Text) ? 18 : 26;
        Width = Math.Max(w, 40);
    }

    private (Color back, Color fore, Color? border) Colors()
    {
        Color baseColor = _kind switch
        {
            ButtonKind.Danger => Theme.Danger,
            ButtonKind.Success => Theme.Success,
            ButtonKind.Warning => Theme.Warning,
            ButtonKind.Info => Theme.Info,
            _ => Theme.Primary
        };
        if (_kind == ButtonKind.Secondary)
        {
            var back = _down ? Color.FromArgb(226, 232, 240) : _hover ? Theme.SurfaceAlt : Theme.Surface;
            return (back, Theme.Text, Theme.Border);
        }
        if (_kind == ButtonKind.Ghost)
        {
            var back = _down ? Theme.Lighten(Theme.Primary, 0.75f) : _hover ? Theme.PrimarySoft : Color.Transparent;
            return (back, Theme.PrimaryDark, null);
        }
        var c = !Enabled ? Theme.Lighten(baseColor, 0.45f) : _down ? Theme.Darken(baseColor, 0.2f) : _hover ? Theme.Darken(baseColor, 0.1f) : baseColor;
        return (c, Color.White, null);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.EffectiveBack(Parent));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var (back, fore, border) = Colors();
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.RoundedRect(rect, Radius))
        {
            if (back != Color.Transparent)
                using (var b = new SolidBrush(back)) g.FillPath(b, path);
            if (border.HasValue)
                using (var p = new Pen(border.Value)) g.DrawPath(p, path);
            if (Focused && ShowFocusCues)
                using (var p = new Pen(Color.FromArgb(120, Theme.Primary), 2)) g.DrawPath(p, path);
        }

        var hasGlyph = !string.IsNullOrEmpty(Glyph);
        var hasText = !string.IsNullOrEmpty(Text);
        using var iconFont = Theme.Icon(Font.Size + 1);
        var textSize = hasText ? TextRenderer.MeasureText(g, Text, Font) : Size.Empty;
        var iconW = hasGlyph ? 18 : 0;
        var gap = hasGlyph && hasText ? 8 : 0;
        var total = iconW + gap + textSize.Width;
        var x = (Width - total) / 2;
        if (hasGlyph)
            Txt.Draw(g, Glyph, iconFont, new Rectangle(x, 0, iconW, Height), fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (hasText)
            Txt.Draw(g, Text, Font, new Rectangle(x + iconW + gap, 0, textSize.Width + 2, Height), fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
}

/// <summary>KPI tile: coloured icon bubble, caption, big value and a hint line.</summary>
public class StatCard : Card
{
    private string _value = "0";
    private string _hint = "";

    public string Caption { get; set; } = "";
    public string Glyph { get; set; } = Theme.Glyph.Chart;
    public Color AccentColor { get; set; } = Theme.Primary;

    public string Value { get => _value; set { _value = value; Invalidate(); } }
    public string Hint { get => _hint; set { _hint = value; Invalidate(); } }

    public StatCard()
    {
        Height = 112;
        Padding = new Padding(18);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // Accent strip on the left edge.
        using (var path = Theme.RoundedRect(new RectangleF(0.5f, 14, 4, Height - 28), 2))
        using (var b = new SolidBrush(AccentColor)) g.FillPath(b, path);

        // Icon bubble top-right; the value gets the full card width underneath the caption.
        var size = Height < 100 ? 34 : 40;
        var bubble = new Rectangle(Width - 16 - size, 14, size, size);
        using (var b = new SolidBrush(Color.FromArgb(28, AccentColor))) g.FillEllipse(b, bubble);
        using (var f = Theme.Icon(size < 40 ? 13 : 15))
            Txt.Draw(g, Glyph, f, bubble, AccentColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var compact = Height < 100;
        Txt.Draw(g, Caption, Theme.BodyBold, new Rectangle(18, 16, Width - 36 - size, 22), Theme.TextMuted, TextFormatFlags.EndEllipsis);
        var valueFont = FitFont(g, _value, Width - 30, compact ? 15F : 18F);
        Txt.Draw(g, _value, valueFont, new Rectangle(16, compact ? 36 : 44, Width - 28, compact ? 30 : 36), Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        if (!ReferenceEquals(valueFont, _fontCache)) valueFont.Dispose();
        Txt.Draw(g, _hint, Theme.Small, new Rectangle(18, compact ? 66 : 84, Width - 36, 18), Theme.TextMuted, TextFormatFlags.EndEllipsis);
    }

    private Font? _fontCache;

    /// <summary>Largest font (down to 11pt) at which the value fits in the available width.</summary>
    private Font FitFont(Graphics g, string text, int width, float max)
    {
        for (var size = max; size > 11; size -= 1)
        {
            var f = new Font("Segoe UI Semibold", size);
            if (TextRenderer.MeasureText(g, text, f).Width <= width) return f;
            f.Dispose();
        }
        return _fontCache ??= new Font("Segoe UI Semibold", 11F);
    }
}

/// <summary>Simple rounded-bar chart, enough for a sales trend on the dashboard.</summary>
public class BarChart : Control
{
    private List<(string label, decimal value)> _data = new();
    private int _hover = -1;

    public Color BarColor { get; set; } = Theme.Primary;
    public Func<decimal, string> Format { get; set; } = v => v.ToString("N0");

    public BarChart()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Surface;
    }

    public void SetData(IEnumerable<(string label, decimal value)> data)
    {
        _data = data.ToList();
        Invalidate();
    }

    private Rectangle PlotArea => new(48, 12, Math.Max(10, Width - 60), Math.Max(10, Height - 44));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int idx = -1;
        if (_data.Count > 0)
        {
            var plot = PlotArea;
            var slot = plot.Width / (float)_data.Count;
            var i = (int)((e.X - plot.X) / slot);
            if (e.X >= plot.X && i >= 0 && i < _data.Count) idx = i;
        }
        if (idx != _hover) { _hover = idx; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var plot = PlotArea;
        var max = _data.Count == 0 ? 0 : _data.Max(d => d.value);
        if (max <= 0) max = 1;
        max = NiceCeiling(max);

        // Grid lines + axis labels
        using var gridPen = new Pen(Theme.Border) { DashStyle = DashStyle.Dash };
        for (int i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4f;
            g.DrawLine(gridPen, plot.X, y, plot.Right, y);
            Txt.Draw(g, ShortNumber(max * i / 4), Theme.Small, new Rectangle(0, (int)y - 8, 44, 16), Theme.TextMuted, TextFormatFlags.Right);
        }

        if (_data.Count == 0) return;
        var slot = plot.Width / (float)_data.Count;
        var barW = Math.Min(36, slot * 0.6f);
        // Skip labels so they never overlap.
        var labelW = _data.Max(d => TextRenderer.MeasureText(d.label, Theme.Small).Width) + 6;
        var step = Math.Max(1, (int)Math.Ceiling(labelW / slot));
        for (int i = 0; i < _data.Count; i++)
        {
            var (label, value) = _data[i];
            var h = (float)(value / max) * plot.Height;
            var x = plot.X + slot * i + (slot - barW) / 2;
            var rect = new RectangleF(x, plot.Bottom - h, barW, Math.Max(h, 0));
            if (h > 1)
            {
                var color = i == _hover ? Theme.PrimaryDark : i == _data.Count - 1 ? BarColor : Theme.Lighten(BarColor, 0.35f);
                using var path = Theme.RoundedRect(rect, Math.Min(6, barW / 2));
                using var b = new LinearGradientBrush(new RectangleF(rect.X, rect.Y - 1, rect.Width, rect.Height + 2), color, Theme.Lighten(color, 0.25f), LinearGradientMode.Vertical);
                g.FillPath(b, path);
            }
            if ((_data.Count - 1 - i) % step == 0)
                Txt.Draw(g, label, Theme.Small, new Rectangle((int)(plot.X + slot * i + slot / 2 - labelW / 2f), plot.Bottom + 6, labelW, 18), Theme.TextMuted, TextFormatFlags.HorizontalCenter);
        }

        if (_hover >= 0)
        {
            var (label, value) = _data[_hover];
            var text = $"{label}: {Format(value)}";
            var size = TextRenderer.MeasureText(text, Theme.SmallBold);
            var x = Math.Min(plot.Right - size.Width - 12, Math.Max(plot.X, plot.X + slot * _hover + slot / 2 - size.Width / 2f));
            var tip = new RectangleF(x, plot.Y, size.Width + 12, size.Height + 8);
            using var path = Theme.RoundedRect(tip, 6);
            using var b = new SolidBrush(Theme.Sidebar);
            g.FillPath(b, path);
            Txt.Draw(g, text, Theme.SmallBold, Rectangle.Round(tip), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private static decimal NiceCeiling(decimal v)
    {
        var pow = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)v)));
        foreach (var m in new[] { 1m, 2m, 2.5m, 5m, 10m })
            if (m * pow >= v) return m * pow;
        return v;
    }

    private static string ShortNumber(decimal v) =>
        v >= 1_000_000 ? $"{v / 1_000_000:0.#}M" : v >= 1_000 ? $"{v / 1_000:0.#}K" : v.ToString("0");
}

/// <summary>Navigation entry of the dark sidebar.</summary>
public class SidebarButton : Control
{
    private bool _hover;
    private bool _active;

    public string Glyph { get; set; } = "";
    public string? Badge { get; set; }

    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    public SidebarButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Height = 42;
        Cursor = Cursors.Hand;
        BackColor = Theme.Sidebar;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var rect = new RectangleF(10, 3, Width - 20, Height - 6);
        if (_active || _hover)
        {
            using var path = Theme.RoundedRect(rect, 8);
            using var b = _active
                ? (Brush)new LinearGradientBrush(rect, Theme.Primary, Theme.Lighten(Theme.Primary, 0.15f), LinearGradientMode.Horizontal)
                : new SolidBrush(Theme.SidebarHover);
            g.FillPath(b, path);
        }
        var fore = _active ? Color.White : _hover ? Color.FromArgb(226, 232, 240) : Theme.SidebarText;
        using (var f = Theme.Icon(12))
            Txt.Draw(g, Glyph, f, new Rectangle(22, 0, 24, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        Txt.Draw(g, Text, _active ? Theme.BodyBold : Theme.Body, new Rectangle(56, 0, Width - 100, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (!string.IsNullOrEmpty(Badge))
        {
            var size = TextRenderer.MeasureText(Badge, Theme.SmallBold);
            var br = new RectangleF(Width - 22 - size.Width - 8, (Height - 20) / 2f, size.Width + 8, 20);
            using var path = Theme.RoundedRect(br, 10);
            using var b = new SolidBrush(_active ? Color.FromArgb(60, 255, 255, 255) : Theme.Danger);
            g.FillPath(b, path);
            Txt.Draw(g, Badge, Theme.SmallBold, Rectangle.Round(br), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    // Expose as a push button to screen readers / UI Automation.
    protected override AccessibleObject CreateAccessibilityInstance() => new NavAccessible(this);

    private sealed class NavAccessible : ControlAccessibleObject
    {
        private readonly SidebarButton _owner;
        public NavAccessible(SidebarButton owner) : base(owner) => _owner = owner;
        public override AccessibleRole Role => AccessibleRole.PushButton;
        public override string? Name => _owner.Text;
        public override string DefaultAction => "Press";
        public override void DoDefaultAction() => _owner.OnClick(EventArgs.Empty);
    }
}

/// <summary>A small coloured status pill (e.g. "Paid", "Expired").</summary>
public static class Pill
{
    public static void Paint(DataGridViewCellPaintingEventArgs e, string text, Color color)
    {
        e.PaintBackground(e.CellBounds, true);
        var g = e.Graphics!;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var size = TextRenderer.MeasureText(text, Theme.SmallBold);
        var r = new RectangleF(e.CellBounds.X + 8, e.CellBounds.Y + (e.CellBounds.Height - 22) / 2f, size.Width + 14, 22);
        using var path = Theme.RoundedRect(r, 11);
        using var b = new SolidBrush(Color.FromArgb(32, color));
        g.FillPath(b, path);
        Txt.Draw(g, text, Theme.SmallBold, Rectangle.Round(r), Theme.Darken(color, 0.25f), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        e.Handled = true;
    }
}

/// <summary>BindingList that supports clicking grid headers to sort.</summary>
public class SortableBindingList<T> : BindingList<T>
{
    private bool _sorted;
    private ListSortDirection _direction;
    private PropertyDescriptor? _property;

    public SortableBindingList(IEnumerable<T> items) : base(items.ToList()) { }

    protected override bool SupportsSortingCore => true;
    protected override bool IsSortedCore => _sorted;
    protected override ListSortDirection SortDirectionCore => _direction;
    protected override PropertyDescriptor? SortPropertyCore => _property;

    protected override void ApplySortCore(PropertyDescriptor prop, ListSortDirection direction)
    {
        var list = (List<T>)Items;
        var sorted = (direction == ListSortDirection.Ascending
            ? list.OrderBy(x => prop.GetValue(x), Comparer<object?>.Default)
            : list.OrderByDescending(x => prop.GetValue(x), Comparer<object?>.Default)).ToList();
        list.Clear();
        list.AddRange(sorted);
        _sorted = true;
        _direction = direction;
        _property = prop;
        OnListChanged(new ListChangedEventArgs(ListChangedType.Reset, -1));
    }

    protected override void RemoveSortCore() => _sorted = false;
}
