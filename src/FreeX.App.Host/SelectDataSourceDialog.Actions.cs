using System.Windows;
using System.Windows.Controls;

namespace FreeX.App.Host;

public sealed partial class SelectDataSourceDialog
{
    private void RefreshPreviewLists()
    {
        if (_seriesList is null || _axisLabelsList is null)
            return;

        var preview = InferPreviewEntries(_rangeBox.Text, _firstColumnCategoriesBox.IsChecked == true);
        _seriesList.ItemsSource = preview.Series
            .Select(series => UiText.Format("SelectDataSource_SeriesListItemFormat", series.Name, series.ValuesRangeText))
            .ToList();
        _axisLabelsList.ItemsSource = preview.Categories.Select(category => category.Label).ToList();
        SelectFirstItemWhenAvailable(_seriesList);
        SelectFirstItemWhenAvailable(_axisLabelsList);
        UpdateActionButtonState();
    }

    private void AddSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        var index = _seriesList.Items.Count + 1;
        _seriesList.ItemsSource = null;
        _seriesList.Items.Add(UiText.Format("SelectDataSource_NewSeriesListItem", index));
        _seriesList.SelectedIndex = _seriesList.Items.Count - 1;
        UpdateActionButtonState();
    }

    private void EditSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_seriesList.SelectedIndex < 0 && _seriesList.Items.Count > 0)
            _seriesList.SelectedIndex = 0;
    }

    private void RemoveSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_seriesList.SelectedIndex < 0)
            return;

        var items = _seriesList.Items.Cast<object>().Select(item => item.ToString() ?? "").ToList();
        var removedIndex = _seriesList.SelectedIndex;
        items.RemoveAt(_seriesList.SelectedIndex);
        _seriesList.ItemsSource = items;
        _seriesList.SelectedIndex = items.Count == 0 ? -1 : Math.Min(removedIndex, items.Count - 1);
        UpdateActionButtonState();
    }

    private void EditAxisLabelsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_axisLabelsList.Items.Count > 0)
            _axisLabelsList.SelectedIndex = 0;
    }

    private static void SelectFirstItemWhenAvailable(ListBox list)
    {
        list.SelectedIndex = list.Items.Count == 0 ? -1 : 0;
    }

    private void UpdateActionButtonState()
    {
        if (_editSeriesButton is not null)
            _editSeriesButton.IsEnabled = _seriesList.SelectedIndex >= 0;
        if (_removeSeriesButton is not null)
            _removeSeriesButton.IsEnabled = _seriesList.SelectedIndex >= 0;
        if (_editAxisLabelsButton is not null)
            _editAxisLabelsButton.IsEnabled = _axisLabelsList.SelectedIndex >= 0;
    }

    private static void HiddenEmptyCellsButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = sender is DependencyObject dependencyObject
            ? Window.GetWindow(dependencyObject)
            : null;
        MessageBox.Show(owner,
            UiText.Get("SelectDataSource_HiddenEmptyCellsMessage"),
            UiText.Get("SelectDataSource_HiddenEmptyCellsTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Information); // owner is dynamic (static handler); DialogMessageHelper requires a non-null Window
    }
}
