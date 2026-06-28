using FreeX.Core.Commands;
using FreeX.Core.Model;
using System.Globalization;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>
/// The pie/doughnut layout state read from a chart and edited back through the dialog: the angle of the first
/// slice, which slice is exploded and by how far, and (doughnut only) the hole size. Distances/hole are
/// fractions (0..1).
/// </summary>
public readonly record struct ChartPieFormatInput(
    int FirstSliceAngle,
    int ExplodedSliceIndex,
    double ExplodedSliceDistance,
    double DoughnutHoleSize);

public enum ChartPieFormatParseIssue
{
    None,
    FirstSliceAngle,
    ExplodedSliceIndex,
    ExplodedSliceDistance,
    DoughnutHoleSize
}

/// <summary>
/// Portable (no UI) planner for the "Format Pie/Doughnut" editing dialog. Single-sources the read/validate/
/// project rules and maps an edited <see cref="ChartPieFormatInput"/> onto the <see cref="ChartLayoutOptions"/>
/// the shell hands to the Core <see cref="SetChartLayoutCommand"/>. Every field already exists on
/// <see cref="ChartModel"/> and is clamped by Core's <c>ApplyOptions</c>, so no Core change is needed. Reused
/// across every shell. (The WPF host's <c>ChartPieFormatDialog</c> is the behavior reference.)
/// </summary>
public static class ChartPieFormatPlanner
{
    public const int MinFirstSliceAngle = 0;
    public const int MaxFirstSliceAngle = 359;

    /// <summary>The exploded-slice distance bounds (fraction of radius) Core clamps to.</summary>
    public const double MinExplodedDistance = 0;
    public const double MaxExplodedDistance = 0.5;

    /// <summary>The doughnut hole-size bounds (fraction of radius) Core clamps to.</summary>
    public const double MinHoleSize = 0.1;
    public const double MaxHoleSize = 0.9;

    /// <summary>True when the chart is a pie/doughnut family that has these layout options.</summary>
    public static bool Supports(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return ChartTypeSupport.SupportsFirstSliceAngle(chart.Type);
    }

    /// <summary>True when the chart is a doughnut (so the hole-size field applies).</summary>
    public static bool SupportsHoleSize(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return ChartTypeSupport.SupportsDoughnutHoleSize(chart.Type);
    }

    /// <summary>Reads the chart's current pie/doughnut layout into the dialog input shape.</summary>
    public static ChartPieFormatInput Read(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return Normalize(new ChartPieFormatInput(
            (int)chart.FirstSliceAngle,
            chart.ExplodedSliceIndex,
            chart.ExplodedSliceDistance,
            chart.DoughnutHoleSize));
    }

    /// <summary>Validates the edited input. Returns null when valid, else an English reason.</summary>
    public static string? Validate(ChartPieFormatInput input)
    {
        if (input.FirstSliceAngle < MinFirstSliceAngle || input.FirstSliceAngle > MaxFirstSliceAngle)
            return $"Enter a first-slice angle between {MinFirstSliceAngle} and {MaxFirstSliceAngle} degrees.";

        if (!double.IsFinite(input.ExplodedSliceDistance)
            || input.ExplodedSliceDistance < MinExplodedDistance
            || input.ExplodedSliceDistance > MaxExplodedDistance)
        {
            return "Enter an exploded-slice distance between 0% and 50%.";
        }

        if (!double.IsFinite(input.DoughnutHoleSize)
            || input.DoughnutHoleSize < MinHoleSize
            || input.DoughnutHoleSize > MaxHoleSize)
        {
            return "Enter a doughnut hole size between 10% and 90%.";
        }

        return null;
    }

    public static bool TryParseDialogInput(
        string firstSliceAngleText,
        string explodedSliceIndexText,
        string explodedDistancePercentText,
        string doughnutHoleSizePercentText,
        bool includeDoughnutHoleSize,
        out ChartPieFormatInput input,
        out ChartPieFormatParseIssue issue)
    {
        if (!TryParseClampedInt(firstSliceAngleText, MinFirstSliceAngle, MaxFirstSliceAngle, out var angle))
        {
            input = default;
            issue = ChartPieFormatParseIssue.FirstSliceAngle;
            return false;
        }

        if (!TryParseInt(explodedSliceIndexText, out var explodedIndex))
        {
            input = default;
            issue = ChartPieFormatParseIssue.ExplodedSliceIndex;
            return false;
        }

        if (!TryParseClampedInt(
                explodedDistancePercentText,
                ToDisplayPercent(MinExplodedDistance),
                ToDisplayPercent(MaxExplodedDistance),
                out var explodedDistancePercent))
        {
            input = default;
            issue = ChartPieFormatParseIssue.ExplodedSliceDistance;
            return false;
        }

        var holeSizePercent = ToDisplayPercent(0.55);
        if (includeDoughnutHoleSize &&
            !TryParseClampedInt(
                doughnutHoleSizePercentText,
                ToDisplayPercent(MinHoleSize),
                ToDisplayPercent(MaxHoleSize),
                out holeSizePercent))
        {
            input = default;
            issue = ChartPieFormatParseIssue.DoughnutHoleSize;
            return false;
        }

        input = new ChartPieFormatInput(
            angle,
            explodedIndex,
            FromDisplayPercent(explodedDistancePercent),
            FromDisplayPercent(holeSizePercent));
        issue = ChartPieFormatParseIssue.None;
        return true;
    }

    public static int ToDisplayPercent(double value) =>
        (int)Math.Round(value * 100);

    public static double FromDisplayPercent(int value) =>
        value / 100.0;

    public static ChartPieFormatInput Normalize(ChartPieFormatInput input) =>
        new(
            Math.Clamp(input.FirstSliceAngle, MinFirstSliceAngle, MaxFirstSliceAngle),
            input.ExplodedSliceIndex,
            double.IsFinite(input.ExplodedSliceDistance)
                ? Math.Clamp(input.ExplodedSliceDistance, MinExplodedDistance, MaxExplodedDistance)
                : MinExplodedDistance,
            double.IsFinite(input.DoughnutHoleSize)
                ? Math.Clamp(input.DoughnutHoleSize, MinHoleSize, MaxHoleSize)
                : MinHoleSize);

    /// <summary>Builds the <see cref="ChartLayoutOptions"/> delta for the edited pie/doughnut layout.</summary>
    public static ChartLayoutOptions Plan(ChartPieFormatInput input)
    {
        var normalized = Normalize(input);
        return new(
            FirstSliceAngle: normalized.FirstSliceAngle,
            ExplodedSliceIndex: normalized.ExplodedSliceIndex,
            ExplodedSliceDistance: normalized.ExplodedSliceDistance,
            DoughnutHoleSize: normalized.DoughnutHoleSize);
    }

    private static bool TryParseClampedInt(string text, int min, int max, out int value) =>
        TryParseInt(text, out value) && value >= min && value <= max;

    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
        || int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
