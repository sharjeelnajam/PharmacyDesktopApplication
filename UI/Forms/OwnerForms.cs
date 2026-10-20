using PharmacyDesktop.Core;
using PharmacyDesktop.UI.Controls;
using PharmacyDesktop.UI.Pages;

namespace PharmacyDesktop.UI.Forms;

/// <summary>Owner sign-in (opened with Ctrl+Shift+O on the login screen).</summary>
public static class OwnerLoginDialog
{
    private static int _failures;

    public static bool SignIn(IWin32Window owner)
    {
        if (!LicenseService.OwnerConfigured)
        {
            Ui.Error("Owner sign-in is not configured in this build (Core\\OwnerSecrets.cs is missing).");
            return false;
        }
        using var d = new FormDialog("Owner sign in", "Software owner access — manage pharmacies and subscriptions.", 1, 420);
        d.AddText("user", "OWNER USERNAME", "");
        d.AddText("pass", "PASSWORD", "", password: true);
        d.Validator = x =>
        {
            if (_failures >= 5) Thread.Sleep(Math.Min(10_000, 1000 * (_failures - 4))); // slow down guessing
            if (LicenseService.VerifyOwner(x.Str("user"), x["pass"].Text)) { _failures = 0; return null; }
            _failures++;
            return "Invalid owner credentials.";
        };
        return d.ShowDialog(owner) == DialogResult.OK;
    }
}

/// <summary>Shown when a pharmacy can't log in; lets the store enter a renewal key from the owner.</summary>
public class LicenseBlockedDialog : Form
{
    private readonly Tenant _tenant;
    private readonly TextBox _key;
    private readonly Label _error;

    public static bool TryUnlock(IWin32Window owner, Tenant tenant, LicenseStatus status)
    {
        using var dlg = new LicenseBlockedDialog(tenant, status);
        return dlg.ShowDialog(owner) == DialogResult.OK;
    }

    private LicenseBlockedDialog(Tenant tenant, LicenseStatus status)
    {
        _tenant = tenant;
        Text = "PharmaDesk — access blocked";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        BackColor = Theme.Surface;
        Font = Theme.Body;
        ClientSize = new Size(520, 360);
        AutoScaleMode = AutoScaleMode.None;

        var icon = new Label { Text = Theme.Glyph.Lock, Font = Theme.Icon(22), ForeColor = Theme.Danger, AutoSize = true, Location = new Point(24, 24) };
        var title = Ui.Label($"{tenant.Name} — {status.Label}", Theme.H2);
        title.Location = new Point(70, 22);
        var msg = new Label { Text = status.Message, Location = new Point(72, 56), Size = new Size(420, 70), ForeColor = Theme.TextMuted, UseMnemonic = false };

        var keyLbl = Ui.FieldLabel($"RENEWAL KEY  (pharmacy code: {tenant.Code})");
        keyLbl.Location = new Point(24, 146);
        _key = Ui.TextBox("XXXX-XXXX-XXXX-XXXX", 470);
        _key.Location = new Point(24, 168);
        _key.CharacterCasing = CharacterCasing.Upper;
        _key.Font = new Font("Consolas", 13F);
        _error = Ui.Label("", Theme.BodyBold, Theme.Danger);
        _error.Location = new Point(24, 208);
        var hint = Ui.Label("Ask your software provider for a renewal key, then enter it above.", Theme.Small, Theme.TextMuted);
        hint.Location = new Point(24, 236);

        var activate = new ModernButton("Activate", Theme.Glyph.Lock) { Location = new Point(334, 296), Width = 160 };
        activate.Click += (_, _) => Activate();
        var close = new ModernButton("Close", "", ButtonKind.Secondary) { Location = new Point(224, 296), Width = 100 };
        close.Click += (_, _) => DialogResult = DialogResult.Cancel;
        AcceptButton = activate;
        CancelButton = close;

        Controls.AddRange(new Control[] { icon, title, msg, keyLbl, _key, _error, hint, activate, close });
        if (status.State == LicenseState.ClockTampered)
        {
            // A key can't fix a wrong system date.
            keyLbl.Visible = _key.Visible = hint.Visible = activate.Visible = false;
            ClientSize = new Size(520, 210);
            close.Location = new Point(394, 150);
        }
    }

    private void Activate()
    {
        _error.Text = "";
        DateTime? expiry = null;
        if (!Ui.Try(() => expiry = Session.Master.ApplyRenewalKey(_tenant, _key.Text))) return;
        if (expiry == null) { _error.Text = "This key is not valid for this pharmacy."; _key.SelectAll(); _key.Focus(); return; }
        Ui.Info($"Thank you! {_tenant.Name} is active until {expiry:dd-MMM-yyyy}.", "Subscription renewed");
        DialogResult = DialogResult.OK;
    }
}

/// <summary>
/// Owner control panel: create pharmacies, set/extend subscriptions, disable/enable, manage logins,
/// and generate renewal keys for pharmacies running on other computers.
/// </summary>
public class OwnerForm : Form
{
    private sealed record OwnerRow(Tenant Tenant, string Code, string Pharmacy, string Contact, string Phone, int Users,
        string Status, DateTime? Expires, string DaysLeft, DateTime? LastUsed, DateTime Created);

    private readonly DataGridView _grid;
    private readonly TextBox _search;
    private readonly ComboBox _filter;
    private readonly StatCard _total, _active, _expiring, _blocked;
    private readonly ModernButton _toggle;
    private readonly Label _summary;

    private static MasterDb Db => Session.Master;

    public OwnerForm()
    {
        Text = "PharmaDesk — Owner Control Panel";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 760);
        MinimumSize = new Size(1100, 640);
        WindowState = FormWindowState.Maximized;
        BackColor = Theme.Background;
        Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.None;

        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Theme.Sidebar };
        var hTitle = Ui.Label("Owner Control Panel", new Font("Segoe UI Semibold", 18F), Color.White);
        hTitle.Location = new Point(28, 12);
        var hSub = Ui.Label("Create pharmacies, control subscriptions and logins, and generate renewal keys.", Theme.Small, Theme.SidebarText);
        hSub.Location = new Point(30, 46);
        var exit = new ModernButton("Sign out", Theme.Glyph.Power, ButtonKind.Danger) { Anchor = AnchorStyles.Top | AnchorStyles.Right };
        exit.Click += (_, _) => Close();
        header.Controls.AddRange(new Control[] { hTitle, hSub, exit });
        header.Resize += (_, _) => exit.Location = new Point(header.Width - exit.Width - 24, 20);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 18), BackColor = Theme.Background };

        var stats = new TableLayoutPanel { Dock = DockStyle.Top, Height = 126, ColumnCount = 4, BackColor = Theme.Background };
        for (int i = 0; i < 4; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        stats.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        StatCard Card(string caption, string glyph, Color color, bool last = false) =>
            new() { Caption = caption, Glyph = glyph, AccentColor = color, Dock = DockStyle.Fill, Margin = new Padding(0, 0, last ? 0 : 14, 14) };
        _total = Card("Pharmacies", Theme.Glyph.Home, Theme.Info);
        _active = Card("Active", Theme.Glyph.Heart, Theme.Success);
        _expiring = Card("Expiring in 7 days", Theme.Glyph.Clock, Theme.Warning);
        _blocked = Card("Expired / Disabled", Theme.Glyph.Lock, Theme.Danger, true);
        stats.Controls.Add(_total, 0, 0);
        stats.Controls.Add(_active, 1, 0);
        stats.Controls.Add(_expiring, 2, 0);
        stats.Controls.Add(_blocked, 3, 0);

        var filters = new Card { Dock = DockStyle.Top, Height = 88, Padding = new Padding(18, 12, 18, 8) };
        var flow = Ui.Toolbar();
        flow.Dock = DockStyle.Fill;
        _search = Ui.TextBox("Pharmacy name, code or phone", 280);
        _filter = Ui.Combo(new object[] { "All", "Active", "Expiring", "Expired", "Disabled", "Not activated / Invalid" }, 200);
        _filter.SelectedIndex = 0;
        flow.Controls.Add(Ui.Field("SEARCH", _search));
        flow.Controls.Add(Ui.Field("STATUS", _filter));
        var keyBtn = new ModernButton("Renewal key for another computer", Theme.Glyph.Lock, ButtonKind.Secondary) { Margin = new Padding(0, 20, 8, 0) };
        keyBtn.Click += (_, _) => RenewalKeyDialog.Show(this, null);
        flow.Controls.Add(keyBtn);
        filters.Controls.Add(flow);
        _search.TextChanged += (_, _) => LoadData();
        _filter.SelectedIndexChanged += (_, _) => LoadData();

        _grid = Ui.Grid();
        _grid.Col("Code", "Code", 100);
        _grid.Col("Pharmacy", "Pharmacy", 200, fill: true);
        _grid.Col("Contact", "Contact", 130);
        _grid.Col("Phone", "Phone", 120);
        _grid.Col("Users", "Logins", 65, "N0", right: true);
        _grid.Col("Status", "Status", 120);
        _grid.DateCol("Expires", "Expires on", 110);
        _grid.Col("DaysLeft", "Days left", 80, right: true);
        _grid.DateCol("LastUsed", "Last used", 105);
        _grid.DateCol("Created", "Created", 105);
        _grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Status" && e.Value is string s)
                Pill.Paint(e, s, s == "Active" ? Theme.Success : s == "Expiring" ? Theme.Warning : s == "Disabled" ? Theme.TextMuted : Theme.Danger);
        };
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Edit(); };
        _grid.SelectionChanged += (_, _) => UpdateToggle();

        var add = new ModernButton("New Pharmacy", Theme.Glyph.Add);
        add.Click += (_, _) => { if (PharmacyDialog.Create(this)) LoadData(); };
        var edit = new ModernButton("Edit", Theme.Glyph.Edit, ButtonKind.Secondary);
        edit.Click += (_, _) => Edit();
        var users = new ModernButton("Logins", Theme.Glyph.People, ButtonKind.Secondary);
        users.Click += (_, _) => { if (Selected is { } t) { OwnerUsersDialog.Show(this, t); LoadData(); } };
        var extend = new ModernButton("Extend ▾", Theme.Glyph.Calendar, ButtonKind.Success);
        extend.Click += (_, _) => ExtendMenu().Show(extend, new Point(0, extend.Height));
        _toggle = new ModernButton("Disable", Theme.Glyph.Lock, ButtonKind.Danger);
        _toggle.Click += (_, _) => ToggleActive();
        var key = new ModernButton("Renewal Key", Theme.Glyph.Tag, ButtonKind.Secondary);
        key.Click += (_, _) => { if (Selected is { } t) RenewalKeyDialog.Show(this, t); };
        var export = new ModernButton("Export for Customer", Theme.Glyph.Folder, ButtonKind.Info);
        export.Click += (_, _) => Export();
        var remove = new ModernButton("Remove", Theme.Glyph.Delete, ButtonKind.Secondary);
        remove.Click += (_, _) => Remove();

        var card = Ui.GridCard("Pharmacies on this computer", _grid);
        _summary = (Label)card.Tag!;

        // Actions for the selected pharmacy get their own row so they never crowd the grid title.
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, BackColor = Theme.Background, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        foreach (var b in new Control[] { add, edit, users, extend, _toggle, export, key, remove }) { b.Margin = new Padding(0, 0, 10, 0); actions.Controls.Add(b); }

        body.Controls.Add(card);
        body.Controls.Add(actions);
        body.Controls.Add(filters);
        body.Controls.Add(stats);
        Controls.Add(body);
        Controls.Add(header);

        Load += (_, _) => LoadData();
    }

    private Tenant? Selected => _grid.Selected<OwnerRow>()?.Tenant;

    private void LoadData()
    {
        var q = _search.Text.Trim();
        var f = _filter.SelectedItem as string ?? "All";
        var userCounts = Db.Users.Rows.GroupBy(u => u.TenantId).ToDictionary(g => g.Key, g => g.Count());

        var all = Db.Tenants.Rows.Select(t =>
        {
            var st = LicenseService.Check(t);
            var days = st.State == LicenseState.Active ? (st.DaysLeft?.ToString() ?? "∞") : st.State == LicenseState.Expired ? $"{(int)(t.ExpiresOn!.Value - DateTime.Today).TotalDays}" : "—";
            return new OwnerRow(t, t.Code, t.Name, t.ContactPerson, t.Phone, userCounts.TryGetValue(t.Id, out var c) ? c : 0,
                st.Label, t.ExpiresOn, days, t.LastSeen, t.CreatedAt);
        }).ToList();

        _total.Value = all.Count.ToString();
        _total.Hint = $"{Db.Users.Rows.Count} login(s) in total";
        _active.Value = all.Count(r => r.Status is "Active" or "Expiring").ToString();
        _active.Hint = "Can log in today";
        _expiring.Value = all.Count(r => r.Status == "Expiring").ToString();
        _expiring.Hint = "Renew soon";
        _blocked.Value = all.Count(r => r.Status is not ("Active" or "Expiring")).ToString();
        _blocked.Hint = "Cannot log in";

        var rows = all
            .Where(r => q.Length == 0 || r.Pharmacy.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Code.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Phone.Contains(q))
            .Where(r => f switch
            {
                "Active" => r.Status is "Active" or "Expiring",
                "Expiring" => r.Status == "Expiring",
                "Expired" => r.Status == "Expired",
                "Disabled" => r.Status == "Disabled",
                "Not activated / Invalid" => r.Status is "Not activated" or "Invalid" or "Clock changed",
                _ => true
            })
            .OrderBy(r => r.Pharmacy)
            .ToList();
        _grid.Bind(rows);
        _summary.Text = $"{rows.Count} pharmacy(ies) · data folder: {ExcelDatabase.DataFolder}";
        UpdateToggle();
    }

    private void UpdateToggle()
    {
        var t = Selected;
        var disabled = t != null && !t.IsActive;
        _toggle.Text = disabled ? "Enable" : "Disable";
        _toggle.Kind = disabled ? ButtonKind.Success : ButtonKind.Danger;
        _toggle.AutoSizeToText();
    }

    private void Edit()
    {
        if (Selected is { } t && PharmacyDialog.Edit(this, t)) LoadData();
    }

    private ContextMenuStrip ExtendMenu()
    {
        var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
        void Months(string text, int months) => menu.Items.Add(text, null, (_, _) => Extend(t => ExtendFrom(t).AddMonths(months)));
        Months("+ 1 month", 1);
        Months("+ 3 months", 3);
        Months("+ 6 months", 6);
        Months("+ 1 year", 12);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Set expiry date…", null, (_, _) =>
        {
            if (Selected is not { } t) return;
            using var d = new FormDialog("Set expiry date", t.Name, 1, 380);
            d.AddDate("date", "LAST DAY THE PHARMACY CAN LOG IN", t.ExpiresOn ?? DateTime.Today.AddMonths(1));
            if (d.ShowDialog(this) == DialogResult.OK) Extend(_ => d.DateValue("date").Date);
        });
        menu.Items.Add("Remove expiry (unlimited)", null, (_, _) => Extend(_ => null));
        menu.Items.Add("Expire now (block logins)", null, (_, _) => Extend(_ => DateTime.Today.AddDays(-1)));
        return menu;
    }

    /// <summary>Extensions start from the current expiry if it's still in the future, otherwise from today.</summary>
    private static DateTime ExtendFrom(Tenant t) => t.ExpiresOn.HasValue && t.ExpiresOn.Value.Date >= DateTime.Today ? t.ExpiresOn.Value.Date : DateTime.Today;

    private void Extend(Func<Tenant, DateTime?> newExpiry)
    {
        if (Selected is not { } t) { Ui.Info("Select a pharmacy first."); return; }
        var expiry = newExpiry(t);
        if (Ui.Try(() => Db.UpdateLicense(t, x => { x.ExpiresOn = expiry; if (x.LastSeen > DateTime.Today) x.LastSeen = DateTime.Today; })))
        {
            LoadData();
            Ui.Info(expiry.HasValue ? $"{t.Name} can log in until {expiry:dd-MMM-yyyy}." : $"{t.Name} now has no expiry date.");
        }
    }

    private void ToggleActive()
    {
        if (Selected is not { } t) return;
        var enable = !t.IsActive;
        if (!enable && !Ui.Confirm($"Disable '{t.Name}'?\n\nAll of its logins will be blocked immediately on this computer. Its data is kept.")) return;
        if (Ui.Try(() => Db.UpdateLicense(t, x => x.IsActive = enable))) LoadData();
    }

    /// <summary>Builds a zip for one customer: exe + Data folder holding only that pharmacy.</summary>
    private void Export()
    {
        if (Selected is not { } t) { Ui.Info("Select a pharmacy first."); return; }
        var st = LicenseService.Check(t);
        if (!st.CanLogin && !Ui.Confirm($"'{t.Name}' is currently {st.Label.ToLower()} - the customer would be blocked on first run.\n\nExport anyway?")) return;
        using var dlg = new FolderBrowserDialog { Description = $"Choose where to create the package for {t.Name}", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var folder = Path.Combine(dlg.SelectedPath, "PharmaDesk_" + t.Code);
        if (File.Exists(Path.Combine(folder, "Data", t.Code + ".xlsx")) &&
            !Ui.Confirm("A package for this customer already exists there.\n\nRefresh its licence and logins? (The customer's business data is kept.)")) return;
        string? zip = null;
        if (!Ui.Try(() => zip = CustomerPackage.Create(Db, t, folder))) return;
        Ui.Info($"Package ready for {t.Name}:\n\n{zip}\n\nSend this zip. The customer unzips it anywhere and runs PharmaDesk.exe.\nIt contains only this pharmacy and has no owner panel.", "Package created");
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{zip}\"");
    }

    private void Remove()
    {
        if (Selected is not { } t) return;
        if (!Ui.Confirm($"Remove '{t.Name}' ({t.Code}) and all its logins from this computer?\n\nIts data workbook is moved to Data\\Backups, not deleted.")) return;
        if (!Ui.Confirm("Are you sure? The pharmacy will no longer appear on the login screen.")) return;
        if (Ui.Try(() => Db.RemovePharmacy(t))) LoadData();
    }
}

/// <summary>Create or edit a pharmacy (owner only).</summary>
public static class PharmacyDialog
{
    private static readonly string[] Plans = { "1 month", "3 months", "6 months", "1 year", "No expiry" };

    public static bool Create(IWin32Window owner)
    {
        using var d = new FormDialog("New pharmacy", "Creates the pharmacy, its Admin login and its own Excel data file.", 2, 700);
        var name = d.AddText("name", "PHARMACY NAME *", "");
        var code = d.AddText("code", "PHARMACY CODE * (used in renewal keys)", "");
        d.AddText("contact", "CONTACT PERSON", "");
        d.AddText("phone", "PHONE", "");
        d.AddText("license", "DRUG LICENSE NO.", "");
        d.AddComboFixed("plan", "SUBSCRIPTION", Plans, "1 month");
        d.AddText("address", "ADDRESS", "", fullRow: true);
        d.AddText("admin", "ADMIN FULL NAME *", "");
        d.AddText("user", "ADMIN USERNAME *", "admin");
        d.AddText("pass", "ADMIN PASSWORD * (min 4)", "", password: true);
        d.AddText("pass2", "CONFIRM PASSWORD *", "", password: true);
        d.AddCheck("allowUsers", "Pharmacy admin may create staff logins", false);

        var codeEdited = false;
        code.KeyPress += (_, _) => codeEdited = true;
        name.TextChanged += (_, _) => { if (!codeEdited) code.Text = Session.Master.SuggestCode(name.Text); };

        Tenant? created = null;
        d.Validator = x =>
        {
            if (x.Str("name").Length < 2) return "Enter the pharmacy name.";
            if (x.Str("code").Length < 2) return "Enter a short pharmacy code (letters/numbers).";
            if (Session.Master.Tenants.Rows.Any(t => t.Code.Equals(x.Str("code"), StringComparison.OrdinalIgnoreCase))) return "This pharmacy code is already used.";
            if (x.Str("admin").Length < 2) return "Enter the admin's full name.";
            if (x.Str("user").Length < 3) return "Username must be at least 3 characters.";
            if (x["pass"].Text.Length < 4) return "Password must be at least 4 characters.";
            if (x["pass"].Text != x["pass2"].Text) return "Passwords do not match.";

            DateTime? expiry = x.Str("plan") switch
            {
                "1 month" => DateTime.Today.AddMonths(1),
                "3 months" => DateTime.Today.AddMonths(3),
                "6 months" => DateTime.Today.AddMonths(6),
                "1 year" => DateTime.Today.AddYears(1),
                _ => null
            };
            var ok = Ui.Try(() => created = Session.Master.RegisterPharmacy(new Tenant
            {
                Name = x.Str("name"), Code = x.Str("code"), ContactPerson = x.Str("contact"), Phone = x.Str("phone"),
                LicenseNo = x.Str("license"), Address = x.Str("address"), ExpiresOn = expiry, AllowUserManagement = x.Bool("allowUsers")
            }, x.Str("admin"), x.Str("user"), x["pass"].Text).tenant);
            return ok ? null : "The pharmacy could not be created.";
        };
        if (d.ShowDialog(owner) != DialogResult.OK || created == null) return false;
        Ui.Info($"Pharmacy '{created.Name}' created.\n\nCode: {created.Code}\nAdmin username: {d.Str("user")}\n" +
                (created.ExpiresOn.HasValue ? $"Subscription until: {created.ExpiresOn:dd-MMM-yyyy}" : "No expiry"), "Pharmacy created");
        return true;
    }

    public static bool Edit(IWin32Window owner, Tenant t)
    {
        using var d = new FormDialog("Edit pharmacy", $"Code {t.Code} — use Extend / Disable for the subscription.", 2, 680);
        d.AddText("name", "PHARMACY NAME *", t.Name);
        d.AddText("contact", "CONTACT PERSON", t.ContactPerson);
        d.AddText("phone", "PHONE", t.Phone);
        d.AddText("license", "DRUG LICENSE NO.", t.LicenseNo);
        d.AddText("address", "ADDRESS", t.Address, fullRow: true);
        d.AddText("notes", "OWNER NOTES (payments, plan…)", t.Notes, fullRow: true);
        d.AddCheck("allowUsers", "Pharmacy admin may create staff logins", t.AllowUserManagement);
        d.Validator = x => x.Str("name").Length < 2 ? "Enter the pharmacy name." : null;
        if (d.ShowDialog(owner) != DialogResult.OK) return false;
        return Ui.Try(() => Session.Master.UpdateLicense(t, x =>
        {
            x.Name = d.Str("name"); x.ContactPerson = d.Str("contact"); x.Phone = d.Str("phone");
            x.LicenseNo = d.Str("license"); x.Address = d.Str("address"); x.Notes = d.Str("notes");
            x.AllowUserManagement = d.Bool("allowUsers");
        }));
    }
}

/// <summary>Owner view of one pharmacy's logins.</summary>
public class OwnerUsersDialog : Form
{
    private sealed record Row(AppUser User, string FullName, string Username, string Role, string Status, DateTime? LastLogin);

    private readonly Tenant _tenant;
    private readonly DataGridView _grid;

    public static void Show(IWin32Window owner, Tenant tenant)
    {
        using var d = new OwnerUsersDialog(tenant);
        d.ShowDialog(owner);
    }

    private OwnerUsersDialog(Tenant tenant)
    {
        _tenant = tenant;
        Text = $"Logins — {tenant.Name}";
        Size = new Size(860, 520);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        BackColor = Theme.Background;
        Font = Theme.Body;
        Padding = new Padding(16);

        _grid = Ui.Grid();
        _grid.Col("FullName", "Full name", 180, fill: true);
        _grid.Col("Username", "Username", 140);
        _grid.Col("Role", "Role", 110);
        _grid.Col("Status", "Status", 90);
        _grid.DateCol("LastLogin", "Last login", 150, time: true);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditSelected(); };

        var add = new ModernButton("Add Login", Theme.Glyph.Add);
        add.Click += (_, _) => { if (UserEditor.Edit(this, _tenant.Id, null, 0)) LoadData(); };
        var edit = new ModernButton("Edit / Reset password", Theme.Glyph.Edit, ButtonKind.Secondary);
        edit.Click += (_, _) => EditSelected();
        var toggle = new ModernButton("Enable / Disable", Theme.Glyph.Lock, ButtonKind.Secondary);
        toggle.Click += (_, _) =>
        {
            if (_grid.Selected<Row>() is not { } r) return;
            if (Ui.Try(() => Session.Master.Commit(() => r.User.IsActive = !r.User.IsActive))) LoadData();
        };
        var del = new ModernButton("Delete", Theme.Glyph.Delete, ButtonKind.Secondary);
        del.Click += (_, _) =>
        {
            if (_grid.Selected<Row>() is not { } r || !Ui.Confirm($"Delete login '{r.Username}'?")) return;
            if (Ui.Try(() => Session.Master.Commit(() => Session.Master.Users.Remove(r.User)))) LoadData();
        };
        Controls.Add(Ui.GridCard($"{tenant.Name} ({tenant.Code})", _grid, del, toggle, edit, add));
        Load += (_, _) => LoadData();
    }

    private void EditSelected()
    {
        if (_grid.Selected<Row>() is { } r && UserEditor.Edit(this, _tenant.Id, r.User, 0)) LoadData();
    }

    private void LoadData() =>
        _grid.Bind(Session.Master.Users.Rows.Where(u => u.TenantId == _tenant.Id).OrderBy(u => u.FullName)
            .Select(u => new Row(u, u.FullName, u.Username, u.Role, u.IsActive ? "Active" : "Disabled", u.LastLogin)));
}

/// <summary>Generates a renewal key for a pharmacy (here or on another computer, by its code).</summary>
public static class RenewalKeyDialog
{
    public static void Show(IWin32Window owner, Tenant? tenant)
    {
        using var d = new FormDialog("Generate renewal key",
            "The store enters this key on its 'access blocked' screen to unlock until the chosen date.", 2, 560);
        d.AddText("code", "PHARMACY CODE *", tenant?.Code ?? "");
        var start = tenant?.ExpiresOn is { } e && e.Date >= DateTime.Today ? e.Date : DateTime.Today;
        d.AddDate("date", "VALID UNTIL", start.AddMonths(1));
        var keyBox = d.AddText("key", "RENEWAL KEY (copy and send to the store)", "", fullRow: true);
        keyBox.ReadOnly = true;
        keyBox.Font = new Font("Consolas", 13F);
        if (tenant != null) ((TextBox)d["code"]).ReadOnly = true;

        void Generate()
        {
            var code = d.Str("code");
            keyBox.Text = code.Length < 2 ? "" : LicenseService.CreateRenewalKey(code, d.DateValue("date"));
        }
        d["code"].TextChanged += (_, _) => Generate();
        ((DateTimePicker)d["date"]).ValueChanged += (_, _) => Generate();
        Generate();

        d.Validator = x =>
        {
            if (keyBox.Text.Length == 0) return "Enter the pharmacy code.";
            Clipboard.SetText($"PharmaDesk renewal key for {x.Str("code").ToUpperInvariant()} (valid until {x.DateValue("date"):dd-MMM-yyyy}): {keyBox.Text}");
            Ui.Info("Key copied to the clipboard — paste it into WhatsApp / SMS / email for the store.");
            return null;
        };
        d.ShowDialog(owner);
    }
}
