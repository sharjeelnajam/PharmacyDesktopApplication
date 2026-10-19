using PharmacyDesktop.Core;
using PharmacyDesktop.UI.Controls;
using PharmacyDesktop.UI.Forms;

namespace PharmacyDesktop.UI.Pages;

/// <summary>Shared layout for simple "search + list + add/edit/delete" pages.</summary>
public abstract class ListPage<TRow> : PageBase where TRow : class
{
    protected readonly TextBox Search;
    protected readonly DataGridView Grid;
    protected readonly Label Summary;

    protected ListPage(string cardTitle, string searchHint, string addText)
    {
        var filters = new Card { Dock = DockStyle.Top, Height = 88, Padding = new Padding(18, 12, 18, 8) };
        var flow = Ui.Toolbar();
        flow.Dock = DockStyle.Fill;
        Search = Ui.TextBox(searchHint, 300);
        flow.Controls.Add(Ui.Field("SEARCH", Search));
        filters.Controls.Add(flow);
        Search.TextChanged += (_, _) => LoadData();

        Grid = Ui.Grid();
        Grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && Grid.Selected<TRow>() is { } r) OnEdit(r); };

        var add = new ModernButton(addText, Theme.Glyph.Add);
        add.Click += (_, _) => OnAdd();
        var edit = new ModernButton("Edit", Theme.Glyph.Edit, ButtonKind.Secondary);
        edit.Click += (_, _) => { if (Grid.Selected<TRow>() is { } r) OnEdit(r); };
        var del = new ModernButton("Delete", Theme.Glyph.Delete, ButtonKind.Secondary);
        del.Click += (_, _) => { if (Grid.Selected<TRow>() is { } r) OnDelete(r); };
        var excel = new ModernButton("Excel", Theme.Glyph.Excel, ButtonKind.Success);
        excel.Click += (_, _) => GridHelper.ExportToExcel(Grid, cardTitle);
        var print = new ModernButton("Print", Theme.Glyph.Print, ButtonKind.Secondary);
        print.Click += (_, _) => GridHelper.Print(Grid, cardTitle);

        var card = Ui.GridCard(cardTitle, Grid, add, edit, del, excel, print);
        Summary = (Label)card.Tag!;

        Controls.Add(card);
        Controls.Add(Ui.Spacer(14));
        Controls.Add(filters);
    }

    public override void OnPageShown() => LoadData();
    protected abstract void LoadData();
    protected abstract void OnAdd();
    protected abstract void OnEdit(TRow row);
    protected abstract void OnDelete(TRow row);
    protected bool Match(params string[] values) => Search.Text.Trim() is var q && (q.Length == 0 || values.Any(v => v.Contains(q, StringComparison.OrdinalIgnoreCase)));
}

// ─────────────────────────────── Suppliers ───────────────────────────────

public sealed record SupplierRow(Supplier Supplier, string Name, string Contact, string Phone, string Email, string Address, int Purchases, decimal Total, DateTime? Last, string Status);

public class SuppliersPage : ListPage<SupplierRow>
{
    public override string Title => "Suppliers";
    public override string Subtitle => "Distributors and companies you buy from";

    public SuppliersPage() : base("Suppliers", "Name, contact or phone", "Add Supplier")
    {
        Grid.Col("Name", "Supplier", 200, fill: true);
        Grid.Col("Contact", "Contact person", 140);
        Grid.Col("Phone", "Phone", 120);
        Grid.Col("Email", "Email", 160);
        Grid.Col("Address", "Address", 180);
        Grid.Col("Purchases", "Purchases", 85, "N0", right: true);
        Grid.Money("Total", "Total purchased", 120);
        Grid.DateCol("Last", "Last purchase", 110);
        Grid.Col("Status", "Status", 90);
    }

    protected override void LoadData()
    {
        var byId = Session.Db.Purchases.Rows.GroupBy(p => p.SupplierId).ToDictionary(g => g.Key, g => (Count: g.Count(), Total: g.Sum(p => p.GrandTotal), Last: g.Max(p => p.Date)));
        var rows = Session.Db.Suppliers.Rows.Where(s => Match(s.Name, s.ContactPerson, s.Phone)).OrderBy(s => s.Name).Select(s =>
        {
            var has = byId.TryGetValue(s.Id, out var st);
            return new SupplierRow(s, s.Name, s.ContactPerson, s.Phone, s.Email, s.Address, has ? st.Count : 0, has ? st.Total : 0, has ? st.Last : null, s.IsActive ? "Active" : "Inactive");
        }).ToList();
        Grid.Bind(rows);
        Summary.Text = $"{rows.Count} supplier(s)";
    }

    protected override void OnAdd() { if (EditSupplier(FindForm(), null) != null) LoadData(); }
    protected override void OnEdit(SupplierRow row) { if (EditSupplier(FindForm(), row.Supplier) != null) LoadData(); }

    protected override void OnDelete(SupplierRow row)
    {
        if (row.Purchases > 0)
        {
            if (Ui.Confirm($"'{row.Name}' has purchase history and can't be deleted. Mark as inactive?"))
                Ui.Try(() => Session.Db.Commit(() => row.Supplier.IsActive = false));
        }
        else if (Ui.Confirm($"Delete supplier '{row.Name}'?"))
            Ui.Try(() => Session.Db.Commit(() => Session.Db.Suppliers.Remove(row.Supplier)));
        LoadData();
    }

    public static Supplier? EditSupplier(IWin32Window? owner, Supplier? supplier)
    {
        var s = supplier ?? new Supplier();
        using var d = new FormDialog(supplier == null ? "Add supplier" : "Edit supplier", null, 2, 640);
        d.AddText("name", "SUPPLIER / COMPANY NAME *", s.Name, fullRow: true);
        d.AddText("contact", "CONTACT PERSON", s.ContactPerson);
        d.AddText("phone", "PHONE", s.Phone);
        d.AddText("email", "EMAIL", s.Email);
        d.AddCheck("active", "Active", s.IsActive);
        d.AddText("address", "ADDRESS", s.Address, fullRow: true);
        d.Validator = x =>
        {
            if (x.Str("name").Length < 2) return "Enter the supplier name.";
            if (Session.Db.Suppliers.Rows.Any(o => o != supplier && o.Name.Equals(x.Str("name"), StringComparison.OrdinalIgnoreCase))) return "A supplier with this name already exists.";
            return null;
        };
        if (d.ShowDialog(owner) != DialogResult.OK) return null;
        var ok = Ui.Try(() => Session.Db.Commit(() =>
        {
            s.Name = d.Str("name"); s.ContactPerson = d.Str("contact"); s.Phone = d.Str("phone");
            s.Email = d.Str("email"); s.Address = d.Str("address"); s.IsActive = d.Bool("active");
            if (supplier == null) Session.Db.Suppliers.Add(s);
        }));
        return ok ? Session.Db.Suppliers.Rows.FirstOrDefault(x => x.Name == s.Name) : null;
    }
}

// ─────────────────────────────── Patients ───────────────────────────────

public sealed record PatientRow(Customer Customer, string MrNo, string Name, string Phone, string Gender, int Age, string Address, int Visits, decimal Spent, decimal Due, DateTime? LastVisit);

public class PatientsPage : ListPage<PatientRow>
{
    public override string Title => "Patients";
    public override string Subtitle => "Customers, visit history and credit balances";

    public PatientsPage() : base("Patients", "Name, MR # or phone", "Add Patient")
    {
        Grid.Col("MrNo", "MR #", 90);
        Grid.Col("Name", "Name", 180, fill: true);
        Grid.Col("Phone", "Phone", 120);
        Grid.Col("Gender", "Gender", 75);
        Grid.Col("Age", "Age", 55, "N0", right: true);
        Grid.Col("Address", "Address", 180);
        Grid.Col("Visits", "Visits", 65, "N0", right: true);
        Grid.Money("Spent", "Total spent", 110);
        Grid.Money("Due", "Due", 90);
        Grid.DateCol("LastVisit", "Last visit", 105);
        Grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && Grid.Columns[e.ColumnIndex].Name == "Due" && e.Value is decimal d && d > 0)
                e.CellStyle!.ForeColor = Theme.Danger;
        };
    }

    protected override void LoadData()
    {
        var stats = Session.Db.Sales.Rows.Where(s => s.CustomerId > 0).GroupBy(s => s.CustomerId)
            .ToDictionary(g => g.Key, g => (Visits: g.Count(), Spent: g.Sum(s => s.GrandTotal - s.Returned), Due: g.Sum(s => s.Due), Last: g.Max(s => s.Date)));
        var rows = Session.Db.Customers.Rows.Where(c => Match(c.Name, c.MrNo, c.Phone)).OrderBy(c => c.Name).Select(c =>
        {
            var has = stats.TryGetValue(c.Id, out var st);
            return new PatientRow(c, c.MrNo, c.Name, c.Phone, c.Gender, c.Age, c.Address, has ? st.Visits : 0, has ? st.Spent : 0, has ? st.Due : 0, has ? st.Last : null);
        }).ToList();
        Grid.Bind(rows);
        Summary.Text = $"{rows.Count} patient(s) · total due {Session.Money(rows.Sum(r => r.Due))}";
    }

    protected override void OnAdd() { if (EditCustomer(FindForm(), null) != null) LoadData(); }
    protected override void OnEdit(PatientRow row) { if (EditCustomer(FindForm(), row.Customer) != null) LoadData(); }

    protected override void OnDelete(PatientRow row)
    {
        if (row.Visits > 0) { Ui.Error($"'{row.Name}' has sales history and can't be deleted."); return; }
        if (Ui.Confirm($"Delete patient '{row.Name}'?"))
            Ui.Try(() => Session.Db.Commit(() => Session.Db.Customers.Remove(row.Customer)));
        LoadData();
    }

    public static Customer? EditCustomer(IWin32Window? owner, Customer? customer)
    {
        var c = customer ?? new Customer { MrNo = Session.Db.NextMrNo() };
        using var d = new FormDialog(customer == null ? "Add patient" : "Edit patient", $"MR # {c.MrNo}", 2, 600);
        d.AddText("name", "FULL NAME *", c.Name);
        d.AddText("phone", "PHONE", c.Phone);
        d.AddComboFixed("gender", "GENDER", new[] { "", "Male", "Female", "Other" }, c.Gender);
        d.AddNumber("age", "AGE", c.Age, 0, 130);
        d.AddText("address", "ADDRESS", c.Address, fullRow: true);
        d.Validator = x => x.Str("name").Length < 2 ? "Enter the patient's name." : null;
        if (d.ShowDialog(owner) != DialogResult.OK) return null;
        var ok = Ui.Try(() => Session.Db.Commit(() =>
        {
            c.Name = d.Str("name"); c.Phone = d.Str("phone"); c.Gender = d.Str("gender"); c.Age = d.Int("age"); c.Address = d.Str("address");
            if (customer == null) Session.Db.Customers.Add(c);
        }));
        return ok ? Session.Db.Customers.Rows.FirstOrDefault(x => x.MrNo == c.MrNo) : null;
    }
}

// ─────────────────────────────── Missed sales ───────────────────────────────

public sealed record MissedRow(MissedSale Missed, DateTime Date, string Item, int Qty, string Customer, string Phone, string Notes, string User);

public class MissedSalesPage : ListPage<MissedRow>
{
    public override string Title => "Missed Sales";
    public override string Subtitle => "Demand you couldn't fulfil — use it to plan purchases";

    public MissedSalesPage() : base("Missed sales", "Medicine or customer", "Log Missed Sale")
    {
        Grid.DateCol("Date", "Date", 150, time: true);
        Grid.Col("Item", "Medicine requested", 200, fill: true);
        Grid.Col("Qty", "Qty", 60, "N0", right: true);
        Grid.Col("Customer", "Customer", 150);
        Grid.Col("Phone", "Phone", 120);
        Grid.Col("Notes", "Notes", 200);
        Grid.Col("User", "Logged by", 110);
    }

    protected override void LoadData()
    {
        var rows = Session.Db.MissedSales.Rows.Where(m => Match(m.ItemName, m.CustomerName)).OrderByDescending(m => m.Date)
            .Select(m => new MissedRow(m, m.Date, m.ItemName, m.Qty, m.CustomerName, m.Phone, m.Notes, m.CreatedBy)).ToList();
        Grid.Bind(rows);
        var top = rows.Where(r => r.Date >= DateTime.Today.AddDays(-30)).GroupBy(r => r.Item, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).FirstOrDefault();
        Summary.Text = $"{rows.Count} record(s)" + (top != null ? $" · most requested (30 days): {top.Key} ×{top.Count()}" : "");
    }

    protected override void OnAdd() { if (AddMissedSale(FindForm())) LoadData(); }

    protected override void OnEdit(MissedRow row)
    {
        var m = row.Missed;
        using var d = Dialog("Edit missed sale", m);
        if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
        Ui.Try(() => Session.Db.Commit(() => Apply(d, m)));
        LoadData();
    }

    protected override void OnDelete(MissedRow row)
    {
        if (Ui.Confirm("Delete this record?")) Ui.Try(() => Session.Db.Commit(() => Session.Db.MissedSales.Remove(row.Missed)));
        LoadData();
    }

    private static FormDialog Dialog(string title, MissedSale m)
    {
        var d = new FormDialog(title, "Record a medicine a customer asked for but you didn't have.", 2, 600);
        d.AddCombo("item", "MEDICINE *", Session.Db.Items.Rows.Select(i => i.Name).Concat(Session.Db.MissedSales.Rows.Select(x => x.ItemName)), m.ItemName, editable: true);
        d.AddNumber("qty", "QUANTITY", Math.Max(1, m.Qty), 0, 100_000, 1);
        d.AddText("customer", "CUSTOMER NAME", m.CustomerName);
        d.AddText("phone", "PHONE (to call back)", m.Phone);
        d.AddText("notes", "NOTES", m.Notes, fullRow: true);
        d.Validator = x => x.Str("item").Length < 2 ? "Enter the medicine name." : null;
        return d;
    }

    private static void Apply(FormDialog d, MissedSale m)
    {
        m.ItemName = d.Str("item"); m.Qty = d.Int("qty"); m.CustomerName = d.Str("customer"); m.Phone = d.Str("phone"); m.Notes = d.Str("notes");
    }

    public static bool AddMissedSale(IWin32Window? owner)
    {
        var m = new MissedSale { CreatedBy = Session.User.FullName };
        using var d = Dialog("Log missed sale", m);
        if (d.ShowDialog(owner) != DialogResult.OK) return false;
        return Ui.Try(() => Session.Db.Commit(() => { Apply(d, m); Session.Db.MissedSales.Add(m); }));
    }
}
