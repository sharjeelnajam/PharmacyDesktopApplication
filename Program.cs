using PharmacyDesktop.Core;
using PharmacyDesktop.UI;
using PharmacyDesktop.UI.Forms;

namespace PharmacyDesktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Owner tool: PharmaDesk.exe --owner-hash  → prints a hash to paste into Core\OwnerSecrets.cs.
        // Safe to leave in store builds: it only computes a hash, it changes nothing.
        if (Environment.GetCommandLineArgs().Skip(1).Any(a => a.Equals("--owner-hash", StringComparison.OrdinalIgnoreCase)))
        {
            using var d = new FormDialog("Owner password hash", "Generates the value for OwnerPasswordHash in Core\\OwnerSecrets.cs.", 1, 520);
            d.AddText("p1", "NEW OWNER PASSWORD (min 8)", "", password: true);
            d.AddText("p2", "CONFIRM", "", password: true);
            d.Validator = x => x["p1"].Text.Length < 8 ? "Use at least 8 characters." : x["p1"].Text != x["p2"].Text ? "Passwords do not match." : null;
            if (d.ShowDialog() == DialogResult.OK)
            {
                var hash = PasswordHasher.Hash(d["p1"].Text);
                Clipboard.SetText(hash);
                MessageBox.Show($"Copied to clipboard:\n\n{hash}\n\nPaste it as OwnerPasswordHash in Core\\OwnerSecrets.cs and rebuild.", "Owner password hash");
            }
            return;
        }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowError(e.ExceptionObject as Exception);

        // Only one copy per folder, so two windows never write the same Excel files at once.
        using var mutex = new Mutex(true, "PharmaDesk_" + AppContext.BaseDirectory.Replace('\\', '_').Replace(':', '_'), out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("PharmaDesk is already running.", "PharmaDesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Session.Init(MasterDb.Open());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PharmaDesk — cannot open data", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new LoginForm());
    }

    private static void ShowError(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var log = Path.Combine(ExcelDatabase.DataFolder, "error.log");
            File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { /* ignore */ }
        var message = ex is DataFileLockedException ? ex.Message : "An unexpected error occurred:\n" + ex.Message + "\n\nDetails were written to Data\\error.log.";
        MessageBox.Show(message, "PharmaDesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
