using ClosedXML.Excel;
using PharmacyDesktop.Core;
using PharmacyDesktop.UI.Controls;
using PharmacyDesktop.UI.Forms;

namespace PharmacyDesktop.UI.Pages;

/// <summary>Medicine master list with stock levels, filters, add/edit and Excel import.</summary>
public class ItemsPage : PageBase
{
    private readonly TextBox _search;
    private readonly ComboBox _category, _manufacturer, _stockLevel, _status;
    private readonly DataGridView _grid;
    private readonly Label _summary;
    private bool _loading;

    public override string Title => "Medicines / Items";
    public override string Subtitle => "Your product catalogue with live stock levels";

    private sealed record ItemRow(Item Item, bool Active, string Name, string Generic, string Barcode, string Category, string Manufacturer,
        string Rack, int PackSize, int StockUnits, string Stock, decimal Cost, decimal Retail, decimal UnitPrice, decimal Value, DateTime? Expiry, string Level);

    public static readonly string[] Units = { "Tablet", "Capsule", "Syrup", "Injection", "Drops", "Cream", "Ointment", "Sachet", "Inhaler", "Bottle", "Piece", "Strip", "Vial", "Tube" };
    public static readonly string[] DefaultCategories = { "Tablets", "Capsules", "Syrups", "Injections", "Drops", "Creams & Ointments", "Surgical", "Baby Care", "Vitamins & Supplements", "Medical Devices", "Personal Care" };

    public ItemsPage()
    {
        var filters = new Card { Dock = DockStyle.Top, Height = 88, Padding = new Padding(18, 12, 18, 8) };
        var flow = Ui.Toolbar();
        flow.Dock = DockStyle.Fill;
        _search = Ui.TextBox("Name, generic or barcode", 240);
        _category = Ui.Combo(width: 170);
        _manufacturer = Ui.Combo(width: 170);
        _stockLevel = Ui.Combo(new object[] { "All", "In stock", "Low stock", "Out of stock" }, 130);
        _status = Ui.Combo(new object[] { "Active", "Inactive", "All" }, 110);
        _stockLevel.SelectedIndex = 0;
        _status.SelectedIndex = 0;
        flow.Controls.Add(Ui.Field("SEARCH", _search));
        flow.Controls.Add(Ui.Field("CATEGORY", _category));
        flow.Controls.Add(Ui.Field("MANUFACTURER", _manufacturer));
        flow.Controls.Add(Ui.Field("STOCK LEVEL", _stockLevel));
        flow.Controls.Add(Ui.Field("STATUS", _status));
        filters.Controls.Add(flow);
        _search.TextChanged += (_, _) => LoadData();
        foreach (var c in new[] { _category, _manufacturer, _stockLevel, _status }) c.SelectedIndexChanged += (_, _) => { if (!_loading) LoadData(); };

        _grid = Ui.Grid();
        _grid.Col("Name", "Medicine", 200, fill: true, fillWeight: 160);
        _grid.Col("Generic", "Generic", 140, fill: true);
        _grid.Col("Category", "Category", 110);
        _grid.Col("Manufacturer", "Manufacturer", 120);
        _grid.Col("Rack", "Rack", 60);
        _grid.Col("PackSize", "Pack", 55, "N0", right: true);
        _grid.Col("Stock", "Stock (units)", 130);
        _grid.Money("Cost", "Cost / pack", 95);
        _grid.Money("Retail", "Retail / pack", 100);
        _grid.Money("UnitPrice", "Unit price", 85);
        _grid.Money("Value", "Stock value", 100);
        _grid.DateCol("Expiry", "Near expiry", 100);
        _grid.Col("Level", "Level", 100);
        _grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Level" && e.Value is string s)
                Pill.Paint(e, s, s == "OK" ? Theme.Success : s == "Low" ? Theme.Warning : s == "Inactive" ? Theme.TextMuted : Theme.Danger);
        };
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Expiry" || e.Value is not DateTime d) return;
            e.CellStyle!.ForeColor = d < DateTime.Today ? Theme.Danger : d < DateTime.Today.AddDays(90) ? Theme.Darken(Theme.Warning, 0.2f) : Theme.Text;
        };
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Edit(); };

        var add = new ModernButton("Add Item", Theme.Glyph.Add);
        add.Click += (_, _) => { if (EditItem(FindForm(), null) != null) LoadData(); };
        var edit = new ModernButton("Edit", Theme.Glyph.Edit, ButtonKind.Secondary);
        edit.Click += (_, _) => Edit();
        var del = new ModernButton("Delete", Theme.Glyph.Delete, ButtonKind.Secondary);
        del.Click += (_, _) => Delete();
        var import = new ModernButton("Import", Theme.Glyph.Import, ButtonKind.Secondary);
        import.Click += (_, _) => Import();
        var excel = new ModernButton("Excel", Theme.Glyph.Excel, ButtonKind.Success);
        excel.Click += (_, _) => GridHelper.ExportToExcel(_grid, "Items");
        var print = new ModernButton("Print", Theme.Glyph.Print, ButtonKind.Secondary);
        print.Click += (_, _) => GridHelper.Print(_grid, "Items & Stock");

        var card = Ui.GridCard("Catalogue", _grid, add, edit, del, import, excel, print);
        _summary = (Label)card.Tag!;

        Controls.Add(card);
        Controls.Add(Ui.Spacer(14));
        Controls.Add(filters);
    }

    public override void OnPageShown()
    {
        _loading = true;
        Fill(_category, Session.Db.Items.Rows.Select(i => i.Category));
        Fill(_manufacturer, Session.Db.Items.Rows.Select(i => i.Manufacturer));
        _loading = false;
        LoadData();
    }

    private static void Fill(ComboBox c, IEnumerable<string> values)
    {
        var current = c.SelectedItem as string;
        c.Items.Clear();
        c.Items.Add("All");
        foreach (var v in values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v)) c.Items.Add(v);
        c.SelectedItem = current != null && c.Items.Contains(current) ? current : "All";
    }

    private void LoadData()
    {
        var svc = Session.Svc;
        var stock = svc.StockMap();
        var expiry = svc.NearestExpiryMap();
        var value = Session.Db.Batches.Rows.Where(b => b.QtyRemaining > 0).GroupBy(b => b.ItemId).ToDictionary(g => g.Key, g => g.Sum(b => b.QtyRemaining * b.CostPerUnit));
        var q = _search.Text.Trim();
        var cat = _category.SelectedItem as string ?? "All";
        var man = _manufacturer.SelectedItem as string ?? "All";
        var level = _stockLevel.SelectedItem as string ?? "All";
        var status = _status.SelectedItem as string ?? "Active";

        var rows = Session.Db.Items.Rows
            .Where(i => status == "All" || (status == "Active") == i.IsActive)
            .Where(i => q.Length == 0 || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || i.GenericName.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Barcode.Equals(q, StringComparison.OrdinalIgnoreCase))
            .Where(i => cat == "All" || i.Category.Equals(cat, StringComparison.OrdinalIgnoreCase))
            .Where(i => man == "All" || i.Manufacturer.Equals(man, StringComparison.OrdinalIgnoreCase))
            .Select(i =>
            {
                var s = stock.TryGetValue(i.Id, out var v) ? v : 0;
                var lvl = !i.IsActive ? "Inactive" : s <= 0 ? "Out" : s <= svc.ReorderLevelFor(i) ? "Low" : "OK";
                return new ItemRow(i, i.IsActive, i.Name, i.GenericName, i.Barcode, i.Category, i.Manufacturer, i.Rack, i.PackSize, s,
                    PharmacyService.FormatQty(s, i.PackSize), i.PurchasePrice, i.RetailPrice, i.UnitPrice,
                    Math.Round(value.TryGetValue(i.Id, out var val) ? val : 0, 2), expiry.TryGetValue(i.Id, out var e) ? e : null, lvl);
            })
            .Where(r => level == "All" || (level == "In stock" && r.StockUnits > 0) || (level == "Low stock" && r.Level == "Low") || (level == "Out of stock" && r.StockUnits <= 0))
            .OrderBy(r => r.Name)
            .ToList();

        _grid.Bind(rows);
        _summary.Text = $"{rows.Count:N0} item(s) · stock value {Session.Money(rows.Sum(r => r.Value))}";
    }

    private void Edit()
    {
        var row = _grid.Selected<ItemRow>();
        if (row == null) return;
        if (EditItem(FindForm(), row.Item) != null) LoadData();
    }

    private void Delete()
    {
        var row = _grid.Selected<ItemRow>();
        if (row == null) return;
        var item = row.Item;
        if (Session.Svc.ItemHasTransactions(item))
        {
            if (!item.IsActive) { Ui.Info("This item is already inactive."); return; }
            if (Ui.Confirm($"'{item.Name}' has stock or sales history and can't be deleted.\nMark it as inactive instead?"))
                Ui.Try(() => Session.Db.Commit(() => item.IsActive = false));
        }
        else if (Ui.Confirm($"Delete '{item.Name}' permanently?"))
        {
            Ui.Try(() => Session.Db.Commit(() => Session.Db.Items.Remove(item)));
        }
        LoadData();
    }

    /// <summary>Add (item == null) or edit an item. Returns the saved item or null if cancelled.</summary>
    public static Item? EditItem(IWin32Window? owner, Item? item, string? presetName = null)
    {
        var db = Session.Db;
        var isNew = item == null;
        var it = item ?? new Item { Name = presetName ?? "", ReorderLevel = 0 };
        using var d = new FormDialog(isNew ? "Add new item" : "Edit item", isNew ? "Prices are per pack; stock is counted in units." : it.Name, 2, 720);
        d.AddText("name", "MEDICINE NAME *", it.Name);
        d.AddText("generic", "GENERIC / FORMULA", it.GenericName);
        d.AddCombo("category", "CATEGORY", DefaultCategories.Concat(db.Items.Rows.Select(i => i.Category)), it.Category, editable: true);
        d.AddCombo("manufacturer", "MANUFACTURER", db.Items.Rows.Select(i => i.Manufacturer), it.Manufacturer, editable: true);
        d.AddText("barcode", "BARCODE", it.Barcode);
        d.AddCombo("rack", "RACK / SHELF", db.Items.Rows.Select(i => i.Rack), it.Rack, editable: true);
        d.AddCombo("unit", "UNIT (smallest sellable)", Units.Concat(db.Items.Rows.Select(i => i.Unit)), it.Unit, editable: true);
        d.AddNumber("pack", "UNITS PER PACK (conversion)", Math.Max(1, it.PackSize), 0, 100_000, 1);
        d.AddNumber("cost", $"PURCHASE PRICE / PACK ({Session.Cur})", it.PurchasePrice, 2);
        d.AddNumber("retail", $"RETAIL PRICE / PACK ({Session.Cur}) *", it.RetailPrice, 2);
        d.AddNumber("reorder", "RE-ORDER LEVEL (units, 0 = default)", it.ReorderLevel, 0, 1_000_000);
        d.AddCheck("active", "Active (available for sale)", it.IsActive);
        d.Validator = x =>
        {
            if (x.Str("name").Length < 2) return "Enter the medicine name.";
            if (db.Items.Rows.Any(i => i != item && i.Name.Equals(x.Str("name"), StringComparison.OrdinalIgnoreCase)))
                return "An item with this name already exists.";
            var bc = x.Str("barcode");
            if (bc.Length > 0 && db.Items.Rows.Any(i => i != item && i.Barcode.Equals(bc, StringComparison.OrdinalIgnoreCase)))
                return "This barcode is already assigned to another item.";
            if (x.Num("retail") <= 0) return "Enter the retail price.";
            return null;
        };
        if (d.ShowDialog(owner) != DialogResult.OK) return null;

        var ok = Ui.Try(() => db.Commit(() =>
        {
            it.Name = d.Str("name");
            it.GenericName = d.Str("generic");
            it.Category = d.Str("category");
            it.Manufacturer = d.Str("manufacturer");
            it.Barcode = d.Str("barcode");
            it.Rack = d.Str("rack");
            it.Unit = d.Str("unit").Length > 0 ? d.Str("unit") : "Unit";
            it.PackSize = d.Int("pack");
            it.PurchasePrice = d.Num("cost");
            it.RetailPrice = d.Num("retail");
            it.ReorderLevel = d.Int("reorder");
            it.IsActive = d.Bool("active");
            if (isNew) db.Items.Add(it);
        }));
        return ok ? db.Items.Rows.FirstOrDefault(i => i.Name == it.Name) : null;
    }

    // ───── Excel import ─────

    private static readonly string[] ImportColumns =
        { "Name", "GenericName", "Category", "Manufacturer", "Barcode", "Rack", "Unit", "PackSize", "PurchasePrice", "RetailPrice", "ReorderLevel", "OpeningStockUnits", "BatchNo", "Expiry" };

    private void Import()
    {
        var choice = MessageBox.Show(
            "Import medicines from an Excel file.\n\nYes = choose a file to import\nNo = save a blank template first",
            "Import items", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.No) { SaveTemplate(); return; }
        if (choice != DialogResult.Yes) return;

        using var open = new OpenFileDialog { Filter = "Excel Workbook|*.xlsx" };
        if (open.ShowDialog() != DialogResult.OK) return;

        int added = 0, updated = 0, stocked = 0;
        Ui.Try(() =>
        {
            using var wb = new XLWorkbook(open.FileName);
            var ws = wb.Worksheets.First();
            var header = ws.FirstRowUsed() ?? throw new InvalidOperationException("The sheet is empty.");
            var cols = header.CellsUsed().ToDictionary(c => c.GetString().Trim().Replace(" ", ""), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
            if (!cols.ContainsKey("Name")) throw new InvalidOperationException("The first row must contain a 'Name' column. Use the template.");

            string S(IXLRow r, string col) => cols.TryGetValue(col, out var i) ? r.Cell(i).GetString().Trim() : "";
            decimal N(IXLRow r, string col) => cols.TryGetValue(col, out var i) && r.Cell(i).TryGetValue<double>(out var v) ? (decimal)v : 0;

            var db = Session.Db;
            var opening = new List<PurchaseLine>();
            db.Commit(() =>
            {
                foreach (var r in ws.RowsUsed().Where(r => r.RowNumber() > header.RowNumber()))
                {
                    var name = S(r, "Name");
                    if (name.Length == 0) continue;
                    var item = db.Items.Rows.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    var isNew = item == null;
                    item ??= new Item { Name = name };
                    if (S(r, "GenericName").Length > 0) item.GenericName = S(r, "GenericName");
                    if (S(r, "Category").Length > 0) item.Category = S(r, "Category");
                    if (S(r, "Manufacturer").Length > 0) item.Manufacturer = S(r, "Manufacturer");
                    if (S(r, "Barcode").Length > 0) item.Barcode = S(r, "Barcode");
                    if (S(r, "Rack").Length > 0) item.Rack = S(r, "Rack");
                    if (S(r, "Unit").Length > 0) item.Unit = S(r, "Unit");
                    if (N(r, "PackSize") > 0) item.PackSize = (int)N(r, "PackSize");
                    if (N(r, "PurchasePrice") > 0) item.PurchasePrice = N(r, "PurchasePrice");
                    if (N(r, "RetailPrice") > 0) item.RetailPrice = N(r, "RetailPrice");
                    if (N(r, "ReorderLevel") > 0) item.ReorderLevel = (int)N(r, "ReorderLevel");
                    if (isNew) { db.Items.Add(item); added++; } else updated++;

                    var qty = (int)N(r, "OpeningStockUnits");
                    if (qty > 0)
                    {
                        DateTime? exp = null;
                        if (cols.TryGetValue("Expiry", out var ec))
                        {
                            var cell = r.Cell(ec);
                            if (cell.TryGetValue<DateTime>(out var dt)) exp = dt;
                            else exp = AddStockPage.ParseExpiry(cell.GetString());
                        }
                        var pack = Math.Max(1, item.PackSize);
                        var unitCost = item.PurchasePrice / pack;
                        opening.Add(new PurchaseLine
                        {
                            ItemId = item.Id, ItemName = item.Name, BatchNo = S(r, "BatchNo"), Expiry = exp, PackSize = 1, Packs = qty,
                            UnitCost = unitCost, NetUnitCost = item.PurchasePrice, RetailPrice = item.RetailPrice / pack, Total = Math.Round(unitCost * qty, 2)
                        });
                    }
                }
            });

            if (opening.Count > 0)
            {
                // Opening stock is booked as one purchase from "Opening Stock"; keep the item pack sizes intact.
                var packs = opening.ToDictionary(l => l.ItemId, l => db.Items.Find(l.ItemId)!.PackSize);
                var prices = opening.ToDictionary(l => l.ItemId, l => (db.Items.Find(l.ItemId)!.PurchasePrice, db.Items.Find(l.ItemId)!.RetailPrice));
                Session.Svc.SavePurchase(new Purchase { SupplierName = "Opening Stock", Notes = "Imported from Excel", SubTotal = opening.Sum(l => l.Total), GrandTotal = opening.Sum(l => l.Total) }, opening);
                db.Commit(() =>
                {
                    foreach (var (id, pack) in packs)
                    {
                        var it = db.Items.Find(id)!;
                        it.PackSize = pack;
                        (it.PurchasePrice, it.RetailPrice) = prices[id];
                    }
                });
                stocked = opening.Count;
            }
        });
        Ui.Info($"Import finished.\n\nNew items: {added}\nUpdated items: {updated}\nOpening stock lines: {stocked}");
        OnPageShown();
    }

    private static void SaveTemplate()
    {
        using var dlg = new SaveFileDialog { Filter = "Excel Workbook|*.xlsx", FileName = "Items_Import_Template.xlsx" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        Ui.Try(() =>
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Items");
            for (int i = 0; i < ImportColumns.Length; i++)
            {
                ws.Cell(1, i + 1).Value = ImportColumns[i];
                ws.Cell(1, i + 1).Style.Font.Bold = true;
                ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromArgb(204, 251, 241);
            }
            object[] sample = { "Panadol 500mg", "Paracetamol", "Tablets", "GSK", "", "A1", "Tablet", 200, 380, 450, 50, 400, "PN2301", "2027-06-30" };
            for (int i = 0; i < sample.Length; i++)
                ws.Cell(2, i + 1).Value = sample[i] is int n ? n : (XLCellValue)sample[i].ToString();
            ws.Columns().AdjustToContents();
            wb.SaveAs(dlg.FileName);
        });
        Ui.Info("Template saved. Prices are per pack; OpeningStockUnits is in units.");
    }
}
