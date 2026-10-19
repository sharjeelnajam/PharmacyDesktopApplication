using System.Globalization;

namespace PharmacyDesktop.Core;

public static class SettingKeys
{
    public const string PharmacyName = "PharmacyName";
    public const string Address = "Address";
    public const string Phone = "Phone";
    public const string LicenseNo = "LicenseNo";
    public const string ReceiptFooter = "ReceiptFooter";
    public const string Currency = "Currency";
    public const string ExpiryAlertDays = "ExpiryAlertDays";
    public const string DefaultReorderLevel = "DefaultReorderLevel";
    public const string AllowExpiredSale = "AllowExpiredSale";
    public const string PrintAfterSave = "PrintAfterSave";
}

/// <summary>One workbook per pharmacy holding all of its operational data.</summary>
public sealed class TenantDb : ExcelDatabase
{
    public ExcelTable<Item> Items { get; } = new("Items");
    public ExcelTable<Batch> Batches { get; } = new("Batches");
    public ExcelTable<Supplier> Suppliers { get; } = new("Suppliers");
    public ExcelTable<Customer> Customers { get; } = new("Patients");
    public ExcelTable<Purchase> Purchases { get; } = new("Purchases");
    public ExcelTable<PurchaseLine> PurchaseLines { get; } = new("PurchaseLines");
    public ExcelTable<Sale> Sales { get; } = new("Sales");
    public ExcelTable<SaleLine> SaleLines { get; } = new("SaleLines");
    public ExcelTable<SaleReturn> SaleReturns { get; } = new("SaleReturns");
    public ExcelTable<SaleReturnLine> SaleReturnLines { get; } = new("SaleReturnLines");
    public ExcelTable<StockAdjustment> Adjustments { get; } = new("StockAdjustments");
    public ExcelTable<MissedSale> MissedSales { get; } = new("MissedSales");
    public ExcelTable<Setting> Settings { get; } = new("Settings");

    protected override IEnumerable<IExcelTable> Tables => new IExcelTable[]
    {
        Items, Batches, Suppliers, Customers, Purchases, PurchaseLines, Sales, SaleLines,
        SaleReturns, SaleReturnLines, Adjustments, MissedSales, Settings
    };

    private TenantDb(string path) : base(path) { }

    public static TenantDb Open(string path)
    {
        var db = new TenantDb(path);
        db.LoadOrCreate();
        return db;
    }

    // ─── Settings ───
    public string GetSetting(string key, string fallback = "") =>
        Settings.Rows.FirstOrDefault(s => s.Key == key)?.Value ?? fallback;

    public int GetSettingInt(string key, int fallback) =>
        int.TryParse(GetSetting(key), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public bool GetSettingBool(string key, bool fallback) =>
        bool.TryParse(GetSetting(key), out var v) ? v : fallback;

    public void SetSetting(string key, string value)
    {
        var s = Settings.Rows.FirstOrDefault(x => x.Key == key);
        if (s == null) Settings.Add(new Setting { Key = key, Value = value });
        else s.Value = value;
    }

    public string Currency => GetSetting(SettingKeys.Currency, "Rs.");

    // ─── Running numbers ───
    private static int MaxNumber(IEnumerable<string> numbers, string prefix) =>
        numbers.Select(n => n.StartsWith(prefix) && int.TryParse(n[prefix.Length..], out var x) ? x : 0)
               .DefaultIfEmpty(0).Max();

    public string NextInvoiceNo() => "INV-" + (MaxNumber(Sales.Rows.Select(s => s.InvoiceNo), "INV-") + 1).ToString("D6");
    public string NextDocumentNo() => "GRN-" + (MaxNumber(Purchases.Rows.Select(p => p.DocumentNo), "GRN-") + 1).ToString("D6");
    public string NextReturnNo() => "RET-" + (MaxNumber(SaleReturns.Rows.Select(r => r.ReturnNo), "RET-") + 1).ToString("D5");
    public string NextMrNo() => "MR-" + (MaxNumber(Customers.Rows.Select(c => c.MrNo), "MR-") + 1).ToString("D5");
}
