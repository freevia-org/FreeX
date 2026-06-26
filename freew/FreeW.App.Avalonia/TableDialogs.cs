using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// AV-TBLDLG: FreeW Avalonia <b>Table Properties</b> dialog — a tabbed modal <see cref="Window"/> over the
/// caret's table, mirroring Word's "Table Properties" (Table Tools &gt; Layout &gt; Properties) across four tabs:
/// <list type="bullet">
///   <item>Table — preferred width (+ "on" toggle), alignment (Left/Center/Right), text wrapping (None/Around).</item>
///   <item>Row — explicit height with the Exactly/At least rule, "Allow row to break across pages",
///     "Repeat as header row at the top of each page".</item>
///   <item>Column — preferred column width.</item>
///   <item>Cell — preferred cell width, vertical alignment (Top/Center/Bottom).</item>
/// </list>
///
/// <para>
/// Pre-populated from <see cref="DocumentView.GetCaretTableProperties"/> on open; on OK the values are applied
/// via <see cref="DocumentView.ApplyTableProperties"/> as one undoable step. The apply path is exposed as
/// <see cref="ApplyResult"/> so tests can drive the model without showing a window. Measurements are in points,
/// matching the other FreeW Avalonia dialogs (Page Setup / Paragraph).
/// </para>
///
/// Deferred (not in the model-backed subset): indent-from-left, default cell margins, cell spacing, per-cell
/// margin override, and the cell horizontal alignment (that lives on the Layout tab's Alignment group).
/// </summary>
public sealed class TablePropertiesDialog : Window
{
    private static readonly string[] AlignmentNames = ["Left", "Center", "Right"];
    private static readonly TableAlignment[] AlignmentValues =
        [TableAlignment.Left, TableAlignment.Center, TableAlignment.Right];

    private static readonly string[] WrappingNames = ["None", "Around"];

    // Row tab presents the two explicit rules (Auto is implied by clearing the height "on" toggle).
    private static readonly string[] RowRuleNames = ["At least", "Exactly"];
    private static readonly TableRowHeightRule[] RowRuleValues =
        [TableRowHeightRule.AtLeast, TableRowHeightRule.Exact];

    private static readonly string[] CellVAlignNames = ["Top", "Center", "Bottom"];
    private static readonly TableCellVerticalAlignment[] CellVAlignValues =
        [TableCellVerticalAlignment.Top, TableCellVerticalAlignment.Center, TableCellVerticalAlignment.Bottom];

    // ── Table tab ─────────────────────────────────────────────────────────────
    private readonly CheckBox _preferredWidthOn = new() { Content = "Preferred width (pt):", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _preferredWidth = MakeNumericBox();
    private readonly ComboBox _alignment = new() { MinWidth = 140 };
    private readonly ComboBox _wrapping = new() { MinWidth = 140 };

    // ── Row tab ───────────────────────────────────────────────────────────────
    private readonly CheckBox _rowHeightOn = new() { Content = "Specify height (pt):", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _rowHeight = MakeNumericBox();
    private readonly ComboBox _rowRule = new() { MinWidth = 140 };
    private readonly CheckBox _allowRowBreak = new() { Content = "Allow row to break across pages" };
    private readonly CheckBox _repeatHeader = new() { Content = "Repeat as header row at the top of each page" };

    // ── Column tab ────────────────────────────────────────────────────────────
    private readonly CheckBox _columnWidthOn = new() { Content = "Preferred width (pt):", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _columnWidth = MakeNumericBox();

    // ── Cell tab ──────────────────────────────────────────────────────────────
    private readonly CheckBox _cellWidthOn = new() { Content = "Preferred width (pt):", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _cellWidth = MakeNumericBox();
    private readonly ComboBox _cellVAlign = new() { MinWidth = 140 };

    private readonly TextBlock _status = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x00, 0x00)),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
        IsVisible = false,
    };

    public TablePropertiesDialog(DocumentView.TablePropertiesSnapshot current)
    {
        Title = "Table Properties";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _alignment.ItemsSource = AlignmentNames;
        _wrapping.ItemsSource = WrappingNames;
        _rowRule.ItemsSource = RowRuleNames;
        _cellVAlign.ItemsSource = CellVAlignNames;

        // ── Pre-populate from the caret table ────────────────────────────────
        _preferredWidthOn.IsChecked = current.PreferredWidthPt is not null;
        _preferredWidth.Text = Pt(current.PreferredWidthPt ?? 0);
        _alignment.SelectedIndex = Math.Max(0, Array.IndexOf(AlignmentValues, current.Alignment));
        _wrapping.SelectedIndex = current.TextWrapping ? 1 : 0;

        _rowHeightOn.IsChecked = current.RowHeightPt is not null;
        _rowHeight.Text = Pt(current.RowHeightPt ?? 0);
        var ruleIdx = Array.IndexOf(RowRuleValues, current.RowHeightRule);
        _rowRule.SelectedIndex = ruleIdx >= 0 ? ruleIdx : 0;
        _allowRowBreak.IsChecked = current.AllowRowBreak;
        _repeatHeader.IsChecked = current.RepeatHeaderRow;

        _columnWidthOn.IsChecked = current.ColumnWidthPt is not null;
        _columnWidth.Text = Pt(current.ColumnWidthPt ?? 0);

        _cellWidthOn.IsChecked = current.CellPreferredWidthPt is not null;
        _cellWidth.Text = Pt(current.CellPreferredWidthPt ?? 0);
        _cellVAlign.SelectedIndex = Math.Max(0, Array.IndexOf(CellVAlignValues, current.CellVerticalAlignment));

        // ── Layout ───────────────────────────────────────────────────────────
        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Table",  Content = BuildTableTab() },
                new TabItem { Header = "Row",     Content = BuildRowTab() },
                new TabItem { Header = "Column",  Content = BuildColumnTab() },
                new TabItem { Header = "Cell",    Content = BuildCellTab() },
            },
        };

        var ok = new Button { Content = "OK", MinWidth = 84, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 84, IsCancel = true };
        ok.Click += (_, _) => OnOk();
        cancel.Click += (_, _) => Close(null);

        var outer = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        outer.Children.Add(tabs);
        outer.Children.Add(_status);
        outer.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { ok, cancel },
        });
        Content = outer;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(null); e.Handled = true; }
        };
    }

    private Control BuildTableTab()
    {
        var panel = new StackPanel { Margin = new Thickness(10), Spacing = 8 };
        panel.Children.Add(Row(_preferredWidthOn, _preferredWidth));
        panel.Children.Add(LabeledRow("Alignment:", _alignment));
        panel.Children.Add(LabeledRow("Text wrapping:", _wrapping));
        return panel;
    }

    private Control BuildRowTab()
    {
        var panel = new StackPanel { Margin = new Thickness(10), Spacing = 8 };
        panel.Children.Add(Row(_rowHeightOn, _rowHeight));
        panel.Children.Add(LabeledRow("Row height is:", _rowRule));
        panel.Children.Add(_allowRowBreak);
        panel.Children.Add(_repeatHeader);
        return panel;
    }

    private Control BuildColumnTab()
    {
        var panel = new StackPanel { Margin = new Thickness(10), Spacing = 8 };
        panel.Children.Add(Row(_columnWidthOn, _columnWidth));
        return panel;
    }

    private Control BuildCellTab()
    {
        var panel = new StackPanel { Margin = new Thickness(10), Spacing = 8 };
        panel.Children.Add(Row(_cellWidthOn, _cellWidth));
        panel.Children.Add(LabeledRow("Vertical alignment:", _cellVAlign));
        return panel;
    }

    private void OnOk()
    {
        _status.IsVisible = false;

        double? preferredWidth = null;
        if (_preferredWidthOn.IsChecked == true)
        {
            if (!TryParsePos(_preferredWidth.Text, "Preferred table width", out var w)) return;
            preferredWidth = w;
        }

        double? rowHeight = null;
        if (_rowHeightOn.IsChecked == true)
        {
            if (!TryParsePos(_rowHeight.Text, "Row height", out var h)) return;
            rowHeight = h;
        }

        double? columnWidth = null;
        if (_columnWidthOn.IsChecked == true)
        {
            if (!TryParsePos(_columnWidth.Text, "Column width", out var cw)) return;
            columnWidth = cw;
        }

        double? cellWidth = null;
        if (_cellWidthOn.IsChecked == true)
        {
            if (!TryParsePos(_cellWidth.Text, "Cell width", out var cellw)) return;
            cellWidth = cellw;
        }

        var result = new TablePropertiesValues(
            PreferredWidthPt: preferredWidth,
            Alignment: AlignmentValues[Math.Clamp(_alignment.SelectedIndex, 0, AlignmentValues.Length - 1)],
            TextWrapping: _wrapping.SelectedIndex == 1,
            RowHeightPt: rowHeight,
            // The rule is only meaningful when a height is set; default to AtLeast when not.
            RowHeightRule: rowHeight is null
                ? TableRowHeightRule.Auto
                : RowRuleValues[Math.Clamp(_rowRule.SelectedIndex, 0, RowRuleValues.Length - 1)],
            AllowRowBreak: _allowRowBreak.IsChecked == true,
            RepeatHeaderRow: _repeatHeader.IsChecked == true,
            ColumnWidthPt: columnWidth,
            CellPreferredWidthPt: cellWidth,
            CellVerticalAlignment: CellVAlignValues[Math.Clamp(_cellVAlign.SelectedIndex, 0, CellVAlignValues.Length - 1)]);

        Close(result);
    }

    /// <summary>
    /// AV-TBLDLG: apply a dialog result onto the editor's caret table via
    /// <see cref="DocumentView.ApplyTableProperties"/>. Safe to call without showing the window (tests).
    /// </summary>
    public static void ApplyResult(DocumentView editor, TablePropertiesValues result)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(result);
        editor.ApplyTableProperties(result);
    }

    /// <summary>
    /// Show the Table Properties dialog modally over the caret's table and, on OK, apply the result. No-op
    /// when the caret is not inside a table. Must be called from the UI thread.
    /// </summary>
    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);

        if (editor.GetCaretTableProperties() is not { } current)
            return; // not in a table
        var dialog = new TablePropertiesDialog(current);
        var result = await dialog.ShowDialog<TablePropertiesValues?>(owner);
        if (result is null) return;
        ApplyResult(editor, result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool TryParsePos(string? text, string field, out double value)
    {
        value = 0;
        var t = (text ?? string.Empty).Trim();
        if (!double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out value) || value <= 0)
        {
            _status.Text = $"Invalid value for {field}: \"{t}\". Enter a positive number.";
            _status.IsVisible = true;
            return false;
        }
        return true;
    }

    private static string Pt(double v) => v == 0 ? string.Empty : v.ToString("G5", CultureInfo.InvariantCulture);

    private static TextBox MakeNumericBox() => new() { Width = 90 };

    private static Control Row(Control left, Control right)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(left);
        row.Children.Add(right);
        return row;
    }

    private static Control LabeledRow(string label, Control field)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, MinWidth = 110 });
        row.Children.Add(field);
        return row;
    }
}

/// <summary>
/// AV-TBLDLG: FreeW Avalonia <b>Insert Table</b> dialog — Word's "Insert Table…" (Insert &gt; Table &gt; Insert
/// Table…). Collects the number of columns and rows (numeric) plus an AutoFit option, and on OK inserts the
/// table via <see cref="DocumentView.InsertTable"/>. The AutoFit option is captured in the result for parity
/// but the Avalonia <c>InsertTable</c> currently inserts a fixed-grid table (AutoFit application deferred).
/// </summary>
public sealed class InsertTableDialog : Window
{
    private readonly NumericUpDown _columns = MakeCount(5);
    private readonly NumericUpDown _rows = MakeCount(2);
    private readonly RadioButton _fitContent = new() { Content = "AutoFit to contents", GroupName = "autofit" };
    private readonly RadioButton _fitWindow = new() { Content = "AutoFit to window", GroupName = "autofit" };
    private readonly RadioButton _fixedWidth = new() { Content = "Fixed column width", GroupName = "autofit", IsChecked = true };

    /// <summary>The chosen (rows, columns, autofit) on OK, or null on cancel.</summary>
    public InsertTableResult? Result { get; private set; }

    /// <summary>Result of the Insert Table dialog. <see cref="AutoFit"/> mirrors the model enum (Fixed default).</summary>
    public sealed record InsertTableResult(int Rows, int Columns, AutoFitMode AutoFit);

    public InsertTableDialog(int initialColumns = 5, int initialRows = 2)
    {
        Title = "Insert Table";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _columns.Value = Math.Clamp(initialColumns, 1, 63);
        _rows.Value = Math.Clamp(initialRows, 1, 1000);

        var size = new StackPanel { Spacing = 8 };
        size.Children.Add(new TextBlock { Text = "Table size", FontWeight = FontWeight.SemiBold });
        size.Children.Add(LabeledRow("Number of columns:", _columns));
        size.Children.Add(LabeledRow("Number of rows:", _rows));

        var fit = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 0) };
        fit.Children.Add(new TextBlock { Text = "AutoFit behavior", FontWeight = FontWeight.SemiBold });
        fit.Children.Add(_fixedWidth);
        fit.Children.Add(_fitContent);
        fit.Children.Add(_fitWindow);

        var ok = new Button { Content = "OK", MinWidth = 84, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 84, IsCancel = true };
        ok.Click += (_, _) => OnOk();
        cancel.Click += (_, _) => Close();

        var outer = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        outer.Children.Add(size);
        outer.Children.Add(fit);
        outer.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { ok, cancel },
        });
        Content = outer;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
    }

    private void OnOk()
    {
        var cols = (int)Math.Clamp(_columns.Value ?? 1, 1, 63);
        var rows = (int)Math.Clamp(_rows.Value ?? 1, 1, 1000);
        var autoFit = _fitContent.IsChecked == true ? AutoFitMode.Contents
            : _fitWindow.IsChecked == true ? AutoFitMode.Window
            : AutoFitMode.Fixed;
        Result = new InsertTableResult(rows, cols, autoFit);
        Close();
    }

    /// <summary>
    /// AV-TBLDLG: apply an Insert Table result by inserting the table at the caret via
    /// <see cref="DocumentView.InsertTable"/>. Safe to call without showing the window (tests).
    /// </summary>
    public static void ApplyResult(DocumentView editor, InsertTableResult result)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(result);
        editor.InsertTable(result.Rows, result.Columns);
    }

    /// <summary>Show the Insert Table dialog modally and, on OK, insert the table. Must run on the UI thread.</summary>
    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);

        var dialog = new InsertTableDialog();
        await dialog.ShowDialog(owner);
        if (dialog.Result is { } result)
            ApplyResult(editor, result);
    }

    private static NumericUpDown MakeCount(int value) => new()
    {
        Minimum = 1,
        Maximum = 1000,
        Increment = 1,
        Value = value,
        Width = 90,
        FormatString = "0",
    };

    private static Control LabeledRow(string label, Control field)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 });
        row.Children.Add(field);
        return row;
    }
}
