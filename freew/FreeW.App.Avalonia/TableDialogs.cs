using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Input;
using System.Collections.Generic;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class TableFormulaDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle;

    private readonly TextBox _formula;
    private readonly ComboBox _format;
    private readonly ComboBox _function;
    private readonly TextBlock _validation = new();
    private static readonly DialogFocusPlan FocusPlan = FreeWDialogFocusPlanner.TableFormula;

    public TableFormulaField? Result { get; private set; }

    public TableFormulaDialog(TableFormulaDialogInitialState initialState)
    {
        ArgumentNullException.ThrowIfNull(initialState);

        Title = TableFormulaDialogPlanner.Title;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, "TableFormulaDialog");

        _formula = new TextBox { Text = initialState.FormulaText };
        _format = new ComboBox { IsEditable = true };
        foreach (var format in TableFormulaDialogPlanner.NumberFormats)
            _format.Items.Add(format);
        _format.SelectedIndex = Math.Clamp(
            initialState.NumberFormatIndex,
            0,
            _format.ItemCount - 1);

        _function = new ComboBox();
        foreach (var function in TableFormulaDialogPlanner.Functions)
            _function.Items.Add(function);
        _function.SelectionChanged += (_, _) => PasteSelectedFunction();

        ApplyInputChrome(_formula, FocusPlan.InitialFocusTargetAutomationId);
        ApplyComboChrome(_format, "TableFormulaNumberFormatBox");
        ApplyComboChrome(_function, "TableFormulaPasteFunctionBox");
        AvaloniaCompactDialogChrome.ApplyValidationStatus(
            _validation,
            DialogChromeStyle,
            new Thickness(0, 6, 0, 0));
        AutomationProperties.SetAutomationId(_validation, "TableFormulaValidationText");

        var body = new StackPanel { Margin = new Thickness(14), Spacing = 4 };
        body.Children.Add(Label(TableFormulaDialogPlanner.FormulaLabel));
        body.Children.Add(_formula);
        body.Children.Add(Label(TableFormulaDialogPlanner.NumberFormatLabel, top: 6));
        body.Children.Add(_format);
        body.Children.Add(Label(TableFormulaDialogPlanner.PasteFunctionLabel, top: 6));
        body.Children.Add(_function);
        body.Children.Add(_validation);

        var ok = Button("OK", "TableFormulaOkButton", isDefault: true);
        ok.Click += (_, _) => Accept();
        var cancel = Button("Cancel", "TableFormulaCancelButton", isCancel: true);
        cancel.Click += (_, _) => Close();
        body.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow(
            [ok, cancel],
            new Thickness(0, 8, 0, 0)));

        Content = body;
        Opened += (_, _) => FocusFormula();
    }

    internal TextBox FormulaBoxForTest => _formula;
    internal ComboBox FormatBoxForTest => _format;
    internal ComboBox FunctionBoxForTest => _function;
    internal TextBlock ValidationForTest => _validation;

    internal TableFormulaField? AcceptForTest(string? formula, string? format)
    {
        _formula.Text = formula;
        _format.Text = format;
        TryAccept(close: false);
        return Result;
    }

    internal void PasteFunctionForTest(string functionName)
    {
        _function.SelectedItem = functionName;
        PasteSelectedFunction();
    }

    private void Accept() => TryAccept(close: true);

    private void TryAccept(bool close)
    {
        if (!TableFormulaDialogPlanner.TryBuildResult(
                new TableFormulaDialogInput(_formula.Text, _format.Text),
                out var result,
                out var errorMessage))
        {
            _validation.Text = errorMessage ?? TableFormulaDialogPlanner.ValidationMessage;
            _validation.IsVisible = true;
            FocusFormula();
            return;
        }

        _validation.IsVisible = false;
        Result = result;
        if (close)
            Close();
    }

    private void PasteSelectedFunction()
    {
        if (_function.SelectedItem is not string functionName)
            return;

        var pasted = TableFormulaDialogPlanner.PasteFunction(_formula.Text, functionName);
        _formula.Text = pasted.Text;
        _formula.CaretIndex = pasted.CaretIndex;
        _formula.Focus();
        _function.SelectedIndex = -1;
    }

    private void FocusFormula()
    {
        if (FocusPlan.SelectAllOnFocus)
            AvaloniaCompactDialogChrome.FocusAndSelect(_formula);
        else
            _formula.Focus();
    }

    private static TextBlock Label(string text, double top = 0) =>
        new() { Text = text, Margin = new Thickness(0, top, 0, 0) };

    private static void ApplyInputChrome(TextBox box, string automationId)
    {
        AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);
        AutomationProperties.SetAutomationId(box, automationId);
    }

    private static void ApplyComboChrome(ComboBox box, string automationId)
    {
        AvaloniaCompactDialogChrome.ApplyComboBox(box, DialogChromeStyle);
        AutomationProperties.SetAutomationId(box, automationId);
    }

    private static Button Button(
        string text,
        string automationId,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new Button
        {
            Content = text,
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, 72, isDefault);
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }
}

internal enum TablePropertiesDialogTab
{
    Table,
    Row,
    Column,
    Cell,
}

internal sealed class TablePropertiesDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle with
        {
            // WPF's standard action row keeps the two buttons 14px apart and does not
            // paint the default-button border until that button receives focus.
            ActionSpacing = 14,
            DefaultButtonBorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
        };

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
    private readonly TextBlock _validation = new();
    private readonly TabControl _tabs;
    private readonly Control[] _initialFocusTargets;
    private readonly List<string> _focusTrace = [];

    public TablePropertiesValues? Result { get; private set; }

    public TablePropertiesDialog(
        ModelTableContext context,
        TablePropertiesDialogTab initialTab = TablePropertiesDialogTab.Table)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = TablePropertiesDialogPlanner.BuildInitialState(
            context,
            CultureInfo.CurrentCulture);

        Title = "Table Properties";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, "TablePropertiesDialog");

        _preferredWidth = NumberBox(state.PreferredWidthText, "TablePropertiesPreferredWidthBox");
        _preferredWidthOn = Check("Preferred width (pt):", state.PreferredWidthOn, "TablePropertiesPreferredWidthCheckBox");
        _alignment = Combo(TablePropertiesDialogPlanner.AlignmentNames, state.AlignmentIndex, "TablePropertiesAlignmentBox");
        _wrapping = Combo(TablePropertiesDialogPlanner.WrappingNames, state.WrappingIndex, "TablePropertiesWrappingBox");
        _allowFloatingOverlap = new CheckBox
        {
            Content = "Allow overlap",
            IsThreeState = true,
            IsChecked = state.FloatingTableAllowsOverlap,
            Margin = new Thickness(4, 4, 8, 4),
            IsEnabled = state.WrappingIndex == 1,
        };
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(_allowFloatingOverlap, DialogChromeStyle);
        AutomationProperties.SetAutomationId(_allowFloatingOverlap, "TablePropertiesAllowOverlapCheckBox");
        _wrapping.SelectionChanged += (_, _) => _allowFloatingOverlap.IsEnabled = _wrapping.SelectedIndex == 1;
        _indent = NumberBox(state.IndentText, "TablePropertiesIndentBox");
        _cellMarginTop = NumberBox(state.DefaultCellMarginTopText, "TablePropertiesDefaultMarginTopBox");
        _cellMarginLeft = NumberBox(state.DefaultCellMarginLeftText, "TablePropertiesDefaultMarginLeftBox");
        _cellMarginBottom = NumberBox(state.DefaultCellMarginBottomText, "TablePropertiesDefaultMarginBottomBox");
        _cellMarginRight = NumberBox(state.DefaultCellMarginRightText, "TablePropertiesDefaultMarginRightBox");
        _cellSpacing = NumberBox(state.CellSpacingText, "TablePropertiesCellSpacingBox");
        _cellSpacingOn = Check("Allow spacing between cells (pt):", state.CellSpacingOn, "TablePropertiesCellSpacingCheckBox");

        _rowHeight = NumberBox(state.RowHeightText, "TablePropertiesRowHeightBox");
        _rowHeightOn = Check("Specify height (pt):", state.RowHeightOn, "TablePropertiesRowHeightCheckBox");
        _rowRule = Combo(TablePropertiesDialogPlanner.RowRuleNames, state.RowRuleIndex, "TablePropertiesRowRuleBox");
        _allowRowBreak = Check("Allow row to break across pages", state.AllowRowBreak, "TablePropertiesAllowRowBreakCheckBox");
        _repeatHeader = Check("Repeat as header row at the top of each page", state.RepeatHeaderRow, "TablePropertiesRepeatHeaderCheckBox");

        _columnWidth = NumberBox(state.ColumnWidthText, "TablePropertiesColumnWidthBox");
        _columnWidthOn = Check("Preferred width (pt):", state.ColumnWidthOn, "TablePropertiesColumnWidthCheckBox");

        _cellWidth = NumberBox(state.CellWidthText, "TablePropertiesCellWidthBox");
        _cellWidthOn = Check("Preferred width (pt):", state.CellWidthOn, "TablePropertiesCellWidthCheckBox");
        _cellVAlign = Combo(TablePropertiesDialogPlanner.CellVerticalAlignmentNames, state.CellVerticalAlignmentIndex, "TablePropertiesCellVerticalAlignmentBox");
        _cmTop = NumberBox(state.CellMarginTopText, "TablePropertiesCellMarginTopBox");
        _cmLeft = NumberBox(state.CellMarginLeftText, "TablePropertiesCellMarginLeftBox");
        _cmBottom = NumberBox(state.CellMarginBottomText, "TablePropertiesCellMarginBottomBox");
        _cmRight = NumberBox(state.CellMarginRightText, "TablePropertiesCellMarginRightBox");
        _cellMarginsOn = Check("Same as the whole table", state.CellMarginsSameAsTable, "TablePropertiesSameMarginsCheckBox");
        _cellWrapText = Check("Wrap text", state.CellWrapText, "TablePropertiesCellWrapTextCheckBox");
        _cellFitText = Check("Fit text", state.CellFitText, "TablePropertiesCellFitTextCheckBox");

        _tabs = new TabControl { Margin = new Thickness(14, 14, 14, 0) };
        _tabs.Items.Add(TabPage("Table", "TablePropertiesTableTab", BuildTableTab()));
        _tabs.Items.Add(TabPage("Row", "TablePropertiesRowTab", BuildRowTab()));
        _tabs.Items.Add(TabPage("Column", "TablePropertiesColumnTab", BuildColumnTab()));
        _tabs.Items.Add(TabPage("Cell", "TablePropertiesCellTab", BuildCellTab()));
        _tabs.SelectionChanged += (_, _) =>
        {
            if (_tabs.SelectedIndex is >= 0 and < 4)
                _focusTrace.Add($"TabPage:{((TabItem)_tabs.SelectedItem!).Header}");
        };
        _tabs.SelectedIndex = Math.Clamp((int)initialTab, 0, 3);
        AutomationProperties.SetAutomationId(_tabs, "TablePropertiesTabs");
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(
            _tabs,
            DialogChromeStyle,
            contentPaneMargin: new Thickness(-12, 0, -12, 0));
        _initialFocusTargets = [_preferredWidth, _rowHeight, _columnWidth, _cellWidth];

        AvaloniaCompactDialogChrome.ApplyValidationStatus(
            _validation,
            DialogChromeStyle,
            new Thickness(14, 6, 14, 0));
        AutomationProperties.SetAutomationId(_validation, "TablePropertiesValidationText");

        var ok = TableFormulaDialogButton("OK", "TablePropertiesOkButton", isDefault: true);
        ok.Click += (_, _) => Accept();
        var cancel = TableFormulaDialogButton("Cancel", "TablePropertiesCancelButton", isCancel: true);
        cancel.Click += (_, _) => Close();
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [ok, cancel],
            new Thickness(14, 12, 14, 12),
            DialogChromeStyle);

        var bottom = new StackPanel();
        bottom.Children.Add(_validation);
        bottom.Children.Add(buttons);
        DockPanel.SetDock(bottom, Dock.Bottom);
        Content = new DockPanel { LastChildFill = true, Children = { bottom, _tabs } };
        AddHandler(InputElement.GotFocusEvent, (_, args) =>
        {
            if (args.Source is Control control && AutomationProperties.GetAutomationId(control) is { Length: > 0 } automationId)
                _focusTrace.Add(automationId);
        }, RoutingStrategies.Bubble);
        Opened += (_, _) => FocusInitialField();
    }

    internal TabControl TabsForTest => _tabs;
    internal TextBlock ValidationForTest => _validation;
    internal Control InitialFocusTargetForTest => _initialFocusTargets[_tabs.SelectedIndex];
    internal IReadOnlyList<string> FocusTraceForValidation => _focusTrace;

    internal TablePropertiesValues? AcceptForTest() => TryAccept(close: false);

    private Control BuildTableTab()
    {
        var grid = TwoColumnGrid(4, 137);
        AddRow(grid, 0, _preferredWidthOn, _preferredWidth);
        AddRow(grid, 1, "Alignment:", _alignment);
        AddRow(grid, 2, "Text wrapping:", _wrapping);
        AddRow(grid, 3, "Indent from left (pt):", _indent);

        var margins = TwoColumnGrid(4, 54);
        AddRow(margins, 0, "Top:", _cellMarginTop);
        AddRow(margins, 1, "Left:", _cellMarginLeft);
        AddRow(margins, 2, "Bottom:", _cellMarginBottom);
        AddRow(margins, 3, "Right:", _cellMarginRight);
        var spacing = TwoColumnGrid(1, 203);
        AddRow(spacing, 0, _cellSpacingOn, _cellSpacing);

        return Stack(
            grid,
            _allowFloatingOverlap,
            Header("Default cell margins (pt):"),
            margins,
            spacing);
    }

    private static TabItem TabPage(string header, string automationId, Control content)
    {
        var tab = new TabItem { Header = header, Content = content };
        AutomationProperties.SetAutomationId(tab, automationId);
        return tab;
    }

    private Control BuildRowTab()
    {
        var grid = TwoColumnGrid(2, 131);
        AddRow(grid, 0, _rowHeightOn, _rowHeight);
        AddRow(grid, 1, "Row height is:", _rowRule);
        var checks = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        checks.Children.Add(_allowRowBreak);
        checks.Children.Add(_repeatHeader);
        return Stack(grid, checks);
    }

    private Control BuildColumnTab()
    {
        var grid = TwoColumnGrid(1, 137);
        AddRow(grid, 0, _columnWidthOn, _columnWidth);
        return Stack(grid);
    }

    private Control BuildCellTab()
    {
        var grid = TwoColumnGrid(2, 137);
        AddRow(grid, 0, _cellWidthOn, _cellWidth);
        AddRow(grid, 1, "Vertical alignment:", _cellVAlign);
        var margins = TwoColumnGrid(4, 54);
        AddRow(margins, 0, "Top:", _cmTop);
        AddRow(margins, 1, "Left:", _cmLeft);
        AddRow(margins, 2, "Bottom:", _cmBottom);
        AddRow(margins, 3, "Right:", _cmRight);
        return Stack(grid, _cellWrapText, _cellFitText, _cellMarginsOn, Header("Cell margins (pt):"), margins);
    }

    private void Accept() => TryAccept(close: true);

    private TablePropertiesValues? TryAccept(bool close)
    {
        var input = new TablePropertiesDialogInput(
            _preferredWidthOn.IsChecked == true,
            _preferredWidth.Text,
            _alignment.SelectedIndex,
            _wrapping.SelectedIndex,
            _indent.Text,
            _cellMarginTop.Text,
            _cellMarginLeft.Text,
            _cellMarginBottom.Text,
            _cellMarginRight.Text,
            _cellSpacingOn.IsChecked == true,
            _cellSpacing.Text,
            _rowHeightOn.IsChecked == true,
            _rowHeight.Text,
            _rowRule.SelectedIndex,
            _allowRowBreak.IsChecked == true,
            _repeatHeader.IsChecked == true,
            _columnWidthOn.IsChecked == true,
            _columnWidth.Text,
            _cellWidthOn.IsChecked == true,
            _cellWidth.Text,
            _cellVAlign.SelectedIndex,
            _cellMarginsOn.IsChecked == true,
            _cmTop.Text,
            _cmLeft.Text,
            _cmBottom.Text,
            _cmRight.Text,
            _cellWrapText.IsChecked == true,
            _cellFitText.IsChecked == true,
            _allowFloatingOverlap.IsChecked);

        if (!TablePropertiesDialogPlanner.TryBuildResult(
                input,
                CultureInfo.CurrentCulture,
                out var result,
                out var errorMessage))
        {
            _validation.Text = errorMessage ?? TablePropertiesDialogPlanner.ValidationMessage;
            _validation.IsVisible = true;
            FocusInitialField();
            return null;
        }

        _validation.IsVisible = false;
        Result = result;
        if (close)
            Close();
        return Result;
    }

    private void FocusInitialField()
    {
        var target = _initialFocusTargets[Math.Clamp(_tabs.SelectedIndex, 0, 3)];
        target.Focus();
        if (target is TextBox textBox)
            textBox.SelectAll();
    }

    private static StackPanel Stack(params Control[] controls)
    {
        var panel = new StackPanel { Margin = new Thickness(14) };
        foreach (var control in controls)
            panel.Children.Add(control);
        return panel;
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        Foreground = Brushes.Black,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 10, 0, 4),
    };

    private static Grid TwoColumnGrid(int rows, double? firstColumnWidth = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = firstColumnWidth is double width ? new GridLength(width) : GridLength.Auto,
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < rows; index++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    private static TextBox NumberBox(string text, string automationId)
    {
        var box = new TextBox { Text = text, MinWidth = 120 };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);
        AutomationProperties.SetAutomationId(box, automationId);
        return box;
    }

    private static CheckBox Check(string text, bool isChecked, string automationId)
    {
        var box = new CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 4, 8, 4),
        };
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(box, DialogChromeStyle);
        AutomationProperties.SetAutomationId(box, automationId);
        return box;
    }

    private static ComboBox Combo(
        IReadOnlyList<string> items,
        int selectedIndex,
        string automationId)
    {
        var combo = new ComboBox { MinWidth = 180 };
        foreach (var item in items)
            combo.Items.Add(item);
        combo.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Count - 1);
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, DialogChromeStyle);
        AutomationProperties.SetAutomationId(combo, automationId);
        return combo;
    }

    private static void AddRow(Grid grid, int row, string label, Control field)
    {
        var block = new TextBlock
        {
            Text = label,
            Foreground = Brushes.Black,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4),
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);
        PlaceField(grid, row, field);
    }

    private static void AddRow(Grid grid, int row, CheckBox toggle, Control field)
    {
        Grid.SetRow(toggle, row);
        Grid.SetColumn(toggle, 0);
        grid.Children.Add(toggle);
        PlaceField(grid, row, field);
    }

    private static void PlaceField(Grid grid, int row, Control field)
    {
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        field.Margin = new Thickness(0, 4, 0, 4);
        grid.Children.Add(field);
    }

    private static Button TableFormulaDialogButton(
        string text,
        string automationId,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new Button
        {
            Content = text,
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, 72, isDefault);
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }
}
