using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Input;
using System.Collections.Generic;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed partial class TableFormulaDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle;

    private readonly TableFormulaDialogSession _session;
    private readonly TextBox _formula;
    private readonly ComboBox _format;
    private readonly ComboBox _function;
    private readonly TextBlock _validation = new();
    private static readonly Free.Shared.Shell.DialogFocusPlan<string> FocusPlan = FreeWDialogFocusPlanner.TableFormula;

    public TableFormulaField? Result { get; private set; }

    public TableFormulaDialog(TableFormulaDialogInitialState initialState)
    {
        _session = new TableFormulaDialogSession(initialState);

        Title = TableFormulaDialogPlanner.Title;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, TableFormulaDialogPlanner.AutomationId);

        _formula = new TextBox { Text = _session.InitialState.FormulaText };
        _format = new ComboBox { IsEditable = true };
        foreach (var format in _session.NumberFormats)
            _format.Items.Add(format);
        _format.SelectedIndex = Math.Clamp(
            _session.InitialState.NumberFormatIndex,
            0,
            _format.ItemCount - 1);

        _function = new ComboBox();
        foreach (var function in _session.Functions)
            _function.Items.Add(function);
        _function.SelectionChanged += (_, _) => PasteSelectedFunction();

        ApplyInputChrome(_formula, FocusPlan.InitialFocusTarget);
        ApplyComboChrome(_format, TableFormulaDialogPlanner.NumberFormatAutomationId);
        ApplyComboChrome(_function, TableFormulaDialogPlanner.PasteFunctionAutomationId);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(
            _validation,
            DialogChromeStyle,
            new Thickness(0, 6, 0, 0));
        AutomationProperties.SetAutomationId(_validation, TableFormulaDialogPlanner.ValidationAutomationId);

        var body = new StackPanel { Margin = new Thickness(14), Spacing = 4 };
        body.Children.Add(Label(TableFormulaDialogPlanner.FormulaLabel));
        body.Children.Add(_formula);
        body.Children.Add(Label(TableFormulaDialogPlanner.NumberFormatLabel, top: 6));
        body.Children.Add(_format);
        body.Children.Add(Label(TableFormulaDialogPlanner.PasteFunctionLabel, top: 6));
        body.Children.Add(_function);
        body.Children.Add(_validation);

        var ok = Button(
            TableFormulaDialogPlanner.AcceptButtonLabel,
            TableFormulaDialogPlanner.AcceptButtonAutomationId,
            isDefault: true);
        ok.Click += (_, _) => Accept();
        var cancel = Button(
            TableFormulaDialogPlanner.CancelButtonLabel,
            TableFormulaDialogPlanner.CancelButtonAutomationId,
            isCancel: true);
        cancel.Click += (_, _) => Close();
        body.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow(
            [ok, cancel],
            new Thickness(0, 8, 0, 0)));

        Content = body;
        Opened += (_, _) => FocusFormula();
    }

    private void Accept() => TryAccept(close: true);

    private void TryAccept(bool close)
    {
        var acceptance = _session.PlanAcceptance(
            new TableFormulaDialogInput(_formula.Text, _format.Text));
        if (!acceptance.IsAccepted)
        {
            _validation.Text = acceptance.ValidationMessage!;
            _validation.IsVisible = true;
            FocusFormula();
            return;
        }

        _validation.IsVisible = false;
        Result = acceptance.Result;
        if (close)
            Close();
    }

    private void PasteSelectedFunction()
    {
        if (_function.SelectedItem is not string functionName)
            return;

        var pasted = _session.PasteFunction(_formula.Text, functionName);
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

internal sealed partial class TablePropertiesDialog : FreeWDialogWindow
{
    private static readonly TablePropertiesDialogVisualMetrics Layout =
        TablePropertiesDialogPlanner.VisualMetrics;

    private static AvaloniaCompactDialogChromeStyle DialogChromeStyle =>
        AvaloniaCompactDialogChrome.WindowsStyle with
        {
            // WPF's standard action row keeps the two buttons 14px apart and does not
            // paint the default-button border until that button receives focus.
            ActionSpacing = Layout.ActionSpacing,
            DefaultButtonBorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
        };

    private readonly TablePropertiesDialogSession _session;
    private readonly IUserMessageService _messageService;
    private readonly CheckBox _preferredWidthOn;
    private readonly TextBox _preferredWidth;
    private readonly ComboBox _alignment;
    private readonly ComboBox _wrapping;
    private readonly CheckBox _allowFloatingOverlap;
    private readonly ComboBox _floatingHorizontalAnchor;
    private readonly ComboBox _floatingHorizontalMode;
    private readonly TextBox _floatingHorizontalOffset;
    private readonly ComboBox _floatingVerticalAnchor;
    private readonly ComboBox _floatingVerticalMode;
    private readonly TextBox _floatingVerticalOffset;
    private readonly TextBox _floatingDistanceTop;
    private readonly TextBox _floatingDistanceLeft;
    private readonly TextBox _floatingDistanceBottom;
    private readonly TextBox _floatingDistanceRight;
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
    private readonly List<string> _focusTrace = [];

    public TablePropertiesValues? Result { get; private set; }

    public TablePropertiesDialog(
        ModelTableContext context,
        TablePropertiesDialogTabKind initialTab = TablePropertiesDialogTabKind.Table,
        IUserMessageService? messageService = null)
    {
        _session = new TablePropertiesDialogSession(context, CultureInfo.CurrentCulture, initialTab);
        _messageService = messageService ?? new AvaloniaUserMessageService(this);
        var state = _session.InitialState;

        Title = TablePropertiesDialogPlanner.Title;
        // The WPF authority capture uses monochrome glyph edges for this compact route.
        // Keep the correction local to Table Properties rather than changing shared dialog chrome.
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        Width = Layout.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, TablePropertiesDialogPlanner.AutomationId);

        _preferredWidth = NumberBox(state.PreferredWidthText, TablePropertiesDialogPlanner.PreferredWidthAutomationId);
        _preferredWidthOn = Check(TablePropertiesDialogPlanner.PreferredWidthLabel, state.PreferredWidthOn, TablePropertiesDialogPlanner.PreferredWidthToggleAutomationId);
        _alignment = Combo(_session.AlignmentNames, state.AlignmentIndex, TablePropertiesDialogPlanner.AlignmentAutomationId);
        _wrapping = Combo(_session.WrappingNames, state.WrappingIndex, TablePropertiesDialogPlanner.WrappingAutomationId);
        _allowFloatingOverlap = new CheckBox
        {
            Content = TablePropertiesDialogPlanner.AllowOverlapLabel,
            IsThreeState = true,
            IsChecked = state.FloatingTableAllowsOverlap,
            // WPF's native Positioning pane leaves the disabled overlap row below the
            // 560x600 evidence viewport. Preserve that measured ten-DIP bottom cadence
            // so the row cannot paint into the tab pane's action-row boundary.
            Margin = new Thickness(0, Layout.RowVerticalInset + 10, 0, Layout.RowVerticalInset),
            IsEnabled = state.WrappingIndex == 1,
        };
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(_allowFloatingOverlap, DialogChromeStyle);
        AutomationProperties.SetAutomationId(_allowFloatingOverlap, TablePropertiesDialogPlanner.AllowOverlapAutomationId);
        _floatingHorizontalAnchor = Combo(
            _session.FloatingHorizontalAnchorNames,
            state.FloatingHorizontalAnchorIndex,
            TablePropertiesDialogPlanner.HorizontalAnchorAutomationId);
        _floatingHorizontalMode = Combo(
            _session.FloatingHorizontalModeNames,
            state.FloatingHorizontalModeIndex,
            TablePropertiesDialogPlanner.HorizontalModeAutomationId);
        _floatingHorizontalOffset = NumberBox(state.FloatingHorizontalOffsetText, TablePropertiesDialogPlanner.HorizontalOffsetAutomationId);
        _floatingVerticalAnchor = Combo(
            _session.FloatingVerticalAnchorNames,
            state.FloatingVerticalAnchorIndex,
            TablePropertiesDialogPlanner.VerticalAnchorAutomationId);
        _floatingVerticalMode = Combo(
            _session.FloatingVerticalModeNames,
            state.FloatingVerticalModeIndex,
            TablePropertiesDialogPlanner.VerticalModeAutomationId);
        _floatingVerticalOffset = NumberBox(state.FloatingVerticalOffsetText, TablePropertiesDialogPlanner.VerticalOffsetAutomationId);
        _floatingDistanceTop = NumberBox(state.FloatingDistanceTopText, TablePropertiesDialogPlanner.DistanceTopAutomationId);
        _floatingDistanceLeft = NumberBox(state.FloatingDistanceLeftText, TablePropertiesDialogPlanner.DistanceLeftAutomationId);
        _floatingDistanceBottom = NumberBox(state.FloatingDistanceBottomText, TablePropertiesDialogPlanner.DistanceBottomAutomationId);
        _floatingDistanceRight = NumberBox(state.FloatingDistanceRightText, TablePropertiesDialogPlanner.DistanceRightAutomationId);
        _wrapping.SelectionChanged += (_, _) => UpdateFloatingPositionControls();
        _floatingHorizontalMode.SelectionChanged += (_, _) => UpdateFloatingPositionControls();
        _floatingVerticalMode.SelectionChanged += (_, _) => UpdateFloatingPositionControls();
        _indent = NumberBox(state.IndentText, TablePropertiesDialogPlanner.IndentAutomationId);
        _cellMarginTop = NumberBox(state.DefaultCellMarginTopText, TablePropertiesDialogPlanner.DefaultMarginTopAutomationId);
        _cellMarginLeft = NumberBox(state.DefaultCellMarginLeftText, TablePropertiesDialogPlanner.DefaultMarginLeftAutomationId);
        _cellMarginBottom = NumberBox(state.DefaultCellMarginBottomText, TablePropertiesDialogPlanner.DefaultMarginBottomAutomationId);
        _cellMarginRight = NumberBox(state.DefaultCellMarginRightText, TablePropertiesDialogPlanner.DefaultMarginRightAutomationId);
        _cellSpacing = NumberBox(state.CellSpacingText, TablePropertiesDialogPlanner.CellSpacingAutomationId);
        _cellSpacingOn = Check(TablePropertiesDialogPlanner.CellSpacingLabel, state.CellSpacingOn, TablePropertiesDialogPlanner.CellSpacingToggleAutomationId);

        _rowHeight = NumberBox(state.RowHeightText, TablePropertiesDialogPlanner.RowHeightAutomationId);
        _rowHeightOn = Check(TablePropertiesDialogPlanner.SpecifyRowHeightLabel, state.RowHeightOn, TablePropertiesDialogPlanner.RowHeightToggleAutomationId);
        _rowRule = Combo(_session.RowRuleNames, state.RowRuleIndex, TablePropertiesDialogPlanner.RowRuleAutomationId);
        _allowRowBreak = Check(TablePropertiesDialogPlanner.AllowRowBreakLabel, state.AllowRowBreak, TablePropertiesDialogPlanner.AllowRowBreakAutomationId);
        _repeatHeader = Check(TablePropertiesDialogPlanner.RepeatHeaderLabel, state.RepeatHeaderRow, TablePropertiesDialogPlanner.RepeatHeaderAutomationId);
        // WPF authority stacks these as a flush first row followed by one 4px inter-item gap.
        // The general field-row helper adds vertical margins on both controls, so remove that
        // template-neutral spacing for this dedicated checkbox stack.
        _allowRowBreak.Margin = new Thickness(0);
        _repeatHeader.Margin = new Thickness(0, Layout.RowVerticalInset, 0, 0);

        _columnWidth = NumberBox(state.ColumnWidthText, TablePropertiesDialogPlanner.ColumnWidthAutomationId);
        _columnWidthOn = Check(TablePropertiesDialogPlanner.PreferredWidthLabel, state.ColumnWidthOn, TablePropertiesDialogPlanner.ColumnWidthToggleAutomationId);

        _cellWidth = NumberBox(state.CellWidthText, TablePropertiesDialogPlanner.CellWidthAutomationId);
        _cellWidthOn = Check(TablePropertiesDialogPlanner.PreferredWidthLabel, state.CellWidthOn, TablePropertiesDialogPlanner.CellWidthToggleAutomationId);
        _cellVAlign = Combo(_session.CellVerticalAlignmentNames, state.CellVerticalAlignmentIndex, TablePropertiesDialogPlanner.CellVerticalAlignmentAutomationId);
        _cmTop = NumberBox(state.CellMarginTopText, TablePropertiesDialogPlanner.CellMarginTopAutomationId);
        _cmLeft = NumberBox(state.CellMarginLeftText, TablePropertiesDialogPlanner.CellMarginLeftAutomationId);
        _cmBottom = NumberBox(state.CellMarginBottomText, TablePropertiesDialogPlanner.CellMarginBottomAutomationId);
        _cmRight = NumberBox(state.CellMarginRightText, TablePropertiesDialogPlanner.CellMarginRightAutomationId);
        _cellMarginsOn = Check(TablePropertiesDialogPlanner.SameMarginsLabel, state.CellMarginsSameAsTable, TablePropertiesDialogPlanner.SameMarginsAutomationId);
        _cellWrapText = Check(TablePropertiesDialogPlanner.WrapTextLabel, state.CellWrapText, TablePropertiesDialogPlanner.CellWrapTextAutomationId);
        _cellFitText = Check(TablePropertiesDialogPlanner.FitTextLabel, state.CellFitText, TablePropertiesDialogPlanner.CellFitTextAutomationId);

        _tabs = new TabControl
        {
            Margin = new Thickness(Layout.OuterInset, Layout.OuterInset, Layout.OuterInset, 0),
            // WPF clips the selected tab viewport at the action-row boundary. Without
            // an explicit Avalonia clip, the Cell tab's long Positioning content paints
            // over the buttons when the fixed evidence surface is shorter than the tab.
            ClipToBounds = true,
        };
        _tabs.Items.Add(TabPage(TablePropertiesDialogPlanner.TableTabLabel, TablePropertiesDialogPlanner.TableTabAutomationId, BuildTableTab()));
        _tabs.Items.Add(TabPage(TablePropertiesDialogPlanner.RowTabLabel, TablePropertiesDialogPlanner.RowTabAutomationId, BuildRowTab()));
        _tabs.Items.Add(TabPage(TablePropertiesDialogPlanner.ColumnTabLabel, TablePropertiesDialogPlanner.ColumnTabAutomationId, BuildColumnTab()));
        _tabs.Items.Add(TabPage(TablePropertiesDialogPlanner.CellTabLabel, TablePropertiesDialogPlanner.CellTabAutomationId, BuildCellTab()));
        _tabs.SelectionChanged += (_, _) =>
        {
            if (_tabs.SelectedIndex is >= 0 and < 4)
                _focusTrace.Add($"TabPage:{((TabItem)_tabs.SelectedItem!).Header}");
            if (_tabs.SelectedIndex == (int)TablePropertiesDialogTabKind.Cell)
                NormalizeCellComboSurfaces();
        };
        _tabs.SelectedIndex = (int)_session.InitialFocusPlan.Tab;
        AutomationProperties.SetAutomationId(_tabs, TablePropertiesDialogPlanner.TabsAutomationId);
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(
            _tabs,
            DialogChromeStyle,
            contentPaneMargin: new Thickness(
                Layout.AvaloniaTabPaneHorizontalCompensation,
                0,
                Layout.AvaloniaTabPaneHorizontalCompensation,
                0));
        AvaloniaCompactDialogChrome.ApplyValidationStatus(
            _validation,
            DialogChromeStyle,
            new Thickness(Layout.OuterInset, 6, Layout.OuterInset, 0));
        AutomationProperties.SetAutomationId(_validation, TablePropertiesDialogPlanner.ValidationAutomationId);

        var ok = TableFormulaDialogButton(TablePropertiesDialogPlanner.AcceptButtonLabel, TablePropertiesDialogPlanner.AcceptButtonAutomationId, isDefault: true);
        ok.Click += (_, _) => Accept();
        var cancel = TableFormulaDialogButton(TablePropertiesDialogPlanner.CancelButtonLabel, TablePropertiesDialogPlanner.CancelButtonAutomationId, isCancel: true);
        cancel.Click += (_, _) => Close();
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [ok, cancel],
            new Thickness(
                Layout.OuterInset,
                Layout.ActionTopInset,
                Layout.OuterInset,
                Layout.ActionBottomInset),
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
        Opened += (_, _) =>
        {
            FocusInitialField();
            if (_tabs.SelectedIndex == (int)TablePropertiesDialogTabKind.Cell)
                NormalizeCellComboSurfaces();
        };
    }

    internal IReadOnlyList<string> FocusTraceForValidation => _focusTrace;
    private Control BuildTableTab()
    {
        var grid = TwoColumnGrid(4, Layout.AvaloniaMainLabelColumnWidth);
        AddRow(grid, 0, _preferredWidthOn, _preferredWidth);
        AddRow(grid, 1, TablePropertiesDialogPlanner.AlignmentLabel, _alignment);
        AddRow(grid, 2, TablePropertiesDialogPlanner.TextWrappingLabel, _wrapping);
        AddRow(grid, 3, TablePropertiesDialogPlanner.IndentFromLeftLabel, _indent);

        var margins = TwoColumnGrid(4, Layout.AvaloniaMarginLabelColumnWidth);
        AddRow(margins, 0, TablePropertiesDialogPlanner.TopLabel, _cellMarginTop);
        AddRow(margins, 1, TablePropertiesDialogPlanner.LeftLabel, _cellMarginLeft);
        AddRow(margins, 2, TablePropertiesDialogPlanner.BottomLabel, _cellMarginBottom);
        AddRow(margins, 3, TablePropertiesDialogPlanner.RightLabel, _cellMarginRight);
        var spacing = TwoColumnGrid(1, Layout.AvaloniaCellSpacingLabelColumnWidth);
        AddRow(spacing, 0, _cellSpacingOn, _cellSpacing);

        return Stack(
            grid,
            Header(TablePropertiesDialogPlanner.DefaultCellMarginsLabel),
            margins,
            spacing);
    }

    private Control BuildFloatingPositioningPanel()
    {
        var position = TwoColumnGrid(6, Layout.AvaloniaMainLabelColumnWidth);
        position.Margin = new Thickness(0, 0, Layout.AvaloniaPositionGridRightInset, 0);
        AddRow(position, 0, TablePropertiesDialogPlanner.HorizontalRelativeToLabel, _floatingHorizontalAnchor);
        AddRow(position, 1, TablePropertiesDialogPlanner.HorizontalAlignmentLabel, _floatingHorizontalMode);
        AddRow(position, 2, TablePropertiesDialogPlanner.HorizontalPositionLabel, _floatingHorizontalOffset);
        AddRow(position, 3, TablePropertiesDialogPlanner.VerticalRelativeToLabel, _floatingVerticalAnchor);
        AddRow(position, 4, TablePropertiesDialogPlanner.VerticalAlignmentLabel, _floatingVerticalMode);
        AddRow(position, 5, TablePropertiesDialogPlanner.VerticalPositionLabel, _floatingVerticalOffset);

        var distances = TwoColumnGrid(4, Layout.AvaloniaMarginLabelColumnWidth);
        AddRow(distances, 0, TablePropertiesDialogPlanner.TopLabel, _floatingDistanceTop);
        AddRow(distances, 1, TablePropertiesDialogPlanner.LeftLabel, _floatingDistanceLeft);
        AddRow(distances, 2, TablePropertiesDialogPlanner.BottomLabel, _floatingDistanceBottom);
        AddRow(distances, 3, TablePropertiesDialogPlanner.RightLabel, _floatingDistanceRight);

        var stack = new StackPanel { Margin = new Thickness(Layout.ExpanderContentInset) };
        stack.Children.Add(position);
        stack.Children.Add(Header(
            TablePropertiesDialogPlanner.DistanceFromTextLabel,
            top: Layout.SecondarySectionTopInset));
        stack.Children.Add(distances);
        stack.Children.Add(_allowFloatingOverlap);
        var expander = new Expander { Header = TablePropertiesDialogPlanner.PositioningLabel, IsExpanded = true, Content = stack };
        AvaloniaCompactDialogChrome.ApplyWpfExpander(expander, DialogChromeStyle);
        UpdateFloatingPositionControls();
        return expander;
    }

    private void UpdateFloatingPositionControls()
    {
        var state = _session.PlanEnabledState(
            _wrapping.SelectedIndex,
            _floatingHorizontalMode.SelectedIndex,
            _floatingVerticalMode.SelectedIndex);
        _allowFloatingOverlap.IsEnabled = state.FloatingControlsEnabled;
        _floatingHorizontalAnchor.IsEnabled = state.FloatingControlsEnabled;
        _floatingHorizontalMode.IsEnabled = state.FloatingControlsEnabled;
        _floatingHorizontalOffset.IsEnabled = state.HorizontalOffsetEnabled;
        _floatingVerticalAnchor.IsEnabled = state.FloatingControlsEnabled;
        _floatingVerticalMode.IsEnabled = state.FloatingControlsEnabled;
        _floatingVerticalOffset.IsEnabled = state.VerticalOffsetEnabled;
        _floatingDistanceTop.IsEnabled = state.FloatingControlsEnabled;
        _floatingDistanceLeft.IsEnabled = state.FloatingControlsEnabled;
        _floatingDistanceBottom.IsEnabled = state.FloatingControlsEnabled;
        _floatingDistanceRight.IsEnabled = state.FloatingControlsEnabled;
    }

    private void NormalizeCellComboSurfaces()
    {
        AvaloniaCompactDialogChrome.ApplyWpfDisabledComboSurface(_floatingHorizontalAnchor);
        AvaloniaCompactDialogChrome.ApplyWpfDisabledComboSurface(_floatingHorizontalMode);
        AvaloniaCompactDialogChrome.ApplyWpfDisabledComboSurface(_floatingVerticalAnchor);
        AvaloniaCompactDialogChrome.ApplyWpfDisabledComboSurface(_floatingVerticalMode);
    }

    private static TabItem TabPage(string header, string automationId, Control content)
    {
        var tab = new TabItem { Header = header, Content = content };
        AutomationProperties.SetAutomationId(tab, automationId);
        return tab;
    }

    private Control BuildRowTab()
    {
        var grid = TwoColumnGrid(2, Layout.AvaloniaRowLabelColumnWidth);
        AddRow(grid, 0, _rowHeightOn, _rowHeight);
        AddRow(grid, 1, TablePropertiesDialogPlanner.RowHeightRuleLabel, _rowRule);
        var checks = new StackPanel { Margin = new Thickness(0, Layout.SecondarySectionTopInset, 0, 0) };
        checks.Children.Add(_allowRowBreak);
        checks.Children.Add(_repeatHeader);
        return Stack(grid, checks);
    }

    private Control BuildColumnTab()
    {
        var grid = TwoColumnGrid(1, Layout.AvaloniaMainLabelColumnWidth);
        AddRow(grid, 0, _columnWidthOn, _columnWidth);
        return Stack(grid);
    }

    private Control BuildCellTab()
    {
        var grid = TwoColumnGrid(2, Layout.AvaloniaMainLabelColumnWidth);
        AddRow(grid, 0, _cellWidthOn, _cellWidth);
        AddRow(grid, 1, TablePropertiesDialogPlanner.VerticalAlignmentLabel, _cellVAlign);
        var margins = TwoColumnGrid(4, Layout.AvaloniaMarginLabelColumnWidth);
        AddRow(margins, 0, TablePropertiesDialogPlanner.TopLabel, _cmTop);
        AddRow(margins, 1, TablePropertiesDialogPlanner.LeftLabel, _cmLeft);
        AddRow(margins, 2, TablePropertiesDialogPlanner.BottomLabel, _cmBottom);
        AddRow(margins, 3, TablePropertiesDialogPlanner.RightLabel, _cmRight);
        return Stack(
            grid,
            BuildFloatingPositioningPanel(),
            _cellWrapText,
            _cellFitText,
            _cellMarginsOn,
            Header(TablePropertiesDialogPlanner.CellMarginsLabel),
            margins);
    }

    private async void Accept()
    {
        var acceptance = CaptureAcceptance();
        if (!acceptance.IsAccepted)
        {
            _validation.IsVisible = false;
            await _messageService.ShowWarningAsync(
                acceptance.ValidationMessage ?? TablePropertiesDialogPlanner.ValidationMessage);
            FocusInitialField();
            return;
        }

        Result = acceptance.Result;
        Close();
    }

    private TablePropertiesValues? TryAccept(bool close)
    {
        var acceptance = CaptureAcceptance();
        if (!acceptance.IsAccepted)
        {
            _validation.Text = acceptance.ValidationMessage!;
            _validation.IsVisible = true;
            FocusInitialField();
            return null;
        }

        _validation.IsVisible = false;
        Result = acceptance.Result;
        if (close)
            Close();
        return Result;
    }

    private TablePropertiesDialogAcceptance CaptureAcceptance() =>
        _session.PlanAcceptance(_session.CaptureInput(
            automationId => Control<CheckBox>(automationId).IsChecked,
            automationId => Control<TextBox>(automationId).Text,
            automationId => Control<ComboBox>(automationId).SelectedIndex));

    private T Control<T>(string automationId) where T : Control =>
        this.GetLogicalDescendants()
            .OfType<T>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);

    private void FocusInitialField()
    {
        var focusPlan = _session.PlanFocus((TablePropertiesDialogTabKind)_tabs.SelectedIndex);
        var target = ResolveFocusTarget(focusPlan);
        target.Focus();
        if (focusPlan.SelectAllOnFocus)
            target.SelectAll();
    }

    private TextBox ResolveFocusTarget(TablePropertiesDialogFocusPlan plan) =>
        plan.TargetAutomationId switch
        {
            TablePropertiesDialogPlanner.RowHeightAutomationId => _rowHeight,
            TablePropertiesDialogPlanner.ColumnWidthAutomationId => _columnWidth,
            TablePropertiesDialogPlanner.CellWidthAutomationId => _cellWidth,
            _ => _preferredWidth,
        };

    private static StackPanel Stack(params Control[] controls)
    {
        var panel = new StackPanel { Margin = new Thickness(Layout.ContentInset) };
        foreach (var control in controls)
            panel.Children.Add(control);
        return panel;
    }

    private static TextBlock Header(string text, double? top = null) => new()
    {
        Text = text,
        Foreground = Brushes.Black,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(
            0,
            top ?? Layout.SectionHeaderTopInset,
            0,
            Layout.SectionHeaderBottomInset),
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
        var box = new TextBox { Text = text, MinWidth = Layout.NumberFieldMinWidth };
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
            Margin = new Thickness(
                0,
                Layout.RowVerticalInset,
                Layout.LabelRightInset,
                Layout.RowVerticalInset),
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
        var combo = new ComboBox { MinWidth = Layout.ChoiceFieldMinWidth };
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
            Margin = new Thickness(
                0,
                Layout.RowVerticalInset,
                Layout.LabelRightInset,
                Layout.RowVerticalInset),
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
        field.Margin = new Thickness(0, Layout.RowVerticalInset, 0, Layout.RowVerticalInset);
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
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            DialogChromeStyle,
            Layout.ActionButtonWidth,
            isDefault);
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }
}
