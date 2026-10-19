using System.Drawing.Drawing2D;
using PharmacyDesktop.Core;
using PharmacyDesktop.UI.Controls;
using PharmacyDesktop.UI.Pages;

namespace PharmacyDesktop.UI.Forms;

/// <summary>Application shell: dark sidebar navigation, header bar and the page host.</summary>
public class MainForm : Form
{
    private sealed record NavEntry(string Key, string Text, string Glyph, string Section, Func<PageBase> Create, params string[] Roles);

    private readonly Panel _sidebar;
    private readonly Panel _host;
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly Label _clock;
    private readonly Dictionary<string, SidebarButton> _buttons = new();
    private readonly Dictionary<string, PageBase> _pages = new();
    private readonly List<NavEntry> _nav;
    private string _currentKey = "";

    public static MainForm? Current { get; private set; }
    public bool LoggedOut { get; private set; }

    public MainForm()
    {
        Current = this;
        Text = $"PharmaDesk — {Session.Tenant.Name}";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1240, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        Font = Theme.Body;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;

        string[] all = { Roles.Admin, Roles.Pharmacist, Roles.Cashier };
        string[] staff = { Roles.Admin, Roles.Pharmacist };
        string[] admin = { Roles.Admin };
        _nav = new List<NavEntry>
        {
            new("dashboard", "Dashboard", Theme.Glyph.Home, "MAIN", () => new DashboardPage(), all),
            new("pos", "New Sale (POS)", Theme.Glyph.Cart, "MAIN", () => new PosPage(), all),
            new("sales", "Sales History", Theme.Glyph.History, "MAIN", () => new SalesPage(), all),
            new("items", "Medicines / Items", Theme.Glyph.Package, "INVENTORY", () => new ItemsPage(), staff),
            new("addstock", "Add Stock", Theme.Glyph.Add, "INVENTORY", () => new AddStockPage(), staff),
            new("purchases", "Purchases", Theme.Glyph.Document, "INVENTORY", () => new PurchasesPage(), staff),
            new("expiry", "Stock & Expiry", Theme.Glyph.Calendar, "INVENTORY", () => new StockExpiryPage(), staff),
            new("adjust", "Stock Adjustment", Theme.Glyph.Sync, "INVENTORY", () => new AdjustmentPage(), staff),
            new("suppliers", "Suppliers", Theme.Glyph.Work, "PEOPLE", () => new SuppliersPage(), staff),
            new("patients", "Patients", Theme.Glyph.People, "PEOPLE", () => new PatientsPage(), all),
            new("missed", "Missed Sales", Theme.Glyph.Warning, "PEOPLE", () => new MissedSalesPage(), all),
            new("reports", "Reports", Theme.Glyph.Chart, "ADMIN", () => new ReportsPage(), staff),
            new("users", "Users", Theme.Glyph.Admin, "ADMIN", () => new UsersPage(), admin),
            new("settings", "Settings", Theme.Glyph.Settings, "ADMIN", () => new SettingsPage(), admin),
        };

        // ── Sidebar ──
        _sidebar = new Panel { Dock = DockStyle.Left, Width = 248, BackColor = Theme.Sidebar };
        var brand = new BrandHeader { Dock = DockStyle.Top, Height = 80 };
        // The nav list scrolls with the mouse wheel on small screens, without a (grey) scrollbar.
        var navPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar };
        var navInner = new Panel { Location = new Point(0, 0), Width = _sidebar.Width, BackColor = Theme.Sidebar };
        navPanel.Controls.Add(navInner);
        var logout = new SidebarButton { Text = "Logout", Glyph = Theme.Glyph.Power, Dock = DockStyle.Bottom, Height = 46 };
        logout.Click += (_, _) => Logout();

        var y = 4;
        string? section = null;
        foreach (var n in _nav.Where(n => n.Roles.Contains(Session.User.Role)))
        {
            if (n.Section != section)
            {
                section = n.Section;
                var lbl = new Label
                {
                    Text = section, Font = new Font("Segoe UI Semibold", 7.5F), ForeColor = Theme.SidebarSection,
                    Location = new Point(24, y + 8), AutoSize = true, BackColor = Theme.Sidebar
                };
                navInner.Controls.Add(lbl);
                y += 28;
            }
            var btn = new SidebarButton { Text = n.Text, Glyph = n.Glyph, Location = new Point(0, y), Width = _sidebar.Width, Height = 38 };
            var key = n.Key;
            btn.Click += (_, _) => Navigate(key);
            navInner.Controls.Add(btn);
            _buttons[n.Key] = btn;
            y += 38;
        }
        navInner.Height = y + 8;

        void ScrollNav(int delta)
        {
            var min = Math.Min(0, navPanel.Height - navInner.Height);
            navInner.Top = Math.Max(min, Math.Min(0, navInner.Top + delta));
        }
        foreach (Control c in navInner.Controls) c.MouseWheel += (_, e) => ScrollNav(e.Delta / 3);
        navInner.MouseWheel += (_, e) => ScrollNav(e.Delta / 3);
        navPanel.MouseWheel += (_, e) => ScrollNav(e.Delta / 3);
        navPanel.Resize += (_, _) => ScrollNav(0);

        _sidebar.Controls.Add(navPanel);
        _sidebar.Controls.Add(logout);
        _sidebar.Controls.Add(brand);

        // ── Header ──
        var header = new HeaderPanel { Dock = DockStyle.Top, Height = 72 };
        _title = Ui.Label("Dashboard", Theme.H1);
        _title.Location = new Point(28, 10);
        _subtitle = Ui.Label("", Theme.Small, Theme.TextMuted);
        _subtitle.Location = new Point(30, 44);

        var userChip = new UserChip { Dock = DockStyle.Right, Width = 240 };
        var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
        menu.Items.Add("Change password", null, (_, _) => ChangePassword());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Logout", null, (_, _) => Logout());
        userChip.Click += (_, _) => menu.Show(userChip, new Point(userChip.Width - menu.Width - 10, userChip.Height - 6));

        _clock = new Label
        {
            Dock = DockStyle.Right, Width = 200, TextAlign = ContentAlignment.MiddleRight, Font = Theme.BodyBold,
            ForeColor = Theme.TextMuted, BackColor = Theme.Surface, Padding = new Padding(0, 0, 10, 0)
        };
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) => _clock.Text = DateTime.Now.ToString("ddd, dd MMM yyyy   hh:mm tt");
        timer.Start();
        _clock.Text = DateTime.Now.ToString("ddd, dd MMM yyyy   hh:mm tt");

        header.Controls.Add(_title);
        header.Controls.Add(_subtitle);
        header.Controls.Add(_clock);
        header.Controls.Add(userChip);

        _host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(24, 20, 24, 20) };

        Controls.Add(_host);
        Controls.Add(header);
        Controls.Add(_sidebar);

        Load += (_, _) => Navigate("dashboard");
        FormClosed += (_, _) => { timer.Dispose(); Current = null; };
    }

    public void Navigate(string key)
    {
        var entry = _nav.FirstOrDefault(n => n.Key == key);
        if (entry == null || !entry.Roles.Contains(Session.User.Role)) return;

        if (!_pages.TryGetValue(key, out var page))
        {
            page = entry.Create();
            page.Dock = DockStyle.Fill;
            _pages[key] = page;
        }

        SuspendLayout();
        _host.Controls.Clear();
        _host.Controls.Add(page);
        ResumeLayout();

        foreach (var (k, b) in _buttons) b.Active = k == key;
        _currentKey = key;
        _title.Text = page.Title;
        _subtitle.Text = page.Subtitle;
        page.OnPageShown();
        RefreshBadges();
    }

    public void RefreshBadges()
    {
        if (Session.Db == null) return;
        var alertDays = Session.Db.GetSettingInt(SettingKeys.ExpiryAlertDays, 90);
        var expiring = Session.Db.Batches.Rows.Count(b => b.QtyRemaining > 0 && b.Expiry.HasValue && b.Expiry.Value <= DateTime.Today.AddDays(alertDays));
        if (_buttons.TryGetValue("expiry", out var eb)) { eb.Badge = expiring > 0 ? expiring.ToString() : null; eb.Invalidate(); }

        var stock = Session.Svc.StockMap();
        var low = Session.Db.Items.Rows.Count(i => i.IsActive && (stock.TryGetValue(i.Id, out var s) ? s : 0) <= Session.Svc.ReorderLevelFor(i));
        if (_buttons.TryGetValue("items", out var ib)) { ib.Badge = low > 0 ? low.ToString() : null; ib.Invalidate(); }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F2: Navigate("pos"); return true;
            case Keys.F3: Navigate("addstock"); return true;
            case Keys.Control | Keys.D: Navigate("dashboard"); return true;
            case Keys.Control | Keys.H: Navigate("sales"); return true;
        }
        if (_pages.TryGetValue(_currentKey, out var page) && page.HandleKey(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Logout()
    {
        if (_pages.TryGetValue("pos", out var pos) && pos is PosPage p && p.HasItems &&
            !Ui.Confirm("The current sale has not been saved. Logout anyway?")) return;
        LoggedOut = true;
        Close();
    }

    private void ChangePassword()
    {
        using var d = new FormDialog("Change password", null, 1, 420);
        d.AddText("cur", "CURRENT PASSWORD", "", password: true);
        d.AddText("new", "NEW PASSWORD (min 4 chars)", "", password: true);
        d.AddText("new2", "CONFIRM NEW PASSWORD", "", password: true);
        d.Validator = x =>
        {
            if (!PasswordHasher.Verify(x["cur"].Text, Session.User.PasswordHash)) return "Current password is incorrect.";
            if (x["new"].Text.Length < 4) return "New password must be at least 4 characters.";
            if (x["new"].Text != x["new2"].Text) return "New passwords do not match.";
            return null;
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        if (Ui.Try(() => Session.Master.Commit(() => Session.User.PasswordHash = PasswordHasher.Hash(d["new"].Text))))
            Ui.Info("Your password has been changed.");
    }

    // ── Small painted pieces of the shell ──

    private sealed class BrandHeader : Panel
    {
        public BrandHeader()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Sidebar;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Sidebar);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var logo = new RectangleF(20, 22, 44, 44);
            using (var path = Theme.RoundedRect(logo, 12))
            using (var b = new LinearGradientBrush(logo, Color.FromArgb(45, 212, 191), Theme.Primary, 45f))
                g.FillPath(b, path);
            using (var w = new SolidBrush(Color.White))
            {
                g.FillRectangle(w, logo.X + 18, logo.Y + 10, 8, 24);
                g.FillRectangle(w, logo.X + 10, logo.Y + 18, 24, 8);
            }
            using var f = new Font("Segoe UI", 15F, FontStyle.Bold);
            Txt.Draw(g, "PharmaDesk", f, new Point(74, 20), Color.White);
            Txt.Draw(g, Session.Tenant?.Name ?? "", Theme.Small, new Rectangle(76, 48, Width - 86, 18), Theme.SidebarText, TextFormatFlags.EndEllipsis);
            using var p = new Pen(Theme.SidebarHover);
            g.DrawLine(p, 20, Height - 1, Width - 20, Height - 1);
        }
    }

    private sealed class HeaderPanel : Panel
    {
        public HeaderPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Surface);
            using var p = new Pen(Theme.Border);
            e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    private sealed class UserChip : Control
    {
        private bool _hover;

        public UserChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            BackColor = Theme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(8, 12, Width - 24, Height - 24);
            if (_hover)
            {
                using var path = Theme.RoundedRect(box, 10);
                using var b = new SolidBrush(Theme.SurfaceAlt);
                g.FillPath(b, path);
            }
            var user = Session.User;
            if (user == null) return;
            var initials = string.Concat(user.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(s => char.ToUpper(s[0])));
            var avatar = new RectangleF(16, (Height - 38) / 2f, 38, 38);
            using (var b = new LinearGradientBrush(avatar, Theme.Accent, Theme.Primary, 45f)) g.FillEllipse(b, avatar);
            Txt.Draw(g, initials, Theme.BodyBold, Rectangle.Round(avatar), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Txt.Draw(g, user.FullName, Theme.BodyBold, new Rectangle(62, 17, Width - 100, 20), Theme.Text, TextFormatFlags.EndEllipsis);
            Txt.Draw(g, user.Role, Theme.Small, new Rectangle(62, 37, Width - 100, 18), Theme.TextMuted, TextFormatFlags.EndEllipsis);
            using var f = Theme.Icon(8);
            Txt.Draw(g, "", f, new Rectangle(Width - 40, 0, 20, Height), Theme.TextMuted, TextFormatFlags.VerticalCenter);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    }
}
