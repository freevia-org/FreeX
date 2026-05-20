using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private PendingPivotLayout? _pendingPivotLayout;
    private IReadOnlyList<PivotFieldListItem> _pivotFieldListAvailableItems = [];

    private void PivotFieldListDeferLayoutCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (PivotFieldListDeferLayoutCheckBox.IsChecked == false &&
            _pendingPivotLayout is not null)
        {
            PivotFieldListUpdateBtn_Click(sender, e);
        }
    }

    private void PivotFieldListUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingPivotLayout is not { } pending ||
            !TryGetActivePivotTable(out _, out var pivotTable) ||
            !string.Equals(pending.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase))
        {
            _pendingPivotLayout = null;
            RefreshPivotFieldListPane();
            return;
        }

        ApplyPivotFieldListLayout(
            pivotTable,
            pending.RowFields,
            pending.ColumnFields,
            pending.PageFields,
            pending.DataFields,
            forceApply: true);
    }

    private void PivotFieldListSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyPivotAvailableFieldFilter();
    }

    private void ApplyPivotAvailableFieldFilter()
    {
        if (PivotAvailableFieldsList is null)
            return;

        PivotAvailableFieldsList.ItemsSource = PivotUiPlanner.FilterPivotFieldListItems(
            _pivotFieldListAvailableItems,
            PivotFieldListSearchBox?.Text);
    }

    private PendingPivotLayout? GetDisplayedPivotLayout(PivotTableModel pivotTable)
    {
        return _pendingPivotLayout is { } pending &&
               string.Equals(pending.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase)
            ? pending
            : null;
    }

    private sealed record PendingPivotLayout(
        string PivotTableName,
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        IReadOnlyList<PivotDataFieldModel> DataFields);
}
