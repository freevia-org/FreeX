using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Freexcel.App.Host;

public partial class PivotFieldFilterDialog : Window
{
    private readonly ObservableCollection<PivotFilterItem> _items;
    private readonly ICollectionView _view;

    public PivotFieldFilterDialog(IEnumerable<string> items, IEnumerable<string>? selectedItems = null, bool canUseValueFilters = true)
        : this(
            items.Select(item => new AutoFilterChecklistItem(item, item)),
            selectedItems,
            canUseValueFilters)
    {
    }

    public PivotFieldFilterDialog(
        IEnumerable<AutoFilterChecklistItem> items,
        IEnumerable<string>? selectedItems = null,
        bool canUseValueFilters = true)
    {
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
        ValueFilterUnavailableText.Visibility = canUseValueFilters ? Visibility.Collapsed : Visibility.Visible;
        _view = CollectionViewSource.GetDefaultView(FilterItemsList.ItemsSource);
        _view.Filter = FilterItem;
        UpdateSelectAllState();
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

    private void UpdateSelectAllState()
    {
        var visible = _items.Where(item => FilterItem(item)).ToList();
        SelectAllCheckBox.IsChecked = visible.Count > 0 && visible.All(item => item.IsChecked);
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
    ValueFilter
}
