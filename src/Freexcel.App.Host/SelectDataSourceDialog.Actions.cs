using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public sealed partial class SelectDataSourceDialog
{
    private void RefreshPreviewLists()
    {
        if (_seriesList is null || _axisLabelsList is null)
            return;

        var preview = InferPreviewEntries(_rangeBox.Text, _firstColumnCategoriesBox.IsChecked == true);
        _seriesList.ItemsSource = preview.Series.Select(series => $"{series.Name}    {series.ValuesRangeText}").ToList();
        _axisLabelsList.ItemsSource = preview.Categories.Select(category => category.Label).ToList();
    }

    private void AddSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        var index = _seriesList.Items.Count + 1;
        _seriesList.ItemsSource = null;
        _seriesList.Items.Add($"Series {index}    <select range>");
        _seriesList.SelectedIndex = _seriesList.Items.Count - 1;
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
        items.RemoveAt(_seriesList.SelectedIndex);
        _seriesList.ItemsSource = items;
    }

    private void EditAxisLabelsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_axisLabelsList.Items.Count > 0)
            _axisLabelsList.SelectedIndex = 0;
    }

    private static void HiddenEmptyCellsButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = sender is DependencyObject dependencyObject
            ? Window.GetWindow(dependencyObject)
            : null;
        MessageBox.Show(owner,
            "Hidden rows and columns are not plotted. Empty cells are shown as gaps.",
            "Hidden and Empty Cell Settings",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
