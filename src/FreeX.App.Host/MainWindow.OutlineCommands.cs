using System.Windows;
using FreeX.App.UI;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void GroupRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteWorksheetLayout(_session.GroupSelectedOutline, "Group"))
            return;
        UpdateViewport();
    }

    private void GroupRowsMenuItem_Click(object sender, RoutedEventArgs e) => GroupRowsBtn_Click(sender, e);

    private void UngroupRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteWorksheetLayout(_session.UngroupSelectedOutline, "Ungroup"))
            return;
        UpdateViewport();
    }

    private void UngroupRowsMenuItem_Click(object sender, RoutedEventArgs e) => UngroupRowsBtn_Click(sender, e);

    private void ClearOutlineMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteWorksheetLayout(_session.ClearActiveWorksheetOutline, "Clear Outline"))
            return;
        UpdateViewport();
    }

    private void CollapseGroupBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteWorksheetLayout(
                () => _session.SetSelectedOutlineGroupsCollapsed(collapse: true),
                "Collapse Group"))
            return;
        UpdateViewport();
    }

    private void ExpandGroupBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteWorksheetLayout(
                () => _session.SetSelectedOutlineGroupsCollapsed(collapse: false),
                "Expand Group"))
            return;
        UpdateViewport();
    }

    private void OnOutlineGroupToggleRequested(GridOutlineGroupToggleRequest request)
    {
        var label = request.Collapse ? "Collapse Group" : "Expand Group";
        var axis = request.Axis == GridOutlineGroupAxis.Columns
            ? OutlineGroupingAxis.Columns
            : OutlineGroupingAxis.Rows;
        if (!TryExecuteWorksheetLayout(
                () => _session.SetOutlineGroupCollapsed(
                    axis,
                    request.Start,
                    request.End,
                    request.Level,
                    request.Collapse),
                label))
            return;
        UpdateViewport();
    }
}
