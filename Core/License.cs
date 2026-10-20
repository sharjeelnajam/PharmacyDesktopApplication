using System.Security.Cryptography;
using System.Text;

namespace PharmacyDesktop.Core;

public enum LicenseState { Active, Disabled, Expired, NotActivated, Tampered, ClockTampered }

public sealed record LicenseStatus(LicenseState State, int? DaysLeft, string Message)
{
    public bool CanLogin => State == LicenseState.Active;
    public string Label => State switch
    {
        LicenseState.Active => DaysLeft is <= 7 ? "Expiring" : "Active",
        LicenseState.Disabled => "Disabled",
        LicenseState.Expired => "Expired",
        LicenseState.NotActivated => "Not activated",
        LicenseState.ClockTampered => "Clock changed",
        _ => "Invalid"
    };
}

/// <summary>
/// Subscription control for pharmacies. Each pharmacy row in Pharmacies.xlsx carries an HMAC signature
/// over its licence fields (active flag, expiry, last-seen date, user-management flag), keyed with a secret
/// compiled into the exe. Editing those cells in Excel invalidates the signature and blocks the pharmacy.
/// Renewal keys let the owner extend a pharmacy that runs on a computer they can't access.
/// </summary>
public static class LicenseService
{
    private static readonly DateTime KeyEpoch = new(2024, 1, 1);
    private const string Base32 = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no I, O, 0, 1 to avoid misreading

    private static byte[] Secret => Convert.FromBase64String(OwnerSecrets.LicenseSecret);

    private static string Payload(Tenant t) =>
        $"{t.Code.ToUpperInvariant()}|{t.IsActive}|{t.ExpiresOn:yyyyMMdd}|{t.LastSeen:yyyyMMdd}|{t.AllowUserManagement}";

    private static string Mac(string text) =>
        Convert.ToBase64String(HMACSHA256.HashData(Secret, Encoding.UTF8.GetBytes(text)));

    /// <summary>Re-signs the licence fields. Call after the owner (or a valid renewal key) changes them.</summary>
    public static void Sign(Tenant t)
    {
        t.ExpiresOn = t.ExpiresOn?.Date;
        t.LastSeen = t.LastSeen?.Date;
        t.LicenseSig = Mac(Payload(t));
    }

    public static bool SignatureValid(Tenant t) =>
        !string.IsNullOrEmpty(t.LicenseSig) &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(t.LicenseSig), Encoding.UTF8.GetBytes(Mac(Payload(t))));

    public static LicenseStatus Check(Tenant t)
    {
        var today = DateTime.Today;
        if (string.IsNullOrEmpty(t.LicenseSig))
            return new(LicenseState.NotActivated, null, "This pharmacy has not been activated yet.\nPlease contact the software provider.");
        if (!SignatureValid(t))
            return new(LicenseState.Tampered, null, "The licence information for this pharmacy is invalid (the data file was modified).\nPlease contact the software provider.");
        if (!t.IsActive)
            return new(LicenseState.Disabled, null, "This pharmacy's account has been disabled.\nPlease contact the software provider.");
        if (t.LastSeen.HasValue && today < t.LastSeen.Value.Date.AddDays(-1))
            return new(LicenseState.ClockTampered, null, $"The computer's date ({today:dd-MMM-yyyy}) is earlier than the last time PharmaDesk was used ({t.LastSeen:dd-MMM-yyyy}).\nPlease correct the system date and time.");
        if (t.ExpiresOn.HasValue && today > t.ExpiresOn.Value.Date)
            return new(LicenseState.Expired, 0, $"The subscription for this pharmacy expired on {t.ExpiresOn:dd-MMM-yyyy}.\nEnter a renewal key or contact the software provider.");
        int? days = t.ExpiresOn.HasValue ? (int)(t.ExpiresOn.Value.Date - today).TotalDays : null;
        return new(LicenseState.Active, days, days.HasValue ? $"Valid until {t.ExpiresOn:dd-MMM-yyyy}" : "No expiry");
    }

    /// <summary>Records today's date as last-used (for clock-rollback detection) and re-signs.</summary>
    public static void Touch(Tenant t)
    {
        if (!SignatureValid(t)) return;
        if (t.LastSeen?.Date >= DateTime.Today) return;
        t.LastSeen = DateTime.Today;
        Sign(t);
    }

    // ───── Renewal keys: XXXX-XXXX-XXXX-XXXX, bound to the pharmacy code ─────

    public static string CreateRenewalKey(string pharmacyCode, DateTime expiresOn)
    {
        var days = (ushort)Math.Clamp((expiresOn.Date - KeyEpoch).TotalDays, 0, ushort.MaxValue);
        var mac = HMACSHA256.HashData(Secret, Encoding.UTF8.GetBytes($"RENEW|{pharmacyCode.Trim().ToUpperInvariant()}|{days}"));
        var bytes = new byte[10];
        bytes[0] = (byte)(days >> 8);
        bytes[1] = (byte)days;
        Array.Copy(mac, 0, bytes, 2, 8);
        var text = ToBase32(bytes);
        return string.Join("-", Enumerable.Range(0, 4).Select(i => text.Substring(i * 4, 4)));
    }

    /// <summary>Returns the new expiry date if the key is valid for this pharmacy code, otherwise null.</summary>
    public static DateTime? ParseRenewalKey(string pharmacyCode, string key)
    {
        var clean = new string(key.ToUpperInvariant().Where(c => Base32.Contains(c)).ToArray());
        if (clean.Length != 16) return null;
        var bytes = FromBase32(clean);
        var days = (ushort)((bytes[0] << 8) | bytes[1]);
        var expected = CreateRenewalKey(pharmacyCode, KeyEpoch.AddDays(days)).Replace("-", "");
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(clean))
            ? KeyEpoch.AddDays(days) : null;
    }

    private static string ToBase32(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5) { sb.Append(Base32[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Base32[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[] FromBase32(string text)
    {
        var result = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in text)
        {
            buffer = (buffer << 5) | Base32.IndexOf(c);
            bits += 5;
            if (bits >= 8) { result.Add((byte)(buffer >> (bits - 8))); bits -= 8; }
        }
        return result.ToArray();
    }

    // ───── Owner sign-in ─────

    public static bool OwnerConfigured => OwnerSecrets.OwnerPasswordHash.Length > 0;

    public static bool VerifyOwner(string username, string password) =>
        OwnerConfigured &&
        username.Trim().Equals(OwnerSecrets.OwnerUsername, StringComparison.OrdinalIgnoreCase) &&
        PasswordHasher.Verify(password, OwnerSecrets.OwnerPasswordHash);
}
