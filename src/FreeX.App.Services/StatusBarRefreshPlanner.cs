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
        IStatusBarTextProvider textProvider,
        WorksheetViewMode? viewModeOverride = null,
        bool isManualCalculationMode = false,
        bool hasPendingRecalculation = false)
    {
        ArgumentNullException.ThrowIfNull(textProvider);

        // R83-app-view-modes-5-1: a window's displayed view mode can differ from sheet.ViewMode
        // (Excel "New Window" keeps each window's own view mode independent of any sibling
        // window's changes to the shared Sheet) -- callers that track their own view mode pass it
        // as viewModeOverride so the status bar reflects THIS window, not the shared model.
        var viewMode = viewModeOverride is { } overriddenViewMode
            ? WorksheetViewModeUiStatePlanner.ToStatusBarViewMode(overriddenViewMode)
            : sheet is null
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
                textProvider.GetReadyText(isManualCalculationMode, hasPendingRecalculation),
                default);
        }

        if (stats.IsEmpty)
        {
            return new StatusBarRefreshPlan(
                StatusBarRefreshAction.Ready,
                viewMode,
                zoomPercent,
                StatusBarReadyTextPlanner.BuildReadyText(
                    sheet,
                    range.Start,
                    textProvider,
                    isManualCalculationMode,
                    hasPendingRecalculation),
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
