using System.Windows;
using System.Windows.Controls;

namespace FreeX.App.Host;

public sealed partial class SelectDataSourceDialog
{
    private void RefreshPreviewLists()
    {
        if (_seriesList is null || _axisLabelsList is null)
            return;

        var preview = InferPreviewEntries(
            _rangeBox.Text,
            _firstColumnCategoriesBox.IsChecked == true,
            _switchRowColumnBox.IsChecked == true);
        _seriesListItems = preview.Series
            .Select(series => UiText.Format("SelectDataSource_SeriesListItemFormat", series.Name, series.ValuesRangeText))
            .ToList();
        // A fresh preview (range/category-flag/switch-row-column edit) re-derives the series list
        // from scratch, so any not-yet-applied Remove Series clicks no longer refer to a meaningful
        // position -- reset the pending-removal queue along with the visible list (R92-app-chart-
        // data-edit-5-1). Whatever the FINAL range/flags are when OK is clicked is what
        // RemoveChartSeriesCommand's indexes will be interpreted against (see
        // MainWindow.ChartCommands.cs's SelectChartDataSourceBtn_Click for the apply order).
        _pendingSeriesRemovals.Clear();
        _realSeriesCount = _seriesListItems.Count;
        _seriesList.ItemsSource = _seriesListItems;
        _axisLabelsList.ItemsSource = preview.Categories.Select(category => category.Label).ToList();
        SelectFirstItemWhenAvailable(_seriesList);
        SelectFirstItemWhenAvailable(_axisLabelsList);
        UpdateActionButtonState();
    }

    private void AddSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        // Add Series remains decorative (no backing chart command -- FreeX charts have no
        // independent per-series range storage a new series could point at; see
        // RemoveChartSeriesCommand's class docs for why only Remove is wired to a real command in
        // this round). Appending to _seriesListItems (rather than the old ItemsSource=null dance)
        // at least stops it from silently wiping the other, real entries out of the visible list.
        var items = _seriesListItems.ToList();
        items.Add(UiText.Format("SelectDataSource_NewSeriesListItem", items.Count + 1));
        _seriesListItems = items;
        _seriesList.ItemsSource = null;
        _seriesList.ItemsSource = _seriesListItems;
        _seriesList.SelectedIndex = _seriesListItems.Count - 1;
        UpdateActionButtonState();
    }

    private void EditSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_seriesList.SelectedIndex < 0 && _seriesList.Items.Count > 0)
            _seriesList.SelectedIndex = 0;
    }

    private void RemoveSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        var index = _seriesList.SelectedIndex;
        if (index < 0)
            return;

        // Only a real (chart-backed) row -- index < _realSeriesCount -- queues an actual
        // RemoveChartSeriesCommand; an Add-Series placeholder row (appended after all real rows)
        // is never backed by chart data, so removing it is just a list edit (R92-app-chart-data-
        // edit-5-1). Recorded in click order: MainWindow replays each pending index in that same
        // order against the live chart, which reproduces exactly what this ListBox shows, the same
        // way it already re-numbers its own remaining rows after each click below.
        if (index < _realSeriesCount)
        {
            _pendingSeriesRemovals.Add(index);
            _realSeriesCount--;
        }

        var items = _seriesListItems.ToList();
        items.RemoveAt(index);
        _seriesListItems = items;
        _seriesList.ItemsSource = null;
        _seriesList.ItemsSource = _seriesListItems;
        _seriesList.SelectedIndex = _seriesListItems.Count == 0 ? -1 : Math.Min(index, _seriesListItems.Count - 1);
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

    private void HiddenEmptyCellsButton_Click(object sender, RoutedEventArgs e)
    {
        // R92-app-chart-data-edit-5-3: was a static info-only MessageBox with no controls; now a
        // real sub-dialog whose choice MainWindow.ChartCommands.cs applies via
        // ConfigureChartHiddenEmptyCellsCommand (works for any chart, not just a PivotChart).
        var owner = sender is DependencyObject dependencyObject
            ? Window.GetWindow(dependencyObject)
            : this;
        var dialog = new HiddenEmptyCellSettingsDialog(_blankDisplayMode, _showDataInHiddenRowsAndColumns) { Owner = owner };
        if (dialog.ShowDialog() != true)
            return;

        _blankDisplayMode = dialog.Result.BlankDisplayMode;
        _showDataInHiddenRowsAndColumns = dialog.Result.ShowDataInHiddenRowsAndColumns;
    }
}
