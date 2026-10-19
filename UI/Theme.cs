using System.Drawing.Drawing2D;

namespace PharmacyDesktop.UI;

/// <summary>Colours, fonts and drawing helpers shared by every screen.</summary>
public static class Theme
{
    public static readonly Color Primary = Color.FromArgb(13, 148, 136);
    public static readonly Color PrimaryDark = Color.FromArgb(15, 118, 110);
    public static readonly Color PrimarySoft = Color.FromArgb(204, 251, 241);
    public static readonly Color Accent = Color.FromArgb(99, 102, 241);

    public static readonly Color Sidebar = Color.FromArgb(15, 23, 42);
    public static readonly Color SidebarHover = Color.FromArgb(30, 41, 59);
    public static readonly Color SidebarText = Color.FromArgb(148, 163, 184);
    public static readonly Color SidebarSection = Color.FromArgb(71, 85, 105);

    public static readonly Color Background = Color.FromArgb(241, 245, 249);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceAlt = Color.FromArgb(248, 250, 252);
    public static readonly Color Border = Color.FromArgb(226, 232, 240);
    public static readonly Color Text = Color.FromArgb(15, 23, 42);
    public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);

    public static readonly Color Success = Color.FromArgb(16, 185, 129);
    public static readonly Color Danger = Color.FromArgb(239, 68, 68);
    public static readonly Color Warning = Color.FromArgb(245, 158, 11);
    public static readonly Color Info = Color.FromArgb(59, 130, 246);
    public static readonly Color Purple = Color.FromArgb(139, 92, 246);
    public static readonly Color Pink = Color.FromArgb(236, 72, 153);

    public static readonly Color DangerSoft = Color.FromArgb(254, 226, 226);
    public static readonly Color WarningSoft = Color.FromArgb(254, 243, 199);
    public static readonly Color SuccessSoft = Color.FromArgb(209, 250, 229);

    public static readonly Font Body = new("Segoe UI", 9.75F);
    public static readonly Font BodyBold = new("Segoe UI Semibold", 9.75F);
    public static readonly Font Small = new("Segoe UI", 8.5F);
    public static readonly Font SmallBold = new("Segoe UI Semibold", 8.5F);
    public static readonly Font H1 = new("Segoe UI Semibold", 18F);
    public static readonly Font H2 = new("Segoe UI Semibold", 13F);
    public static readonly Font H3 = new("Segoe UI Semibold", 11F);
    public static readonly Font Big = new("Segoe UI Semibold", 20F);
    public static readonly Font Huge = new("Segoe UI", 26F, FontStyle.Bold);

    public static Font Icon(float size) => new("Segoe MDL2 Assets", size);

    /// <summary>Segoe MDL2 Assets glyphs used throughout the app.</summary>
    public static class Glyph
    {
        public const string Home = "";
        public const string Cart = "";
        public const string History = "";
        public const string Package = "";
        public const string Add = "";
        public const string Document = "";
        public const string Calendar = "";
        public const string Warning = "";
        public const string People = "";
        public const string Person = "";
        public const string Settings = "";
        public const string Chart = "";
        public const string Power = "";
        public const string Refresh = "";
        public const string Search = "";
        public const string Print = "";
        public const string Delete = "";
        public const string Edit = "";
        public const string Save = "";
        public const string Excel = "";
        public const string Folder = "";
        public const string Admin = "";
        public const string Work = "";
        public const string Money = "";
        public const string Undo = "";
        public const string Lock = "";
        public const string Clear = "";
        public const string Tag = "";
        public const string Sync = "";
        public const string Import = "";
        public const string Heart = "";
        public const string Close = "";
        public const string Trending = "";
        public const string Clock = "";
    }

    /// <summary>First non-transparent background up the parent chain (for painting rounded corners).</summary>
    public static Color EffectiveBack(Control? c)
    {
        while (c != null && (c.BackColor.A < 255)) c = c.Parent;
        return c?.BackColor ?? Background;
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Darken(Color c, float amount) =>
        Color.FromArgb(c.A, (int)(c.R * (1 - amount)), (int)(c.G * (1 - amount)), (int)(c.B * (1 - amount)));

    public static Color Lighten(Color c, float amount) =>
        Color.FromArgb(c.A, c.R + (int)((255 - c.R) * amount), c.G + (int)((255 - c.G) * amount), c.B + (int)((255 - c.B) * amount));

    public static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = Surface;
        g.BorderStyle = BorderStyle.None;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.GridColor = Border;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight = 40;
        g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = SurfaceAlt,
            ForeColor = TextMuted,
            Font = SmallBold,
            SelectionBackColor = SurfaceAlt,
            SelectionForeColor = TextMuted,
            Padding = new Padding(8, 0, 4, 0),
            Alignment = DataGridViewContentAlignment.MiddleLeft
        };
        g.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface,
            ForeColor = Text,
            Font = Body,
            SelectionBackColor = PrimarySoft,
            SelectionForeColor = Text,
            Padding = new Padding(8, 0, 4, 0)
        };
        g.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(250, 251, 253) };
        g.RowHeadersVisible = false;
        g.RowTemplate.Height = 38;
        g.AllowUserToResizeRows = false;
        g.AllowUserToAddRows = false;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = false;
        g.AutoGenerateColumns = false;
        g.ReadOnly = true;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(g, true);
    }
}

/// <summary>TextRenderer wrapper that never treats '&amp;' as a mnemonic prefix (item names like "J&amp;J").</summary>
public static class Txt
{
    public static void Draw(IDeviceContext g, string? text, Font font, Rectangle bounds, Color color, TextFormatFlags flags = TextFormatFlags.Default) =>
        TextRenderer.DrawText(g, text, font, bounds, color, flags | TextFormatFlags.NoPrefix);

    public static void Draw(IDeviceContext g, string? text, Font font, Point pt, Color color) =>
        TextRenderer.DrawText(g, text, font, pt, color, TextFormatFlags.NoPrefix);
}
