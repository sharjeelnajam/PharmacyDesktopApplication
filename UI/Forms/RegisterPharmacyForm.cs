using PharmacyDesktop.Core;

namespace PharmacyDesktop.UI.Forms;

/// <summary>Creates a new pharmacy (tenant) with its own Excel workbook and an Admin account.</summary>
public class RegisterPharmacyForm : FormDialog
{
    public Tenant? CreatedTenant { get; private set; }
    public string CreatedUsername { get; private set; } = "";

    public RegisterPharmacyForm(bool firstRun)
        : base(firstRun ? "Welcome! Set up your first pharmacy" : "Register a new pharmacy",
               "Each pharmacy keeps its data in its own Excel file next to the application.", 2, 680)
    {
        var name = AddText("name", "PHARMACY NAME *", "");
        var code = AddText("code", "PHARMACY CODE *", "");
        AddText("phone", "PHONE", "");
        AddText("license", "DRUG LICENSE NO.", "");
        AddText("address", "ADDRESS", "", fullRow: true);
        AddText("admin", "ADMIN FULL NAME *", "");
        AddText("user", "ADMIN USERNAME *", "admin");
        AddText("pass", "PASSWORD * (min 4 chars)", "", password: true);
        AddText("pass2", "CONFIRM PASSWORD *", "", password: true);

        var codeEdited = false;
        code.KeyPress += (_, _) => codeEdited = true;
        name.TextChanged += (_, _) => { if (!codeEdited) code.Text = Session.Master.SuggestCode(name.Text); };

        Validator = d =>
        {
            if (d.Str("name").Length < 2) return "Enter the pharmacy name.";
            if (d.Str("code").Length < 2) return "Enter a short pharmacy code (letters/numbers).";
            if (Session.Master.Tenants.Rows.Any(t => t.Code.Equals(d.Str("code"), StringComparison.OrdinalIgnoreCase)))
                return "This pharmacy code is already used. Choose another.";
            if (d.Str("admin").Length < 2) return "Enter the admin's full name.";
            if (d.Str("user").Length < 3) return "Username must be at least 3 characters.";
            if (d["pass"].Text.Length < 4) return "Password must be at least 4 characters.";
            if (d["pass"].Text != d["pass2"].Text) return "Passwords do not match.";

            string? error = null;
            Ui.Try(() =>
            {
                var (tenant, _) = Session.Master.RegisterPharmacy(new Tenant
                {
                    Name = d.Str("name"),
                    Code = d.Str("code"),
                    Phone = d.Str("phone"),
                    LicenseNo = d.Str("license"),
                    Address = d.Str("address")
                }, d.Str("admin"), d.Str("user"), d["pass"].Text);
                CreatedTenant = tenant;
                CreatedUsername = d.Str("user");
            });
            if (CreatedTenant == null) error = "The pharmacy could not be created.";
            return error;
        };
    }
}
