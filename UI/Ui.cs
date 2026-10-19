using ClosedXML.Excel;
using PharmacyDesktop.Core;
using PharmacyDesktop.UI.Controls;

namespace PharmacyDesktop.UI;

/// <summary>Factory helpers so pages stay short and consistent.</summary>
public static class Ui
{
    public static Label Label(string text, Font? font = null, Color? color = null, bool autoSize = true) => new()
    {
        Text = text,
        Font = font ?? Theme.Body,
        ForeColor = color ?? Theme.Text,
        AutoSize = autoSize,
        UseMnemonic = false,
        BackColor = Color.Transparent
    };

    public static Label FieldLabel(string text) => new()
    {
        Text = text,
        Font = Theme.SmallBold,
        ForeColor = Theme.TextMuted,
        AutoSize = true,
        UseMnemonic = false,
        BackColor = Color.Transparent,
        Margin = new Padding(0, 0, 0, 4)
    };

    public static TextBox TextBox(string placeholder = "", int width = 200) => new()
    {
        PlaceholderText = placeholder,
        Font = new Font("Segoe UI", 10.5F),
        BorderStyle = BorderStyle.FixedSingle,
        Width = width,
        BackColor = Color.White,
        ForeColor = Theme.Text
    };

    public static ComboBox Combo(IEnumerable<object>? items = null, int width = 200, bool editable = false)
    {
        var c = new ComboBox
        {
            Font = new Font("Segoe UI", 10.5F),
            Width = width,
            DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.System,
            BackColor = Color.White,
            IntegralHeight = false,
            MaxDropDownItems = 15
        };
        if (items != null) c.Items.AddRange(items.ToArray());
        if (editable)
        {
            c.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            c.AutoCompleteSource = AutoCompleteSource.ListItems;
        }
        return c;
    }

    public static DateTimePicker Date(DateTime? value = null, int width = 150) => new()
    {
        Font = new Font("Segoe UI", 10.5F),
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "dd-MMM-yyyy",
        Width = width,
        Value = value ?? DateTime.Today
    };

    public static NumericUpDown Number(decimal value = 0, int decimals = 0, decimal max = 100_000_000, int width = 120) => new()
    {
        Font = new Font("Segoe UI", 10.5F),
        DecimalPlaces = decimals,
        Maximum = max,
        Minimum = 0,
        Value = Math.Min(max, Math.Max(0, value)),
        Width = width,
        ThousandsSeparator = true,
        TextAlign = HorizontalAlignment.Right
    };

    /// <summary>Label stacked above an input — used in toolbars and forms.</summary>
    public static Panel Field(string caption, Control input, int? width = null)
    {
        input.Dock = DockStyle.Bottom;
        var p = new Panel { Width = width ?? input.Width, Height = 58, BackColor = Color.Transparent, Margin = new Padding(0, 0, 14, 6) };
        var lbl = FieldLabel(caption);
        lbl.Location = new Point(0, 0);
        p.Controls.Add(input);
        p.Controls.Add(lbl);
        return p;
    }

    public static FlowLayoutPanel Toolbar() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = true,
        BackColor = Color.Transparent,
        Padding = new Padding(0),
        Margin = new Padding(0)
    };

    public static Panel Spacer(int height) => new() { Dock = DockStyle.Top, Height = height, BackColor = Color.Transparent };

    /// <summary>Card with a title row and a grid filling the rest.</summary>
    public static Card GridCard(string title, DataGridView grid, params Control[] titleButtons)
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 12) };
        var header = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.Surface };
        var lbl = Label(title, Theme.H3);
        lbl.Location = new Point(0, 4);
        header.Controls.Add(lbl);
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, BackColor = Theme.Surface, Padding = new Padding(0, 2, 0, 0)
        };
        foreach (var b in titleButtons) { b.Margin = new Padding(6, 0, 0, 0); flow.Controls.Add(b); }
        header.Controls.Add(flow);
        grid.Dock = DockStyle.Fill;
        card.Controls.Add(grid);
        card.Controls.Add(header);
        // Optional one-line summary under the title; pages fetch it via card.Tag.
        var summary = Label("", Theme.Small, Theme.TextMuted);
        summary.Location = new Point(1, 30);
        header.Controls.Add(summary);
        card.Tag = summary;
        return card;
    }

    public static DataGridView Grid()
    {
        var g = new DataGridView();
        Theme.StyleGrid(g);
        return g;
    }

    public static void Info(string text, string title = "PharmaDesk") =>
        MessageBox.Show(text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static void Error(string text, string title = "PharmaDesk") =>
        MessageBox.Show(text, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static bool Confirm(string text, string title = "Please confirm") =>
        MessageBox.Show(text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    /// <summary>Runs an action and turns expected failures into friendly messages.</summary>
    public static bool Try(Action action)
    {
        try
        {
            Cursor.Current = Cursors.WaitCursor;
            action();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DataFileLockedException)
        {
            Error(ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Error("Something went wrong:\n" + ex.Message);
            return false;
        }
        finally
        {
            Cursor.Current = Cursors.Default;
        }
    }
}

/// <summary>Column definitions, Excel export and printing for read-only grids.</summary>
public static class GridHelper
{
    public static DataGridViewTextBoxColumn Col(this DataGridView g, string prop, string header, int width = 120,
        string? format = null, bool fill = false, bool right = false, int fillWeight = 100)
    {
        var c = new DataGridViewTextBoxColumn
        {
            DataPropertyName = prop,
            Name = prop,
            HeaderText = header,
            Width = width,
            SortMode = DataGridViewColumnSortMode.Automatic,
            MinimumWidth = 50
        };
        if (fill) { c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; c.FillWeight = fillWeight; c.MinimumWidth = width; }
        if (format != null) c.DefaultCellStyle.Format = format;
        if (right)
        {
            c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        g.Columns.Add(c);
        return c;
    }

    public static DataGridViewTextBoxColumn Money(this DataGridView g, string prop, string header, int width = 110) =>
        g.Col(prop, header, width, "N2", right: true);

    public static DataGridViewTextBoxColumn DateCol(this DataGridView g, string prop, string header, int width = 110, bool time = false) =>
        g.Col(prop, header, width, time ? "dd-MMM-yy hh:mm tt" : "dd-MMM-yyyy");

    public static void Bind<T>(this DataGridView g, IEnumerable<T> rows)
    {
        var sortCol = g.SortedColumn;
        var order = g.SortOrder;
        g.DataSource = new SortableBindingList<T>(rows);
        if (sortCol != null && order != SortOrder.None && g.Columns.Contains(sortCol))
            g.Sort(sortCol, order == SortOrder.Ascending ? System.ComponentModel.ListSortDirection.Ascending : System.ComponentModel.ListSortDirection.Descending);
    }

    public static T? Selected<T>(this DataGridView g) where T : class => g.CurrentRow?.DataBoundItem as T;

    public static void ExportToExcel(DataGridView grid, string title)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Excel Workbook|*.xlsx",
            FileName = $"{title.Replace(' ', '_')}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var ok = Ui.Try(() =>
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(title.Length > 31 ? title[..31] : title);
            ws.Cell(1, 1).Value = $"{Session.Tenant?.Name} — {title}";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = $"Generated {DateTime.Now:dd-MMM-yyyy hh:mm tt} by {Session.User?.FullName}";

            var cols = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible && c is not DataGridViewButtonColumn)
                           .OrderBy(c => c.DisplayIndex).ToList();
            for (int c = 0; c < cols.Count; c++)
            {
                var cell = ws.Cell(4, c + 1);
                cell.Value = cols[c].HeaderText;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(13, 148, 136);
            }

            int r = 5;
            foreach (DataGridViewRow row in grid.Rows)
            {
                for (int c = 0; c < cols.Count; c++)
                {
                    var v = row.Cells[cols[c].Index].Value;
                    var cell = ws.Cell(r, c + 1);
                    switch (v)
                    {
                        case decimal d: cell.Value = (double)d; cell.Style.NumberFormat.Format = "#,##0.00"; break;
                        case int i: cell.Value = i; break;
                        case double db: cell.Value = db; break;
                        case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = "dd-mmm-yyyy"; break;
                        case null: break;
                        default: cell.Value = row.Cells[cols[c].Index].FormattedValue?.ToString() ?? ""; break;
                    }
                }
                r++;
            }
            ws.Columns().AdjustToContents(4, r);
            wb.SaveAs(dlg.FileName);
        });
        if (ok && Ui.Confirm("Export complete. Open the file now?", "Export"))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
    }

    public static void Print(DataGridView grid, string title, string? subtitle = null) =>
        new GridPrinter(grid, title, subtitle).Preview();
}
