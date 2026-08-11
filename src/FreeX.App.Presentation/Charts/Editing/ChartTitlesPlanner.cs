using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>
/// The current chart/axis title text presented to a "Chart Titles" dialog, and the normalized text the
/// planner produces from edited input.
/// </summary>
public readonly record struct ChartTitlesInput(string ChartTitle, string XAxisTitle, string YAxisTitle);

/// <summary>
/// Portable (no UI) planner for the "Chart Titles" editing dialog. Single-sources how raw title text is
/// normalized (trimmed; whitespace-only collapsed to empty) and projects the result into a
/// <see cref="ChartLayoutOptions"/> the shell hands to the Core <see cref="SetChartLayoutCommand"/>. Axis
/// titles are dropped for chart types that have no axes (pie/doughnut), mirroring Core's
/// <c>EnforceAxisTitleSupport</c> so the dialog never offers axis titles that the command would clear.
/// Reused across every shell.
/// </summary>
public static class ChartTitlesPlanner
{
    /// <summary>Reads the chart's current titles into the dialog input shape.</summary>
    public static ChartTitlesInput Read(ChartModel chart) =>
        new(chart.Title ?? string.Empty, chart.XAxisTitle ?? string.Empty, chart.YAxisTitle ?? string.Empty);

    /// <summary>Trims edited title text and converts null or whitespace-only values to empty strings.</summary>
    public static ChartTitlesInput Normalize(ChartTitlesInput input) =>
        new(
            NormalizeText(input.ChartTitle),
            NormalizeText(input.XAxisTitle),
            NormalizeText(input.YAxisTitle));

    /// <summary>True when axis titles apply to <paramref name="type"/> (false for pie/doughnut).</summary>
    public static bool SupportsAxisTitles(ChartType type) => ChartTypeSupport.SupportsAxes(type);

    /// <summary>
    /// Builds the <see cref="ChartLayoutOptions"/> delta for the edited titles. Each title is trimmed;
    /// whitespace-only input becomes an empty string (clears the title). For axis-less chart types the
    /// axis-title fields are set to empty so the command clears any stale axis titles rather than leaving
    /// them dangling.
    /// </summary>
    public static ChartLayoutOptions Plan(ChartType type, ChartTitlesInput input)
    {
        var normalized = Normalize(input);
        var supportsAxes = SupportsAxisTitles(type);

        return new ChartLayoutOptions(
            Title: normalized.ChartTitle,
            XAxisTitle: supportsAxes ? normalized.XAxisTitle : string.Empty,
            YAxisTitle: supportsAxes ? normalized.YAxisTitle : string.Empty);
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return text.Trim();
    }
}
