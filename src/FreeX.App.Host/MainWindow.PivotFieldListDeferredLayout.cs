using System.Windows;
using System.Windows.Controls;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private PivotFieldLayoutDraft? _pendingPivotLayout;
    private IReadOnlyList<PivotAvailableFieldItemModel> _pivotFieldListAvailableItems = [];

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

        PivotAvailableFieldsList.ItemsSource = PivotFieldListPaneBuilder.FilterAvailableFields(
            _pivotFieldListAvailableItems,
            PivotFieldListSearchBox?.Text);
    }

    private PivotFieldLayoutDraft? GetDisplayedPivotLayout(PivotTableModel pivotTable)
    {
        return _pendingPivotLayout is { } pending &&
               string.Equals(pending.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase)
            ? pending
            : null;
    }

    private PivotFieldLayoutDraft GetDisplayedOrCurrentPivotLayout(PivotTableModel pivotTable)
    {
        return GetDisplayedPivotLayout(pivotTable) ?? new PivotFieldLayoutDraft(
            pivotTable.Name,
            PivotFieldLayoutPlanner.Capture(pivotTable));
    }
}
