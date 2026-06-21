using System.Collections.Generic;
using System.Text;
using Free.Shared.AppServices;

namespace FreeX.App.Avalonia;

/// <summary>
/// UI-free helpers that drive the Avalonia footer from the platform-neutral status-bar model. They
/// wrap the shared <see cref="StatusBarDisplayModelBuilder"/> (the same builder + selection-stats path
/// the WPF host uses) and apply the per-option visibility map the "Customize Status Bar" menu toggles,
/// so the rendering policy is testable without a running shell.
/// </summary>
internal static class AvaloniaStatusBarSource
{
    /// <summary>
    /// Default per-option visibility, keyed by the <c>StatusBarCustomizeContextMenuPlanner</c> OptionTag
    /// values. Defaults mirror Excel / the WPF host (the standard set of toggles on), so the first render
    /// shows the full readout.
    /// </summary>
    public static Dictionary<string, bool> CreateDefaultOptionVisibility() =>
        new(StringComparer.Ordinal)
        {
            ["CellMode"] = true,
            ["EndMode"] = false,
            ["SelectionMode"] = true,
            ["PageNumber"] = false,
            ["Average"] = true,
            ["Count"] = true,
            ["NumericalCount"] = false,
            ["Minimum"] = false,
            ["Maximum"] = false,
            ["Sum"] = true,
            ["ViewShortcuts"] = true,
            ["Zoom"] = true,
            ["ZoomSlider"] = true,
        };

    /// <summary>
    /// Builds the neutral <see cref="StatusBarViewModel"/> for the given selection stats and zoom using
    /// the shared <see cref="StatusBarDisplayModelBuilder"/>. The Avalonia session has no page-layout /
    /// page-break view yet, so the view mode is <see cref="StatusBarViewMode.Normal"/>.
    /// </summary>
    public static StatusBarViewModel BuildModel(WorkbookSelectionStats stats, int zoomPercent, string readyText) =>
        stats.IsEmpty
            ? StatusBarDisplayModelBuilder.Ready(StatusBarViewMode.Normal, zoomPercent, readyText)
            : StatusBarDisplayModelBuilder.Stats(
                StatusBarViewMode.Normal,
                zoomPercent,
                stats,
                AvaloniaStatusBarTextProvider.Instance);

    /// <summary>
    /// Joins the model's visible aggregate readouts (filtered by <paramref name="optionVisibility"/>)
    /// into the single-line text the Avalonia footer shows, preserving the WPF readout order and the
    /// legacy three-space separator.
    /// </summary>
    public static string FormatVisibleReadouts(
        StatusBarViewModel model,
        IReadOnlyDictionary<string, bool> optionVisibility)
    {
        if (!model.AreStatsVisible)
            return "";

        var builder = new StringBuilder();
        foreach (var readout in model.Readouts)
        {
            if (!readout.IsVisible || readout.Value.Length == 0)
                continue;
            if (!IsOptionVisible(optionVisibility, ReadoutOptionTag(readout.Kind)))
                continue;

            if (builder.Length > 0)
                builder.Append("   ");
            builder.Append(readout.Value);
        }

        return builder.ToString();
    }

    public static bool IsOptionVisible(IReadOnlyDictionary<string, bool> optionVisibility, string optionTag) =>
        optionVisibility.TryGetValue(optionTag, out var visible) && visible;

    public static string ReadoutOptionTag(StatusBarReadoutKind kind) =>
        kind switch
        {
            StatusBarReadoutKind.Average => "Average",
            StatusBarReadoutKind.Count => "Count",
            StatusBarReadoutKind.NumericalCount => "NumericalCount",
            StatusBarReadoutKind.Sum => "Sum",
            StatusBarReadoutKind.Minimum => "Minimum",
            StatusBarReadoutKind.Maximum => "Maximum",
            _ => "Count"
        };

    /// <summary>
    /// Resolves the customize-menu header text for a planner resource key. The Avalonia shell has no
    /// <c>UiText</c> resource system, so it mirrors the WPF host's <c>StatusBar_*</c> resource values.
    /// </summary>
    public static string CustomizeHeader(string resourceKey) =>
        resourceKey switch
        {
            "StatusBar_CustomizeStatusBar" => "Customize Status Bar",
            "StatusBar_CellMode" => "Cell Mode",
            "StatusBar_EndMode" => "End Mode",
            "StatusBar_SelectionMode" => "Selection Mode",
            "StatusBar_PageNumber" => "Page Number",
            "StatusBar_Average" => "Average",
            "StatusBar_Count" => "Count",
            "StatusBar_NumericalCount" => "Numerical Count",
            "StatusBar_Minimum" => "Minimum",
            "StatusBar_Maximum" => "Maximum",
            "StatusBar_Sum" => "Sum",
            "StatusBar_ViewShortcuts" => "View Shortcuts",
            "StatusBar_Zoom" => "Zoom",
            "StatusBar_ZoomSlider" => "Zoom Slider",
            _ => resourceKey
        };
}
