using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void RefreshSlicerTimelinePane()
    {
        if (SlicerTimelinePane is null)
            return;

        var slicers = _workbook.Slicers
            .Where(slicer => !string.IsNullOrWhiteSpace(slicer.Name))
            .Select(slicer => new SlicerPaneItem(
                slicer.Name,
                slicer.SourceFieldName ?? slicer.CacheName,
                BuildSlicerTiles(slicer),
                SlicerTimelinePlanner.HasActiveSlicerFilter(slicer)))
            .ToList();
        var timelines = _workbook.Timelines
            .Where(timeline => !string.IsNullOrWhiteSpace(timeline.Name))
            .Select(SlicerTimelinePlanner.BuildTimelineItem)
            .ToList();

        SlicerItemsControl.ItemsSource = slicers;
        TimelineItemsControl.ItemsSource = timelines;
        if (slicers.Count == 0 && timelines.Count == 0)
        {
            SlicerTimelinePane.Visibility = Visibility.Collapsed;
            _slicerTimelinePaneDismissed = false;
        }
        else if (!_slicerTimelinePaneDismissed)
            SlicerTimelinePane.Visibility = Visibility.Visible;
    }

    private IReadOnlyList<SlicerTileItem> BuildSlicerTiles(SlicerModel slicer)
    {
        return SlicerTimelinePlanner.BuildSlicerTiles(slicer, ReadSlicerSourceItems(slicer));
    }

    private IReadOnlyList<string> ReadSlicerSourceItems(SlicerModel slicer)
    {
        if (string.IsNullOrWhiteSpace(slicer.SourcePivotTableName) ||
            string.IsNullOrWhiteSpace(slicer.SourceFieldName))
        {
            return [];
        }

        foreach (var sheet in _workbook.Sheets)
        {
            PivotTableModel? pivotTable = null;
            foreach (var pivot in sheet.PivotTables)
            {
                if (!string.Equals(pivot.Name, slicer.SourcePivotTableName, StringComparison.OrdinalIgnoreCase))
                    continue;

                pivotTable = pivot;
                break;
            }

            if (pivotTable is null)
                continue;

            var headers = ReadPivotSourceHeaders(sheet, pivotTable);
            var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, slicer.SourceFieldName);
            return sourceIndex is null ? [] : ReadPivotFieldItems(sheet, pivotTable, sourceIndex.Value);
        }

        return [];
    }

    private void SlicerTimelinePaneCloseBtn_Click(object sender, RoutedEventArgs e)
    {
        _slicerTimelinePaneDismissed = true;
        SlicerTimelinePane.Visibility = Visibility.Collapsed;
    }

    private void SlicerTileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SlicerTileItem tile })
            return;

        SlicerModel? slicer = null;
        foreach (var item in _workbook.Slicers)
        {
            if (!string.Equals(item.Name, tile.SlicerName, StringComparison.OrdinalIgnoreCase))
                continue;

            slicer = item;
            break;
        }

        if (slicer is null)
            return;

        var allItems = ReadSlicerSourceItems(slicer).ToList();
        var selected = SlicerTimelinePlanner.ToggleSlicerSelection(allItems, slicer.SelectedItems, tile.Caption);

        if (!TryExecuteCommand(new SetSlicerSelectionCommand(slicer.Name, selected.ToList()), "Slicer"))
            return;

        UpdateViewport();
    }

    private void SlicerClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string slicerName })
            return;

        if (!TryExecuteCommand(new SetSlicerSelectionCommand(slicerName, []), "Slicer"))
            return;

        UpdateViewport();
    }

    private void TimelineApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TimelinePaneItem item })
            return;

        if (!TryExecuteCommand(
                new SetTimelineRangeCommand(
                    item.Name,
                    SlicerTimelinePlanner.NormalizeTimelineDateInput(item.SelectedStartDate),
                    SlicerTimelinePlanner.NormalizeTimelineDateInput(item.SelectedEndDate)),
                "Timeline"))
            return;

        UpdateViewport();
    }

    private void TimelineClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TimelinePaneItem item })
            return;

        if (!TryExecuteCommand(new SetTimelineRangeCommand(item.Name, null, null), "Timeline"))
            return;

        UpdateViewport();
    }

    // ── Native slicer / timeline click handlers (from GridView.TryHandleNativeSlicerTimelineClick) ──

    private void OnNativeSlicerClearFilterRequested(string slicerName)
    {
        if (!TryExecuteCommand(new SetSlicerSelectionCommand(slicerName, []), "Slicer"))
            return;

        UpdateViewport();
    }

    private void OnNativeSlicerTileToggleRequested(string slicerName, string caption)
    {
        SlicerModel? slicer = null;
        foreach (var item in _workbook.Slicers)
        {
            if (string.Equals(item.Name, slicerName, StringComparison.OrdinalIgnoreCase))
            {
                slicer = item;
                break;
            }
        }

        if (slicer is null)
            return;

        // P8/H45: GridView reports a plain click on an on-grid slicer tile with no modifier info
        // (NativeSlicerTileToggleRequested is Action<string,string>), so this path must apply Excel's
        // plain-click REPLACE semantics — the same behaviour Avalonia gets from
        // SlicerLayoutBuilder.Toggle(..., additive: false) — instead of the additive toggle used by
        // SlicerTimelinePlanner.ToggleSlicerSelection (which is for the Ctrl-click-aware slicer pane).
        // A plain click on a caption replaces the whole selection with just that item; a second plain
        // click on the lone already-selected item clears the filter back to "everything selected".
        var isSoleSelection = slicer.SelectedItems.Count == 1 &&
            string.Equals(slicer.SelectedItems[0], caption, StringComparison.CurrentCultureIgnoreCase);
        List<string> selected = isSoleSelection ? [] : [caption];

        if (!TryExecuteCommand(new SetSlicerSelectionCommand(slicerName, selected), "Slicer"))
            return;

        UpdateViewport();
    }

    private void OnNativeTimelineClearFilterRequested(string timelineName)
    {
        if (!TryExecuteCommand(new SetTimelineRangeCommand(timelineName, null, null), "Timeline"))
            return;

        UpdateViewport();
    }

    private void OnNativeTimelineGranularityToggleRequested(string timelineName)
    {
        TimelineModel? timeline = null;
        foreach (var item in _workbook.Timelines)
        {
            if (string.Equals(item.Name, timelineName, StringComparison.OrdinalIgnoreCase))
            {
                timeline = item;
                break;
            }
        }

        if (timeline is null)
            return;

        var nextLevel = SetTimelineGranularityCommand.CycleLevel(timeline.Level);
        if (!TryExecuteCommand(new SetTimelineGranularityCommand(timelineName, nextLevel), "Timeline"))
            return;

        UpdateViewport();
    }

    private void OnNativeTimelineRangeRequested(string timelineName, string? startDate, string? endDate)
    {
        if (!TryExecuteCommand(new SetTimelineRangeCommand(timelineName, startDate, endDate), "Timeline"))
            return;

        UpdateViewport();
    }
}
