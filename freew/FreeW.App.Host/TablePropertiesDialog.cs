using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// The values the <see cref="TablePropertiesDialog"/> produces, applied onto the caret's table / row /
/// column / cell by <see cref="FreeW.App.Host.Editing.DocumentView.ApplyTableProperties"/>. They map directly
/// onto the model's table / row / cell properties, which round-trip via w:tblPr / w:trPr / w:tcPr.
/// </summary>
public sealed record TablePropertiesValues(
    // Table tab.
    double? PreferredWidthPt,
    TableAlignment Alignment,
    bool TextWrapping,
    double? IndentFromLeftPt,
    TableCellMargins? DefaultCellMargins,
    double? CellSpacingPt,
    // Row tab.
    double? RowHeightPt,
    TableRowHeightRule RowHeightRule,
    bool AllowRowBreak,
    bool RepeatHeaderRow,
    // Column tab.
    double? ColumnWidthPt,
    // Cell tab.
    double? CellPreferredWidthPt,
    TableCellVerticalAlignment CellVerticalAlignment,
    TableCellMargins? CellMargins);

/// <summary>
/// Word's "Table Properties" dialog (Table Tools &gt; Layout &gt; Properties), edited across four tabs that
/// mirror Word's layout:
/// <list type="bullet">
/// <item>Table — preferred width, alignment (left/center/right), text wrapping (none/around), indent-from-left,
/// default cell margins and cell spacing.</item>
/// <item>Row — explicit height with the exact/at-least rule, "allow row to break across pages" and "repeat as
/// header row at the top of each page".</item>
/// <item>Column — preferred column width.</item>
/// <item>Cell — preferred cell width, vertical alignment (top/center/bottom) and a per-cell margin override.</item>
/// </list>
///
/// <para>
/// The dialog only produces a <see cref="TablePropertiesValues"/>; the ribbon command applies it through
/// <see cref="FreeW.App.Host.Editing.DocumentView.ApplyTableProperties"/> — the same commit + re-render path the
/// other table commands use — so the edited values round-trip through the w:tblPr / w:trPr / w:tcPr writers.
/// Measurements are shown in points, matching FreeW's other tables/page-setup dialogs. Built on the shared
/// <see cref="Free.Shared.Ribbon.Wpf.DialogWindow"/> + dialog helpers (button row, focus), exactly like
/// <see cref="PageSetupDialog"/>.
/// </para>
/// </summary>
internal sealed class TablePropertiesDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    /// <summary>Which tab the dialog opens on (Table by default).</summary>
    internal enum Tab { Table, Row, Column, Cell }

    private static readonly string[] AlignmentNames = ["Left", "Center", "Right"];
    private static readonly TableAlignment[] AlignmentValues =
        [TableAlignment.Left, TableAlignment.Center, TableAlignment.Right];

    private static readonly string[] WrappingNames = ["None", "Around"];

    private static readonly string[] RowRuleNames = ["At least", "Exactly"];
    private static readonly TableRowHeightRule[] RowRuleValues =
        [TableRowHeightRule.AtLeast, TableRowHeightRule.Exact];

    private static readonly string[] CellVAlignNames = ["Top", "Center", "Bottom"];
    private static readonly TableCellVerticalAlignment[] CellVAlignValues =
        [TableCellVerticalAlignment.Top, TableCellVerticalAlignment.Center, TableCellVerticalAlignment.Bottom];

    // Table tab.
    private readonly CheckBox _preferredWidthOn;
    private readonly TextBox _preferredWidth;
    private readonly ComboBox _alignment;
    private readonly ComboBox _wrapping;
    private readonly TextBox _indent;
    private readonly TextBox _cellMarginTop;
    private readonly TextBox _cellMarginLeft;
    private readonly TextBox _cellMarginBottom;
    private readonly TextBox _cellMarginRight;
    private readonly CheckBox _cellSpacingOn;
    private readonly TextBox _cellSpacing;

    // Row tab.
    private readonly CheckBox _rowHeightOn;
    private readonly TextBox _rowHeight;
    private readonly ComboBox _rowRule;
    private readonly CheckBox _allowRowBreak;
    private readonly CheckBox _repeatHeader;

    // Column tab.
    private readonly CheckBox _columnWidthOn;
    private readonly TextBox _columnWidth;

    // Cell tab.
    private readonly CheckBox _cellWidthOn;
    private readonly TextBox _cellWidth;
    private readonly ComboBox _cellVAlign;
    private readonly CheckBox _cellMarginsOn;
    private readonly TextBox _cmTop;
    private readonly TextBox _cmLeft;
    private readonly TextBox _cmBottom;
    private readonly TextBox _cmRight;

    private TablePropertiesValues? _result;

    private TablePropertiesDialog(Window? owner, ModelTableContext context, Tab initialTab)
    {
        Owner = owner;
        Title = "Table Properties";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var table = context.Table;
        var row = context.Row;
        var cell = context.Cell;

        // Table tab fields.
        _preferredWidth = NumberBox(table.PreferredWidthPt ?? 0);
        _preferredWidthOn = Check("Preferred width (pt):", table.PreferredWidthPt is not null);
        _alignment = Combo(AlignmentNames, Math.Max(0, Array.IndexOf(AlignmentValues, table.Alignment)));
        _wrapping = Combo(WrappingNames, table.TextWrapping ? 1 : 0);
        _indent = NumberBox(table.IndentFromLeftPt ?? 0);
        var defaults = table.DefaultCellMargins ?? TableCellMargins.Default;
        _cellMarginTop = NumberBox(defaults.TopPt);
        _cellMarginLeft = NumberBox(defaults.LeftPt);
        _cellMarginBottom = NumberBox(defaults.BottomPt);
        _cellMarginRight = NumberBox(defaults.RightPt);
        _cellSpacing = NumberBox(table.CellSpacingPt ?? 0);
        _cellSpacingOn = Check("Allow spacing between cells (pt):", table.CellSpacingPt is not null);

        // Row tab fields.
        _rowHeight = NumberBox(row?.HeightPt ?? 0);
        _rowHeightOn = Check("Specify height (pt):", row?.HeightPt is not null);
        var rule = row?.HeightRule ?? TableRowHeightRule.AtLeast;
        _rowRule = Combo(RowRuleNames, Math.Max(0, Array.IndexOf(RowRuleValues,
            rule == TableRowHeightRule.Exact ? TableRowHeightRule.Exact : TableRowHeightRule.AtLeast)));
        _allowRowBreak = new CheckBox { Content = "Allow row to break across pages", IsChecked = row?.AllowBreakAcrossPages ?? true };
        _repeatHeader = new CheckBox { Content = "Repeat as header row at the top of each page", IsChecked = table.Formatting.RepeatHeaderRow, Margin = new Thickness(0, 4, 0, 0) };

        // Column tab fields. Seed from the caret cell's width (per-cell preferred width is the model's column width).
        _columnWidth = NumberBox(cell?.WidthPt ?? 0);
        _columnWidthOn = Check("Preferred width (pt):", cell?.WidthPt is not null);

        // Cell tab fields.
        _cellWidth = NumberBox(cell?.WidthPt ?? 0);
        _cellWidthOn = Check("Preferred width (pt):", cell?.WidthPt is not null);
        _cellVAlign = Combo(CellVAlignNames, Math.Max(0, Array.IndexOf(CellVAlignValues, cell?.VerticalAlignment ?? TableCellVerticalAlignment.Top)));
        var cellMar = cell?.Margins ?? defaults;
        _cmTop = NumberBox(cellMar.TopPt);
        _cmLeft = NumberBox(cellMar.LeftPt);
        _cmBottom = NumberBox(cellMar.BottomPt);
        _cmRight = NumberBox(cellMar.RightPt);
        _cellMarginsOn = Check("Same as the whole table", cell?.Margins is null);

        var tabs = new TabControl { Margin = new Thickness(14, 14, 14, 0) };
        tabs.Items.Add(new TabItem { Header = "Table", Content = BuildTableTab() });
        tabs.Items.Add(new TabItem { Header = "Row", Content = BuildRowTab() });
        tabs.Items.Add(new TabItem { Header = "Column", Content = BuildColumnTab() });
        tabs.Items.Add(new TabItem { Header = "Cell", Content = BuildCellTab() });
        tabs.SelectedIndex = (int)initialTab;

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(14, 12, 14, 12));

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(tabs);
        Content = root;

        DialogFocus.FocusAndSelect(_preferredWidth);
    }

    private UIElement BuildTableTab()
    {
        var grid = TwoColumnGrid(4);
        AddRow(grid, 0, _preferredWidthOn, _preferredWidth);
        AddRow(grid, 1, "Alignment:", _alignment);
        AddRow(grid, 2, "Text wrapping:", _wrapping);
        AddRow(grid, 3, "Indent from left (pt):", _indent);

        var marginsHeader = new TextBlock { Text = "Default cell margins (pt):", Margin = new Thickness(0, 10, 0, 4), FontWeight = FontWeights.SemiBold };
        var marginsGrid = TwoColumnGrid(4);
        AddRow(marginsGrid, 0, "Top:", _cellMarginTop);
        AddRow(marginsGrid, 1, "Left:", _cellMarginLeft);
        AddRow(marginsGrid, 2, "Bottom:", _cellMarginBottom);
        AddRow(marginsGrid, 3, "Right:", _cellMarginRight);

        var spacingGrid = TwoColumnGrid(1);
        AddRow(spacingGrid, 0, _cellSpacingOn, _cellSpacing);

        var stack = new StackPanel { Margin = new Thickness(14) };
        stack.Children.Add(grid);
        stack.Children.Add(marginsHeader);
        stack.Children.Add(marginsGrid);
        stack.Children.Add(spacingGrid);
        return stack;
    }

    private UIElement BuildRowTab()
    {
        var grid = TwoColumnGrid(2);
        AddRow(grid, 0, _rowHeightOn, _rowHeight);
        AddRow(grid, 1, "Row height is:", _rowRule);

        var checks = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        checks.Children.Add(_allowRowBreak);
        checks.Children.Add(_repeatHeader);

        var stack = new StackPanel { Margin = new Thickness(14) };
        stack.Children.Add(grid);
        stack.Children.Add(checks);
        return stack;
    }

    private UIElement BuildColumnTab()
    {
        var grid = TwoColumnGrid(1);
        AddRow(grid, 0, _columnWidthOn, _columnWidth);
        return new StackPanel { Margin = new Thickness(14), Children = { grid } };
    }

    private UIElement BuildCellTab()
    {
        var grid = TwoColumnGrid(2);
        AddRow(grid, 0, _cellWidthOn, _cellWidth);
        AddRow(grid, 1, "Vertical alignment:", _cellVAlign);

        var marginsHeader = new TextBlock { Text = "Cell margins (pt):", Margin = new Thickness(0, 10, 0, 4), FontWeight = FontWeights.SemiBold };
        var marginsGrid = TwoColumnGrid(4);
        AddRow(marginsGrid, 0, "Top:", _cmTop);
        AddRow(marginsGrid, 1, "Left:", _cmLeft);
        AddRow(marginsGrid, 2, "Bottom:", _cmBottom);
        AddRow(marginsGrid, 3, "Right:", _cmRight);

        var stack = new StackPanel { Margin = new Thickness(14) };
        stack.Children.Add(grid);
        stack.Children.Add(_cellMarginsOn);
        stack.Children.Add(marginsHeader);
        stack.Children.Add(marginsGrid);
        return stack;
    }

    private static Grid TwoColumnGrid(int rows)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < rows; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    private static ComboBox Combo(string[] items, int selectedIndex)
    {
        var combo = new ComboBox { MinWidth = 180 };
        foreach (var item in items)
            combo.Items.Add(item);
        combo.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Length - 1);
        return combo;
    }

    private static TextBox NumberBox(double value) => new()
    {
        Text = value.ToString("0.##", CultureInfo.CurrentCulture),
        MinWidth = 120
    };

    private static CheckBox Check(string content, bool isChecked) =>
        new() { Content = content, IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };

    // Adds a "<label>: <field>" row to the two-column grid.
    private static void AddRow(Grid grid, int row, string label, UIElement field)
    {
        var block = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);
        PlaceField(grid, row, field);
    }

    // Adds a "<checkbox> <field>" row (the checkbox toggles whether the field's value is applied).
    private static void AddRow(Grid grid, int row, CheckBox toggle, UIElement field)
    {
        Grid.SetRow(toggle, row);
        Grid.SetColumn(toggle, 0);
        grid.Children.Add(toggle);
        PlaceField(grid, row, field);
    }

    private static void PlaceField(Grid grid, int row, UIElement field)
    {
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, 4, 0, 4);
        grid.Children.Add(field);
    }

    private void Accept()
    {
        // Validate every visible number; an unchecked optional field is not required to parse to a value.
        if (!TryReadOptional(_preferredWidthOn, _preferredWidth, out var preferredWidth)
            || !TryParse(_indent.Text, out var indent) || indent < 0
            || !TryParse(_cellMarginTop.Text, out var dmTop) || dmTop < 0
            || !TryParse(_cellMarginLeft.Text, out var dmLeft) || dmLeft < 0
            || !TryParse(_cellMarginBottom.Text, out var dmBottom) || dmBottom < 0
            || !TryParse(_cellMarginRight.Text, out var dmRight) || dmRight < 0
            || !TryReadOptional(_cellSpacingOn, _cellSpacing, out var cellSpacing)
            || !TryReadOptional(_rowHeightOn, _rowHeight, out var rowHeight)
            || !TryReadOptional(_columnWidthOn, _columnWidth, out var columnWidth)
            || !TryReadOptional(_cellWidthOn, _cellWidth, out var cellWidth)
            || !TryParse(_cmTop.Text, out var cmTop) || cmTop < 0
            || !TryParse(_cmLeft.Text, out var cmLeft) || cmLeft < 0
            || !TryParse(_cmBottom.Text, out var cmBottom) || cmBottom < 0
            || !TryParse(_cmRight.Text, out var cmRight) || cmRight < 0)
        {
            DialogMessageHelper.ShowWarning(this, "Enter non-negative measurements (in points) for every checked field.");
            return;
        }

        _result = new TablePropertiesValues(
            PreferredWidthPt: preferredWidth,
            Alignment: AlignmentValues[Math.Max(0, _alignment.SelectedIndex)],
            TextWrapping: _wrapping.SelectedIndex == 1,
            IndentFromLeftPt: indent > 0 ? indent : null,
            DefaultCellMargins: new TableCellMargins(dmTop, dmLeft, dmBottom, dmRight),
            CellSpacingPt: cellSpacing,
            RowHeightPt: rowHeight,
            RowHeightRule: rowHeight is null ? TableRowHeightRule.Auto : RowRuleValues[Math.Max(0, _rowRule.SelectedIndex)],
            AllowRowBreak: _allowRowBreak.IsChecked == true,
            RepeatHeaderRow: _repeatHeader.IsChecked == true,
            ColumnWidthPt: columnWidth,
            CellPreferredWidthPt: cellWidth,
            CellVerticalAlignment: CellVAlignValues[Math.Max(0, _cellVAlign.SelectedIndex)],
            // "Same as the whole table" checked → no per-cell override (null); unchecked → explicit margins.
            CellMargins: _cellMarginsOn.IsChecked == true ? null : new TableCellMargins(cmTop, cmLeft, cmBottom, cmRight));
        Close();
    }

    // Reads an optional number: when the toggle is off the value is null (and the box need not parse);
    // when on the box must parse to a non-negative value (else validation fails).
    private static bool TryReadOptional(CheckBox toggle, TextBox box, out double? value)
    {
        if (toggle.IsChecked != true)
        {
            value = null;
            return true;
        }
        if (TryParse(box.Text, out var v) && v >= 0)
        {
            value = v;
            return true;
        }
        value = null;
        return false;
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    /// <summary>
    /// Test seam: builds a non-modal dialog instance seeded from <paramref name="context"/> so unit tests can
    /// exercise the control wiring without a modal loop.
    /// </summary>
    internal static TablePropertiesDialog CreateForTest(ModelTableContext context, Tab initialTab = Tab.Table) =>
        new(owner: null, context, initialTab);

    /// <summary>
    /// Test seam: validates the current control values and returns the <see cref="TablePropertiesValues"/> they
    /// describe (or null when validation fails), without closing the window — the same mapping
    /// <see cref="Accept"/> performs.
    /// </summary>
    internal TablePropertiesValues? AcceptForTest()
    {
        Accept();
        return _result;
    }

    /// <summary>
    /// Show the Table Properties dialog seeded from the caret's <paramref name="context"/>, opened on
    /// <paramref name="initialTab"/>. Returns the chosen values, or null if cancelled.
    /// </summary>
    public static TablePropertiesValues? Prompt(Window? owner, ModelTableContext context, Tab initialTab = Tab.Table)
    {
        var dialog = new TablePropertiesDialog(owner, context, initialTab);
        dialog.ShowDialog();
        return dialog._result;
    }
}

/// <summary>
/// The model objects under the caret that the <see cref="TablePropertiesDialog"/> seeds from: the table plus
/// the caret's row and cell (the row/cell may be null when the caret location can't be resolved).
/// </summary>
public sealed record ModelTableContext(Table Table, TableRow? Row, TableCell? Cell);
