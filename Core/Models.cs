namespace PharmacyDesktop.Core;

/// <summary>Every row stored in a worksheet has an integer Id (column A).</summary>
public interface IEntity
{
    int Id { get; set; }
}

// ───────────────────────────── Master workbook (Pharmacies.xlsx) ─────────────────────────────

public class Tenant : IEntity
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string LicenseNo { get; set; } = "";
    public string ContactPerson { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // ── Subscription (signed; see LicenseService) ──
    /// <summary>Last day the pharmacy may log in. Null = no expiry.</summary>
    public DateTime? ExpiresOn { get; set; }
    /// <summary>Last date the app was used, for detecting a clock set backwards.</summary>
    public DateTime? LastSeen { get; set; }
    /// <summary>When true the pharmacy's own Admin may create/edit staff logins.</summary>
    public bool AllowUserManagement { get; set; }
    public string LicenseSig { get; set; } = "";

    public override string ToString() => Name;
}

public class AppUser : IEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Cashier;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastLogin { get; set; }
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Pharmacist = "Pharmacist";
    public const string Cashier = "Cashier";
    public static readonly string[] All = { Admin, Pharmacist, Cashier };
}

// ───────────────────────────── Tenant workbook (<CODE>.xlsx) ─────────────────────────────

public class Item : IEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string GenericName { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Category { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Rack { get; set; } = "";
    /// <summary>Smallest sellable unit, e.g. Tablet, Capsule, Bottle.</summary>
    public string Unit { get; set; } = "Tablet";
    /// <summary>Units per pack (conversion unit), e.g. 30 tablets per box.</summary>
    public int PackSize { get; set; } = 1;
    /// <summary>Last purchase cost per pack.</summary>
    public decimal PurchasePrice { get; set; }
    /// <summary>Retail price per pack.</summary>
    public decimal RetailPrice { get; set; }
    /// <summary>Re-order level in units.</summary>
    public int ReorderLevel { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public decimal UnitPrice => PackSize <= 0 ? RetailPrice : Math.Round(RetailPrice / PackSize, 2);
    public override string ToString() => Name;
}

public class Supplier : IEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string ContactPerson { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public override string ToString() => Name;
}

public class Customer : IEntity
{
    public int Id { get; set; }
    public string MrNo { get; set; } = "";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Gender { get; set; } = "";
    public int Age { get; set; }
    public string Address { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public override string ToString() => string.IsNullOrWhiteSpace(Phone) ? $"{Name}  ({MrNo})" : $"{Name}  ({MrNo} · {Phone})";
}

public class Purchase : IEntity
{
    public int Id { get; set; }
    public string DocumentNo { get; set; } = "";
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public string SupplierInvoiceNo { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Now;
    public DateTime SupplierInvoiceDate { get; set; } = DateTime.Today;
    public decimal SubTotal { get; set; }
    public decimal Discount { get; set; }
    public decimal SalesTax { get; set; }
    public decimal GrandTotal { get; set; }
    public int ItemCount { get; set; }
    public string Notes { get; set; } = "";
    public string CreatedBy { get; set; } = "";
}

public class PurchaseLine : IEntity
{
    public int Id { get; set; }
    public int PurchaseId { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public string BatchNo { get; set; } = "";
    public DateTime? Expiry { get; set; }
    public int PackSize { get; set; } = 1;
    public int Packs { get; set; }
    public int BonusPacks { get; set; }
    public int QtyUnits { get; set; }
    public decimal UnitCost { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal TaxPct { get; set; }
    public decimal NetUnitCost { get; set; }
    public decimal RetailPrice { get; set; }
    public decimal Total { get; set; }
}

/// <summary>A lot of stock received on a purchase; quantities are in units.</summary>
public class Batch : IEntity
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string BatchNo { get; set; } = "";
    public DateTime? Expiry { get; set; }
    public int PurchaseId { get; set; }
    public int QtyIn { get; set; }
    public int QtyRemaining { get; set; }
    public decimal CostPerUnit { get; set; }
    public decimal RetailPerUnit { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.Now;
}

public class Sale : IEntity
{
    public int Id { get; set; }
    public string InvoiceNo { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Now;
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = "Walk-in Customer";
    public decimal SubTotal { get; set; }
    public decimal Discount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal Cost { get; set; }
    public decimal Profit { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public decimal CashReceived { get; set; }
    public decimal Change { get; set; }
    public decimal Due { get; set; }
    public decimal Returned { get; set; }
    public string Status { get; set; } = "Paid";
    public string Notes { get; set; } = "";
    public string CreatedBy { get; set; } = "";
}

public class SaleLine : IEntity
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public int BatchId { get; set; }
    public string BatchNo { get; set; } = "";
    public DateTime? Expiry { get; set; }
    public int Qty { get; set; }
    public decimal Rate { get; set; }
    public decimal Discount { get; set; }
    public decimal Amount { get; set; }
    public decimal CostPerUnit { get; set; }
    public int ReturnedQty { get; set; }
}

public class SaleReturn : IEntity
{
    public int Id { get; set; }
    public string ReturnNo { get; set; } = "";
    public int SaleId { get; set; }
    public string InvoiceNo { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Now;
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "";
    public string CreatedBy { get; set; } = "";
}

public class SaleReturnLine : IEntity
{
    public int Id { get; set; }
    public int ReturnId { get; set; }
    public int SaleLineId { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public int BatchId { get; set; }
    public int Qty { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
}

public class StockAdjustment : IEntity
{
    public int Id { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public int ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public int BatchId { get; set; }
    public string BatchNo { get; set; } = "";
    /// <summary>Positive adds stock, negative removes it (units).</summary>
    public int QtyChange { get; set; }
    public decimal CostValue { get; set; }
    public string Reason { get; set; } = "";
    public string Notes { get; set; } = "";
    public string CreatedBy { get; set; } = "";
}

public class MissedSale : IEntity
{
    public int Id { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public string ItemName { get; set; } = "";
    public int Qty { get; set; } = 1;
    public string CustomerName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Notes { get; set; } = "";
    public string CreatedBy { get; set; } = "";
}

public class Setting : IEntity
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
