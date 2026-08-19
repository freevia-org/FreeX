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

    /// <summary>
    /// Handles a click on a numbered "Show Outline Level N" gutter button (the boxed "1"/"2"/...
    /// boxes above the outline brackets). Matches Excel and the Avalonia shell's
    /// <c>MainWindow.OutlineGrid.ShowRowOutlineLevel</c>/<c>ShowColumnOutlineLevel</c>: shows every
    /// summary row/column through the clicked depth and re-collapses anything nested deeper,
    /// sheet-wide, via the shared <see cref="FreeX.App.Services.WorkbookSession.ShowRowOutlineLevel"/>/
    /// <see cref="FreeX.App.Services.WorkbookSession.ShowColumnOutlineLevel"/> sequence.
    /// </summary>
    private void OnOutlineLevelButtonRequested(GridOutlineLevelButtonRequest request)
    {
        if (!TryExecuteWorksheetLayout(
                () => request.Axis == GridOutlineGroupAxis.Columns
                    ? _session.ShowColumnOutlineLevel(request.Level)
                    : _session.ShowRowOutlineLevel(request.Level),
                "Show Outline Level"))
            return;
        UpdateViewport();
    }
}
