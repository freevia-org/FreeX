using Free.Shared.AppServices;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public readonly record struct WorksheetViewModeUiState(
    WorksheetViewMode ViewMode,
    StatusBarViewMode StatusBarViewMode,
    bool NormalChecked,
    bool PageLayoutChecked,
    bool PageBreakPreviewChecked,
    bool UsesPageBreakPreviewOverlay);

public static class WorksheetViewModeUiStatePlanner
{
    public static WorksheetViewModeUiState Build(WorksheetViewMode viewMode) =>
        new(
            viewMode,
            ToStatusBarViewMode(viewMode),
            NormalChecked: viewMode == WorksheetViewMode.Normal,
            PageLayoutChecked: viewMode == WorksheetViewMode.PageLayout,
            PageBreakPreviewChecked: viewMode == WorksheetViewMode.PageBreakPreview,
            UsesPageBreakPreviewOverlay: viewMode is WorksheetViewMode.PageLayout or WorksheetViewMode.PageBreakPreview);

    public static StatusBarViewMode ToStatusBarViewMode(WorksheetViewMode viewMode) =>
        viewMode switch
        {
            WorksheetViewMode.PageLayout => StatusBarViewMode.PageLayout,
            WorksheetViewMode.PageBreakPreview => StatusBarViewMode.PageBreak,
            _ => StatusBarViewMode.Normal
        };
}
