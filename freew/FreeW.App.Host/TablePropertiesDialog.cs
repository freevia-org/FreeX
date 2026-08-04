using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Table Properties" dialog (Table Tools &gt; Layout &gt; Properties), edited across four tabs that
/// mirror Word's layout.
/// </summary>
internal sealed class TablePropertiesDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    internal enum Tab { Table, Row, Column, Cell }

    private readonly CheckBox _preferredWidthOn;
    private readonly TextBox _preferredWidth;
    private readonly ComboBox _alignment;
    private readonly ComboBox _wrapping;
    private readonly CheckBox _allowFloatingOverlap;
    private readonly TextBox _indent;
    private readonly TextBox _cellMarginTop;
    private readonly TextBox _cellMarginLeft;
    private readonly TextBox _cellMarginBottom;
    private readonly TextBox _cellMarginRight;
    private readonly CheckBox _cellSpacingOn;
    private readonly TextBox _cellSpacing;

    private readonly CheckBox _rowHeightOn;
    private readonly TextBox _rowHeight;
    private readonly ComboBox _rowRule;
    private readonly CheckBox _allowRowBreak;
    private readonly CheckBox _repeatHeader;

    private readonly CheckBox _columnWidthOn;
    private readonly TextBox _columnWidth;

    private readonly CheckBox _cellWidthOn;
    private readonly TextBox _cellWidth;
    private readonly ComboBox _cellVAlign;
    private readonly CheckBox _cellMarginsOn;
    private readonly TextBox _cmTop;
    private readonly TextBox _cmLeft;
    private readonly TextBox _cmBottom;
    private readonly TextBox _cmRight;
    private readonly CheckBox _cellWrapText;
    private readonly CheckBox _cellFitText;

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

        var state = TablePropertiesDialogPlanner.BuildInitialState(context, CultureInfo.CurrentCulture);

        _preferredWidth = NumberBox(state.PreferredWidthText);
        _preferredWidthOn = Check("Preferred width (pt):", state.PreferredWidthOn);
        _alignment = Combo(TablePropertiesDialogPlanner.AlignmentNames, state.AlignmentIndex);
        _wrapping = Combo(TablePropertiesDialogPlanner.WrappingNames, state.WrappingIndex);
        _allowFloatingOverlap = new CheckBox
        {
            Content = "Allow overlap",
            IsThreeState = true,
            IsChecked = state.FloatingTableAllowsOverlap,
            Margin = new Thickness(0, 4, 0, 4)
        };
        _allowFloatingOverlap.IsEnabled = state.WrappingIndex == 1;
        _wrapping.SelectionChanged += (_, _) => _allowFloatingOverlap.IsEnabled = _wrapping.SelectedIndex == 1;
        AutomationProperties.SetAutomationId(_allowFloatingOverlap, "TablePropertiesAllowOverlapCheckBox");
        _indent = NumberBox(state.IndentText);
        _cellMarginTop = NumberBox(state.DefaultCellMarginTopText);
        _cellMarginLeft = NumberBox(state.DefaultCellMarginLeftText);
        _cellMarginBottom = NumberBox(state.DefaultCellMarginBottomText);
        _cellMarginRight = NumberBox(state.DefaultCellMarginRightText);
        _cellSpacing = NumberBox(state.CellSpacingText);
        _cellSpacingOn = Check("Allow spacing between cells (pt):", state.CellSpacingOn);

        _rowHeight = NumberBox(state.RowHeightText);
        _rowHeightOn = Check("Specify height (pt):", state.RowHeightOn);
        _rowRule = Combo(TablePropertiesDialogPlanner.RowRuleNames, state.RowRuleIndex);
        _allowRowBreak = new CheckBox { Content = "Allow row to break across pages", IsChecked = state.AllowRowBreak };
        _repeatHeader = new CheckBox { Content = "Repeat as header row at the top of each page", IsChecked = state.RepeatHeaderRow, Margin = new Thickness(0, 4, 0, 0) };

        _columnWidth = NumberBox(state.ColumnWidthText);
        _columnWidthOn = Check("Preferred width (pt):", state.ColumnWidthOn);

        _cellWidth = NumberBox(state.CellWidthText);
        _cellWidthOn = Check("Preferred width (pt):", state.CellWidthOn);
        _cellVAlign = Combo(TablePropertiesDialogPlanner.CellVerticalAlignmentNames, state.CellVerticalAlignmentIndex);
        _cmTop = NumberBox(state.CellMarginTopText);
        _cmLeft = NumberBox(state.CellMarginLeftText);
        _cmBottom = NumberBox(state.CellMarginBottomText);
        _cmRight = NumberBox(state.CellMarginRightText);
        _cellMarginsOn = Check("Same as the whole table", state.CellMarginsSameAsTable);
        _cellWrapText = Check("Wrap text", state.CellWrapText);
        _cellFitText = Check("Fit text", state.CellFitText);
        AutomationProperties.SetAutomationId(_cellWrapText, "TablePropertiesCellWrapTextCheckBox");
        AutomationProperties.SetAutomationId(_cellFitText, "TablePropertiesCellFitTextCheckBox");

        AutomationProperties.SetAutomationId(_preferredWidth, "TablePropertiesPreferredWidthBox");
        AutomationProperties.SetAutomationId(_rowHeight, "TablePropertiesRowHeightBox");
        AutomationProperties.SetAutomationId(_columnWidth, "TablePropertiesColumnWidthBox");
        AutomationProperties.SetAutomationId(_cellWidth, "TablePropertiesCellWidthBox");

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

        var initialFocus = initialTab switch
        {
            Tab.Row => _rowHeight,
            Tab.Column => _columnWidth,
            Tab.Cell => _cellWidth,
            _ => _preferredWidth,
        };
        DialogFocus.FocusAndSelect(initialFocus);
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
        stack.Children.Add(_allowFloatingOverlap);
        stack.Children.Add(_cellWrapText);
        stack.Children.Add(_cellFitText);
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

    private static ComboBox Combo(IReadOnlyList<string> items, int selectedIndex)
    {
        var combo = new ComboBox { MinWidth = 180 };
        foreach (var item in items)
            combo.Items.Add(item);
        combo.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Count - 1);
        return combo;
    }

    private static TextBox NumberBox(string text) => new()
    {
        Text = text,
        MinWidth = 120
    };

    private static CheckBox Check(string content, bool isChecked) =>
        new() { Content = content, IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };

    private static void AddRow(Grid grid, int row, string label, UIElement field)
    {
        var block = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);
        PlaceField(grid, row, field);
    }

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
        var input = new TablePropertiesDialogInput(
            PreferredWidthOn: _preferredWidthOn.IsChecked == true,
            PreferredWidthText: _preferredWidth.Text,
            AlignmentIndex: _alignment.SelectedIndex,
            WrappingIndex: _wrapping.SelectedIndex,
            IndentText: _indent.Text,
            DefaultCellMarginTopText: _cellMarginTop.Text,
            DefaultCellMarginLeftText: _cellMarginLeft.Text,
            DefaultCellMarginBottomText: _cellMarginBottom.Text,
            DefaultCellMarginRightText: _cellMarginRight.Text,
            CellSpacingOn: _cellSpacingOn.IsChecked == true,
            CellSpacingText: _cellSpacing.Text,
            RowHeightOn: _rowHeightOn.IsChecked == true,
            RowHeightText: _rowHeight.Text,
            RowRuleIndex: _rowRule.SelectedIndex,
            AllowRowBreak: _allowRowBreak.IsChecked == true,
            RepeatHeaderRow: _repeatHeader.IsChecked == true,
            ColumnWidthOn: _columnWidthOn.IsChecked == true,
            ColumnWidthText: _columnWidth.Text,
            CellWidthOn: _cellWidthOn.IsChecked == true,
            CellWidthText: _cellWidth.Text,
            CellVerticalAlignmentIndex: _cellVAlign.SelectedIndex,
            CellMarginsSameAsTable: _cellMarginsOn.IsChecked == true,
            CellMarginTopText: _cmTop.Text,
            CellMarginLeftText: _cmLeft.Text,
            CellMarginBottomText: _cmBottom.Text,
            CellMarginRightText: _cmRight.Text,
            CellWrapText: _cellWrapText.IsChecked == true,
            CellFitText: _cellFitText.IsChecked == true,
            FloatingTableAllowsOverlap: _allowFloatingOverlap.IsChecked);

        if (!TablePropertiesDialogPlanner.TryBuildResult(
                input,
                CultureInfo.CurrentCulture,
                out _result,
                out var errorMessage))
        {
            DialogMessageHelper.ShowWarning(this, errorMessage ?? TablePropertiesDialogPlanner.ValidationMessage);
            return;
        }

        Close();
    }

    internal static TablePropertiesDialog CreateForTest(ModelTableContext context, Tab initialTab = Tab.Table) =>
        new(owner: null, context, initialTab);

    internal TablePropertiesValues? AcceptForTest()
    {
        Accept();
        return _result;
    }

    public static TablePropertiesValues? Prompt(Window? owner, ModelTableContext context, Tab initialTab = Tab.Table)
    {
        var dialog = new TablePropertiesDialog(owner, context, initialTab);
        dialog.ShowDialog();
        return dialog._result;
    }
}
