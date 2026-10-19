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
