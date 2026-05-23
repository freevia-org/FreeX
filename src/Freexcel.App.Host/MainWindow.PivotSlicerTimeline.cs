using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
                BuildSlicerTiles(slicer)))
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
            var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
                string.Equals(pivot.Name, slicer.SourcePivotTableName, StringComparison.OrdinalIgnoreCase));
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

        var slicer = _workbook.Slicers.FirstOrDefault(item =>
            string.Equals(item.Name, tile.SlicerName, StringComparison.OrdinalIgnoreCase));
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
}
