using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// WPF host compatibility facade for the shared slicer/timeline pane planner. Keep model selection,
/// filter visibility, and native-control cache logic in <see cref="SlicerTimelinePanePlanner"/>; this
/// adapter preserves existing Host call sites while WPF owns only binding and control construction.
/// </summary>
public static class SlicerTimelinePlanner
{
    public static IReadOnlyList<SlicerTileItem> BuildSlicerTiles(SlicerModel slicer, IEnumerable<string> sourceItems) =>
        SlicerTimelinePanePlanner.BuildSlicerTiles(slicer, sourceItems);

    public static IReadOnlyList<string> ToggleSlicerSelection(
        IReadOnlyCollection<string> allItems,
        IReadOnlyCollection<string> selectedItems,
        string caption) =>
        SlicerTimelinePanePlanner.ToggleSlicerSelection(allItems, selectedItems, caption);

    public static IReadOnlyList<string> ReplaceSlicerSelection(
        IReadOnlyCollection<string> selectedItems,
        string caption) =>
        SlicerTimelinePanePlanner.ReplaceSlicerSelection(selectedItems, caption);

    public static IReadOnlyList<string> ExtendSlicerSelection(
        IReadOnlyList<string> allItems,
        IReadOnlyCollection<string> selectedItems,
        string caption) =>
        SlicerTimelinePanePlanner.ExtendSlicerSelection(allItems, selectedItems, caption);

    public static bool HasActiveSlicerFilter(SlicerModel slicer) =>
        SlicerTimelinePanePlanner.HasActiveSlicerFilter(slicer);

    public static bool HasActiveTimelineFilter(TimelineModel timeline) =>
        SlicerTimelinePanePlanner.HasActiveTimelineFilter(timeline);

    public static TimelinePaneItem BuildTimelineItem(TimelineModel timeline) =>
        SlicerTimelinePanePlanner.BuildTimelineItem(timeline);

    public static string? NormalizeTimelineDateInput(string? value) =>
        SlicerTimelinePanePlanner.NormalizeTimelineDateInput(value);

    public static IReadOnlyList<SlicerModel> GetNativeVisualSlicers(Workbook workbook, Sheet activeSheet) =>
        SlicerTimelinePanePlanner.GetNativeVisualSlicers(workbook, activeSheet);

    public static IReadOnlyList<TimelineModel> GetNativeVisualTimelines(Workbook workbook, Sheet activeSheet) =>
        SlicerTimelinePanePlanner.GetNativeVisualTimelines(workbook, activeSheet);

    public static NativeVisualFilters GetNativeVisualFilters(Workbook workbook, Sheet activeSheet) =>
        SlicerTimelinePanePlanner.GetNativeVisualFilters(workbook, activeSheet);
}
