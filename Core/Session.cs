namespace PharmacyDesktop.Core;

/// <summary>The logged-in pharmacy and user. Everything the UI touches goes through here.</summary>
public static class Session
{
    public static MasterDb Master { get; private set; } = null!;
    public static Tenant Tenant { get; private set; } = null!;
    public static AppUser User { get; private set; } = null!;
    public static TenantDb Db { get; private set; } = null!;
    public static PharmacyService Svc { get; private set; } = null!;

    public static bool IsAdmin => User?.Role == Roles.Admin;
    public static bool IsCashier => User?.Role == Roles.Cashier;
    public static string Cur => Db?.Currency ?? "Rs.";

    public static void Init(MasterDb master) => Master = master;

    public static void Start(Tenant tenant, AppUser user)
    {
        Tenant = tenant;
        User = user;
        Db = TenantDb.Open(MasterDb.TenantFile(tenant));
        Svc = new PharmacyService(Db, user.FullName);
    }

    public static void End()
    {
        Tenant = null!;
        User = null!;
        Db = null!;
        Svc = null!;
    }

    public static string Money(decimal v) => $"{Cur} {v:N2}";
}
