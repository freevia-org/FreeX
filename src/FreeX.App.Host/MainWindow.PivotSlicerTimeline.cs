using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.SlicerTimeline;
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

        var gesture = (Keyboard.Modifiers & ModifierKeys.Shift) != 0
            ? SlicerSelectionGesture.Extend
            : (Keyboard.Modifiers & ModifierKeys.Control) != 0
                ? SlicerSelectionGesture.Toggle
                : SlicerSelectionGesture.Replace;
        ApplySlicerTimelinePlan(
            PivotApplication.PlanSlicerSelection(slicer, tile.Caption, gesture),
            "Slicer");
    }

    private void SlicerClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string slicerName })
            return;

        ApplySlicerTimelinePlan(PivotApplication.PlanClearSlicer(slicerName), "Slicer");
    }

    private void TimelineApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TimelinePaneItem item })
            return;

        ApplySlicerTimelinePlan(
            PivotApplication.PlanTimelineRange(item.Name, item.SelectedStartDate, item.SelectedEndDate),
            "Timeline");
    }

    private void TimelineClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TimelinePaneItem item })
            return;

        ApplySlicerTimelinePlan(PivotApplication.PlanClearTimeline(item.Name), "Timeline");
    }

    // ── Native slicer / timeline click handlers (from GridView.TryHandleNativeSlicerTimelineClick) ──

    private void OnNativeSlicerClearFilterRequested(string slicerName)
    {
        ApplySlicerTimelinePlan(PivotApplication.PlanClearSlicer(slicerName), "Slicer");
    }

    private void OnNativeSlicerTileToggleRequested(string slicerName, string caption)
    {
        ApplySlicerTimelinePlan(
            PivotApplication.PlanSlicerSelection(
                slicerName,
                caption,
                SlicerSelectionGesture.Replace),
            "Slicer");
    }

    private void OnNativeTimelineClearFilterRequested(string timelineName)
    {
        ApplySlicerTimelinePlan(PivotApplication.PlanClearTimeline(timelineName), "Timeline");
    }

    private void OnNativeTimelineGranularityToggleRequested(string timelineName)
    {
        ApplySlicerTimelinePlan(
            PivotApplication.PlanCycleTimelineGranularity(timelineName),
            "Timeline");
    }

    private void OnNativeTimelineRangeRequested(string timelineName, string? startDate, string? endDate)
    {
        ApplySlicerTimelinePlan(
            PivotApplication.PlanTimelineRange(timelineName, startDate, endDate),
            "Timeline");
    }

    private bool ApplySlicerTimelinePlan(PivotApplicationPlan? plan, string title) =>
        plan is not null && ApplyPivotApplicationPlan(plan, title);
}
