using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

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
    string CriteriaText);

public sealed class AutoFilterDialog : Window
{
    private readonly List<AutoFilterDialogItem> _allItems;
    private readonly ObservableCollection<AutoFilterDialogItem> _items;
    private readonly TextBox _searchBox = new();
    private readonly TextBox _criteriaBox = new();
    private readonly ComboBox _criteriaSuggestionBox = new()
    {
        Visibility = Visibility.Collapsed,
        IsTextSearchEnabled = true
    };
    private readonly RadioButton _sortNone = new() { Content = "_No sort", IsChecked = true };
    private readonly RadioButton _sortAscending = new() { Content = "Sort _A to Z" };
    private readonly RadioButton _sortDescending = new() { Content = "Sort _Z to A" };

    public AutoFilterDialogResult Result { get; private set; }

    public AutoFilterDialog(IEnumerable<AutoFilterChecklistItem> items)
        : this(items.Select(item => new AutoFilterDialogItem(item.DisplayText, item.Value, true)))
    {
    }

    public AutoFilterDialog(AutoFilterMenuPlan menuPlan)
        : this(CreateDialogItems(menuPlan))
    {
        Title = $"AutoFilter - {menuPlan.HeaderText}";
        var suggestions = GetCriteriaSuggestions(menuPlan);
        if (suggestions.Count > 0)
        {
            _criteriaSuggestionBox.ItemsSource = suggestions;
            _criteriaSuggestionBox.Visibility = Visibility.Visible;
            _criteriaSuggestionBox.ToolTip = "Filter criteria";
            _criteriaBox.ToolTip = $"Criteria suggestions: {string.Join(", ", suggestions)}";
        }
    }

    public AutoFilterDialog(IEnumerable<AutoFilterDialogItem> items)
    {
        _allItems = items.ToList();
        _items = new ObservableCollection<AutoFilterDialogItem>(_allItems);
        Result = BuildResult(AutoFilterSortDirection.None, _allItems, string.Empty, string.Empty);

        Title = "AutoFilter";
        Width = 360;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel { Margin = new Thickness(16) };
        var stack = new StackPanel();
        root.Children.Add(stack);

        stack.Children.Add(_sortNone);
        stack.Children.Add(_sortAscending);
        stack.Children.Add(_sortDescending);

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
        selectAll.Click += (_, _) => ReplaceAllItems(SelectAll(_allItems));
        var clearAll = new Button { Content = "_Clear All", Width = 88 };
        clearAll.Click += (_, _) => ReplaceAllItems(ClearAll(_allItems));
        selectionRow.Children.Add(selectAll);
        selectionRow.Children.Add(clearAll);
        stack.Children.Add(selectionRow);

        stack.Children.Add(new Label { Content = "_Criteria text", Target = _criteriaBox, Padding = new Thickness(0) });
        _criteriaSuggestionBox.Margin = new Thickness(0, 4, 0, 4);
        _criteriaSuggestionBox.SelectionChanged += (_, _) =>
        {
            if (_criteriaSuggestionBox.SelectedItem is string suggestion)
                _criteriaBox.Text = suggestion;
        };
        stack.Children.Add(_criteriaSuggestionBox);

        _criteriaBox.Margin = new Thickness(0, 4, 0, 12);
        stack.Children.Add(_criteriaBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var ok = new Button { Content = "_OK", IsDefault = true, Width = 76, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) =>
        {
            Result = BuildResult(GetSortDirection(), _allItems, _searchBox.Text, _criteriaBox.Text);
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

    public static AutoFilterDialogResult BuildResult(
        AutoFilterSortDirection sortDirection,
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText,
        string? criteriaText)
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
            normalizedCriteria);
    }

    public static IReadOnlyList<string> GetCriteriaSuggestions(AutoFilterMenuPlan menuPlan) =>
        menuPlan.Entries
            .FirstOrDefault(entry => entry.Kind == AutoFilterMenuEntryKind.FilterFamily)
            ?.CriteriaSuggestions
            .Where(suggestion => !string.IsNullOrWhiteSpace(suggestion))
            .ToList() ?? [];

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
