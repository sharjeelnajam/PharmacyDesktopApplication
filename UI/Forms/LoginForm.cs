using System.Drawing.Drawing2D;
using PharmacyDesktop.Core;
using PharmacyDesktop.UI.Controls;

namespace PharmacyDesktop.UI.Forms;

/// <summary>Borderless split-screen login: brand panel on the left, sign-in card on the right.</summary>
public class LoginForm : Form
{
    private readonly ComboBox _pharmacy;
    private readonly TextBox _user;
    private readonly TextBox _pass;
    private readonly Label _error;
    private Point _dragStart;

    public LoginForm()
    {
        Text = "PharmaDesk — Sign in";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(980, 600);
        BackColor = Theme.Surface;
        Font = Theme.Body;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;

        // ── Left brand panel ──
        var brand = new BrandPanel { Dock = DockStyle.Left, Width = 440 };
        brand.MouseDown += StartDrag;
        brand.MouseMove += DoDrag;

        // ── Right form ──
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(70, 40, 70, 30) };
        right.MouseDown += StartDrag;
        right.MouseMove += DoDrag;

        var close = new Label
        {
            Text = Theme.Glyph.Close, Font = Theme.Icon(11), ForeColor = Theme.TextMuted, AutoSize = false,
            Size = new Size(40, 36), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(540 - 46, 6)
        };
        close.Click += (_, _) => Application.Exit();
        close.MouseEnter += (_, _) => { close.BackColor = Theme.Danger; close.ForeColor = Color.White; };
        close.MouseLeave += (_, _) => { close.BackColor = Theme.Surface; close.ForeColor = Theme.TextMuted; };

        var y = 70;
        var title = Ui.Label("Welcome back 👋", new Font("Segoe UI Semibold", 22F));
        title.Location = new Point(70, y);
        var sub = Ui.Label("Sign in to your pharmacy to continue", Theme.Body, Theme.TextMuted);
        sub.Location = new Point(72, y + 46);
        y += 100;

        Control FieldAt(string caption, Control input)
        {
            var lbl = Ui.FieldLabel(caption);
            lbl.Location = new Point(72, y);
            right.Controls.Add(lbl);
            input.Location = new Point(72, y + 22);
            input.AccessibleName = caption;
            input.Width = 400;
            right.Controls.Add(input);
            y += 76;
            return input;
        }

        _pharmacy = Ui.Combo(width: 400);
        _pharmacy.Font = new Font("Segoe UI", 11.5F);
        FieldAt("PHARMACY", _pharmacy);

        _user = Ui.TextBox("Enter your username", 400);
        _user.Font = new Font("Segoe UI", 12F);
        FieldAt("USERNAME", _user);

        _pass = Ui.TextBox("Enter your password", 400);
        _pass.Font = new Font("Segoe UI", 12F);
        _pass.UseSystemPasswordChar = true;
        FieldAt("PASSWORD", _pass);

        var show = new CheckBox { Text = "Show password", AutoSize = true, Location = new Point(72, y - 8), ForeColor = Theme.TextMuted };
        show.CheckedChanged += (_, _) => _pass.UseSystemPasswordChar = !show.Checked;

        _error = Ui.Label("", Theme.BodyBold, Theme.Danger);
        _error.Location = new Point(72, y + 18);

        var login = new ModernButton("Sign in", Theme.Glyph.Lock) { Location = new Point(72, y + 44), Width = 400, Height = 46, Font = new Font("Segoe UI Semibold", 11F) };
        login.Click += (_, _) => DoLogin();

        var version = Ui.Label("PharmaDesk v1.0 · Works fully offline · Data stored in Excel", Theme.Small, Theme.TextMuted);
        version.Location = new Point(72, 560);

        right.Controls.AddRange(new Control[] { close, title, sub, show, _error, login, version });
        Controls.Add(right);
        Controls.Add(brand);

        AcceptButton = login;
        Load += (_, _) => LoadPharmacies();
        Shown += (_, _) =>
        {
            if (Session.Master.Tenants.Rows.Count == 0)
                _error.Text = "No pharmacy is set up on this computer.\nPlease contact your software provider.";
            if (_pharmacy.Items.Count > 0) _user.Focus();
        };
    }

    private void LoadPharmacies(Tenant? select = null)
    {
        if (select != null && !Session.Master.Tenants.Rows.Contains(select)) select = null;
        _pharmacy.Items.Clear();
        foreach (var t in Session.Master.Tenants.Rows.OrderBy(t => t.Name))
            _pharmacy.Items.Add(t);
        var last = Properties.LastPharmacy;
        _pharmacy.SelectedItem = select ?? _pharmacy.Items.Cast<Tenant>().FirstOrDefault(t => t.Code == last) ?? _pharmacy.Items.Cast<Tenant>().FirstOrDefault();
    }

    private void DoLogin()
    {
        _error.Text = "";
        if (_pharmacy.SelectedItem is not Tenant tenant) { _error.Text = "Please select a pharmacy."; return; }
        if (string.IsNullOrWhiteSpace(_user.Text) || _pass.Text.Length == 0) { _error.Text = "Enter your username and password."; return; }

        Cursor = Cursors.WaitCursor;
        try
        {
            var user = Session.Master.Authenticate(tenant, _user.Text, _pass.Text);
            if (user == null)
            {
                _error.Text = "Invalid username or password.";
                _pass.SelectAll();
                _pass.Focus();
                return;
            }

            // Subscription / owner control: block disabled, expired or tampered pharmacies.
            var status = LicenseService.Check(tenant);
            if (!status.CanLogin && !LicenseBlockedDialog.TryUnlock(this, tenant, status)) return;
            status = LicenseService.Check(tenant);
            Session.Master.RecordLogin(tenant);
            if (status.DaysLeft is >= 0 and <= 7)
                Ui.Info($"Your PharmaDesk subscription ends on {tenant.ExpiresOn:dd-MMM-yyyy} ({status.DaysLeft} day(s) left).\nPlease contact your software provider to renew.", "Subscription reminder");

            Session.Start(tenant, user);
            Properties.LastPharmacy = tenant.Code;
            _pass.Clear();
            Hide();
            bool loggedOut;
            using (var main = new MainForm())
            {
                main.ShowDialog();
                loggedOut = main.LoggedOut;
            }

            // Back here after logout (or window closed).
            Session.End();
            if (!loggedOut) { Application.Exit(); return; }
            Show();
            _pass.Focus();
        }
        catch (DataFileLockedException ex)
        {
            Session.End();
            Ui.Error(ex.Message);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    /// <summary>Hidden owner entry: Ctrl+Shift+O opens the owner sign-in.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Shift | Keys.O) && LicenseService.OwnerMode)
        {
            OpenOwnerPanel();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OpenOwnerPanel()
    {
        if (!OwnerLoginDialog.SignIn(this)) return;
        Hide();
        using (var panel = new OwnerForm()) panel.ShowDialog();
        Show();
        LoadPharmacies(_pharmacy.SelectedItem as Tenant);
        _error.Text = Session.Master.Tenants.Rows.Count == 0 ? _error.Text : "";
        _user.Focus();
    }

    private void StartDrag(object? sender, MouseEventArgs e) => _dragStart = e.Location;

    private void DoDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var p = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }

    /// <summary>Remembers the last pharmacy picked on this computer.</summary>
    private static class Properties
    {
        private static readonly string File = Path.Combine(ExcelDatabase.DataFolder, ".lastpharmacy");

        public static string LastPharmacy
        {
            get { try { return System.IO.File.Exists(File) ? System.IO.File.ReadAllText(File).Trim() : ""; } catch { return ""; } }
            set { try { System.IO.File.WriteAllText(File, value); } catch { /* not important */ } }
        }
    }

    /// <summary>Gradient panel with logo, tagline and feature bullets.</summary>
    private sealed class BrandPanel : Panel
    {
        public BrandPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var b = new LinearGradientBrush(ClientRectangle, Color.FromArgb(17, 94, 89), Color.FromArgb(15, 23, 42), 60f))
                g.FillRectangle(b, ClientRectangle);

            // Decorative circles
            using (var b = new SolidBrush(Color.FromArgb(18, 255, 255, 255)))
            {
                g.FillEllipse(b, Width - 180, -90, 300, 300);
                g.FillEllipse(b, -120, Height - 200, 280, 280);
                g.FillEllipse(b, Width - 120, Height - 140, 90, 90);
            }

            // Logo: rounded square with a medical cross
            var logo = new RectangleF(50, 60, 64, 64);
            using (var path = Theme.RoundedRect(logo, 16))
            using (var b = new LinearGradientBrush(logo, Color.FromArgb(45, 212, 191), Theme.Primary, 45f))
                g.FillPath(b, path);
            using (var w = new SolidBrush(Color.White))
            {
                g.FillRectangle(w, logo.X + 26, logo.Y + 14, 12, 36);
                g.FillRectangle(w, logo.X + 14, logo.Y + 26, 36, 12);
            }

            using var fTitle = new Font("Segoe UI", 28F, FontStyle.Bold);
            using var fTag = new Font("Segoe UI", 12F);
            using var fFeature = new Font("Segoe UI", 11F);
            Txt.Draw(g, "PharmaDesk", fTitle, new Point(46, 150), Color.White);
            Txt.Draw(g, "Smart pharmacy management,\nright on your desktop.", fTag, new Rectangle(50, 205, Width - 90, 60), Color.FromArgb(204, 251, 241));

            var features = new[]
            {
                (Theme.Glyph.Cart, "Fast POS with batch & expiry tracking"),
                (Theme.Glyph.Package, "Purchases, stock and supplier records"),
                (Theme.Glyph.Chart, "Sales, profit & expiry reports"),
                (Theme.Glyph.People, "Multiple pharmacies, separate data"),
                (Theme.Glyph.Excel, "Offline — data saved in Excel files"),
            };
            var y = 300;
            using var icon = Theme.Icon(13);
            foreach (var (glyph, text) in features)
            {
                var bubble = new RectangleF(50, y, 34, 34);
                using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillEllipse(b, bubble);
                Txt.Draw(g, glyph, icon, Rectangle.Round(bubble), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                Txt.Draw(g, text, fFeature, new Rectangle(98, y, Width - 120, 34), Color.FromArgb(226, 232, 240), TextFormatFlags.VerticalCenter);
                y += 48;
            }
        }
    }
}
