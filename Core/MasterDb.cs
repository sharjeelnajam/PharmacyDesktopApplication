using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PharmacyDesktop.Core;

public static class PasswordHasher
{
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}

/// <summary>Pharmacies.xlsx — the list of pharmacies (tenants) and their user accounts.</summary>
public sealed class MasterDb : ExcelDatabase
{
    public ExcelTable<Tenant> Tenants { get; } = new("Pharmacies");
    public ExcelTable<AppUser> Users { get; } = new("Users");

    protected override IEnumerable<IExcelTable> Tables => new IExcelTable[] { Tenants, Users };

    private MasterDb(string path) : base(path) { }

    public static MasterDb Open()
    {
        var db = new MasterDb(Path.Combine(DataFolder, "Pharmacies.xlsx"));
        db.LoadOrCreate();
        return db;
    }

    public static string TenantFile(Tenant t) => Path.Combine(DataFolder, $"{t.Code}.xlsx");

    public string SuggestCode(string pharmacyName)
    {
        var letters = Regex.Replace(pharmacyName.ToUpperInvariant(), "[^A-Z0-9]", "");
        if (letters.Length == 0) letters = "PHARMACY";
        var baseCode = letters.Length > 8 ? letters[..8] : letters;
        var code = baseCode;
        int n = 2;
        while (Tenants.Rows.Any(t => t.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            code = baseCode + n++;
        return code;
    }

    /// <summary>Owner only: creates a pharmacy, its Admin login and its workbook, with a signed licence.</summary>
    public (Tenant tenant, AppUser admin) RegisterPharmacy(Tenant tenant, string adminName, string username, string password)
    {
        tenant.Code = Regex.Replace(tenant.Code.ToUpperInvariant(), "[^A-Z0-9_-]", "");
        if (string.IsNullOrWhiteSpace(tenant.Code)) tenant.Code = SuggestCode(tenant.Name);
        if (Tenants.Rows.Any(t => t.Code.Equals(tenant.Code, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Pharmacy code '{tenant.Code}' is already in use.");

        AppUser admin = null!;
        Commit(() =>
        {
            tenant.LastSeen = DateTime.Today;
            LicenseService.Sign(tenant);
            Tenants.Add(tenant);
            admin = Users.Add(new AppUser
            {
                TenantId = tenant.Id,
                FullName = adminName.Trim(),
                Username = username.Trim(),
                PasswordHash = PasswordHasher.Hash(password),
                Role = Roles.Admin
            });
        });

        // Create the pharmacy's own workbook with its default settings.
        var tdb = TenantDb.Open(TenantFile(tenant));
        tdb.SetSetting(SettingKeys.PharmacyName, tenant.Name);
        tdb.SetSetting(SettingKeys.Address, tenant.Address);
        tdb.SetSetting(SettingKeys.Phone, tenant.Phone);
        tdb.SetSetting(SettingKeys.LicenseNo, tenant.LicenseNo);
        tdb.Save();
        return (tenant, admin);
    }

    public AppUser? Authenticate(Tenant tenant, string username, string password)
    {
        var user = Users.Rows.FirstOrDefault(u => u.TenantId == tenant.Id
                                                  && u.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user == null || !user.IsActive || !PasswordHasher.Verify(password, user.PasswordHash)) return null;
        user.LastLogin = DateTime.Now;
        return user;
    }

    /// <summary>Called after a successful, licence-checked login: stamps last-used date and saves.</summary>
    public void RecordLogin(Tenant tenant)
    {
        LicenseService.Touch(tenant);
        try { Save(); } catch (DataFileLockedException) { /* stamps are not critical */ }
    }

    /// <summary>Owner only: change licence fields and re-sign them.</summary>
    public void UpdateLicense(Tenant tenant, Action<Tenant> change) =>
        Commit(() =>
        {
            change(tenant);
            LicenseService.Sign(tenant);
        });

    /// <summary>Applies a renewal key typed in at a store. Returns the new expiry, or null if the key is wrong.</summary>
    public DateTime? ApplyRenewalKey(Tenant tenant, string key)
    {
        var expiry = LicenseService.ParseRenewalKey(tenant.Code, key);
        if (expiry == null) return null;
        UpdateLicense(tenant, t =>
        {
            t.ExpiresOn = expiry;
            t.IsActive = true;
            if (t.LastSeen > DateTime.Today) t.LastSeen = DateTime.Today;
        });
        return expiry;
    }

    /// <summary>Owner only: removes a pharmacy and its logins. Its data workbook is renamed, not deleted.</summary>
    public void RemovePharmacy(Tenant tenant)
    {
        Commit(() =>
        {
            Users.Rows.RemoveAll(u => u.TenantId == tenant.Id);
            Tenants.Remove(tenant);
        });
        var file = TenantFile(tenant);
        if (File.Exists(file))
            File.Move(file, Path.Combine(BackupFolder, $"{tenant.Code}_removed_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"));
    }
}
