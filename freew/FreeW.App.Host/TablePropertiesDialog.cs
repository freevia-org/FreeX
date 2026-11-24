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
    private static readonly TablePropertiesDialogVisualMetrics Layout =
        TablePropertiesDialogPlanner.VisualMetrics;

    private readonly TablePropertiesDialogSession _session;
    private readonly Dictionary<string, FrameworkElement> _controls = new(StringComparer.Ordinal);
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
        Width = Layout.DialogWidth;
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
            Margin = new Thickness(0, Layout.RowVerticalInset, 0, Layout.RowVerticalInset)
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
        _repeatHeader = new CheckBox
        {
            Content = TablePropertiesDialogPlanner.RepeatHeaderLabel,
            IsChecked = state.RepeatHeaderRow,
            Margin = new Thickness(0, Layout.RowVerticalInset, 0, 0)
        };

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
        RegisterControls(
            _preferredWidthOn, _preferredWidth, _alignment, _wrapping, _allowFloatingOverlap,
            _floatingHorizontalAnchor, _floatingHorizontalMode, _floatingHorizontalOffset,
            _floatingVerticalAnchor, _floatingVerticalMode, _floatingVerticalOffset,
            _floatingDistanceTop, _floatingDistanceLeft, _floatingDistanceBottom, _floatingDistanceRight,
            _indent, _cellMarginTop, _cellMarginLeft, _cellMarginBottom, _cellMarginRight,
            _cellSpacingOn, _cellSpacing, _rowHeightOn, _rowHeight, _rowRule, _allowRowBreak, _repeatHeader,
            _columnWidthOn, _columnWidth, _cellWidthOn, _cellWidth, _cellVAlign, _cellMarginsOn,
            _cmTop, _cmLeft, _cmBottom, _cmRight, _cellWrapText, _cellFitText);

        var tabs = new TabControl
        {
            Margin = new Thickness(Layout.OuterInset, Layout.OuterInset, Layout.OuterInset, 0)
        };
        foreach (var tab in TablePropertiesSurfacePlanner.Tabs)
            tabs.Items.Add(CreateTabItem(tab.Label, tab.AutomationId, BuildStack(tab.Content)));
        UpdateFloatingPositionControls();
        tabs.SelectedIndex = (int)_session.InitialFocusPlan.Tab;
        AutomationProperties.SetAutomationId(tabs, TablePropertiesDialogPlanner.TabsAutomationId);

        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: Layout.ActionButtonWidth,
            rowMargin: new Thickness(
                Layout.OuterInset,
                Layout.ActionTopInset,
                Layout.OuterInset,
                Layout.ActionBottomInset));

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

    private void RegisterControls(params FrameworkElement[] controls)
    {
        foreach (var control in controls)
            _controls[AutomationProperties.GetAutomationId(control)] = control;
    }

    private StackPanel BuildStack(IReadOnlyList<TablePropertiesSurfaceNode> nodes, double? inset = null)
    {
        var stack = new StackPanel { Margin = new Thickness(inset ?? Layout.ContentInset) };
        foreach (var node in nodes)
            stack.Children.Add(BuildNode(node));
        return stack;
    }

    private UIElement BuildNode(TablePropertiesSurfaceNode node) =>
        node switch
        {
            TablePropertiesFieldGridNode grid => BuildGrid(grid),
            TablePropertiesHeaderNode header => new TextBlock
            {
                Text = header.Text,
                Margin = new Thickness(
                    0,
                    header.IsSecondary ? Layout.SecondarySectionTopInset : Layout.SectionHeaderTopInset,
                    0,
                    Layout.SectionHeaderBottomInset),
                FontWeight = FontWeights.SemiBold
            },
            TablePropertiesControlNode control => _controls[control.AutomationId],
            TablePropertiesCheckStackNode checks => BuildCheckStack(checks),
            TablePropertiesExpanderNode expander => new Expander
            {
                Header = expander.Header,
                IsExpanded = true,
                Content = BuildStack(expander.Content, Layout.ExpanderContentInset)
            },
            _ => throw new InvalidOperationException($"Unknown Table Properties surface node {node}."),
        };

    private Grid BuildGrid(TablePropertiesFieldGridNode node)
    {
        var grid = TwoColumnGrid(node.Rows.Count);
        for (var row = 0; row < node.Rows.Count; row++)
        {
            var spec = node.Rows[row];
            var field = _controls[spec.FieldAutomationId];
            if (spec.ToggleAutomationId is { } toggle)
                AddRow(grid, row, (CheckBox)_controls[toggle], field);
            else
                AddRow(grid, row, spec.Label!, field);
        }

        return grid;
    }

    private StackPanel BuildCheckStack(TablePropertiesCheckStackNode node)
    {
        var checks = new StackPanel { Margin = new Thickness(0, Layout.SecondarySectionTopInset, 0, 0) };
        foreach (var id in node.AutomationIds)
            checks.Children.Add(_controls[id]);
        return checks;
    }

    private void UpdateFloatingPositionControls()
    {
        var state = _session.PlanEnabledState(
            _wrapping.SelectedIndex,
            _floatingHorizontalMode.SelectedIndex,
            _floatingVerticalMode.SelectedIndex);
        foreach (var (automationId, enabled) in TablePropertiesSurfacePlanner.ControlEnabledStates(state))
            _controls[automationId].IsEnabled = enabled;
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
        var combo = new ComboBox { MinWidth = Layout.ChoiceFieldMinWidth };
        foreach (var item in items)
            combo.Items.Add(item);
        combo.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Count - 1);
        return combo;
    }

    private static TextBox NumberBox(string text) => new()
    {
        Text = text,
        MinWidth = Layout.NumberFieldMinWidth
    };

    private static CheckBox Check(string content, bool isChecked) =>
        new()
        {
            Content = content,
            IsChecked = isChecked,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(
                0,
                Layout.RowVerticalInset,
                Layout.LabelRightInset,
                Layout.RowVerticalInset)
        };

    private static void AddRow(Grid grid, int row, string label, UIElement field)
    {
        var block = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(
                0,
                Layout.RowVerticalInset,
                Layout.LabelRightInset,
                Layout.RowVerticalInset)
        };
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
            fe.Margin = new Thickness(0, Layout.RowVerticalInset, 0, Layout.RowVerticalInset);
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
        var input = _session.CaptureInput(
            automationId => Control<CheckBox>(automationId).IsChecked,
            automationId => Control<TextBox>(automationId).Text,
            automationId => Control<ComboBox>(automationId).SelectedIndex);

        var acceptance = _session.PlanAcceptance(input);
        if (!acceptance.IsAccepted)
        {
            DialogMessageHelper.ShowWarning(this, acceptance.ValidationMessage);
            return;
        }

        _result = acceptance.Result;
        Close();
    }

    private T Control<T>(string automationId) where T : DependencyObject
    {
        foreach (var control in LogicalDescendants(this).OfType<T>())
        {
            if (AutomationProperties.GetAutomationId(control) == automationId)
                return control;
        }

        throw new InvalidOperationException($"Table Properties control '{automationId}' is unavailable.");
    }

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in LogicalDescendants(child))
                yield return descendant;
        }
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
