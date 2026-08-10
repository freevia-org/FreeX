using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Presentation;
using FreeX.App.Presentation.DataTools;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using SubtotalColumnChoice = FreeX.App.Presentation.DataTools.SubtotalDialogColumnChoice;
using SubtotalFunctionChoice = FreeX.App.Presentation.DataTools.SubtotalDialogFunctionChoice;
using SharedSubtotalDialogAction = FreeX.App.Presentation.DataTools.SubtotalDialogPlanAction;
using SharedSubtotalDialogPlanner = FreeX.App.Presentation.DataTools.SubtotalDialogPlanner;

namespace FreeX.App.Host;

public enum SubtotalDialogAction
{
    Apply,
    RemoveAll
}

public sealed record SubtotalDialogResult(
    uint GroupColumnOffset,
    IReadOnlyList<uint> SubtotalColumnOffsets,
    int FunctionNumber,
    bool ReplaceCurrentSubtotals,
    bool PageBreakBetweenGroups,
    bool SummaryBelowData,
    SubtotalDialogAction Action = SubtotalDialogAction.Apply);

public sealed class SubtotalDialog : Window
{
    private sealed class SubtotalColumnSelection(SubtotalColumnChoice choice)
    {
        public uint Offset { get; } = choice.Offset;
        public string Header { get; } = choice.Header;
        public bool IsSelected { get; set; } = choice.IsSelected;
        public string AutomationId => $"SubtotalColumn{Offset}Box";
        public string AutomationName => UiText.Format("Subtotal_ColumnAutomationNameFormat", Header);
        public string HelpText => UiText.Get("Subtotal_ColumnHelpText");
    }

    private readonly ComboBox _groupColumnBox = new() { DisplayMemberPath = nameof(SubtotalColumnChoice.Header), SelectedValuePath = nameof(SubtotalColumnChoice.Offset) };
    private readonly List<SubtotalColumnSelection> _subtotalColumns;
    private readonly ListBox _subtotalColumnList = new()
    {
        MaxHeight = 118,
        BorderThickness = new Thickness(0),
        ItemTemplate = CreateSubtotalColumnTemplate()
    };
    private readonly ComboBox _functionBox = new()
    {
        ItemsSource = SharedSubtotalDialogPlanner.CreateFunctionChoices(PlannerText),
        DisplayMemberPath = nameof(SubtotalFunctionChoice.Label),
        SelectedValuePath = nameof(SubtotalFunctionChoice.FunctionText),
        SelectedValue = SharedSubtotalDialogPlanner.DefaultFunctionText
    };
    private readonly CheckBox _replaceBox = new() { IsChecked = true };
    private readonly CheckBox _pageBreakBox = new();
    private readonly CheckBox _summaryBelowBox = new();
    private bool _isMovingSubtotalColumnFocus;

    public SubtotalDialogResult? Result { get; private set; }

    /// <summary>
    /// </summary>
    /// <param name="columns">The candidate group/subtotal column choices for the active selection.</param>
    /// <param name="summaryBelowData">
    /// The initial state of the "Summary below data" checkbox. Real Excel derives this from the
    /// active sheet's outline direction (<c>Sheet.OutlineSummaryBelow</c>, defaulting to <see langword="true"/>
    /// when unset) rather than always defaulting to checked, so callers should pass
    /// <c>sheet.OutlineSummaryBelow ?? true</c> here.
    /// </param>
    public SubtotalDialog(
        IEnumerable<SubtotalColumnChoice>? columns = null,
        bool summaryBelowData = true,
        SubtotalDialogPlanResult? initialPlan = null)
    {
        var columnChoices = NormalizeColumnChoices(columns);
        _subtotalColumns = columnChoices.Select(static column => new SubtotalColumnSelection(column)).ToList();

        Title = UiText.Get("Subtotal_Subtotal");
        Width = 380;
        Height = 390;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ApplyAutomationMetadata();

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new Label { Content = UiText.Get("Subtotal_AtEachChangeIn"), Target = _groupColumnBox, Padding = new Thickness(0) });
        ConfigureVirtualizedItemsControl(_groupColumnBox);
        _groupColumnBox.MaxDropDownHeight = 220;
        _groupColumnBox.ItemsSource = columnChoices;
        _groupColumnBox.SelectedValue = initialPlan?.GroupColumnOffset ?? columnChoices[0].Offset;
        root.Children.Add(_groupColumnBox);
        root.Children.Add(new Label { Content = UiText.Get("Subtotal_UseFunction"), Target = _functionBox, Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) });
        if (initialPlan is not null)
        {
            var initialFunction = SharedSubtotalDialogPlanner.CreateFunctionChoices(PlannerText)
                .FirstOrDefault(choice =>
                    SubtotalFunctionService.TryParse(choice.FunctionText, out var number) &&
                    number == initialPlan.FunctionNumber);
            _functionBox.SelectedValue = initialFunction?.FunctionText ?? SharedSubtotalDialogPlanner.DefaultFunctionText;
        }
        root.Children.Add(_functionBox);
        root.Children.Add(new Label { Content = UiText.Get("Subtotal_AddSubtotalTo"), Target = _subtotalColumnList, Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) });
        ConfigureVirtualizedItemsControl(_subtotalColumnList);
        _subtotalColumnList.ItemsSource = _subtotalColumns;
        _subtotalColumnList.GotKeyboardFocus += MoveFocusIntoSubtotalColumnChoices;
        root.Children.Add(new GroupBox { Content = _subtotalColumnList });
        _replaceBox.Content = UiText.Get("Subtotal_ReplaceCurrentSubtotals");
        _pageBreakBox.Content = UiText.Get("Subtotal_PageBreakBetweenGroups");
        _summaryBelowBox.Content = UiText.Get("Subtotal_SummaryBelowData");
        if (initialPlan is not null)
        {
            var selectedOffsets = initialPlan.SubtotalColumnOffsets.ToHashSet();
            foreach (var column in _subtotalColumns)
                column.IsSelected = selectedOffsets.Contains(column.Offset);
        }
        _replaceBox.IsChecked = initialPlan?.ReplaceCurrentSubtotals ?? true;
        _pageBreakBox.IsChecked = initialPlan?.PageBreakBetweenGroups ?? false;
        _summaryBelowBox.IsChecked = summaryBelowData;
        root.Children.Add(_replaceBox);
        root.Children.Add(_pageBreakBox);
        root.Children.Add(_summaryBelowBox);
        root.Children.Add(CreateSubtotalButtonRow(Accept, RemoveAll));
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void ApplyAutomationMetadata()
    {
        AutomationProperties.SetName(_groupColumnBox, UiText.Get("Subtotal_AtEachChangeInAutomationName"));
        AutomationProperties.SetAutomationId(_groupColumnBox, "SubtotalGroupColumnBox");
        AutomationProperties.SetHelpText(_groupColumnBox, UiText.Get("Subtotal_AtEachChangeInHelpText"));

        AutomationProperties.SetName(_functionBox, UiText.Get("Subtotal_UseFunctionAutomationName"));
        AutomationProperties.SetAutomationId(_functionBox, "SubtotalFunctionBox");
        AutomationProperties.SetHelpText(_functionBox, UiText.Get("Subtotal_UseFunctionHelpText"));

        AutomationProperties.SetName(_subtotalColumnList, UiText.Get("Subtotal_AddSubtotalToAutomationName"));
        AutomationProperties.SetAutomationId(_subtotalColumnList, "SubtotalColumnsPanel");
        AutomationProperties.SetHelpText(_subtotalColumnList, UiText.Get("Subtotal_AddSubtotalToHelpText"));

        AutomationProperties.SetName(_replaceBox, UiText.Get("Subtotal_ReplaceCurrentSubtotalsAutomationName"));
        AutomationProperties.SetAutomationId(_replaceBox, "SubtotalReplaceCurrentBox");
        AutomationProperties.SetHelpText(_replaceBox, UiText.Get("Subtotal_ReplaceCurrentSubtotalsHelpText"));

        AutomationProperties.SetName(_pageBreakBox, UiText.Get("Subtotal_PageBreakBetweenGroupsAutomationName"));
        AutomationProperties.SetAutomationId(_pageBreakBox, "SubtotalPageBreakBox");
        AutomationProperties.SetHelpText(_pageBreakBox, UiText.Get("Subtotal_PageBreakBetweenGroupsHelpText"));

        AutomationProperties.SetName(_summaryBelowBox, UiText.Get("Subtotal_SummaryBelowDataAutomationName"));
        AutomationProperties.SetAutomationId(_summaryBelowBox, "SubtotalSummaryBelowBox");
        AutomationProperties.SetHelpText(_summaryBelowBox, UiText.Get("Subtotal_SummaryBelowDataHelpText"));
    }

    public static SubtotalDialogResult CreateResult(
        uint groupColumnOffset,
        IEnumerable<uint> subtotalColumnOffsets,
        string functionText,
        bool replaceCurrentSubtotals,
        bool pageBreakBetweenGroups,
        bool summaryBelowData)
    {
        if (SharedSubtotalDialogPlanner.TryCreateResult(
                groupColumnOffset,
                subtotalColumnOffsets,
                functionText,
                replaceCurrentSubtotals,
                pageBreakBetweenGroups,
                summaryBelowData,
                out var result,
                out var issue))
        {
            return Project(result);
        }

        var error = DescribeCreateResultIssue(issue);
        var parameterName = string.Equals(error, UiText.Get("Subtotal_UnsupportedSubtotalFunction"), StringComparison.Ordinal)
            ? nameof(functionText)
            : nameof(subtotalColumnOffsets);
        throw new ArgumentException(error, parameterName);
    }

    public static SubtotalDialogResult CreateRemoveAllResult() =>
        Project(SharedSubtotalDialogPlanner.CreateRemoveAllResult());

    public static IReadOnlyList<SubtotalColumnChoice> BuildColumnChoices(Sheet sheet, GridRange range)
        => SharedSubtotalDialogPlanner.BuildColumnChoices(sheet, range, PlannerText);

    private void Accept()
    {
        var groupColumnOffset = _groupColumnBox.SelectedValue is uint offset ? offset : 0;
        var subtotalColumnOffsets = _subtotalColumns
            .Where(static column => column.IsSelected)
            .Select(static column => column.Offset)
            .ToList();

        if (!SharedSubtotalDialogPlanner.TryCreateResult(
                groupColumnOffset,
                subtotalColumnOffsets,
                _functionBox.SelectedValue?.ToString() ?? SharedSubtotalDialogPlanner.DefaultFunctionText,
                _replaceBox.IsChecked == true,
                _pageBreakBox.IsChecked == true,
                _summaryBelowBox.IsChecked == true,
                out var plan,
                out var issue))
        {
            var presentation = SubtotalDialogInputParser.DescribeIssue(issue);
            DialogMessageHelper.ShowWarning(
                this,
                presentation.Message.Resolve(UiText.Get, UiText.Format),
                Title);
            FocusInvalidInput(presentation.FocusTarget);
            return;
        }

        Result = Project(plan);
        DialogResult = true;
    }

    private void FocusInvalidInput(SubtotalDialogInputFocusTarget focusTarget)
    {
        if (focusTarget == SubtotalDialogInputFocusTarget.Function)
        {
            FocusFunctionChoice();
            return;
        }

        if (focusTarget == SubtotalDialogInputFocusTarget.GroupColumn)
        {
            _groupColumnBox.Focus();
            Keyboard.Focus(_groupColumnBox);
            return;
        }

        FocusSubtotalColumnChoices();
    }

    private void FocusFunctionChoice()
    {
        _functionBox.Focus();
        Keyboard.Focus(_functionBox);
    }

    private void FocusSubtotalColumnChoices()
    {
        if (_subtotalColumns.Count > 0 && !_isMovingSubtotalColumnFocus)
        {
            _isMovingSubtotalColumnFocus = true;
            try
            {
                _subtotalColumnList.Focus();
                Keyboard.Focus(_subtotalColumnList);
                if (_subtotalColumnList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem firstItem)
                {
                    if (FindVisualDescendant<CheckBox>(firstItem) is { } firstColumnBox)
                    {
                        firstColumnBox.Focus();
                        Keyboard.Focus(firstColumnBox);
                        return;
                    }

                    firstItem.Focus();
                    Keyboard.Focus(firstItem);
                }
            }
            finally
            {
                _isMovingSubtotalColumnFocus = false;
            }
        }
    }

    private void MoveFocusIntoSubtotalColumnChoices(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, _subtotalColumnList))
            FocusSubtotalColumnChoices();
    }

    private void RemoveAll()
    {
        Result = CreateRemoveAllResult();
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _groupColumnBox.Focus();
        Keyboard.Focus(_groupColumnBox);
    }

    private static Grid CreateSubtotalButtonRow(Action accept, Action removeAll)
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var removeButton = new Button
        {
            Content = UiText.Get("Subtotal_RemoveAll"),
            Width = 92,
            Margin = new Thickness(0, 0, 8, 0)
        };
        AutomationProperties.SetName(removeButton, UiText.Get("Subtotal_RemoveAllAutomationName"));
        AutomationProperties.SetAutomationId(removeButton, "SubtotalRemoveAllButton");
        AutomationProperties.SetHelpText(removeButton, UiText.Get("Subtotal_RemoveAllHelpText"));
        removeButton.Click += (_, _) => removeAll();
        grid.Children.Add(removeButton);

        var buttons = TextToColumnsDialog.CreateButtonRow(accept);
        buttons.Margin = new Thickness(0);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        return grid;
    }

    private static IReadOnlyList<SubtotalColumnChoice> NormalizeColumnChoices(IEnumerable<SubtotalColumnChoice>? columns)
        => SharedSubtotalDialogPlanner.NormalizeColumnChoices(columns, PlannerText);

    private static void ConfigureVirtualizedItemsControl(ItemsControl control)
    {
        control.ItemsPanel = CreateVirtualizingStackPanelTemplate();
        control.SetValue(ScrollViewer.CanContentScrollProperty, true);
        control.SetValue(VirtualizingStackPanel.IsVirtualizingProperty, true);
        control.SetValue(VirtualizingStackPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
    }

    private static ItemsPanelTemplate CreateVirtualizingStackPanelTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(VirtualizingStackPanel));
        var template = new ItemsPanelTemplate(factory);
        template.Seal();
        return template;
    }

    private static DataTemplate CreateSubtotalColumnTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(CheckBox));
        factory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 4));
        factory.SetBinding(ContentControl.ContentProperty, new Binding(nameof(SubtotalColumnSelection.Header)));
        factory.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(SubtotalColumnSelection.IsSelected))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        factory.SetBinding(AutomationProperties.AutomationIdProperty, new Binding(nameof(SubtotalColumnSelection.AutomationId)));
        factory.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(SubtotalColumnSelection.AutomationName)));
        factory.SetBinding(AutomationProperties.HelpTextProperty, new Binding(nameof(SubtotalColumnSelection.HelpText)));

        var template = new DataTemplate(typeof(SubtotalColumnSelection))
        {
            VisualTree = factory
        };
        template.Seal();
        return template;
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                return match;

            var nested = FindVisualDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static SubtotalDialogResult Project(SubtotalDialogPlanResult result) =>
        new(
            result.GroupColumnOffset,
            result.SubtotalColumnOffsets,
            result.FunctionNumber,
            result.ReplaceCurrentSubtotals,
            result.PageBreakBetweenGroups,
            result.SummaryBelowData,
            result.Action == SharedSubtotalDialogAction.RemoveAll
                ? SubtotalDialogAction.RemoveAll
                : SubtotalDialogAction.Apply);

    private static string? DescribeCreateResultIssue(SubtotalDialogInputParseIssue issue) =>
        issue == SubtotalDialogInputParseIssue.None
            ? null
            : SubtotalDialogInputParser
                .DescribeIssue(issue)
                .Message
                .Resolve(UiText.Get, UiText.Format);

    private static SubtotalDialogPlannerText PlannerText => SubtotalDialogPlannerText.From(UiText.Get);
}
