using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class AutoFilterDialog : Window
{
    private sealed record FilterChoice(string Label, string Value);

    private readonly List<AutoFilterDialogItem> _allItems;
    private readonly ObservableCollection<AutoFilterDialogItem> _items;
    private readonly TextBox _searchBox = new();
    private readonly Grid _searchBoxHost = new();
    private readonly TextBlock _searchWatermark = new()
    {
        Text = UiText.Get("AutoFilter_Search3"),
        Margin = new Thickness(9, 0, 4, 0),
        VerticalAlignment = System.Windows.VerticalAlignment.Center,
        Foreground = Brushes.Gray,
        IsHitTestVisible = false
    };
    private readonly CheckBox _addCurrentSelectionToFilterBox = new()
    {
        Content = UiText.Get("AutoFilter_AddCurrentSelectionToFilter"),
        Margin = new Thickness(0, 0, 0, 6),
        Visibility = Visibility.Collapsed
    };
    private readonly CheckBox _selectAllBox = new()
    {
        Content = UiText.Get("AutoFilter_SelectAll"),
        IsThreeState = true,
        Margin = new Thickness(0, 0, 0, 4)
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
    private readonly Button _sortAscendingButton = CreateMenuCommandButton(UiText.Get("AutoFilter_SortAToZ"), RibbonCommandIconKind.SortAscending);
    private readonly Button _sortDescendingButton = CreateMenuCommandButton(UiText.Get("AutoFilter_SortZToA"), RibbonCommandIconKind.SortDescending);
    private readonly Button _sortByColorUnavailableButton = CreateMenuCommandButton(UiText.Get("AutoFilter_SortByColor"), RibbonCommandIconKind.Color, Visibility.Collapsed);
    private readonly Button _clearFilterButton = CreateMenuCommandButton(UiText.Get("AutoFilter_ClearFilterFrom2"), RibbonCommandIconKind.Clear);
    private readonly Button _filterByColorUnavailableButton = CreateMenuCommandButton(UiText.Get("AutoFilter_FilterByColor"), RibbonCommandIconKind.Color, Visibility.Collapsed);
    private readonly GroupBox _filterByColorGroup = new() { Header = UiText.Get("AutoFilter_FilterByColor2"), Visibility = Visibility.Collapsed };
    private readonly StackPanel _filterByColorPanel = new();
    private readonly List<Button> _colorChoiceButtons = [];
    private readonly GroupBox _sortByColorGroup = new() { Header = UiText.Get("AutoFilter_SortByColor"), Visibility = Visibility.Collapsed };
    private readonly StackPanel _sortByColorPanel = new();
    private readonly List<Button> _sortByColorButtons = [];
    private readonly Button _textFiltersButton = CreateMenuCommandButton(UiText.Get("AutoFilter_TextFilters"), RibbonCommandIconKind.Filter, Visibility.Collapsed);
    private readonly Button _numberFiltersButton = CreateMenuCommandButton(UiText.Get("AutoFilter_NumberFilters"), RibbonCommandIconKind.Filter, Visibility.Collapsed);
    private readonly Button _dateFiltersButton = CreateMenuCommandButton(UiText.Get("AutoFilter_DateFilters"), RibbonCommandIconKind.Filter, Visibility.Collapsed);
    private readonly ListBox _checklistBox = new();
    private readonly Button _okButton = new() { Content = UiText.Ok, IsDefault = true, Width = 76, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _cancelButton = new() { Content = UiText.Cancel, IsCancel = true, Width = 76 };
    private readonly GroupBox _customFilterGroup = new()
    {
        Header = UiText.Get("AutoFilter_CustomFilter"),
        Visibility = Visibility.Collapsed,
        Margin = new Thickness(0, 8, 0, 0)
    };
    private readonly Label _criteriaSuggestionLabel = new()
    {
        Content = UiText.Get("AutoFilter_CriteriaTemplate"),
        Padding = new Thickness(0),
        Visibility = Visibility.Collapsed
    };
    private AutoFilterColorFilter? _selectedColorFilter;
    private bool _updatingSelectAllBox;
    private bool _useModelessFlyoutCommit;

    public AutoFilterDialogResult Result { get; private set; }
    public event EventHandler<AutoFilterDialogResult>? ResultCommitted;

    public AutoFilterDialog(IEnumerable<AutoFilterChecklistItem> items)
        : this(items.Select(item => new AutoFilterDialogItem(item.DisplayText, item.Value, item.IsChecked)))
    {
    }

    public AutoFilterDialog(AutoFilterMenuPlan menuPlan)
        : this(CreateDialogItems(menuPlan))
    {
        Title = UiText.Format("AutoFilter_TitleWithHeader", menuPlan.HeaderText);
        SetMenuCommandButtonContent(_clearFilterButton, UiText.Format("AutoFilter_ClearFilterFromHeader", menuPlan.HeaderText), RibbonCommandIconKind.Clear);
        _clearFilterButton.IsEnabled = FindClearFilterEntry(menuPlan)?.IsEnabled ?? true;
        SetSortLabels(menuPlan);
        ShowFilterFamilyButton(menuPlan.FilterKind);
        var criteriaSuggestions = AutoFilterDialogCriteriaPlanner.GetCriteriaSuggestions(menuPlan);
        if (criteriaSuggestions.Count > 0)
        {
            _criteriaSuggestionBox.ItemsSource = criteriaSuggestions;
            _criteriaSuggestionBox.Visibility = Visibility.Visible;
            _criteriaSuggestionBox.ToolTip = UiText.Get("AutoFilter_ExcelFilterCriteriaTemplates");
            _criteriaSuggestionLabel.Visibility = Visibility.Visible;
        }

        var criteriaOptions = AutoFilterMenuPlanner.CreateCriteriaOptions(menuPlan.FilterKind, WpfResourceKeyTextResolver.Resources.AutoFilter);
        if (criteriaOptions.Count > 0)
        {
            _criteriaOperatorBox.ItemsSource = criteriaOptions;
            _criteriaOperatorBox.Visibility = Visibility.Visible;
            _criteriaOperatorBox.SelectedIndex = 0;
            _criteriaOperatorBox.ToolTip = UiText.Format(
                "AutoFilter_FilterFamilyOperatorToolTip",
                AutoFilterMenuPlanner.GetFilterFamilyHeader(menuPlan.FilterKind, WpfResourceKeyTextResolver.Resources.AutoFilter));
            _criteriaValueBox.Visibility = Visibility.Visible;
            _criteriaValueBox.ToolTip = UiText.Get("AutoFilter_ValueForTheSelectedTypedFilter");
            _criteriaConnectorBox.Visibility = Visibility.Visible;
            _criteriaOperatorBox2.ItemsSource = AutoFilterDialogCriteriaPlanner.GetSecondRowCriteriaOptions(criteriaOptions);
            _criteriaOperatorBox2.Visibility = Visibility.Visible;
            _criteriaOperatorBox2.SelectedIndex = 0;
            _criteriaOperatorBox2.ToolTip = UiText.Format(
                "AutoFilter_SecondFilterFamilyOperatorToolTip",
                AutoFilterMenuPlanner.GetFilterFamilyHeader(menuPlan.FilterKind, WpfResourceKeyTextResolver.Resources.AutoFilter));
            _criteriaValueBox2.Visibility = Visibility.Visible;
            _criteriaValueBox2.ToolTip = UiText.Get("AutoFilter_ValueForTheSecondTypedFilter");
            _criteriaBox.ToolTip = UiText.Get("AutoFilter_GeneratedCriterionThatWillBeApplied");
        }
        ConfigureFilterFamilySubmenu(menuPlan);

        if (menuPlan.FilterKind == AutoFilterMenuFilterKind.Date)
            _datePresetBox.Visibility = Visibility.Visible;

        var colorOptions = menuPlan.ColorOptions ?? [];
        ConfigureUnavailableColorCommands(colorOptions);
        if (colorOptions.Count > 0 && AutoFilterDialogCriteriaPlanner.HasFilterByColorEntry(menuPlan))
            PopulateColorChoices(colorOptions);

        // R76-render-autofilter-dropdown-4-2: "No Fill" has no single color to sort toward (see
        // AutoFilterDropdownMenuPlanner.CreateSortByColorCommand), so only actual colors are offered.
        var sortColorOptions = colorOptions.Where(option => option.Color is not null).ToList();
        if (sortColorOptions.Count > 0 && AutoFilterDialogCriteriaPlanner.HasSortByColorEntry(menuPlan))
            PopulateSortByColorChoices(sortColorOptions);
    }

    private static AutoFilterMenuEntry? FindClearFilterEntry(AutoFilterMenuPlan menuPlan)
    {
        foreach (var entry in menuPlan.Entries)
        {
            if (entry.Kind == AutoFilterMenuEntryKind.ClearFilter)
                return entry;
        }

        return null;
    }

    public AutoFilterDialog(IEnumerable<AutoFilterDialogItem> items)
    {
        _allItems = items.ToList();
        _items = new ObservableCollection<AutoFilterDialogItem>(_allItems);
        Result = AutoFilterDialogCriteriaPlanner.BuildResult(
            AutoFilterSortDirection.None,
            _allItems,
            string.Empty,
            string.Empty);

        Title = UiText.Get("AutoFilter_AutoFilter");
        Width = 312;
        MaxHeight = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel { Margin = new Thickness(10), LastChildFill = true };
        _datePresetBox.ItemsSource = CreateDatePresetChoices();
        _criteriaConnectorBox.ItemsSource = CreateConnectorChoices();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        _okButton.Click += (_, _) =>
        {
            if (!ValidateTypedCriteriaInputs())
                return;

            CommitResult(AutoFilterDialogCriteriaPlanner.BuildResult(
                AutoFilterSortDirection.None,
                _allItems,
                _searchBox.Text,
                GetCommittedCriteriaText(),
                _selectedColorFilter,
                _addCurrentSelectionToFilterBox.IsChecked == true));
        };
        _cancelButton.Click += (_, _) => CancelResult();
        buttons.Children.Add(_okButton);
        buttons.Children.Add(_cancelButton);
        root.Children.Add(buttons);

        var stack = new StackPanel();
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        scrollViewer.Content = stack;
        root.Children.Add(scrollViewer);

        _sortAscendingButton.Click += (_, _) => ApplySortCommand(AutoFilterSortDirection.Ascending);
        _sortDescendingButton.Click += (_, _) => ApplySortCommand(AutoFilterSortDirection.Descending);
        stack.Children.Add(_sortAscendingButton);
        stack.Children.Add(_sortDescendingButton);
        stack.Children.Add(_sortByColorUnavailableButton);
        _sortByColorGroup.Content = _sortByColorPanel;
        _sortByColorGroup.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(_sortByColorGroup);
        AddFilterMenuSeparator(stack);
        _clearFilterButton.Click += (_, _) =>
        {
            _selectedColorFilter = null;
            _criteriaBox.Clear();
            _criteriaValueBox.Clear();
            _searchBox.Clear();
            ReplaceAllItems(AutoFilterDialogCriteriaPlanner.SelectAll(_allItems));
            CommitResult(AutoFilterDialogCriteriaPlanner.CreateClearFilterResult());
        };
        stack.Children.Add(_clearFilterButton);
        stack.Children.Add(_filterByColorUnavailableButton);
        _filterByColorGroup.Content = _filterByColorPanel;
        _filterByColorGroup.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(_filterByColorGroup);
        foreach (var filterButton in new[] { _textFiltersButton, _numberFiltersButton, _dateFiltersButton })
        {
            filterButton.Margin = new Thickness(0, 4, 0, 0);
            filterButton.Click += (_, _) => TryOpenFilterFamilySubmenu(filterButton);
            stack.Children.Add(filterButton);
        }

        AddFilterMenuSeparator(stack);
        _searchBoxHost.Margin = new Thickness(0, 4, 0, 4);
        _searchBox.MinHeight = 22;
        _searchBoxHost.Children.Add(_searchBox);
        _searchBoxHost.Children.Add(_searchWatermark);
        _searchBox.ToolTip = UiText.Get("AutoFilter_Search3");
        AutomationProperties.SetName(_searchBox, UiText.Get("AutoFilter_Search3"));
        AutomationProperties.SetHelpText(_searchBox, UiText.Get("AutoFilter_Search3"));
        AutomationProperties.SetAccessKey(_searchBox, "S");
        _searchBox.TextChanged += (_, _) =>
        {
            _searchWatermark.Visibility = string.IsNullOrEmpty(_searchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            ApplySearchTextChange();
        };
        stack.Children.Add(_searchBoxHost);
        stack.Children.Add(_addCurrentSelectionToFilterBox);
        _selectAllBox.Checked += (_, _) => SetSelectionForVisibleItems(isSelected: true);
        _selectAllBox.Unchecked += (_, _) => SetSelectionForVisibleItems(isSelected: false);
        stack.Children.Add(_selectAllBox);

        _checklistBox.ItemsSource = _items;
        _checklistBox.Height = 176;
        _checklistBox.Margin = new Thickness(0, 0, 0, 4);
        _checklistBox.ItemTemplate = CreateItemTemplate();
        _checklistBox.PreviewKeyDown += ChecklistBox_PreviewKeyDown;
        AutomationProperties.SetName(_checklistBox, UiText.Get("AutoFilter_FilterValues"));
        stack.Children.Add(_checklistBox);

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

        Content = root;
        PreviewKeyDown += AutoFilterDialog_PreviewKeyDown;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
        UpdateSelectAllBoxState();
    }

    private void ConfigureUnavailableColorCommands(IReadOnlyList<AutoFilterColorOption> colorOptions)
    {
        var hasColorOptions = colorOptions.Count > 0;
        _sortByColorUnavailableButton.Visibility = hasColorOptions ? Visibility.Collapsed : Visibility.Visible;
        _filterByColorUnavailableButton.Visibility = hasColorOptions ? Visibility.Collapsed : Visibility.Visible;
        _sortByColorUnavailableButton.IsEnabled = false;
        _filterByColorUnavailableButton.IsEnabled = false;
        SetMenuCommandButtonContent(
            _sortByColorUnavailableButton,
            UiText.Get("AutoFilter_SortByColor"),
            RibbonCommandIconKind.Color,
            hasCascade: true);
        SetMenuCommandButtonContent(
            _filterByColorUnavailableButton,
            UiText.Get("AutoFilter_FilterByColor"),
            RibbonCommandIconKind.Color,
            hasCascade: true);
    }

    public void ConfigureAsModelessFlyout()
    {
        _useModelessFlyoutCommit = true;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _cancelButton.IsCancel = false;
        Deactivated += OnModelessFlyoutDeactivated;
    }

    private void OnModelessFlyoutDeactivated(object? sender, EventArgs e)
    {
        // Excel-style auto-dismiss: a click that lands anywhere outside the flyout — another cell,
        // the ribbon, a sheet tab, a different worksheet/window, or another application — deactivates
        // this borderless window, so close it. The check is deferred and re-tested so a transient
        // deactivation from opening a child control's popup (which immediately returns activation to
        // the flyout) does not dismiss it mid-interaction.
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (!IsActive)
                    Close();
            }),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CommitResult(AutoFilterDialogResult result)
    {
        Result = result;
        if (_useModelessFlyoutCommit)
        {
            ResultCommitted?.Invoke(this, result);
            Close();
            return;
        }

        DialogResult = true;
    }

    private void CancelResult()
    {
        if (_useModelessFlyoutCommit)
        {
            Close();
            return;
        }

        DialogResult = false;
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
