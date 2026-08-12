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
internal sealed partial class TablePropertiesDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TablePropertiesDialogSession _session;
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

    private TablePropertiesValues? _result;

    private TablePropertiesDialog(
        Window? owner,
        ModelTableContext context,
        TablePropertiesDialogTabKind initialTab)
    {
        _session = new TablePropertiesDialogSession(context, CultureInfo.CurrentCulture, initialTab);
        Owner = owner;
        Title = TablePropertiesDialogPlanner.Title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, TablePropertiesDialogPlanner.AutomationId);

        var state = _session.InitialState;

        _preferredWidth = NumberBox(state.PreferredWidthText);
        _preferredWidthOn = Check(TablePropertiesDialogPlanner.PreferredWidthLabel, state.PreferredWidthOn);
        _alignment = Combo(_session.AlignmentNames, state.AlignmentIndex);
        _wrapping = Combo(_session.WrappingNames, state.WrappingIndex);
        _allowFloatingOverlap = new CheckBox
        {
            Content = TablePropertiesDialogPlanner.AllowOverlapLabel,
            IsThreeState = true,
            IsChecked = state.FloatingTableAllowsOverlap,
            Margin = new Thickness(0, 4, 0, 4)
        };
        AutomationProperties.SetAutomationId(_allowFloatingOverlap, TablePropertiesDialogPlanner.AllowOverlapAutomationId);
        _floatingHorizontalAnchor = Combo(
            _session.FloatingHorizontalAnchorNames,
            state.FloatingHorizontalAnchorIndex);
        _floatingHorizontalMode = Combo(
            _session.FloatingHorizontalModeNames,
            state.FloatingHorizontalModeIndex);
        _floatingHorizontalOffset = NumberBox(state.FloatingHorizontalOffsetText);
        _floatingVerticalAnchor = Combo(
            _session.FloatingVerticalAnchorNames,
            state.FloatingVerticalAnchorIndex);
        _floatingVerticalMode = Combo(
            _session.FloatingVerticalModeNames,
            state.FloatingVerticalModeIndex);
        _floatingVerticalOffset = NumberBox(state.FloatingVerticalOffsetText);
        _floatingDistanceTop = NumberBox(state.FloatingDistanceTopText);
        _floatingDistanceLeft = NumberBox(state.FloatingDistanceLeftText);
        _floatingDistanceBottom = NumberBox(state.FloatingDistanceBottomText);
        _floatingDistanceRight = NumberBox(state.FloatingDistanceRightText);
        AutomationProperties.SetAutomationId(_floatingHorizontalAnchor, TablePropertiesDialogPlanner.HorizontalAnchorAutomationId);
        AutomationProperties.SetAutomationId(_floatingHorizontalMode, TablePropertiesDialogPlanner.HorizontalModeAutomationId);
        AutomationProperties.SetAutomationId(_floatingHorizontalOffset, TablePropertiesDialogPlanner.HorizontalOffsetAutomationId);
        AutomationProperties.SetAutomationId(_floatingVerticalAnchor, TablePropertiesDialogPlanner.VerticalAnchorAutomationId);
        AutomationProperties.SetAutomationId(_floatingVerticalMode, TablePropertiesDialogPlanner.VerticalModeAutomationId);
        AutomationProperties.SetAutomationId(_floatingVerticalOffset, TablePropertiesDialogPlanner.VerticalOffsetAutomationId);
        AutomationProperties.SetAutomationId(_floatingDistanceTop, TablePropertiesDialogPlanner.DistanceTopAutomationId);
        AutomationProperties.SetAutomationId(_floatingDistanceLeft, TablePropertiesDialogPlanner.DistanceLeftAutomationId);
        AutomationProperties.SetAutomationId(_floatingDistanceBottom, TablePropertiesDialogPlanner.DistanceBottomAutomationId);
        AutomationProperties.SetAutomationId(_floatingDistanceRight, TablePropertiesDialogPlanner.DistanceRightAutomationId);
        _wrapping.SelectionChanged += (_, _) => UpdateFloatingPositionControls();
        _floatingHorizontalMode.SelectionChanged += (_, _) => UpdateFloatingPositionControls();
        _floatingVerticalMode.SelectionChanged += (_, _) => UpdateFloatingPositionControls();
        _indent = NumberBox(state.IndentText);
        _cellMarginTop = NumberBox(state.DefaultCellMarginTopText);
        _cellMarginLeft = NumberBox(state.DefaultCellMarginLeftText);
        _cellMarginBottom = NumberBox(state.DefaultCellMarginBottomText);
        _cellMarginRight = NumberBox(state.DefaultCellMarginRightText);
        _cellSpacing = NumberBox(state.CellSpacingText);
        _cellSpacingOn = Check(TablePropertiesDialogPlanner.CellSpacingLabel, state.CellSpacingOn);

        _rowHeight = NumberBox(state.RowHeightText);
        _rowHeightOn = Check(TablePropertiesDialogPlanner.SpecifyRowHeightLabel, state.RowHeightOn);
        _rowRule = Combo(_session.RowRuleNames, state.RowRuleIndex);
        _allowRowBreak = new CheckBox { Content = TablePropertiesDialogPlanner.AllowRowBreakLabel, IsChecked = state.AllowRowBreak };
        _repeatHeader = new CheckBox { Content = TablePropertiesDialogPlanner.RepeatHeaderLabel, IsChecked = state.RepeatHeaderRow, Margin = new Thickness(0, 4, 0, 0) };

        _columnWidth = NumberBox(state.ColumnWidthText);
        _columnWidthOn = Check(TablePropertiesDialogPlanner.PreferredWidthLabel, state.ColumnWidthOn);

        _cellWidth = NumberBox(state.CellWidthText);
        _cellWidthOn = Check(TablePropertiesDialogPlanner.PreferredWidthLabel, state.CellWidthOn);
        _cellVAlign = Combo(_session.CellVerticalAlignmentNames, state.CellVerticalAlignmentIndex);
        _cmTop = NumberBox(state.CellMarginTopText);
        _cmLeft = NumberBox(state.CellMarginLeftText);
        _cmBottom = NumberBox(state.CellMarginBottomText);
        _cmRight = NumberBox(state.CellMarginRightText);
        _cellMarginsOn = Check(TablePropertiesDialogPlanner.SameMarginsLabel, state.CellMarginsSameAsTable);
        _cellWrapText = Check(TablePropertiesDialogPlanner.WrapTextLabel, state.CellWrapText);
        _cellFitText = Check(TablePropertiesDialogPlanner.FitTextLabel, state.CellFitText);
        SetControlAutomationIds();

        var tabs = new TabControl { Margin = new Thickness(14, 14, 14, 0) };
        tabs.Items.Add(CreateTabItem(TablePropertiesDialogPlanner.TableTabLabel, TablePropertiesDialogPlanner.TableTabAutomationId, BuildTableTab()));
        tabs.Items.Add(CreateTabItem(TablePropertiesDialogPlanner.RowTabLabel, TablePropertiesDialogPlanner.RowTabAutomationId, BuildRowTab()));
        tabs.Items.Add(CreateTabItem(TablePropertiesDialogPlanner.ColumnTabLabel, TablePropertiesDialogPlanner.ColumnTabAutomationId, BuildColumnTab()));
        tabs.Items.Add(CreateTabItem(TablePropertiesDialogPlanner.CellTabLabel, TablePropertiesDialogPlanner.CellTabAutomationId, BuildCellTab()));
        tabs.SelectedIndex = (int)_session.InitialFocusPlan.Tab;
        AutomationProperties.SetAutomationId(tabs, TablePropertiesDialogPlanner.TabsAutomationId);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(14, 12, 14, 12));

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(tabs);
        Content = root;

        var focusPlan = _session.InitialFocusPlan;
        var initialFocus = ResolveFocusTarget(focusPlan);
        if (focusPlan.SelectAllOnFocus)
            DialogFocus.FocusAndSelect(initialFocus);
        else
            initialFocus.Focus();
    }

    private UIElement BuildTableTab()
    {
        var grid = TwoColumnGrid(4);
        AddRow(grid, 0, _preferredWidthOn, _preferredWidth);
        AddRow(grid, 1, TablePropertiesDialogPlanner.AlignmentLabel, _alignment);
        AddRow(grid, 2, TablePropertiesDialogPlanner.TextWrappingLabel, _wrapping);
        AddRow(grid, 3, TablePropertiesDialogPlanner.IndentFromLeftLabel, _indent);

        var marginsHeader = new TextBlock { Text = TablePropertiesDialogPlanner.DefaultCellMarginsLabel, Margin = new Thickness(0, 10, 0, 4), FontWeight = FontWeights.SemiBold };
        var marginsGrid = TwoColumnGrid(4);
        AddRow(marginsGrid, 0, TablePropertiesDialogPlanner.TopLabel, _cellMarginTop);
        AddRow(marginsGrid, 1, TablePropertiesDialogPlanner.LeftLabel, _cellMarginLeft);
        AddRow(marginsGrid, 2, TablePropertiesDialogPlanner.BottomLabel, _cellMarginBottom);
        AddRow(marginsGrid, 3, TablePropertiesDialogPlanner.RightLabel, _cellMarginRight);

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
        AddRow(grid, 1, TablePropertiesDialogPlanner.RowHeightRuleLabel, _rowRule);

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
        AddRow(grid, 1, TablePropertiesDialogPlanner.VerticalAlignmentLabel, _cellVAlign);

        var marginsHeader = new TextBlock { Text = TablePropertiesDialogPlanner.CellMarginsLabel, Margin = new Thickness(0, 10, 0, 4), FontWeight = FontWeights.SemiBold };
        var marginsGrid = TwoColumnGrid(4);
        AddRow(marginsGrid, 0, TablePropertiesDialogPlanner.TopLabel, _cmTop);
        AddRow(marginsGrid, 1, TablePropertiesDialogPlanner.LeftLabel, _cmLeft);
        AddRow(marginsGrid, 2, TablePropertiesDialogPlanner.BottomLabel, _cmBottom);
        AddRow(marginsGrid, 3, TablePropertiesDialogPlanner.RightLabel, _cmRight);

        var stack = new StackPanel { Margin = new Thickness(14) };
        stack.Children.Add(grid);
        stack.Children.Add(BuildFloatingPositioningPanel());
        stack.Children.Add(_cellWrapText);
        stack.Children.Add(_cellFitText);
        stack.Children.Add(_cellMarginsOn);
        stack.Children.Add(marginsHeader);
        stack.Children.Add(marginsGrid);
        return stack;
    }

    private UIElement BuildFloatingPositioningPanel()
    {
        var positionGrid = TwoColumnGrid(6);
        AddRow(positionGrid, 0, TablePropertiesDialogPlanner.HorizontalRelativeToLabel, _floatingHorizontalAnchor);
        AddRow(positionGrid, 1, TablePropertiesDialogPlanner.HorizontalAlignmentLabel, _floatingHorizontalMode);
        AddRow(positionGrid, 2, TablePropertiesDialogPlanner.HorizontalPositionLabel, _floatingHorizontalOffset);
        AddRow(positionGrid, 3, TablePropertiesDialogPlanner.VerticalRelativeToLabel, _floatingVerticalAnchor);
        AddRow(positionGrid, 4, TablePropertiesDialogPlanner.VerticalAlignmentLabel, _floatingVerticalMode);
        AddRow(positionGrid, 5, TablePropertiesDialogPlanner.VerticalPositionLabel, _floatingVerticalOffset);

        var distanceGrid = TwoColumnGrid(4);
        AddRow(distanceGrid, 0, TablePropertiesDialogPlanner.TopLabel, _floatingDistanceTop);
        AddRow(distanceGrid, 1, TablePropertiesDialogPlanner.LeftLabel, _floatingDistanceLeft);
        AddRow(distanceGrid, 2, TablePropertiesDialogPlanner.BottomLabel, _floatingDistanceBottom);
        AddRow(distanceGrid, 3, TablePropertiesDialogPlanner.RightLabel, _floatingDistanceRight);

        var stack = new StackPanel { Margin = new Thickness(8) };
        stack.Children.Add(positionGrid);
        stack.Children.Add(new TextBlock
        {
            Text = TablePropertiesDialogPlanner.DistanceFromTextLabel,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 4)
        });
        stack.Children.Add(distanceGrid);
        stack.Children.Add(_allowFloatingOverlap);
        UpdateFloatingPositionControls();
        return new Expander { Header = TablePropertiesDialogPlanner.PositioningLabel, IsExpanded = true, Content = stack };
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

    private void SetControlAutomationIds()
    {
        AutomationProperties.SetAutomationId(_preferredWidth, TablePropertiesDialogPlanner.PreferredWidthAutomationId);
        AutomationProperties.SetAutomationId(_preferredWidthOn, TablePropertiesDialogPlanner.PreferredWidthToggleAutomationId);
        AutomationProperties.SetAutomationId(_alignment, TablePropertiesDialogPlanner.AlignmentAutomationId);
        AutomationProperties.SetAutomationId(_wrapping, TablePropertiesDialogPlanner.WrappingAutomationId);
        AutomationProperties.SetAutomationId(_indent, TablePropertiesDialogPlanner.IndentAutomationId);
        AutomationProperties.SetAutomationId(_cellMarginTop, TablePropertiesDialogPlanner.DefaultMarginTopAutomationId);
        AutomationProperties.SetAutomationId(_cellMarginLeft, TablePropertiesDialogPlanner.DefaultMarginLeftAutomationId);
        AutomationProperties.SetAutomationId(_cellMarginBottom, TablePropertiesDialogPlanner.DefaultMarginBottomAutomationId);
        AutomationProperties.SetAutomationId(_cellMarginRight, TablePropertiesDialogPlanner.DefaultMarginRightAutomationId);
        AutomationProperties.SetAutomationId(_cellSpacing, TablePropertiesDialogPlanner.CellSpacingAutomationId);
        AutomationProperties.SetAutomationId(_cellSpacingOn, TablePropertiesDialogPlanner.CellSpacingToggleAutomationId);
        AutomationProperties.SetAutomationId(_rowHeight, TablePropertiesDialogPlanner.RowHeightAutomationId);
        AutomationProperties.SetAutomationId(_rowHeightOn, TablePropertiesDialogPlanner.RowHeightToggleAutomationId);
        AutomationProperties.SetAutomationId(_rowRule, TablePropertiesDialogPlanner.RowRuleAutomationId);
        AutomationProperties.SetAutomationId(_allowRowBreak, TablePropertiesDialogPlanner.AllowRowBreakAutomationId);
        AutomationProperties.SetAutomationId(_repeatHeader, TablePropertiesDialogPlanner.RepeatHeaderAutomationId);
        AutomationProperties.SetAutomationId(_columnWidth, TablePropertiesDialogPlanner.ColumnWidthAutomationId);
        AutomationProperties.SetAutomationId(_columnWidthOn, TablePropertiesDialogPlanner.ColumnWidthToggleAutomationId);
        AutomationProperties.SetAutomationId(_cellWidth, TablePropertiesDialogPlanner.CellWidthAutomationId);
        AutomationProperties.SetAutomationId(_cellWidthOn, TablePropertiesDialogPlanner.CellWidthToggleAutomationId);
        AutomationProperties.SetAutomationId(_cellVAlign, TablePropertiesDialogPlanner.CellVerticalAlignmentAutomationId);
        AutomationProperties.SetAutomationId(_cmTop, TablePropertiesDialogPlanner.CellMarginTopAutomationId);
        AutomationProperties.SetAutomationId(_cmLeft, TablePropertiesDialogPlanner.CellMarginLeftAutomationId);
        AutomationProperties.SetAutomationId(_cmBottom, TablePropertiesDialogPlanner.CellMarginBottomAutomationId);
        AutomationProperties.SetAutomationId(_cmRight, TablePropertiesDialogPlanner.CellMarginRightAutomationId);
        AutomationProperties.SetAutomationId(_cellMarginsOn, TablePropertiesDialogPlanner.SameMarginsAutomationId);
        AutomationProperties.SetAutomationId(_cellWrapText, TablePropertiesDialogPlanner.CellWrapTextAutomationId);
        AutomationProperties.SetAutomationId(_cellFitText, TablePropertiesDialogPlanner.CellFitTextAutomationId);
    }

    private static TabItem CreateTabItem(string label, string automationId, UIElement content)
    {
        var tab = new TabItem { Header = label, Content = content };
        AutomationProperties.SetAutomationId(tab, automationId);
        return tab;
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

    private TextBox ResolveFocusTarget(TablePropertiesDialogFocusPlan plan) =>
        plan.TargetAutomationId switch
        {
            TablePropertiesDialogPlanner.RowHeightAutomationId => _rowHeight,
            TablePropertiesDialogPlanner.ColumnWidthAutomationId => _columnWidth,
            TablePropertiesDialogPlanner.CellWidthAutomationId => _cellWidth,
            _ => _preferredWidth,
        };

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
            FloatingHorizontalAnchorIndex: _floatingHorizontalAnchor.SelectedIndex,
            FloatingHorizontalModeIndex: _floatingHorizontalMode.SelectedIndex,
            FloatingHorizontalOffsetText: _floatingHorizontalOffset.Text,
            FloatingVerticalAnchorIndex: _floatingVerticalAnchor.SelectedIndex,
            FloatingVerticalModeIndex: _floatingVerticalMode.SelectedIndex,
            FloatingVerticalOffsetText: _floatingVerticalOffset.Text,
            FloatingDistanceTopText: _floatingDistanceTop.Text,
            FloatingDistanceLeftText: _floatingDistanceLeft.Text,
            FloatingDistanceBottomText: _floatingDistanceBottom.Text,
            FloatingDistanceRightText: _floatingDistanceRight.Text,
            FloatingTableAllowsOverlap: _allowFloatingOverlap.IsChecked);

        var acceptance = _session.PlanAcceptance(input);
        if (!acceptance.IsAccepted)
        {
            DialogMessageHelper.ShowWarning(this, acceptance.ValidationMessage);
            return;
        }

        _result = acceptance.Result;
        Close();
    }

    public static TablePropertiesValues? Prompt(
        Window? owner,
        ModelTableContext context,
        TablePropertiesDialogTabKind initialTab = TablePropertiesDialogTabKind.Table)
    {
        var dialog = new TablePropertiesDialog(owner, context, initialTab);
        dialog.ShowDialog();
        return dialog._result;
    }
}
