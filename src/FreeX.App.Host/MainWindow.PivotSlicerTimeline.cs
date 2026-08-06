using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        var sourceSession = new SlicerTimelineSourceSession(_workbook);
        var slicers = _workbook.Slicers
            .Where(slicer => !string.IsNullOrWhiteSpace(slicer.Name))
            .Select(sourceSession.BuildSlicerPaneItem)
            .ToList();
        var timelines = _workbook.Timelines
            .Where(timeline => !string.IsNullOrWhiteSpace(timeline.Name))
            .Select(sourceSession.BuildTimelinePaneItem)
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

    private IReadOnlyList<string> ReadSlicerSourceItems(SlicerModel slicer) =>
        new SlicerTimelineSourceSession(_workbook).ReadSlicerSourceItems(slicer);

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

        // R88-app-slicer-timeline-interaction-5-2: match Excel's slicer click semantics -- a plain
        // click REPLACES the whole selection with just the clicked item (like the native on-grid
        // overlay's SlicerLayoutBuilder.Toggle(additive: false) path), Ctrl+click toggles the item's
        // membership in the existing selection, and Shift+click extends to the contiguous range
        // between the current selection and the clicked item. Only a plain click can narrow a
        // multi-item filter down to a single item; the additive toggle alone can never do that.
        IReadOnlyList<string> selected;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            selected = SlicerTimelinePanePlanner.ExtendSlicerSelection(allItems, slicer.SelectedItems, tile.Caption);
        else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            selected = SlicerTimelinePanePlanner.ToggleSlicerSelection(allItems, slicer.SelectedItems, tile.Caption);
        else
            selected = SlicerTimelinePanePlanner.ReplaceSlicerSelection(slicer.SelectedItems, tile.Caption);

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
                    SlicerTimelinePanePlanner.NormalizeTimelineDateInput(item.SelectedStartDate),
                    SlicerTimelinePanePlanner.NormalizeTimelineDateInput(item.SelectedEndDate)),
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
        // plain-click REPLACE semantics - the same shared ReplaceSlicerSelection path
        // SlicerTileButton_Click now uses for the pane's own plain clicks (R88-app-slicer-timeline-
        // interaction-5-2), matching the behaviour Avalonia gets from SlicerLayoutBuilder.Toggle(...,
        // additive: false). A plain click on a caption replaces the whole selection with just that
        // item; a second plain click on the lone already-selected item clears the filter back to
        // "everything selected".
        var selected = SlicerTimelinePanePlanner.ReplaceSlicerSelection(slicer.SelectedItems, caption);

        if (!TryExecuteCommand(new SetSlicerSelectionCommand(slicerName, selected.ToList()), "Slicer"))
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
