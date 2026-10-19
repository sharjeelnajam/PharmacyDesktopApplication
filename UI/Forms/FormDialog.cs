using PharmacyDesktop.UI.Controls;

namespace PharmacyDesktop.UI.Forms;

/// <summary>
/// Builds simple data-entry dialogs from a list of fields, laid out in a two-column grid.
/// Usage: var d = new FormDialog("Add Supplier"); d.AddText("name", "Name *"); ... if (d.ShowDialog() == OK) d.Text("name")
/// </summary>
public class FormDialog : Form
{
    private readonly TableLayoutPanel _table;
    private readonly Panel _header, _footer;
    private readonly Dictionary<string, Control> _fields = new();
    private int _cell;

    public Func<FormDialog, string?>? Validator { get; set; }

    public FormDialog(string title, string? subtitle = null, int columns = 2, int width = 640)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Surface;
        Font = Theme.Body;
        ShowInTaskbar = false;
        Width = width;
        AutoScaleMode = AutoScaleMode.None;

        var header = _header = new Panel { Dock = DockStyle.Top, Height = subtitle == null ? 62 : 78, BackColor = Theme.Surface, Padding = new Padding(24, 16, 24, 0) };
        var lbl = Ui.Label(title, Theme.H2);
        lbl.Location = new Point(24, 16);
        header.Controls.Add(lbl);
        if (subtitle != null)
        {
            var sub = Ui.Label(subtitle, Theme.Small, Theme.TextMuted);
            sub.Location = new Point(25, 46);
            header.Controls.Add(sub);
        }

        _table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = columns,
            Padding = new Padding(24, 4, 24, 8),
            BackColor = Theme.Surface
        };
        for (int i = 0; i < columns; i++) _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));

        var footer = _footer = new Panel { Dock = DockStyle.Bottom, Height = 68, BackColor = Theme.SurfaceAlt, Padding = new Padding(24, 14, 24, 14) };
        var save = new ModernButton("Save", Theme.Glyph.Save) { Dock = DockStyle.Right, Width = 120 };
        var cancel = new ModernButton("Cancel", "", ButtonKind.Secondary) { Dock = DockStyle.Right, Width = 100 };
        var gap = new Panel { Dock = DockStyle.Right, Width = 10, BackColor = Theme.SurfaceAlt };
        save.Click += (_, _) => TrySave();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; };
        footer.Controls.Add(cancel);
        footer.Controls.Add(gap);
        footer.Controls.Add(save);
        AcceptButton = save;
        CancelButton = cancel;

        Controls.Add(_table);
        Controls.Add(header);
        Controls.Add(footer);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ClientSize = new Size(ClientSize.Width, _header.Height + _table.GetPreferredSize(new Size(ClientSize.Width, 0)).Height + _footer.Height + 8);
        var first = _fields.Values.FirstOrDefault(c => c.Enabled && c is not CheckBox);
        first?.Select();
    }

    private void TrySave()
    {
        var error = Validator?.Invoke(this);
        if (!string.IsNullOrEmpty(error))
        {
            Ui.Error(error, Text);
            return;
        }
        DialogResult = DialogResult.OK;
    }

    private T Add<T>(string key, string caption, T control, bool fullRow) where T : Control
    {
        control.Dock = DockStyle.Top;
        control.AccessibleName = caption;
        var panel = new Panel { Height = control is TextBox { Multiline: true } ? 100 : 56, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 14, 4), BackColor = Theme.Surface };
        if (control is CheckBox)
        {
            control.Dock = DockStyle.None;
            control.Location = new Point(0, 26);
            panel.Controls.Add(control);
        }
        else
        {
            var lbl = Ui.FieldLabel(caption);
            lbl.Dock = DockStyle.Top;
            panel.Controls.Add(control);
            panel.Controls.Add(lbl);
        }

        var cols = _table.ColumnCount;
        if (fullRow && _cell % cols != 0) _cell += cols - _cell % cols;
        var row = _cell / cols;
        while (_table.RowCount <= row) { _table.RowCount++; _table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
        _table.Controls.Add(panel, _cell % cols, row);
        if (fullRow) { _table.SetColumnSpan(panel, cols); _cell += cols; }
        else _cell++;

        _fields[key] = control;
        return control;
    }

    public TextBox AddText(string key, string caption, string? value = "", bool fullRow = false, bool password = false, bool multiline = false)
    {
        var t = Ui.TextBox();
        t.Text = value ?? "";
        if (password) t.UseSystemPasswordChar = true;
        if (multiline) { t.Multiline = true; t.Height = 70; t.ScrollBars = ScrollBars.Vertical; }
        return Add(key, caption, t, fullRow || multiline);
    }

    public ComboBox AddCombo(string key, string caption, IEnumerable<string> options, string? value, bool editable = false, bool fullRow = false)
    {
        var c = Ui.Combo(options.Distinct().Where(o => !string.IsNullOrWhiteSpace(o)).OrderBy(o => o).Cast<object>(), editable: editable);
        if (editable) c.Text = value ?? "";
        else if (value != null && c.Items.Contains(value)) c.SelectedItem = value;
        else if (c.Items.Count > 0) c.SelectedIndex = 0;
        return Add(key, caption, c, fullRow);
    }

    public ComboBox AddComboFixed(string key, string caption, string[] options, string? value)
    {
        var c = Ui.Combo(options.Cast<object>());
        c.SelectedItem = value != null && options.Contains(value) ? value : options.FirstOrDefault();
        return Add(key, caption, c, false);
    }

    public NumericUpDown AddNumber(string key, string caption, decimal value, int decimals = 0, decimal max = 100_000_000, decimal min = 0)
    {
        var n = Ui.Number(value, decimals, max);
        n.Minimum = min;
        n.Value = Math.Max(min, Math.Min(max, value));
        n.TextAlign = HorizontalAlignment.Left;
        n.Enter += (_, _) => n.Select(0, n.Text.Length);
        return Add(key, caption, n, false);
    }

    public DateTimePicker AddDate(string key, string caption, DateTime value) => Add(key, caption, Ui.Date(value), false);

    public CheckBox AddCheck(string key, string caption, bool value) =>
        Add(key, caption, new CheckBox { Text = caption, Checked = value, AutoSize = true, Font = Theme.Body }, false);

    public Control this[string key] => _fields[key];
    public string Str(string key) => (_fields[key] is ComboBox { DropDownStyle: ComboBoxStyle.DropDownList } c ? c.SelectedItem?.ToString() : _fields[key].Text)?.Trim() ?? "";
    public decimal Num(string key) => ((NumericUpDown)_fields[key]).Value;
    public int Int(string key) => (int)((NumericUpDown)_fields[key]).Value;
    public DateTime DateValue(string key) => ((DateTimePicker)_fields[key]).Value;
    public bool Bool(string key) => ((CheckBox)_fields[key]).Checked;
}
