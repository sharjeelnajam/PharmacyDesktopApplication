namespace PharmacyDesktop.Core;

/// <summary>A line requested at the POS before it is allocated to stock batches.</summary>
public sealed class SaleRequestLine
{
    public int ItemId { get; set; }
    public int PreferredBatchId { get; set; }
    public int Qty { get; set; }
    public decimal Rate { get; set; }
    public decimal Discount { get; set; }
}

/// <summary>Business rules for stock, purchases, sales and returns of the logged-in pharmacy.</summary>
public sealed class PharmacyService
{
    private readonly TenantDb _db;
    private readonly string _user;

    public PharmacyService(TenantDb db, string userName)
    {
        _db = db;
        _user = userName;
    }

    // ───────────────────────────── Stock queries ─────────────────────────────

    public Dictionary<int, int> StockMap() =>
        _db.Batches.Rows.Where(b => b.QtyRemaining > 0)
           .GroupBy(b => b.ItemId)
           .ToDictionary(g => g.Key, g => g.Sum(b => b.QtyRemaining));

    public int Stock(int itemId) => _db.Batches.Rows.Where(b => b.ItemId == itemId && b.QtyRemaining > 0).Sum(b => b.QtyRemaining);

    public Dictionary<int, DateTime> NearestExpiryMap() =>
        _db.Batches.Rows.Where(b => b.QtyRemaining > 0 && b.Expiry.HasValue)
           .GroupBy(b => b.ItemId)
           .ToDictionary(g => g.Key, g => g.Min(b => b.Expiry!.Value));

    /// <summary>Batches with stock, first-expiry-first-out.</summary>
    public List<Batch> AvailableBatches(int itemId, bool includeExpired) =>
        _db.Batches.Rows
           .Where(b => b.ItemId == itemId && b.QtyRemaining > 0 && (includeExpired || !IsExpired(b)))
           .OrderBy(b => b.Expiry ?? DateTime.MaxValue).ThenBy(b => b.Id)
           .ToList();

    public static bool IsExpired(Batch b) => b.Expiry.HasValue && b.Expiry.Value.Date < DateTime.Today;

    public static string FormatQty(int units, int packSize)
    {
        if (packSize <= 1 || units < packSize) return units.ToString("N0");
        var packs = units / packSize;
        var loose = units % packSize;
        return loose == 0 ? $"{units:N0}  ({packs}p)" : $"{units:N0}  ({packs}p + {loose})";
    }

    public decimal StockCostValue() => _db.Batches.Rows.Where(b => b.QtyRemaining > 0).Sum(b => b.QtyRemaining * b.CostPerUnit);

    public decimal StockRetailValue()
    {
        var items = _db.Items.Rows.ToDictionary(i => i.Id);
        return _db.Batches.Rows.Where(b => b.QtyRemaining > 0)
                  .Sum(b => b.QtyRemaining * (items.TryGetValue(b.ItemId, out var it) ? it.UnitPrice : b.RetailPerUnit));
    }

    public int ReorderLevelFor(Item item) =>
        item.ReorderLevel > 0 ? item.ReorderLevel : _db.GetSettingInt(SettingKeys.DefaultReorderLevel, 10);

    // ───────────────────────────── Items ─────────────────────────────

    public bool ItemHasTransactions(Item item) =>
        _db.Batches.Rows.Any(b => b.ItemId == item.Id) || _db.SaleLines.Rows.Any(l => l.ItemId == item.Id);

    public Item? FindByBarcode(string code) =>
        string.IsNullOrWhiteSpace(code) ? null
            : _db.Items.Rows.FirstOrDefault(i => i.IsActive && i.Barcode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));

    // ───────────────────────────── Purchases (Add Stock) ─────────────────────────────

    public Purchase SavePurchase(Purchase purchase, List<PurchaseLine> lines)
    {
        if (lines.Count == 0) throw new InvalidOperationException("Add at least one item.");
        foreach (var l in lines)
        {
            if (l.Packs + l.BonusPacks <= 0) throw new InvalidOperationException($"Quantity is required for '{l.ItemName}'.");
            if (l.PackSize <= 0) l.PackSize = 1;
        }

        _db.Commit(() =>
        {
            purchase.DocumentNo = string.IsNullOrWhiteSpace(purchase.DocumentNo) ? _db.NextDocumentNo() : purchase.DocumentNo;
            if (_db.Purchases.Rows.Any(p => p.DocumentNo == purchase.DocumentNo)) purchase.DocumentNo = _db.NextDocumentNo();
            purchase.CreatedBy = _user;
            purchase.ItemCount = lines.Count;
            _db.Purchases.Add(purchase);

            // Spread the invoice-level discount/tax over lines proportionally for accurate costing.
            var linesTotal = lines.Sum(l => l.Total);
            var adjustFactor = linesTotal == 0 ? 1m : (linesTotal - purchase.Discount + purchase.SalesTax) / linesTotal;

            foreach (var l in lines)
            {
                l.PurchaseId = purchase.Id;
                l.QtyUnits = (l.Packs + l.BonusPacks) * l.PackSize;
                _db.PurchaseLines.Add(l);

                var item = _db.Items.Find(l.ItemId);
                var costPerUnit = l.QtyUnits == 0 ? 0 : Math.Round(l.Total * adjustFactor / l.QtyUnits, 4);
                _db.Batches.Add(new Batch
                {
                    ItemId = l.ItemId,
                    BatchNo = l.BatchNo,
                    Expiry = l.Expiry,
                    PurchaseId = purchase.Id,
                    QtyIn = l.QtyUnits,
                    QtyRemaining = l.QtyUnits,
                    CostPerUnit = costPerUnit,
                    RetailPerUnit = Math.Round(l.RetailPrice / l.PackSize, 4),
                    ReceivedAt = purchase.Date
                });

                if (item != null)
                {
                    item.PackSize = l.PackSize;
                    item.PurchasePrice = l.NetUnitCost;
                    if (l.RetailPrice > 0) item.RetailPrice = l.RetailPrice;
                }
            }
        });
        return purchase;
    }

    public bool CanDeletePurchase(Purchase p) =>
        _db.Batches.Rows.Where(b => b.PurchaseId == p.Id).All(b => b.QtyRemaining == b.QtyIn);

    public void DeletePurchase(Purchase p)
    {
        if (!CanDeletePurchase(p))
            throw new InvalidOperationException("Stock from this purchase has already been sold or adjusted, so it can't be deleted.");
        _db.Commit(() =>
        {
            _db.Batches.Rows.RemoveAll(b => b.PurchaseId == p.Id);
            _db.PurchaseLines.Rows.RemoveAll(l => l.PurchaseId == p.Id);
            _db.Purchases.Remove(p);
        });
    }

    // ───────────────────────────── Sales (POS) ─────────────────────────────

    public (Sale sale, List<SaleLine> lines) SaveSale(Sale sale, List<SaleRequestLine> request)
    {
        if (request.Count == 0) throw new InvalidOperationException("The cart is empty.");
        var allowExpired = _db.GetSettingBool(SettingKeys.AllowExpiredSale, false);

        // Validate availability up-front so nothing is half-saved.
        foreach (var g in request.GroupBy(r => r.ItemId))
        {
            var item = _db.Items.Find(g.Key) ?? throw new InvalidOperationException("Item no longer exists.");
            var need = g.Sum(r => r.Qty);
            var have = AvailableBatches(item.Id, allowExpired).Sum(b => b.QtyRemaining);
            if (need <= 0) throw new InvalidOperationException($"Quantity for '{item.Name}' must be more than zero.");
            if (need > have)
                throw new InvalidOperationException($"Not enough stock for '{item.Name}'.\nRequired: {need}, available: {have}.");
        }

        var saleLines = new List<SaleLine>();
        _db.Commit(() =>
        {
            sale.InvoiceNo = _db.NextInvoiceNo();
            sale.CreatedBy = _user;
            _db.Sales.Add(sale);

            foreach (var req in request)
            {
                var item = _db.Items.Find(req.ItemId)!;
                var batches = AvailableBatches(item.Id, allowExpired);
                var preferred = batches.FirstOrDefault(b => b.Id == req.PreferredBatchId);
                if (preferred != null) { batches.Remove(preferred); batches.Insert(0, preferred); }

                var remaining = req.Qty;
                var discountLeft = req.Discount;
                foreach (var b in batches)
                {
                    if (remaining == 0) break;
                    var take = Math.Min(remaining, b.QtyRemaining);
                    b.QtyRemaining -= take;
                    remaining -= take;

                    var lineDiscount = remaining == 0 ? discountLeft : Math.Round(req.Discount * take / req.Qty, 2);
                    discountLeft -= lineDiscount;
                    var line = _db.SaleLines.Add(new SaleLine
                    {
                        SaleId = sale.Id,
                        ItemId = item.Id,
                        ItemName = item.Name,
                        BatchId = b.Id,
                        BatchNo = b.BatchNo,
                        Expiry = b.Expiry,
                        Qty = take,
                        Rate = req.Rate,
                        Discount = lineDiscount,
                        Amount = Math.Round(take * req.Rate - lineDiscount, 2),
                        CostPerUnit = b.CostPerUnit
                    });
                    saleLines.Add(line);
                }
            }

            sale.SubTotal = saleLines.Sum(l => l.Amount);
            sale.GrandTotal = Math.Max(0, sale.SubTotal - sale.Discount);
            sale.Cost = Math.Round(saleLines.Sum(l => l.Qty * l.CostPerUnit), 2);
            sale.Profit = sale.GrandTotal - sale.Cost;
            if (sale.PaymentMethod == "Credit")
            {
                sale.Due = Math.Max(0, sale.GrandTotal - sale.CashReceived);
                sale.Change = 0;
                sale.Status = sale.Due > 0 ? "Due" : "Paid";
            }
            else
            {
                if (sale.CashReceived < sale.GrandTotal) sale.CashReceived = sale.GrandTotal;
                sale.Change = sale.CashReceived - sale.GrandTotal;
                sale.Due = 0;
                sale.Status = "Paid";
            }
        });
        return (sale, saleLines);
    }

    public List<SaleLine> LinesOf(Sale sale) => _db.SaleLines.Rows.Where(l => l.SaleId == sale.Id).ToList();

    public SaleReturn ReturnSale(Sale sale, Dictionary<int, int> qtyBySaleLine, string reason)
    {
        var lines = LinesOf(sale).ToDictionary(l => l.Id);
        var valid = qtyBySaleLine.Where(kv => kv.Value > 0 && lines.ContainsKey(kv.Key)).ToList();
        if (valid.Count == 0) throw new InvalidOperationException("Enter the quantity to return for at least one item.");
        foreach (var (lineId, qty) in valid)
        {
            var l = lines[lineId];
            if (qty > l.Qty - l.ReturnedQty)
                throw new InvalidOperationException($"Can't return {qty} of '{l.ItemName}' — only {l.Qty - l.ReturnedQty} left to return.");
        }

        SaleReturn ret = null!;
        _db.Commit(() =>
        {
            ret = _db.SaleReturns.Add(new SaleReturn
            {
                ReturnNo = _db.NextReturnNo(),
                SaleId = sale.Id,
                InvoiceNo = sale.InvoiceNo,
                Reason = reason,
                CreatedBy = _user
            });

            // Invoice-level discount is shared proportionally by returned items.
            var discountRatio = sale.SubTotal == 0 ? 0 : sale.Discount / sale.SubTotal;
            decimal total = 0, costBack = 0;
            foreach (var (lineId, qty) in valid)
            {
                var l = lines[lineId];
                var lineUnit = l.Qty == 0 ? 0 : l.Amount / l.Qty;
                var amount = Math.Round(lineUnit * qty * (1 - discountRatio), 2);
                l.ReturnedQty += qty;
                total += amount;
                costBack += qty * l.CostPerUnit;

                var batch = _db.Batches.Find(l.BatchId);
                if (batch != null) batch.QtyRemaining += qty;

                _db.SaleReturnLines.Add(new SaleReturnLine
                {
                    ReturnId = ret.Id, SaleLineId = l.Id, ItemId = l.ItemId, ItemName = l.ItemName,
                    BatchId = l.BatchId, Qty = qty, Rate = l.Rate, Amount = amount
                });
            }

            ret.Amount = total;
            sale.Returned += total;
            sale.Profit -= total - Math.Round(costBack, 2);
            sale.Status = lines.Values.All(x => x.ReturnedQty >= x.Qty) ? "Returned" : "Partial Return";
        });
        return ret;
    }

    // ───────────────────────────── Adjustments ─────────────────────────────

    public void Adjust(Item item, Batch? batch, int qtyChange, string reason, string notes)
    {
        if (qtyChange == 0) throw new InvalidOperationException("Quantity change can't be zero.");
        if (batch != null && batch.QtyRemaining + qtyChange < 0)
            throw new InvalidOperationException($"Batch {batch.BatchNo} only has {batch.QtyRemaining} units.");
        if (batch == null && qtyChange < 0)
            throw new InvalidOperationException("Select a batch to remove stock from.");

        _db.Commit(() =>
        {
            var cost = batch?.CostPerUnit ?? (item.PackSize > 0 ? item.PurchasePrice / item.PackSize : item.PurchasePrice);
            if (batch == null)
            {
                batch = _db.Batches.Add(new Batch
                {
                    ItemId = item.Id, BatchNo = "ADJ-" + DateTime.Now.ToString("yyMMdd"), QtyIn = qtyChange,
                    QtyRemaining = qtyChange, CostPerUnit = cost, RetailPerUnit = item.UnitPrice
                });
            }
            else
            {
                batch.QtyRemaining += qtyChange;
            }

            _db.Adjustments.Add(new StockAdjustment
            {
                ItemId = item.Id, ItemName = item.Name, BatchId = batch.Id, BatchNo = batch.BatchNo,
                QtyChange = qtyChange, CostValue = Math.Round(qtyChange * cost, 2), Reason = reason,
                Notes = notes, CreatedBy = _user
            });
        });
    }
}
