using PharmacyDesktop.Core;
using PharmacyDesktop.UI.Controls;
using PharmacyDesktop.UI.Forms;

namespace PharmacyDesktop.UI.Pages;

// ─────────────────────────────── Users ───────────────────────────────

public sealed record UserRow(AppUser User, string FullName, string Username, string Role, string Status, DateTime Created, DateTime? LastLogin);

public class UsersPage : ListPage<UserRow>
{
    public override string Title => "Users";
    public override string Subtitle => $"Staff accounts for {Session.Tenant.Name}";

    public UsersPage() : base("User accounts", "Name or username", "Add User")
    {
        Grid.Col("FullName", "Full name", 200, fill: true);
        Grid.Col("Username", "Username", 150);
        Grid.Col("Role", "Role", 120);
        Grid.Col("Status", "Status", 100);
        Grid.DateCol("Created", "Created", 120);
        Grid.DateCol("LastLogin", "Last login", 150, time: true);
        Grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.Value is not string s) return;
            var col = Grid.Columns[e.ColumnIndex].Name;
            if (col == "Role") Pill.Paint(e, s, s == Roles.Admin ? Theme.Accent : s == Roles.Pharmacist ? Theme.Primary : Theme.Info);
            else if (col == "Status") Pill.Paint(e, s, s == "Active" ? Theme.Success : Theme.TextMuted);
        };
        var info = Ui.Label("Admin: everything · Pharmacist: sales, stock, purchases & reports · Cashier: POS, sales history, patients", Theme.Small, Theme.TextMuted);
        info.Dock = DockStyle.Bottom;
        info.Padding = new Padding(4, 8, 0, 0);
        Controls.Add(info);
        Controls.SetChildIndex(info, Controls.Count - 1);
    }

    private static IEnumerable<AppUser> TenantUsers => Session.Master.Users.Rows.Where(u => u.TenantId == Session.Tenant.Id);

    protected override void LoadData()
    {
        var rows = TenantUsers.Where(u => Match(u.FullName, u.Username)).OrderBy(u => u.FullName)
            .Select(u => new UserRow(u, u.FullName, u.Username, u.Role, u.IsActive ? "Active" : "Disabled", u.CreatedAt, u.LastLogin)).ToList();
        Grid.Bind(rows);
        Summary.Text = $"{rows.Count} user(s)";
    }

    protected override void OnAdd() { if (UserEditor.Edit(FindForm(), Session.Tenant.Id, null, Session.User.Id)) LoadData(); }
    protected override void OnEdit(UserRow row) { if (UserEditor.Edit(FindForm(), Session.Tenant.Id, row.User, Session.User.Id)) LoadData(); }

    protected override void OnDelete(UserRow row)
    {
        if (row.User.Id == Session.User.Id) { Ui.Error("You can't delete your own account."); return; }
        if (row.Role == Roles.Admin && TenantUsers.Count(u => u.Role == Roles.Admin && u.IsActive) <= 1) { Ui.Error("At least one active Admin is required."); return; }
        if (Ui.Confirm($"Delete user '{row.Username}'? (Their name stays on past invoices.)"))
            Ui.Try(() => Session.Master.Commit(() => Session.Master.Users.Remove(row.User)));
        LoadData();
    }
}

/// <summary>Add/edit a login of one pharmacy. Used by the store Admin (Users page) and by the owner panel.</summary>
public static class UserEditor
{
    /// <param name="selfId">Id of the signed-in user (0 for the owner) so people can't lock themselves out.</param>
    public static bool Edit(IWin32Window? owner, int tenantId, AppUser? user, int selfId)
    {
        var tenantUsers = Session.Master.Users.Rows.Where(x => x.TenantId == tenantId);
        var u = user ?? new AppUser { TenantId = tenantId };
        var isNew = user == null;
        using var d = new FormDialog(isNew ? "Add user" : "Edit user", isNew ? null : "Leave password empty to keep the current one.", 2, 600);
        d.AddText("name", "FULL NAME *", u.FullName);
        d.AddText("user", "USERNAME *", u.Username);
        d.AddComboFixed("role", "ROLE", Roles.All, u.Role);
        d.AddCheck("active", "Account active", u.IsActive);
        d.AddText("pass", isNew ? "PASSWORD * (min 4)" : "NEW PASSWORD", "", password: true);
        d.AddText("pass2", "CONFIRM PASSWORD", "", password: true);
        d.Validator = x =>
        {
            if (x.Str("name").Length < 2) return "Enter the full name.";
            if (x.Str("user").Length < 3) return "Username must be at least 3 characters.";
            if (tenantUsers.Any(o => o != user && o.Username.Equals(x.Str("user"), StringComparison.OrdinalIgnoreCase))) return "This username is already taken.";
            var p = x["pass"].Text;
            if ((isNew || p.Length > 0) && p.Length < 4) return "Password must be at least 4 characters.";
            if (p != x["pass2"].Text) return "Passwords do not match.";
            if (!isNew && user!.Id == selfId && (x.Str("role") != Roles.Admin || !x.Bool("active")))
                return "You can't remove your own Admin rights or disable yourself.";
            return null;
        };
        if (d.ShowDialog(owner) != DialogResult.OK) return false;
        return Ui.Try(() => Session.Master.Commit(() =>
        {
            u.FullName = d.Str("name");
            u.Username = d.Str("user");
            u.Role = d.Str("role");
            u.IsActive = d.Bool("active");
            if (d["pass"].Text.Length > 0) u.PasswordHash = PasswordHasher.Hash(d["pass"].Text);
            if (isNew) Session.Master.Users.Add(u);
        }));
    }
}

// ─────────────────────────────── Settings ───────────────────────────────

public class SettingsPage : PageBase
{
    private readonly TextBox _name, _address, _phone, _license, _footer, _currency;
    private readonly NumericUpDown _expiryDays, _reorder;
    private readonly CheckBox _allowExpired, _printAfterSave;
    private readonly Label _dataPath;

    public override string Title => "Settings";
    public override string Subtitle => "Pharmacy details, receipt and inventory preferences, data & backups";

    public SettingsPage()
    {
        AutoScroll = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Theme.Background };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        Card Section(string title, string sub, int height, out Panel body)
        {
            var c = new Card { Dock = DockStyle.Fill, Height = height, Padding = new Padding(22, 16, 22, 16), Margin = new Padding(0, 0, 16, 16) };
            var t = Ui.Label(title, Theme.H3);
            t.Location = new Point(22, 16);
            var s = Ui.Label(sub, Theme.Small, Theme.TextMuted);
            s.Location = new Point(23, 42);
            var inner = new Panel { Location = new Point(22, 72), Size = new Size(600, height - 90), BackColor = Theme.Surface };
            c.Resize += (_, _) => inner.Width = Math.Max(100, c.Width - 44);
            c.Controls.AddRange(new Control[] { t, s, inner });
            body = inner;
            return c;
        }

        Control At(Panel p, string caption, Control input, int x, int y, int w)
        {
            var l = Ui.FieldLabel(caption);
            l.Location = new Point(x, y);
            input.Location = new Point(x, y + 20);
            input.Width = w;
            p.Controls.Add(l);
            p.Controls.Add(input);
            return input;
        }

        // Pharmacy profile
        var profile = Section("Pharmacy profile", "Shown on receipts and reports.", 360, out var pb);
        _name = (TextBox)At(pb, "PHARMACY NAME", Ui.TextBox(), 0, 0, 420);
        _phone = (TextBox)At(pb, "PHONE", Ui.TextBox(), 0, 62, 200);
        _license = (TextBox)At(pb, "DRUG LICENSE NO.", Ui.TextBox(), 220, 62, 200);
        _address = (TextBox)At(pb, "ADDRESS", Ui.TextBox(), 0, 124, 420);
        _footer = (TextBox)At(pb, "RECEIPT FOOTER MESSAGE", Ui.TextBox(), 0, 186, 420);
        layout.Controls.Add(profile, 0, 0);

        // Preferences
        var prefs = Section("Preferences", "How the POS and inventory behave.", 360, out var prb);
        _currency = (TextBox)At(prb, "CURRENCY SYMBOL", Ui.TextBox(), 0, 0, 120);
        _expiryDays = (NumericUpDown)At(prb, "EXPIRY ALERT (DAYS)", Ui.Number(90, 0, 3650), 140, 0, 140);
        _reorder = (NumericUpDown)At(prb, "DEFAULT RE-ORDER LEVEL (units)", Ui.Number(10, 0, 100000), 0, 62, 280);
        _allowExpired = new CheckBox { Text = "Allow selling expired batches", AutoSize = true, Location = new Point(0, 130) };
        _printAfterSave = new CheckBox { Text = "Always print receipt after saving a sale", AutoSize = true, Location = new Point(0, 160) };
        prb.Controls.Add(_allowExpired);
        prb.Controls.Add(_printAfterSave);
        prefs.Margin = new Padding(0, 0, 0, 16);
        layout.Controls.Add(prefs, 1, 0);

        // Data & backup
        var data = Section("Data & backups", "All data is stored offline in Excel workbooks next to the application.", 270, out var db);
        data.Margin = new Padding(0, 0, 0, 16);
        _dataPath = new Label { Location = new Point(0, 0), Size = new Size(600, 64), Font = Theme.Small, ForeColor = Theme.Text, AutoEllipsis = true, UseMnemonic = false, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
        var open = new ModernButton("Open data folder", Theme.Glyph.Folder, ButtonKind.Secondary) { Location = new Point(0, 76) };
        open.Click += (_, _) => System.Diagnostics.Process.Start("explorer.exe", ExcelDatabase.DataFolder);
        var backup = new ModernButton("Backup now…", Theme.Glyph.Save, ButtonKind.Secondary) { Location = new Point(open.Right + 10, 76) };
        backup.Click += (_, _) => Backup();
        var auto = Ui.Label("A daily automatic backup is kept in Data\\Backups (last 30 days).", Theme.Small, Theme.TextMuted);
        auto.Location = new Point(0, 126);
        db.Controls.AddRange(new Control[] { _dataPath, open, backup, auto });
        layout.Controls.Add(data, 0, 1);
        layout.SetColumnSpan(data, 2);

        var save = new ModernButton("Save settings", Theme.Glyph.Save) { Width = 180, Height = 44 };
        save.Click += (_, _) => Save();
        var savePanel = new Panel { Height = 60, Dock = DockStyle.Fill, BackColor = Theme.Background };
        savePanel.Controls.Add(save);
        layout.Controls.Add(savePanel, 0, 2);

        Controls.Add(layout);
    }

    public override void OnPageShown()
    {
        var db = Session.Db;
        _name.Text = db.GetSetting(SettingKeys.PharmacyName, Session.Tenant.Name);
        _address.Text = db.GetSetting(SettingKeys.Address);
        _phone.Text = db.GetSetting(SettingKeys.Phone);
        _license.Text = db.GetSetting(SettingKeys.LicenseNo);
        _footer.Text = db.GetSetting(SettingKeys.ReceiptFooter, "Thank you for your visit! Get well soon.");
        _currency.Text = db.Currency;
        _expiryDays.Value = db.GetSettingInt(SettingKeys.ExpiryAlertDays, 90);
        _reorder.Value = db.GetSettingInt(SettingKeys.DefaultReorderLevel, 10);
        _allowExpired.Checked = db.GetSettingBool(SettingKeys.AllowExpiredSale, false);
        _printAfterSave.Checked = db.GetSettingBool(SettingKeys.PrintAfterSave, false);
        _dataPath.Text = $"This pharmacy's file:  {db.FilePath}\nPharmacies & users:  {Session.Master.FilePath}";
    }

    private void Save()
    {
        if (_name.Text.Trim().Length < 2) { Ui.Error("Enter the pharmacy name."); return; }
        var db = Session.Db;
        var ok = Ui.Try(() => db.Commit(() =>
        {
            db.SetSetting(SettingKeys.PharmacyName, _name.Text.Trim());
            db.SetSetting(SettingKeys.Address, _address.Text.Trim());
            db.SetSetting(SettingKeys.Phone, _phone.Text.Trim());
            db.SetSetting(SettingKeys.LicenseNo, _license.Text.Trim());
            db.SetSetting(SettingKeys.ReceiptFooter, _footer.Text.Trim());
            db.SetSetting(SettingKeys.Currency, _currency.Text.Trim().Length > 0 ? _currency.Text.Trim() : "Rs.");
            db.SetSetting(SettingKeys.ExpiryAlertDays, ((int)_expiryDays.Value).ToString());
            db.SetSetting(SettingKeys.DefaultReorderLevel, ((int)_reorder.Value).ToString());
            db.SetSetting(SettingKeys.AllowExpiredSale, _allowExpired.Checked.ToString());
            db.SetSetting(SettingKeys.PrintAfterSave, _printAfterSave.Checked.ToString());
        }));
        if (!ok) return;

        // Keep the tenant list (login screen) in sync with the profile.
        Ui.Try(() => Session.Master.Commit(() =>
        {
            Session.Tenant.Name = _name.Text.Trim();
            Session.Tenant.Address = _address.Text.Trim();
            Session.Tenant.Phone = _phone.Text.Trim();
            Session.Tenant.LicenseNo = _license.Text.Trim();
        }));
        MainForm.Current?.RefreshBadges();
        Ui.Info("Settings saved.");
    }

    private void Backup()
    {
        using var dlg = new FolderBrowserDialog { Description = "Choose where to save the backup (e.g. a USB drive)" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        Ui.Try(() =>
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            var target = Path.Combine(dlg.SelectedPath, $"PharmaDesk_Backup_{Session.Tenant.Code}_{stamp}");
            Directory.CreateDirectory(target);
            File.Copy(Session.Db.FilePath, Path.Combine(target, Path.GetFileName(Session.Db.FilePath)), true);
            File.Copy(Session.Master.FilePath, Path.Combine(target, Path.GetFileName(Session.Master.FilePath)), true);
            Ui.Info($"Backup saved to:\n{target}");
        });
    }
}
