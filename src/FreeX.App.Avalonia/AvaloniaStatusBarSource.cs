using System.Collections.Generic;
using Free.Shared.AppServices;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// UI-free helpers that drive the Avalonia footer from the platform-neutral status-bar model. They
/// wrap the shared <see cref="StatusBarDisplayModelBuilder"/> (the same builder + selection-stats path
/// the WPF host uses) and apply the per-option visibility map the "Customize Status Bar" menu toggles,
/// so the rendering policy is testable without a running shell.
/// </summary>
internal static class AvaloniaStatusBarSource
{
    private static readonly IStatusBarTextProvider TextProvider =
        new ResourceKeyStatusBarTextProvider(UiText.Get);

    /// <summary>
    /// Default per-option visibility, keyed by the <c>StatusBarCustomizeContextMenuPlanner</c> OptionTag
    /// values. Avalonia uses the same Excel-default profile as the WPF host, so the shared planner stays
    /// the source of truth for both renderers.
    /// </summary>
    public static Dictionary<string, bool> CreateDefaultOptionVisibility() =>
        StatusBarVisibilityPlanner.CreateDefaultOptionVisibility(StatusBarOptionVisibility.ExcelDefaults);

    /// <summary>
    /// Builds the neutral <see cref="StatusBarViewModel"/> for the given selection stats and zoom using
    /// the shared <see cref="StatusBarDisplayModelBuilder"/> and resource-key text provider.
    /// </summary>
    public static StatusBarViewModel BuildModel(
        WorkbookSelectionStats stats,
        int zoomPercent,
        string readyText,
        WorksheetViewMode viewMode = WorksheetViewMode.Normal)
    {
        var statusBarViewMode = WorksheetViewModeUiStatePlanner.ToStatusBarViewMode(viewMode);
        return stats.IsEmpty
            ? StatusBarDisplayModelBuilder.Ready(statusBarViewMode, zoomPercent, readyText)
            : StatusBarDisplayModelBuilder.Stats(
                statusBarViewMode,
                zoomPercent,
                stats,
                TextProvider);
    }

    public static string BuildReadyText(Sheet sheet, CellAddress activeCell) =>
        StatusBarReadyTextPlanner.BuildReadyText(sheet, activeCell, TextProvider);

    public static string NormalizeReadyText(string? status) =>
        StatusBarReadyTextPlanner.NormalizeTransientReadyText(status, TextProvider);

    /// <summary>
    /// R128-status-bar-calculate-indicator: calc-mode-aware variant used for the shell's live status
    /// refresh (<c>MainWindow.StatusBar.cs</c>'s <c>BuildStatusBarViewModel</c>), so the dozens of
    /// <c>RefreshShell("Ready")</c> call sites across <c>MainWindow.cs</c> surface Excel's "Calculate"
    /// cell-mode indicator instead of "Ready" while a Manual-mode edit is still pending
    /// recalculation, without each of those call sites needing to know about calc mode.
    /// </summary>
    public static string NormalizeReadyText(string? status, bool isManualCalculationMode, bool hasPendingRecalculation) =>
        StatusBarReadyTextPlanner.NormalizeTransientReadyText(
            status,
            TextProvider,
            isManualCalculationMode,
            hasPendingRecalculation);

    public static string ReadyText() =>
        TextProvider.GetReadyText();

    /// <summary>
    /// Joins the model's visible aggregate readouts (filtered by <paramref name="optionVisibility"/>)
    /// into the single-line text the Avalonia footer shows, preserving the WPF readout order and the
    /// legacy three-space separator.
    /// </summary>
    public static string FormatVisibleReadouts(
        StatusBarViewModel model,
        IReadOnlyDictionary<string, bool> optionVisibility) =>
        BuildRendererPlan(model, optionVisibility).VisibleReadoutText;

    public static StatusBarPresentationPlan BuildPresentation(
        StatusBarViewModel model,
        IReadOnlyDictionary<string, bool> optionVisibility,
        bool hasPageNumberText = false,
        string fallbackAutomationText = "") =>
        StatusBarPresentationPlanner.Build(
            model,
            StatusBarVisibilityPlanner.FromOptionVisibility(optionVisibility),
            hasPageNumberText,
            fallbackAutomationText);

    public static StatusBarRendererPlan BuildRendererPlan(
        StatusBarViewModel model,
        IReadOnlyDictionary<string, bool> optionVisibility,
        bool hasPageNumberText = false,
        string fallbackAutomationText = "") =>
        StatusBarPresentationPlanner.BuildRendererPlan(
            BuildPresentation(
                model,
                optionVisibility,
                hasPageNumberText,
                fallbackAutomationText));

    public static bool IsOptionVisible(IReadOnlyDictionary<string, bool> optionVisibility, string optionTag) =>
        StatusBarVisibilityPlanner.IsOptionVisible(optionVisibility, optionTag);

    public static string ReadoutOptionTag(StatusBarReadoutKind kind) =>
        StatusBarVisibilityPlanner.ReadoutOptionTag(kind);

    /// <summary>
    /// Resolves the customize-menu header text for a planner resource key through the portable app catalog.
    /// </summary>
    public static string CustomizeHeader(string resourceKey) =>
        UiText.Get(resourceKey);
}
