using Free.Shared.AppServices;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Builds the complete FreeX status-bar renderer contract. Platform hosts retain only native control
/// projection and accessibility notifications.
/// </summary>
public static class FreeXStatusBarRendererPlanner
{
    public static StatusBarViewModel BuildModel(
        WorkbookSelectionStats stats,
        int zoomPercent,
        string readyText,
        WorksheetViewMode viewMode,
        IStatusBarTextProvider textProvider)
    {
        ArgumentNullException.ThrowIfNull(textProvider);

        var statusBarViewMode = WorksheetViewModeUiStatePlanner.ToStatusBarViewMode(viewMode);
        return stats.IsEmpty
            ? StatusBarDisplayModelBuilder.Ready(statusBarViewMode, zoomPercent, readyText)
            : StatusBarDisplayModelBuilder.Stats(statusBarViewMode, zoomPercent, stats, textProvider);
    }

    public static StatusBarRendererPlan BuildRendererPlan(
        StatusBarViewModel model,
        StatusBarOptionVisibility optionVisibility,
        bool hasPageNumberText = false,
        string fallbackAutomationText = "") =>
        StatusBarPresentationPlanner.BuildRendererPlan(
            StatusBarPresentationPlanner.Build(
                model,
                optionVisibility,
                hasPageNumberText,
                fallbackAutomationText));

    public static StatusBarRendererPlan BuildRendererPlan(
        StatusBarViewModel model,
        IReadOnlyDictionary<string, bool> optionVisibility,
        bool hasPageNumberText = false,
        string fallbackAutomationText = "") =>
        BuildRendererPlan(
            model,
            StatusBarVisibilityPlanner.FromOptionVisibility(optionVisibility),
            hasPageNumberText,
            fallbackAutomationText);

    public static string BuildReadyText(
        Sheet sheet,
        CellAddress activeCell,
        IStatusBarTextProvider textProvider) =>
        StatusBarReadyTextPlanner.BuildReadyText(sheet, activeCell, textProvider);

    public static string NormalizeReadyText(
        string? status,
        IStatusBarTextProvider textProvider,
        bool isManualCalculationMode = false,
        bool hasPendingRecalculation = false) =>
        StatusBarReadyTextPlanner.NormalizeTransientReadyText(
            status,
            textProvider,
            isManualCalculationMode,
            hasPendingRecalculation);
}
