# PharmaDesk — Offline Pharmacy Management

A Windows desktop app (WinForms, .NET 8) for running one or more pharmacies **fully offline**.
All data is stored in **Excel workbooks** in a `Data` folder next to `PharmaDesk.exe`.

## Running

- **Release build:** copy `publish\PharmaDesk.exe` anywhere (USB drive, `C:\PharmaDesk`, …) and run it.
  It is self-contained — no .NET install is needed. The `Data` folder is created beside it on first run.
- **From source:** `dotnet run` (or open `PharmacyDesktop.slnx` in Visual Studio and press F5).
- **Re-publish:** `dotnet publish -c Release -o publish`

On first launch you'll be asked to register your first pharmacy and its Admin account.

## Multiple pharmacies (tenants)

- The login screen asks for **Pharmacy → Username → Password**.
- "New pharmacy? Register here" creates another pharmacy with its own Admin.
- Each pharmacy has its **own workbook** (`Data\<CODE>.xlsx`), so data never mixes.
- `Data\Pharmacies.xlsx` holds the pharmacy list and user accounts (passwords are PBKDF2-hashed).

## Data files

| File | Contents |
|---|---|
| `Data\Pharmacies.xlsx` | Sheets: `Pharmacies`, `Users` |
| `Data\<CODE>.xlsx` | Sheets: `Items`, `Batches`, `Suppliers`, `Patients`, `Purchases`, `PurchaseLines`, `Sales`, `SaleLines`, `SaleReturns`, `SaleReturnLines`, `StockAdjustments`, `MissedSales`, `Settings` |
| `Data\Backups\` | Automatic daily copy of each workbook (last 30 days) |
| `Data\error.log` | Unexpected errors, if any |

You can open the workbooks in Excel to view or analyse data. **Close Excel before using the app**,
otherwise saving fails (the app shows a message and nothing is lost in the file). Saves are atomic
(written to a temp file, then swapped in).

## Features

- **Dashboard** — today's/monthly sales & profit, stock value, low stock, expiring & expired stock, 14-day sales chart, top sellers.
- **POS (New Sale)** — search by name/generic or scan a barcode, automatic first-expiry-first-out batch selection (or pick a batch), per-line and invoice discounts, cash/card/online/credit, change & due, profit preview, 80 mm receipt printing.
- **Sales History** — filters, invoice details, reprint, partial/full **sale returns** (stock goes back to its batch).
- **Medicines / Items** — catalogue with live stock (packs + loose units), re-order levels, categories, racks; **Excel import** (with optional opening stock) and template.
- **Add Stock (GRN)** — supplier invoice, batch #, expiry (`MM/YY`), units/pack, packs + bonus, discount %, tax %, retail price, margin.
- **Purchases** — purchase documents with lines, totals, print GRN, delete (only if untouched).
- **Stock & Expiry** — every batch with days-left, expired / near-expiry filters.
- **Stock Adjustment** — expired, damaged, return-to-supplier, count corrections.
- **Suppliers, Patients (with credit due), Missed Sales.**
- **Reports** — Profit & Loss, daily sales, item-wise sales, payment methods, by user, dues, returns, purchases by supplier, stock valuation, re-order list, expiry, adjustments. Export to Excel or print.
- **Users & roles** — Admin (everything), Pharmacist (no users/settings), Cashier (POS, sales, patients, missed sales).
- **Settings** — pharmacy profile for receipts, currency, expiry alert days, default re-order level, allow expired sale, auto-print; backup to any folder.

## Keyboard shortcuts

| Key | Action |
|---|---|
| F2 | New Sale (POS) |
| F3 | Add Stock |
| Ctrl+D / Ctrl+H | Dashboard / Sales History |
| F4 | Focus medicine search |
| F6 | Amount received |
| F9 / F10 | Save sale / Save & print |
| Ctrl+N | New sale (clear cart) |
| Ctrl+S | Save purchase (Add Stock) |

## Code layout

```
Core/   Models, Excel storage (ExcelTable, ExcelDatabase), MasterDb, TenantDb, PharmacyService, Session
UI/     Theme, shared controls, printing, forms (Login, Register, Main, FormDialog) and Pages/*
```
