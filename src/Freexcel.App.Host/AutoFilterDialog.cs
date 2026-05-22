using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum AutoFilterSortDirection
{
    None,
    Ascending,
    Descending
}

public sealed record AutoFilterDialogItem(string DisplayText, string Value, bool IsSelected)
{
    public bool IsSelected { get; set; } = IsSelected;
}

public sealed record AutoFilterDialogResult(
    AutoFilterSortDirection SortDirection,
    IReadOnlyList<string> SelectedValues,
    string SearchText,
    string CriteriaText,
    AutoFilterColorFilter? ColorFilter = null);

public sealed record AutoFilterColorFilter(AutoFilterColorFilterKind Kind, CellColor? Color);

public sealed record AutoFilterCriteriaOption(string Label, string CriteriaPrefix, bool RequiresValue = true);

public sealed class AutoFilterDialog : Window
{
    private readonly List<AutoFilterDialogItem> _allItems;
    private readonly ObservableCollection<AutoFilterDialogItem> _items;
    private readonly TextBox _searchBox = new();
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
    private readonly ComboBox _criteriaConnectorBox = new()
    {
        Visibility = Visibility.Collapsed,
        ItemsSource = new[] { "And", "Or" },
        SelectedIndex = 0
    };
    private readonly ComboBox _criteriaOperatorBox2 = new()
    {
        Visibility = Visibility.Collapsed,
        IsTextSearchEnabled = true,
        DisplayMemberPath = nameof(AutoFilterCriteriaOption.Label)
    };
    private readonly TextBox _criteriaValueBox2 = new() { Visibility = Visibility.Collapsed };
    private readonly RadioButton _sortNone = new() { Content = "_No sort", IsChecked = true };
    private readonly RadioButton _sortAscending = new() { Content = "Sort _A to Z" };
    private readonly RadioButton _sortDescending = new() { Content = "Sort _Z to A" };
    private readonly Button _clearFilterButton = new() { Content = "_Clear Filter From", Margin = new Thickness(0, 8, 0, 0) };
    private readonly GroupBox _filterByColorGroup = new() { Header = "Filter by Color", Visibility = Visibility.Collapsed };
    private readonly StackPanel _filterByColorPanel = new();
    private readonly Button _textFiltersButton = new() { Content = "_Text Filters", Visibility = Visibility.Collapsed };
    private readonly Button _numberFiltersButton = new() { Content = "_Number Filters", Visibility = Visibility.Collapsed };
    private readonly Button _dateFiltersButton = new() { Content = "_Date Filters", Visibility = Visibility.Collapsed };
    private readonly GroupBox _customFilterGroup = new() { Header = "Custom filter", Visibility = Visibility.Collapsed };
    private readonly Label _criteriaSuggestionLabel = new()
    {
        Content = "Criteria _template",
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
        Title = $"AutoFilter - {menuPlan.HeaderText}";
        _clearFilterButton.Content = $"_Clear Filter From \"{menuPlan.HeaderText}\"";
        ShowFilterFamilyButton(menuPlan.FilterKind);
        var criteriaSuggestions = GetCriteriaSuggestions(menuPlan);
        if (criteriaSuggestions.Count > 0)
        {
            _criteriaSuggestionBox.ItemsSource = criteriaSuggestions;
            _criteriaSuggestionBox.Visibility = Visibility.Visible;
            _criteriaSuggestionBox.ToolTip = "Excel filter criteria templates";
            _criteriaSuggestionLabel.Visibility = Visibility.Visible;
        }

        var criteriaOptions = GetCriteriaOptions(menuPlan.FilterKind);
        if (criteriaOptions.Count > 0)
        {
            _criteriaOperatorBox.ItemsSource = criteriaOptions;
            _criteriaOperatorBox.Visibility = Visibility.Visible;
            _criteriaOperatorBox.SelectedIndex = 0;
            _criteriaOperatorBox.ToolTip = $"{GetFilterFamilyHeader(menuPlan.FilterKind)} operator";
            _criteriaValueBox.Visibility = Visibility.Visible;
            _criteriaValueBox.ToolTip = "Value for the selected typed filter";
            _criteriaConnectorBox.Visibility = Visibility.Visible;
            _criteriaOperatorBox2.ItemsSource = criteriaOptions;
            _criteriaOperatorBox2.Visibility = Visibility.Visible;
            _criteriaOperatorBox2.SelectedIndex = 0;
            _criteriaOperatorBox2.ToolTip = $"Second {GetFilterFamilyHeader(menuPlan.FilterKind)} operator";
            _criteriaValueBox2.Visibility = Visibility.Visible;
            _criteriaValueBox2.ToolTip = "Value for the second typed filter";
            _criteriaBox.ToolTip = "Generated criterion that will be applied.";
            _customFilterGroup.Visibility = Visibility.Visible;
        }

        var colorOptions = menuPlan.ColorOptions ?? [];
        if (colorOptions.Count > 0)
            PopulateColorChoices(colorOptions);
    }

    public AutoFilterDialog(IEnumerable<AutoFilterDialogItem> items)
    {
        _allItems = items.ToList();
        _items = new ObservableCollection<AutoFilterDialogItem>(_allItems);
        Result = BuildResult(AutoFilterSortDirection.None, _allItems, string.Empty, string.Empty);

        Title = "AutoFilter";
        Width = 360;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel { Margin = new Thickness(16) };
        var stack = new StackPanel();
        root.Children.Add(stack);

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
        };
        stack.Children.Add(_clearFilterButton);
        _filterByColorGroup.Content = _filterByColorPanel;
        _filterByColorGroup.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(_filterByColorGroup);
        foreach (var filterButton in new[] { _textFiltersButton, _numberFiltersButton, _dateFiltersButton })
        {
            filterButton.Margin = new Thickness(0, 8, 0, 0);
            filterButton.Click += (_, _) =>
            {
                _criteriaOperatorBox.Focus();
                UpdateCriteriaTextFromTypedControls();
            };
            stack.Children.Add(filterButton);
        }

        AddFilterMenuSeparator(stack);
        stack.Children.Add(new Label { Content = "_Search", Target = _searchBox, Padding = new Thickness(0), Margin = new Thickness(0, 12, 0, 2) });
        _searchBox.Margin = new Thickness(0, 0, 0, 8);
        _searchBox.ToolTip = "Search";
        _searchBox.TextChanged += (_, _) => ReplaceItems(FilterItems(_allItems, _searchBox.Text));
        stack.Children.Add(_searchBox);

        var list = new ListBox
        {
            ItemsSource = _items,
            Height = 180,
            Margin = new Thickness(0, 0, 0, 8)
        };
        list.ItemTemplate = CreateItemTemplate();
        stack.Children.Add(list);

        var selectionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var selectAll = new Button { Content = "_Select All", Width = 88, Margin = new Thickness(0, 0, 8, 0) };
        selectAll.Click += (_, _) => ReplaceAllItems(SetSelectionForSearch(_allItems, _searchBox.Text, isSelected: true));
        var clearAll = new Button { Content = "_Clear All", Width = 88 };
        clearAll.Click += (_, _) => ReplaceAllItems(SetSelectionForSearch(_allItems, _searchBox.Text, isSelected: false));
        selectionRow.Children.Add(selectAll);
        selectionRow.Children.Add(clearAll);
        stack.Children.Add(selectionRow);
        AddFilterMenuSeparator(stack);

        var customFilterPanel = new StackPanel();
        _customFilterGroup.Content = customFilterPanel;
        stack.Children.Add(_customFilterGroup);

        customFilterPanel.Children.Add(new Label { Content = "Show rows where:", Padding = new Thickness(0) });
        customFilterPanel.Children.Add(new Label { Content = "Filter _operator", Target = _criteriaOperatorBox, Padding = new Thickness(0) });
        _criteriaOperatorBox.Margin = new Thickness(0, 4, 0, 4);
        _criteriaOperatorBox.SelectionChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        customFilterPanel.Children.Add(_criteriaOperatorBox);

        customFilterPanel.Children.Add(new Label { Content = "Filter _value", Target = _criteriaValueBox, Padding = new Thickness(0) });
        _criteriaValueBox.Margin = new Thickness(0, 4, 0, 4);
        _criteriaValueBox.TextChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        customFilterPanel.Children.Add(_criteriaValueBox);

        customFilterPanel.Children.Add(new Label { Content = "_And / Or", Target = _criteriaConnectorBox, Padding = new Thickness(0) });
        _criteriaConnectorBox.Margin = new Thickness(0, 4, 0, 4);
        _criteriaConnectorBox.SelectionChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        customFilterPanel.Children.Add(_criteriaConnectorBox);

        customFilterPanel.Children.Add(new Label { Content = "Second o_perator", Target = _criteriaOperatorBox2, Padding = new Thickness(0) });
        _criteriaOperatorBox2.Margin = new Thickness(0, 4, 0, 4);
        _criteriaOperatorBox2.SelectionChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        customFilterPanel.Children.Add(_criteriaOperatorBox2);

        customFilterPanel.Children.Add(new Label { Content = "Second va_lue", Target = _criteriaValueBox2, Padding = new Thickness(0) });
        _criteriaValueBox2.Margin = new Thickness(0, 4, 0, 4);
        _criteriaValueBox2.TextChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        customFilterPanel.Children.Add(_criteriaValueBox2);

        customFilterPanel.Children.Add(new Label { Content = "_Criteria text", Target = _criteriaBox, Padding = new Thickness(0) });

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
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        var ok = new Button { Content = "_OK", IsDefault = true, Width = 76, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) =>
        {
            Result = BuildResult(GetSortDirection(), _allItems, _searchBox.Text, _criteriaBox.Text, _selectedColorFilter);
            DialogResult = true;
        };
        var cancel = new Button { Content = "_Cancel", IsCancel = true, Width = 76 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        Content = root;
    }

    public static IReadOnlyList<AutoFilterDialogItem> FilterItems(
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText)
    {
        var needle = searchText?.Trim();
        if (string.IsNullOrEmpty(needle))
            return items.ToList();

        return items
            .Where(item =>
                item.DisplayText.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                item.Value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static IReadOnlyList<AutoFilterDialogItem> SelectAll(IEnumerable<AutoFilterDialogItem> items)
    {
        return items.Select(item => item with { IsSelected = true }).ToList();
    }

    public static IReadOnlyList<AutoFilterDialogItem> ClearAll(IEnumerable<AutoFilterDialogItem> items)
    {
        return items.Select(item => item with { IsSelected = false }).ToList();
    }

    public static IReadOnlyList<AutoFilterDialogItem> SetSelectionForSearch(
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText,
        bool isSelected)
    {
        var visibleValues = FilterItems(items, searchText)
            .Select(item => item.Value)
            .ToHashSet(StringComparer.Ordinal);

        return items
            .Select(item => visibleValues.Contains(item.Value)
                ? item with { IsSelected = isSelected }
                : item)
            .ToList();
    }

    public static string GetFilterFamilyHeader(AutoFilterMenuFilterKind filterKind) =>
        filterKind switch
        {
            AutoFilterMenuFilterKind.Number => "Number Filters",
            AutoFilterMenuFilterKind.Date => "Date Filters",
            _ => "Text Filters"
        };

    public static AutoFilterDialogResult BuildResult(
        AutoFilterSortDirection sortDirection,
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText,
        string? criteriaText,
        AutoFilterColorFilter? colorFilter = null)
    {
        var selectedValues = items
            .Where(item => item.IsSelected)
            .Select(item => item.Value)
            .ToList();
        var normalizedCriteria = string.IsNullOrWhiteSpace(criteriaText)
            ? string.Join(", ", selectedValues)
            : criteriaText.Trim();

        return new AutoFilterDialogResult(
            sortDirection,
            selectedValues,
            searchText?.Trim() ?? string.Empty,
            normalizedCriteria,
            colorFilter);
    }

    public static IReadOnlyList<string> GetCriteriaSuggestions(AutoFilterMenuPlan menuPlan) =>
        menuPlan.Entries
            .FirstOrDefault(entry => entry.Kind == AutoFilterMenuEntryKind.FilterFamily)
            ?.CriteriaSuggestions
            .Where(suggestion => !string.IsNullOrWhiteSpace(suggestion))
            .ToList() ?? [];

    public static IReadOnlyList<AutoFilterCriteriaOption> GetCriteriaOptions(AutoFilterMenuFilterKind filterKind) =>
        filterKind switch
        {
            AutoFilterMenuFilterKind.Number =>
            [
                new("Equals", "="),
                new("Does Not Equal", "<>"),
                new("Greater Than", ">"),
                new("Greater Than Or Equal To", ">="),
                new("Less Than", "<"),
                new("Less Than Or Equal To", "<="),
                new("Between", "between:"),
                new("Top 10", "top:"),
                new("Bottom 10", "bottom:"),
                new("Top 10 Percent", "toppercent:"),
                new("Bottom 10 Percent", "bottompercent:"),
                new("Above Average", "above average", RequiresValue: false),
                new("Below Average", "below average", RequiresValue: false),
                new("Blanks", "blank", RequiresValue: false),
                new("Non-Blanks", "nonblank", RequiresValue: false)
            ],
            AutoFilterMenuFilterKind.Date =>
            [
                new("Equals", "date="),
                new("Does Not Equal", "date<>"),
                new("After", "date>"),
                new("On Or After", "date>="),
                new("Before", "date<"),
                new("On Or Before", "date<="),
                new("Between", "datebetween:"),
                new("Blanks", "blank", RequiresValue: false),
                new("Non-Blanks", "nonblank", RequiresValue: false)
            ],
            _ =>
            [
                new("Equals", "text="),
                new("Does Not Equal", "text<>"),
                new("Contains", "contains:"),
                new("Does Not Contain", "notcontains:"),
                new("Begins With", "begins:"),
                new("Ends With", "ends:"),
                new("Blanks", "blank", RequiresValue: false),
                new("Non-Blanks", "nonblank", RequiresValue: false)
            ]
        };

    public static string BuildCriteriaText(AutoFilterCriteriaOption option, string? value)
    {
        if (!option.RequiresValue)
            return option.CriteriaPrefix;

        return $"{option.CriteriaPrefix}{value?.Trim() ?? string.Empty}";
    }

    public static string BuildCompositeCriteriaText(string? firstCriteria, string? connector, string? secondCriteria)
    {
        var first = firstCriteria?.Trim() ?? string.Empty;
        var second = secondCriteria?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(first))
            return second;

        if (string.IsNullOrWhiteSpace(second))
            return first;

        var prefix = string.Equals(connector, "Or", StringComparison.OrdinalIgnoreCase)
            ? "or"
            : "and";
        return $"{prefix}:{first}|{second}";
    }

    public static bool HasFilterByColorEntry(AutoFilterMenuPlan menuPlan) =>
        menuPlan.Entries.Any(entry => entry.Kind == AutoFilterMenuEntryKind.FilterByColor);

    private static void AddFilterMenuSeparator(Panel stack)
    {
        stack.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
    }

    private void ShowFilterFamilyButton(AutoFilterMenuFilterKind filterKind)
    {
        _textFiltersButton.Visibility = filterKind == AutoFilterMenuFilterKind.Text
            ? Visibility.Visible
            : Visibility.Collapsed;
        _numberFiltersButton.Visibility = filterKind == AutoFilterMenuFilterKind.Number
            ? Visibility.Visible
            : Visibility.Collapsed;
        _dateFiltersButton.Visibility = filterKind == AutoFilterMenuFilterKind.Date
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static IEnumerable<AutoFilterDialogItem> CreateDialogItems(AutoFilterMenuPlan menuPlan) =>
        menuPlan.Entries
            .Where(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem)
            .Select(entry => new AutoFilterDialogItem(entry.Header, entry.Value, true));

    private void ReplaceItems(IEnumerable<AutoFilterDialogItem> items)
    {
        _items.Clear();
        foreach (var item in items)
            _items.Add(item);
    }

    private void ReplaceAllItems(IEnumerable<AutoFilterDialogItem> items)
    {
        _allItems.Clear();
        _allItems.AddRange(items);
        ReplaceItems(FilterItems(_allItems, _searchBox.Text));
    }

    private AutoFilterSortDirection GetSortDirection()
    {
        if (_sortAscending.IsChecked == true)
            return AutoFilterSortDirection.Ascending;

        return _sortDescending.IsChecked == true
            ? AutoFilterSortDirection.Descending
            : AutoFilterSortDirection.None;
    }

    private void UpdateCriteriaTextFromTypedControls()
    {
        if (_criteriaOperatorBox.SelectedItem is not AutoFilterCriteriaOption option)
            return;

        var firstCriteria = BuildCriteriaText(option, _criteriaValueBox.Text);
        var secondCriteria = _criteriaOperatorBox2.SelectedItem is AutoFilterCriteriaOption option2 &&
            (!option2.RequiresValue || !string.IsNullOrWhiteSpace(_criteriaValueBox2.Text))
                ? BuildCriteriaText(option2, _criteriaValueBox2.Text)
                : string.Empty;
        _criteriaBox.Text = BuildCompositeCriteriaText(
            firstCriteria,
            _criteriaConnectorBox.SelectedItem as string,
            secondCriteria);
        _criteriaValueBox.IsEnabled = option.RequiresValue;
        if (_criteriaOperatorBox2.SelectedItem is AutoFilterCriteriaOption secondOption)
            _criteriaValueBox2.IsEnabled = secondOption.RequiresValue;
    }

    private void PopulateColorChoices(IReadOnlyList<AutoFilterColorOption> colorOptions)
    {
        _filterByColorPanel.Children.Clear();
        foreach (var section in colorOptions.GroupBy(option => option.Kind == AutoFilterColorFilterKind.FontColor ? "Font Color" : "Cell Color"))
        {
            _filterByColorPanel.Children.Add(new TextBlock
            {
                Text = section.Key,
                Margin = new Thickness(0, _filterByColorPanel.Children.Count == 0 ? 0 : 8, 0, 4)
            });

            var swatches = new WrapPanel();
            foreach (var option in section)
                swatches.Children.Add(CreateColorChoiceButton(option));
            _filterByColorPanel.Children.Add(swatches);
        }

        _filterByColorGroup.Visibility = Visibility.Visible;
    }

    private Button CreateColorChoiceButton(AutoFilterColorOption option)
    {
        var colorFilter = new AutoFilterColorFilter(option.Kind, option.Color);
        var button = new Button
        {
            Width = 92,
            Height = 24,
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = option.Label
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(CreateColorSwatch(option));
        content.Children.Add(new TextBlock
        {
            Text = option.Kind == AutoFilterColorFilterKind.NoFill ? "No Fill" : ColorInputParser.FormatHexColor(option.Color!.Value),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        });
        button.Content = content;
        button.Click += (_, _) => _selectedColorFilter = colorFilter;
        return button;
    }

    private static Rectangle CreateColorSwatch(AutoFilterColorOption option)
    {
        var fill = option.Color is { } color
            ? new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B))
            : Brushes.White;
        return new Rectangle
        {
            Width = 14,
            Height = 14,
            Fill = fill,
            Stroke = Brushes.Gray,
            StrokeThickness = 1,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
    }

    private static DataTemplate CreateItemTemplate()
    {
        var checkBox = new FrameworkElementFactory(typeof(CheckBox));
        checkBox.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding(nameof(AutoFilterDialogItem.DisplayText)));
        checkBox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(nameof(AutoFilterDialogItem.IsSelected))
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        return new DataTemplate { VisualTree = checkBox };
    }
}
