using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.PivotUI;

namespace FreeX.App.Host;

public partial class PivotFieldFilterDialog : Window
{
    private readonly ObservableCollection<PivotFilterItem> _items;
    private readonly ICollectionView _view;
    private readonly PivotFieldFilterState? _filterState;
    private readonly PivotFieldFilterDialogTab _initialTab;

    public PivotFieldFilterDialog(
        IEnumerable<string> items,
        IEnumerable<string>? selectedItems = null,
        bool canUseValueFilters = true,
        PivotFieldFilterState? filterState = null,
        PivotFieldFilterDialogTab initialTab = PivotFieldFilterDialogTab.SelectItems)
        : this(
            items.Select(item => new AutoFilterChecklistItem(item, item)),
            selectedItems,
            canUseValueFilters,
            filterState,
            initialTab)
    {
    }

    public PivotFieldFilterDialog(
        IEnumerable<AutoFilterChecklistItem> items,
        IEnumerable<string>? selectedItems = null,
        bool canUseValueFilters = true,
        PivotFieldFilterState? filterState = null,
        PivotFieldFilterDialogTab initialTab = PivotFieldFilterDialogTab.SelectItems)
    {
        _filterState = filterState;
        _initialTab = initialTab;
        var selected = selectedItems?.ToHashSet(StringComparer.CurrentCultureIgnoreCase) ?? [];
        var hasExplicitSelection = selected.Count > 0;
        _items = new ObservableCollection<PivotFilterItem>(
            items.DistinctBy(item => item.Value, StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(item => item.DisplayText, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new PivotFilterItem(
                    item.DisplayText,
                    item.Value,
                    !hasExplicitSelection || selected.Contains(item.Value))));

        InitializeComponent();
        FilterItemsList.ItemsSource = _items;
        ValueFilterButton.IsEnabled = canUseValueFilters;
        RemoveValueFilterButton.IsEnabled = canUseValueFilters && filterState?.HasValueFilter == true;
        ValueFilterUnavailableText.Visibility = canUseValueFilters ? Visibility.Collapsed : Visibility.Visible;
        _view = CollectionViewSource.GetDefaultView(FilterItemsList.ItemsSource);
        _view.Filter = FilterItem;
        ApplyFilterState();
        SelectInitialTab();
        UpdateSelectAllState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public IReadOnlyList<string> SelectedItems { get; private set; } = [];
    public PivotFieldFilterDialogAction RequestedAction { get; private set; } = PivotFieldFilterDialogAction.SelectItems;

    private bool FilterItem(object item) =>
        item is PivotFilterItem filterItem &&
        (string.IsNullOrWhiteSpace(FilterSearchBox.Text) ||
         filterItem.Caption.Contains(FilterSearchBox.Text.Trim(), StringComparison.CurrentCultureIgnoreCase));

    private void FilterSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view.Refresh();
        UpdateSelectAllState();
    }

    private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectAllCheckBox.IsChecked == true;
        foreach (var item in _items.Where(item => FilterItem(item)))
            item.IsChecked = selected;
        FilterItemsList.Items.Refresh();
        UpdateSelectAllState();
    }

    private void FilterItemCheckBox_Click(object sender, RoutedEventArgs e) => UpdateSelectAllState();

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedItems = _items
            .Where(item => item.IsChecked)
            .Select(item => item.Value)
            .ToList();
        RequestedAction = PivotFieldFilterDialogAction.SelectItems;
        DialogResult = true;
    }

    private void LabelFilterButton_Click(object sender, RoutedEventArgs e)
    {
        RequestedAction = PivotFieldFilterDialogAction.LabelFilter;
        DialogResult = true;
    }

    private void ValueFilterButton_Click(object sender, RoutedEventArgs e)
    {
        RequestedAction = PivotFieldFilterDialogAction.ValueFilter;
        DialogResult = true;
    }

    private void ClearItemFilterButton_Click(object sender, RoutedEventArgs e)
    {
        RequestedAction = PivotFieldFilterDialogAction.ClearItemFilter;
        DialogResult = true;
    }

    private void ClearFieldFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        RequestedAction = PivotFieldFilterDialogAction.ClearFieldFilters;
        DialogResult = true;
    }

    private void RemoveLabelFilterButton_Click(object sender, RoutedEventArgs e)
    {
        RequestedAction = PivotFieldFilterDialogAction.RemoveLabelFilter;
        DialogResult = true;
    }

    private void RemoveValueFilterButton_Click(object sender, RoutedEventArgs e)
    {
        RequestedAction = PivotFieldFilterDialogAction.RemoveValueFilter;
        DialogResult = true;
    }

    private void ApplyFilterState()
    {
        var state = _filterState;
        ItemFilterSummaryText.Text = state?.ItemSummary ?? UiText.Get("PivotFieldFilter_NoItemFilter");
        LabelFilterSummaryText.Text = state?.LabelSummary ?? UiText.Get("PivotFieldFilter_NoLabelFilter");
        ValueFilterSummaryText.Text = state?.ValueSummary ?? UiText.Get("PivotFieldFilter_NoValueFilter");

        ClearItemFilterButton.IsEnabled = state?.HasItemFilter == true;
        ClearFieldFiltersButton.IsEnabled = state?.HasAnyFilter == true;
        if (state is not null)
            ClearFieldFiltersButton.Content = PivotFieldFilterSummary.FormatClearFilterHeader(state);

        LabelFilterButton.Content = state?.HasLabelFilter == true
            ? "Edit Label Filter..."
            : "Add Label Filter...";
        RemoveLabelFilterButton.IsEnabled = state?.HasLabelFilter == true;

        ValueFilterButton.Content = state?.HasValueFilter == true
            ? "Edit Value Filter..."
            : "Add Value Filter...";
        RemoveValueFilterButton.IsEnabled = ValueFilterButton.IsEnabled && state?.HasValueFilter == true;
    }

    private void SelectInitialTab()
    {
        FilterTabs.SelectedItem = _initialTab switch
        {
            PivotFieldFilterDialogTab.LabelFilters => LabelFiltersTab,
            PivotFieldFilterDialogTab.ValueFilters => ValueFiltersTab,
            _ => SelectItemsTab
        };
    }

    private void UpdateSelectAllState()
    {
        var visible = _items.Where(item => FilterItem(item)).ToList();
        SelectAllCheckBox.IsChecked = visible.Count switch
        {
            0 => false,
            _ when visible.All(item => item.IsChecked) => true,
            _ when visible.All(item => !item.IsChecked) => false,
            _ => null
        };
    }

    private void FocusInitialKeyboardTarget()
    {
        if (ReferenceEquals(FilterTabs.SelectedItem, LabelFiltersTab))
        {
            LabelFilterButton.Focus();
            Keyboard.Focus(LabelFilterButton);
            return;
        }

        if (ReferenceEquals(FilterTabs.SelectedItem, ValueFiltersTab))
        {
            ValueFilterButton.Focus();
            Keyboard.Focus(ValueFilterButton);
            return;
        }

        FilterSearchBox.Focus();
        Keyboard.Focus(FilterSearchBox);
    }

    private sealed class PivotFilterItem(string caption, string value, bool isChecked)
    {
        public string Caption { get; } = caption;
        public string Value { get; } = value;
        public bool IsChecked { get; set; } = isChecked;
    }
}

public enum PivotFieldFilterDialogAction
{
    SelectItems,
    LabelFilter,
    ValueFilter,
    ClearItemFilter,
    ClearFieldFilters,
    RemoveLabelFilter,
    RemoveValueFilter
}

public enum PivotFieldFilterDialogTab
{
    SelectItems,
    LabelFilters,
    ValueFilters
}
