using System.Globalization;
using System.Reflection;
using ClosedXML.Excel;

namespace PharmacyDesktop.Core;

public interface IExcelTable
{
    string SheetName { get; }
    void Load(XLWorkbook wb);
    void Write(XLWorkbook wb);
}

/// <summary>
/// An in-memory list of entities that is persisted as one worksheet.
/// Row 1 holds the property names as headers; columns are matched by header so the
/// sheet stays readable/editable in Excel and tolerant to column re-ordering.
/// </summary>
public sealed class ExcelTable<T> : IExcelTable where T : class, IEntity, new()
{
    private static readonly PropertyInfo[] Props = typeof(T)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && IsSupported(p.PropertyType))
        .OrderBy(p => p.Name == nameof(IEntity.Id) ? 0 : 1)
        .ToArray();

    public ExcelTable(string sheetName) => SheetName = sheetName;

    public string SheetName { get; }
    public List<T> Rows { get; } = new();

    public int NextId() => Rows.Count == 0 ? 1 : Rows.Max(r => r.Id) + 1;

    public T Add(T entity)
    {
        entity.Id = NextId();
        Rows.Add(entity);
        return entity;
    }

    public T? Find(int id) => Rows.FirstOrDefault(r => r.Id == id);

    public bool Remove(T entity) => Rows.Remove(entity);

    public IEnumerable<T> Where(Func<T, bool> predicate) => Rows.Where(predicate);

    private static bool IsSupported(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t == typeof(string) || t == typeof(int) || t == typeof(decimal) || t == typeof(double)
               || t == typeof(bool) || t == typeof(DateTime);
    }

    public void Load(XLWorkbook wb)
    {
        Rows.Clear();
        if (!wb.TryGetWorksheet(SheetName, out var ws)) return;
        var headerRow = ws.FirstRowUsed();
        if (headerRow == null) return;

        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
            columns[cell.GetString().Trim()] = cell.Address.ColumnNumber;

        var map = Props.Where(p => columns.ContainsKey(p.Name))
                       .Select(p => (Prop: p, Col: columns[p.Name]))
                       .ToArray();

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (int r = headerRow.RowNumber() + 1; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            if (row.IsEmpty()) continue;
            var entity = new T();
            foreach (var (prop, col) in map)
            {
                try { prop.SetValue(entity, ReadCell(row.Cell(col), prop.PropertyType)); }
                catch { /* leave default if a cell was hand-edited into an invalid value */ }
            }
            Rows.Add(entity);
        }

        // Repair missing/duplicate ids (e.g. rows typed in manually in Excel).
        var seen = new HashSet<int>();
        int next = Rows.Count == 0 ? 1 : Math.Max(1, Rows.Max(x => x.Id) + 1);
        foreach (var e in Rows)
            if (e.Id <= 0 || !seen.Add(e.Id)) { e.Id = next++; seen.Add(e.Id); }
    }

    public void Write(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add(SheetName);
        for (int c = 0; c < Props.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = Props[c].Name;
        }

        int r = 2;
        foreach (var entity in Rows)
        {
            for (int c = 0; c < Props.Length; c++)
                WriteCell(ws.Cell(r, c + 1), Props[c].GetValue(entity));
            r++;
        }

        // Light formatting so the file is pleasant to open directly in Excel.
        var header = ws.Range(1, 1, 1, Props.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromArgb(13, 148, 136);
        ws.SheetView.FreezeRows(1);
        for (int c = 0; c < Props.Length; c++)
        {
            var t = Nullable.GetUnderlyingType(Props[c].PropertyType) ?? Props[c].PropertyType;
            var col = ws.Column(c + 1);
            if (t == typeof(DateTime)) col.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            else if (t == typeof(decimal)) col.Style.NumberFormat.Format = "0.00";
            col.Width = t == typeof(string) ? 22 : t == typeof(DateTime) ? 18 : 12;
        }
    }

    private static object? ReadCell(IXLCell cell, Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var t = underlying ?? type;
        var v = cell.Value;

        if (v.IsBlank)
        {
            if (underlying != null) return null;
            if (t == typeof(string)) return "";
            return Activator.CreateInstance(t);
        }

        if (t == typeof(string))
        {
            if (v.IsNumber) return v.GetNumber().ToString(CultureInfo.InvariantCulture);
            if (v.IsDateTime) return v.GetDateTime().ToString("yyyy-MM-dd HH:mm");
            if (v.IsBoolean) return v.GetBoolean() ? "TRUE" : "FALSE";
            return cell.GetString();
        }
        if (t == typeof(int))
            return v.IsNumber ? (int)Math.Round(v.GetNumber())
                 : int.TryParse(cell.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var i) ? i : 0;
        if (t == typeof(decimal))
            return v.IsNumber ? Math.Round((decimal)v.GetNumber(), 4)
                 : decimal.TryParse(cell.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
        if (t == typeof(double))
            return v.IsNumber ? v.GetNumber()
                 : double.TryParse(cell.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var db) ? db : 0d;
        if (t == typeof(bool))
        {
            if (v.IsBoolean) return v.GetBoolean();
            if (v.IsNumber) return v.GetNumber() != 0;
            var s = cell.GetString().Trim().ToLowerInvariant();
            return s is "true" or "yes" or "y" or "1" or "active";
        }
        if (t == typeof(DateTime))
        {
            if (v.IsDateTime) return v.GetDateTime();
            if (v.IsNumber) return DateTime.FromOADate(v.GetNumber());
            if (DateTime.TryParse(cell.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return dt;
            return underlying != null ? null : DateTime.MinValue;
        }
        return null;
    }

    private static void WriteCell(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null: cell.Value = Blank.Value; break;
            case string s: cell.Value = s; break;
            case int i: cell.Value = i; break;
            case decimal d: cell.Value = (double)d; break;
            case double db: cell.Value = db; break;
            case bool b: cell.Value = b; break;
            case DateTime dt: cell.Value = dt; break;
            default: cell.Value = value.ToString(); break;
        }
    }
}
