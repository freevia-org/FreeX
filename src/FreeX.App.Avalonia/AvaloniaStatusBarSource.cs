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

    public static string BuildReadyText(Sheet sheet, CellAddress activeCell, string fallbackReadyText) =>
        StatusBarReadyTextPlanner.BuildReadyText(sheet, activeCell, fallbackReadyText);

    /// <summary>
    /// Joins the model's visible aggregate readouts (filtered by <paramref name="optionVisibility"/>)
    /// into the single-line text the Avalonia footer shows, preserving the WPF readout order and the
    /// legacy three-space separator.
    /// </summary>
    public static string FormatVisibleReadouts(
        StatusBarViewModel model,
        IReadOnlyDictionary<string, bool> optionVisibility) =>
        BuildPresentation(model, optionVisibility).VisibleReadoutText;

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
