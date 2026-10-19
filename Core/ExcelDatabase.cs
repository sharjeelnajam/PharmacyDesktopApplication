using ClosedXML.Excel;

namespace PharmacyDesktop.Core;

/// <summary>Raised when the workbook can't be written, typically because it is open in Excel.</summary>
public class DataFileLockedException : Exception
{
    public DataFileLockedException(string path, Exception inner)
        : base($"Could not save data to:\n{path}\n\nIf this file is open in Microsoft Excel, close it and try again.", inner) { }
}

/// <summary>
/// A workbook made of several <see cref="IExcelTable"/> sheets. The whole workbook is loaded into
/// memory on open and rewritten atomically (temp file + replace) on every save, with a daily backup.
/// </summary>
public abstract class ExcelDatabase
{
    protected ExcelDatabase(string filePath) => FilePath = filePath;

    public string FilePath { get; }
    protected abstract IEnumerable<IExcelTable> Tables { get; }

    public static string DataFolder
    {
        get
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Data");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string BackupFolder
    {
        get
        {
            var dir = Path.Combine(DataFolder, "Backups");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    protected void LoadOrCreate()
    {
        if (!File.Exists(FilePath))
        {
            Save();
            return;
        }

        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(stream);
            foreach (var t in Tables) t.Load(wb);
        }
        catch (IOException ex)
        {
            throw new DataFileLockedException(FilePath, ex);
        }
    }

    /// <summary>Discards in-memory changes and re-reads the workbook from disk.</summary>
    public void Reload() => LoadOrCreate();

    /// <summary>
    /// Applies <paramref name="mutate"/> and saves. If the save fails, the in-memory state is
    /// reverted from disk so memory and file never drift apart.
    /// </summary>
    public void Commit(Action mutate)
    {
        try
        {
            mutate();
            Save();
        }
        catch
        {
            try { Reload(); } catch { /* keep the original error */ }
            throw;
        }
    }

    public void Save()
    {
        var temp = FilePath + ".tmp";
        try
        {
            using (var wb = new XLWorkbook())
            {
                foreach (var t in Tables) t.Write(wb);
                using var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                wb.SaveAs(stream);
            }

            if (File.Exists(FilePath))
            {
                BackupDaily();
                File.Replace(temp, FilePath, null);
            }
            else
            {
                File.Move(temp, FilePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* ignore */ }
            throw new DataFileLockedException(FilePath, ex);
        }
    }

    /// <summary>Keeps one copy per day of the file as it was before the first save of that day.</summary>
    private void BackupDaily()
    {
        var name = Path.GetFileNameWithoutExtension(FilePath);
        var target = Path.Combine(BackupFolder, $"{name}_{DateTime.Today:yyyyMMdd}.xlsx");
        if (File.Exists(target)) return;
        File.Copy(FilePath, target);

        // Keep the last 30 daily backups per workbook.
        foreach (var old in Directory.GetFiles(BackupFolder, $"{name}_*.xlsx")
                     .OrderByDescending(f => f).Skip(30))
        {
            try { File.Delete(old); } catch { /* ignore */ }
        }
    }
}
