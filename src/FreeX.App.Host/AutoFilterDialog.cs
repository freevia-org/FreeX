using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class AutoFilterDialog : Window
{
    private sealed record FilterChoice(string Label, string Value);

    private readonly List<AutoFilterDialogItem> _allItems;
    private readonly ObservableCollection<AutoFilterDialogItem> _items;
    private readonly TextBox _searchBox = new();
    private readonly CheckBox _addCurrentSelectionToFilterBox = new()
    {
        Content = UiText.Get("AutoFilter_AddCurrentSelectionToFilter"),
        Margin = new Thickness(0, 0, 0, 8)
    };
    private readonly TextBox _criteriaBox = new() { IsReadOnly = true };
    private readonly ComboBox _criteriaSuggestionBox = new()
    {
        Visibility = Visibility.Collapsed,
        IsTextSearchEnabled = true
    };
    private readonly ComboBox _criteriaOperatorBox = new()
    {
        Visibility = Visibility.Collapsed,
        IsTextSearchEnabled = true,
        DisplayMemberPath = nameof(AutoFilterCriteriaOption.Label)
    };
    private readonly TextBox _criteriaValueBox = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel _betweenCriteriaPanel = new() { Visibility = Visibility.Collapsed };
    private readonly TextBox _betweenMinBox = new() { Width = 82 };
    private readonly TextBox _betweenMaxBox = new() { Width = 82 };
    private readonly StackPanel _topBottomCriteriaPanel = new() { Visibility = Visibility.Collapsed };
    private readonly TextBox _topBottomCountBox = new() { Width = 54, Text = "10" };
    private readonly TextBlock _topBottomUnitText = new() { VerticalAlignment = System.Windows.VerticalAlignment.Center };
    private readonly ComboBox _datePresetBox = new()
    {
        Visibility = Visibility.Collapsed,
        Width = 150,
        DisplayMemberPath = nameof(FilterChoice.Label),
        SelectedValuePath = nameof(FilterChoice.Value),
        SelectedIndex = 0
    };
    private readonly ComboBox _criteriaConnectorBox = new()
    {
        Visibility = Visibility.Collapsed,
        DisplayMemberPath = nameof(FilterChoice.Label),
        SelectedValuePath = nameof(FilterChoice.Value),
        SelectedIndex = 0
    };
    private readonly ComboBox _criteriaOperatorBox2 = new()
    {
        Visibility = Visibility.Collapsed,
        IsTextSearchEnabled = true,
        DisplayMemberPath = nameof(AutoFilterCriteriaOption.Label)
    };
    private readonly TextBox _criteriaValueBox2 = new() { Visibility = Visibility.Collapsed };
    private readonly RadioButton _sortNone = new() { Content = UiText.Get("AutoFilter_NoSort"), IsChecked = true };
    private readonly RadioButton _sortAscending = new() { Content = UiText.Get("AutoFilter_SortAToZ") };
    private readonly RadioButton _sortDescending = new() { Content = UiText.Get("AutoFilter_SortZToA") };
    private readonly Button _clearFilterButton = new() { Content = UiText.Get("AutoFilter_ClearFilterFrom2"), Margin = new Thickness(0, 8, 0, 0) };
    private readonly GroupBox _filterByColorGroup = new() { Header = UiText.Get("AutoFilter_FilterByColor2"), Visibility = Visibility.Collapsed };
    private readonly StackPanel _filterByColorPanel = new();
    private readonly List<Button> _colorChoiceButtons = [];
    private readonly Button _textFiltersButton = new() { Content = UiText.Get("AutoFilter_TextFilters"), Visibility = Visibility.Collapsed };
    private readonly Button _numberFiltersButton = new() { Content = UiText.Get("AutoFilter_NumberFilters"), Visibility = Visibility.Collapsed };
    private readonly Button _dateFiltersButton = new() { Content = UiText.Get("AutoFilter_DateFilters"), Visibility = Visibility.Collapsed };
    private readonly ListBox _checklistBox = new();
    private readonly GroupBox _customFilterGroup = new() { Header = UiText.Get("AutoFilter_CustomFilter"), Visibility = Visibility.Collapsed };
    private readonly Label _criteriaSuggestionLabel = new()
    {
        Content = UiText.Get("AutoFilter_CriteriaTemplate"),
        Padding = new Thickness(0),
        Visibility = Visibility.Collapsed
    };
    private AutoFilterColorFilter? _selectedColorFilter;

    public AutoFilterDialogResult Result { get; private set; }

    public AutoFilterDialog(IEnumerable<AutoFilterChecklistItem> items)
        : this(items.Select(item => new AutoFilterDialogItem(item.DisplayText, item.Value, true)))
    {
    }

    public AutoFilterDialog(AutoFilterMenuPlan menuPlan)
        : this(CreateDialogItems(menuPlan))
    {
        Title = UiText.Format("AutoFilter_TitleWithHeader", menuPlan.HeaderText);
        _clearFilterButton.Content = UiText.Format("AutoFilter_ClearFilterFromHeader", menuPlan.HeaderText);
        SetSortLabels(menuPlan.FilterKind);
        ShowFilterFamilyButton(menuPlan.FilterKind);
        var criteriaSuggestions = GetCriteriaSuggestions(menuPlan);
        if (criteriaSuggestions.Count > 0)
        {
            _criteriaSuggestionBox.ItemsSource = criteriaSuggestions;
            _criteriaSuggestionBox.Visibility = Visibility.Visible;
            _criteriaSuggestionBox.ToolTip = UiText.Get("AutoFilter_ExcelFilterCriteriaTemplates");
            _criteriaSuggestionLabel.Visibility = Visibility.Visible;
        }

        var criteriaOptions = GetCriteriaOptions(menuPlan.FilterKind);
        if (criteriaOptions.Count > 0)
        {
            _criteriaOperatorBox.ItemsSource = criteriaOptions;
            _criteriaOperatorBox.Visibility = Visibility.Visible;
            _criteriaOperatorBox.SelectedIndex = 0;
            _criteriaOperatorBox.ToolTip = UiText.Format("AutoFilter_FilterFamilyOperatorToolTip", GetFilterFamilyHeader(menuPlan.FilterKind));
            _criteriaValueBox.Visibility = Visibility.Visible;
            _criteriaValueBox.ToolTip = UiText.Get("AutoFilter_ValueForTheSelectedTypedFilter");
            _criteriaConnectorBox.Visibility = Visibility.Visible;
            _criteriaOperatorBox2.ItemsSource = criteriaOptions;
            _criteriaOperatorBox2.Visibility = Visibility.Visible;
            _criteriaOperatorBox2.SelectedIndex = 0;
            _criteriaOperatorBox2.ToolTip = UiText.Format("AutoFilter_SecondFilterFamilyOperatorToolTip", GetFilterFamilyHeader(menuPlan.FilterKind));
            _criteriaValueBox2.Visibility = Visibility.Visible;
            _criteriaValueBox2.ToolTip = UiText.Get("AutoFilter_ValueForTheSecondTypedFilter");
            _criteriaBox.ToolTip = UiText.Get("AutoFilter_GeneratedCriterionThatWillBeApplied");
            _customFilterGroup.Visibility = Visibility.Visible;
        }
        ConfigureFilterFamilySubmenu(menuPlan);

        if (menuPlan.FilterKind == AutoFilterMenuFilterKind.Date)
            _datePresetBox.Visibility = Visibility.Visible;

        var colorOptions = menuPlan.ColorOptions ?? [];
        if (colorOptions.Count > 0)
            PopulateColorChoices(colorOptions);
    }

    public AutoFilterDialog(IEnumerable<AutoFilterDialogItem> items)
    {
        _allItems = items.ToList();
        _items = new ObservableCollection<AutoFilterDialogItem>(_allItems);
        Result = BuildResult(AutoFilterSortDirection.None, _allItems, string.Empty, string.Empty);

        Title = UiText.Get("AutoFilter_AutoFilter");
        Width = 360;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel { Margin = new Thickness(16) };
        _datePresetBox.ItemsSource = CreateDatePresetChoices();
        _criteriaConnectorBox.ItemsSource = CreateConnectorChoices();
        var stack = new StackPanel();
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        scrollViewer.Content = stack;
        root.Children.Add(scrollViewer);

        stack.Children.Add(_sortNone);
        stack.Children.Add(_sortAscending);
        stack.Children.Add(_sortDescending);
        AddFilterMenuSeparator(stack);
        _clearFilterButton.Click += (_, _) =>
        {
            _selectedColorFilter = null;
            _criteriaBox.Clear();
            _criteriaValueBox.Clear();
            _searchBox.Clear();
            _sortNone.IsChecked = true;
            ReplaceAllItems(SelectAll(_allItems));
            Result = CreateClearFilterResult();
            DialogResult = true;
        };
        stack.Children.Add(_clearFilterButton);
        _filterByColorGroup.Content = _filterByColorPanel;
        _filterByColorGroup.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(_filterByColorGroup);
        foreach (var filterButton in new[] { _textFiltersButton, _numberFiltersButton, _dateFiltersButton })
        {
            filterButton.Margin = new Thickness(0, 8, 0, 0);
            filterButton.Click += (_, _) => TryOpenFilterFamilySubmenu(filterButton);
            stack.Children.Add(filterButton);
        }

        AddFilterMenuSeparator(stack);
        stack.Children.Add(new Label { Content = UiText.Get("AutoFilter_Search2"), Target = _searchBox, Padding = new Thickness(0), Margin = new Thickness(0, 12, 0, 2) });
        _searchBox.Margin = new Thickness(0, 0, 0, 8);
        _searchBox.ToolTip = UiText.Get("AutoFilter_Search3");
        _searchBox.TextChanged += (_, _) => ReplaceItems(FilterItems(_allItems, _searchBox.Text));
        stack.Children.Add(_searchBox);
        stack.Children.Add(_addCurrentSelectionToFilterBox);

        _checklistBox.ItemsSource = _items;
        _checklistBox.Height = 180;
        _checklistBox.Margin = new Thickness(0, 0, 0, 8);
        _checklistBox.ItemTemplate = CreateItemTemplate();
        _checklistBox.PreviewKeyDown += ChecklistBox_PreviewKeyDown;
        AutomationProperties.SetName(_checklistBox, UiText.Get("AutoFilter_FilterValues"));
        stack.Children.Add(_checklistBox);

        var selectionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var selectAll = new Button { Content = UiText.Get("AutoFilter_SelectAll2"), Width = 88, Margin = new Thickness(0, 0, 8, 0) };
        selectAll.Click += (_, _) => ReplaceAllItems(SetSelectionForSearch(_allItems, _searchBox.Text, isSelected: true));
        var clearAll = new Button { Content = UiText.Get("AutoFilter_ClearAll"), Width = 88 };
        clearAll.Click += (_, _) => ReplaceAllItems(SetSelectionForSearch(_allItems, _searchBox.Text, isSelected: false));
        selectionRow.Children.Add(selectAll);
        selectionRow.Children.Add(clearAll);
        stack.Children.Add(selectionRow);
        AddFilterMenuSeparator(stack);

        var customFilterPanel = new StackPanel();
        _customFilterGroup.Content = customFilterPanel;
        stack.Children.Add(_customFilterGroup);

        customFilterPanel.Children.Add(new Label { Content = UiText.Get("AutoFilter_ShowRowsWhere"), Padding = new Thickness(0) });
        customFilterPanel.Children.Add(new Label { Content = UiText.Get("AutoFilter_DatePreset"), Target = _datePresetBox, Padding = new Thickness(0) });
        _datePresetBox.Margin = new Thickness(0, 4, 0, 4);
        _datePresetBox.SelectionChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        customFilterPanel.Children.Add(_datePresetBox);
        customFilterPanel.Children.Add(new Label { Content = UiText.Get("AutoFilter_FilterOperator"), Target = _criteriaOperatorBox, Padding = new Thickness(0) });
        _criteriaOperatorBox.Margin = new Thickness(0, 4, 0, 4);
        _criteriaOperatorBox.SelectionChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        customFilterPanel.Children.Add(_criteriaOperatorBox);

        customFilterPanel.Children.Add(new Label { Content = UiText.Get("AutoFilter_FilterValue"), Target = _criteriaValueBox, Padding = new Thickness(0) });
        _criteriaValueBox.Margin = new Thickness(0, 4, 0, 4);
        _criteriaValueBox.TextChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        customFilterPanel.Children.Add(_criteriaValueBox);
        customFilterPanel.Children.Add(CreateBetweenCriteriaPanel());
        customFilterPanel.Children.Add(CreateTopBottomCriteriaPanel());

        customFilterPanel.Children.Add(new Label { Content = UiText.Get("AutoFilter_AndOr"), Target = _criteriaConnectorBox, Padding = new Thickness(0) });
        _criteriaConnectorBox.Margin = new Thickness(0, 4, 0, 4);
        _criteriaConnectorBox.SelectionChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        customFilterPanel.Children.Add(_criteriaConnectorBox);

        customFilterPanel.Children.Add(new Label { Content = UiText.Get("AutoFilter_SecondOperator"), Target = _criteriaOperatorBox2, Padding = new Thickness(0) });
        _criteriaOperatorBox2.Margin = new Thickness(0, 4, 0, 4);
        _criteriaOperatorBox2.SelectionChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        customFilterPanel.Children.Add(_criteriaOperatorBox2);

        customFilterPanel.Children.Add(new Label { Content = UiText.Get("AutoFilter_SecondValue"), Target = _criteriaValueBox2, Padding = new Thickness(0) });
        _criteriaValueBox2.Margin = new Thickness(0, 4, 0, 4);
        _criteriaValueBox2.TextChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        customFilterPanel.Children.Add(_criteriaValueBox2);

        customFilterPanel.Children.Add(new Label { Content = UiText.Get("AutoFilter_CriteriaText"), Target = _criteriaBox, Padding = new Thickness(0) });

        _criteriaBox.Margin = new Thickness(0, 4, 0, 12);
        customFilterPanel.Children.Add(_criteriaBox);

        _criteriaSuggestionLabel.Target = _criteriaSuggestionBox;
        customFilterPanel.Children.Add(_criteriaSuggestionLabel);
        _criteriaSuggestionBox.Margin = new Thickness(0, 4, 0, 12);
        _criteriaSuggestionBox.SelectionChanged += (_, _) =>
        {
            if (_criteriaSuggestionBox.SelectedItem is string suggestion)
                _criteriaBox.Text = suggestion;
        };
        customFilterPanel.Children.Add(_criteriaSuggestionBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var ok = new Button { Content = UiText.Ok, IsDefault = true, Width = 76, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) =>
        {
            if (!ValidateTypedCriteriaInputs())
                return;

            Result = BuildResult(
                GetSortDirection(),
                _allItems,
                _searchBox.Text,
                _criteriaBox.Text,
                _selectedColorFilter,
                _addCurrentSelectionToFilterBox.IsChecked == true);
            DialogResult = true;
        };
        var cancel = new Button { Content = UiText.Cancel, IsCancel = true, Width = 76 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
        PreviewKeyDown += AutoFilterDialog_PreviewKeyDown;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private static IReadOnlyList<FilterChoice> CreateDatePresetChoices() =>
        [
            new(UiText.Get("AutoFilter_DatePresetCustom"), "Custom"),
            new(UiText.Get("AutoFilter_DatePresetToday"), "Today"),
            new(UiText.Get("AutoFilter_DatePresetYesterday"), "Yesterday"),
            new(UiText.Get("AutoFilter_DatePresetTomorrow"), "Tomorrow"),
            new(UiText.Get("AutoFilter_DatePresetThisWeek"), "This Week"),
            new(UiText.Get("AutoFilter_DatePresetLastWeek"), "Last Week"),
            new(UiText.Get("AutoFilter_DatePresetNextWeek"), "Next Week"),
            new(UiText.Get("AutoFilter_DatePresetThisMonth"), "This Month"),
            new(UiText.Get("AutoFilter_DatePresetLastMonth"), "Last Month"),
            new(UiText.Get("AutoFilter_DatePresetNextMonth"), "Next Month"),
            new(UiText.Get("AutoFilter_DatePresetThisYear"), "This Year"),
            new(UiText.Get("AutoFilter_DatePresetLastYear"), "Last Year"),
            new(UiText.Get("AutoFilter_DatePresetNextYear"), "Next Year")
        ];

    private static IReadOnlyList<FilterChoice> CreateConnectorChoices() =>
        [
            new(UiText.Get("AutoFilter_ConnectorAnd"), "And"),
            new(UiText.Get("AutoFilter_ConnectorOr"), "Or")
        ];
}
