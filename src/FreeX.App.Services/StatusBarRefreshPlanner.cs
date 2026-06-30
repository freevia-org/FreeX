using Free.Shared.AppServices;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum StatusBarRefreshAction
{
    HideReadouts,
    Ready,
    Stats
}

public readonly record struct StatusBarRefreshPlan(
    StatusBarRefreshAction Action,
    StatusBarViewMode ViewMode,
    int ZoomPercent,
    string ReadyText,
    WorkbookSelectionStats Stats);

public static class StatusBarRefreshPlanner
{
    public static StatusBarRefreshPlan Build(
        Sheet? sheet,
        GridRange? selectedRange,
        WorkbookSelectionStats? selectionStats,
        bool isFileOperationProgressVisible,
        int zoomPercent,
        IStatusBarTextProvider textProvider)
    {
        ArgumentNullException.ThrowIfNull(textProvider);

        var viewMode = sheet is null
            ? StatusBarViewMode.Normal
            : WorksheetViewModeUiStatePlanner.ToStatusBarViewMode(sheet.ViewMode);

        if (isFileOperationProgressVisible)
            return new StatusBarRefreshPlan(StatusBarRefreshAction.HideReadouts, viewMode, zoomPercent, "", default);

        if (sheet is null || selectedRange is not { } range || selectionStats is not { } stats)
        {
            return new StatusBarRefreshPlan(
                StatusBarRefreshAction.Ready,
                viewMode,
                zoomPercent,
                textProvider.GetReadyText(),
                default);
        }

        if (stats.IsEmpty)
        {
            return new StatusBarRefreshPlan(
                StatusBarRefreshAction.Ready,
                viewMode,
                zoomPercent,
                StatusBarReadyTextPlanner.BuildReadyText(sheet, range.Start, textProvider),
                default);
        }

        return new StatusBarRefreshPlan(
            StatusBarRefreshAction.Stats,
            viewMode,
            zoomPercent,
            "",
            stats);
    }
}
