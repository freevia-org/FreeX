using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

public enum SlicerSelectionGesture
{
    Replace,
    Toggle,
    Extend
}

public sealed partial class PivotApplicationSession
{
    public PivotApplicationPlan PlanInsertSlicer(
        PivotApplicationTarget target,
        string slicerName,
        string sourceFieldName)
    {
        ArgumentNullException.ThrowIfNull(target);
        return PlanMutation(
            target,
            new AddSlicerCommand(slicerName, target.PivotTable.Name, sourceFieldName));
    }

    public PivotApplicationPlan PlanInsertTimeline(
        PivotApplicationTarget target,
        string timelineName,
        string sourceFieldName)
    {
        ArgumentNullException.ThrowIfNull(target);
        return PlanMutation(
            target,
            new AddTimelineCommand(timelineName, target.PivotTable.Name, sourceFieldName));
    }

    public PivotApplicationPlan PlanSlicerSelection(
        SlicerModel slicer,
        IReadOnlyList<string> selectedItems)
    {
        ArgumentNullException.ThrowIfNull(slicer);
        ArgumentNullException.ThrowIfNull(selectedItems);
        return PlanMutation(
            target: null,
            new SetSlicerSelectionCommand(slicer.Name, selectedItems),
            slicer.Name);
    }

    public PivotApplicationPlan PlanSlicerSelection(
        SlicerModel slicer,
        string caption,
        SlicerSelectionGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(slicer);
        var allItems = new SlicerTimelineSourceSession(_workbook).ReadSlicerSourceItems(slicer);
        var selected = gesture switch
        {
            SlicerSelectionGesture.Extend =>
                SlicerTimelinePanePlanner.ExtendSlicerSelection(allItems, slicer.SelectedItems, caption),
            SlicerSelectionGesture.Toggle =>
                SlicerTimelinePanePlanner.ToggleSlicerSelection(allItems, slicer.SelectedItems, caption),
            _ => SlicerTimelinePanePlanner.ReplaceSlicerSelection(slicer.SelectedItems, caption),
        };
        return PlanSlicerSelection(slicer, selected);
    }

    public PivotApplicationPlan? PlanSlicerSelection(
        string slicerName,
        string caption,
        SlicerSelectionGesture gesture)
    {
        var slicer = FindSlicer(slicerName);
        return slicer is null ? null : PlanSlicerSelection(slicer, caption, gesture);
    }

    public PivotApplicationPlan? PlanClearSlicer(string slicerName)
    {
        var slicer = FindSlicer(slicerName);
        return slicer is null ? null : PlanSlicerSelection(slicer, []);
    }

    public PivotApplicationPlan PlanTimelineRange(
        TimelineModel timeline,
        string? startDate,
        string? endDate)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        return PlanMutation(
            target: null,
            new SetTimelineRangeCommand(
                timeline.Name,
                SlicerTimelinePanePlanner.NormalizeTimelineDateInput(startDate),
                SlicerTimelinePanePlanner.NormalizeTimelineDateInput(endDate)),
            timeline.Name);
    }

    public PivotApplicationPlan? PlanTimelineRange(
        string timelineName,
        string? startDate,
        string? endDate)
    {
        var timeline = FindTimeline(timelineName);
        return timeline is null ? null : PlanTimelineRange(timeline, startDate, endDate);
    }

    public PivotApplicationPlan? PlanClearTimeline(string timelineName) =>
        PlanTimelineRange(timelineName, null, null);

    public PivotApplicationPlan? PlanCycleTimelineGranularity(string timelineName)
    {
        var timeline = FindTimeline(timelineName);
        if (timeline is null)
            return null;

        return PlanMutation(
            target: null,
            new SetTimelineGranularityCommand(
                timeline.Name,
                SetTimelineGranularityCommand.CycleLevel(timeline.Level)),
            timeline.Name);
    }

    public PivotApplicationPlan? PlanSlicerPointer(
        SlicerModel slicer,
        IReadOnlyList<string> availableItems,
        SlicerLayoutModel layout,
        LayoutPoint point,
        bool additive = false)
    {
        ArgumentNullException.ThrowIfNull(slicer);
        ArgumentNullException.ThrowIfNull(availableItems);
        ArgumentNullException.ThrowIfNull(layout);
        var command = SlicerTimelineInteractionPlanner.BuildSlicerClearFilterCommand(slicer, layout, point) ??
            SlicerTimelineInteractionPlanner.BuildSlicerToggleCommand(
                slicer,
                availableItems,
                layout,
                point,
                additive);
        return command is null
            ? null
            : PlanMutation(target: null, command, slicer.Name);
    }

    public PivotApplicationPlan? PlanTimelinePointer(
        TimelineModel timeline,
        TimelineLayoutModel layout,
        LayoutPoint point)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(layout);
        var command = (IWorkbookCommand?)
            SlicerTimelineInteractionPlanner.BuildTimelineClearFilterCommand(timeline, layout, point) ??
            (IWorkbookCommand?)SlicerTimelineInteractionPlanner.BuildTimelineGranularityCommand(timeline, layout, point) ??
            SlicerTimelineInteractionPlanner.BuildTimelineRangeCommand(timeline, layout, point);
        return command is null
            ? null
            : PlanMutation(target: null, command, timeline.Name);
    }

    private SlicerModel? FindSlicer(string? name) =>
        _workbook.Slicers.FirstOrDefault(slicer =>
            string.Equals(slicer.Name, name, StringComparison.OrdinalIgnoreCase));

    private TimelineModel? FindTimeline(string? name) =>
        _workbook.Timelines.FirstOrDefault(timeline =>
            string.Equals(timeline.Name, name, StringComparison.OrdinalIgnoreCase));
}
