using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>An error-bar amount-kind choice for the "Error Bars" dialog: the value plus its English label.</summary>
public sealed record ChartErrorBarKindChoice(ChartErrorBarKind Kind, string DisplayName);

/// <summary>A direction choice (both / plus / minus) for the "Error Bars" dialog.</summary>
public sealed record ChartErrorBarDirectionChoice(ChartErrorBarDirection Direction, string DisplayName);

/// <summary>
/// The error-bar show/kind/direction/amount state read from a chart and edited back through the dialog.
/// The amount applies to the fixed-value and percentage kinds (it is a numeric amount or a percent); it is
/// ignored for the standard-error kind. Carries only the fields the cross-platform "Error Bars" dialog exposes.
/// </summary>
public readonly record struct ChartErrorBarsInput(
    bool ShowErrorBars,
    ChartErrorBarKind Kind,
    ChartErrorBarDirection Direction,
    double Value,
    bool EndCaps);

/// <summary>
/// Portable (no UI) planner for the "Error Bars" editing dialog (standard-error / percentage / fixed-value /
/// custom amount, both/plus/minus direction, optional end caps). Single-sources the offered kinds and
/// directions, clamps the amount into Excel's range, and projects an edited <see cref="ChartErrorBarsInput"/>
/// into the <see cref="ChartLayoutOptions"/> the shell hands to the Core <see cref="SetChartLayoutCommand"/>.
/// Whether a chart can carry error bars at all is gated by <see cref="SupportsErrorBars"/> (the same
/// cartesian families that carry trendlines). Reused across every shell.
/// </summary>
public static class ChartErrorBarsPlanner
{
    /// <summary>Excel's smallest/largest fixed or percentage error-bar amount.</summary>
    public const double MinValue = 0;
    public const double MaxValue = 1000;

    private static readonly ChartErrorBarKindChoice[] KindCatalog =
    [
        new(ChartErrorBarKind.StandardError, "Standard error"),
        new(ChartErrorBarKind.Percentage, "Percentage"),
        new(ChartErrorBarKind.FixedValue, "Fixed value"),
        new(ChartErrorBarKind.Custom, "Custom"),
    ];

    private static readonly ChartErrorBarDirectionChoice[] DirectionCatalog =
    [
        new(ChartErrorBarDirection.Both, "Both"),
        new(ChartErrorBarDirection.Plus, "Plus"),
        new(ChartErrorBarDirection.Minus, "Minus"),
    ];

    /// <summary>The selectable error-amount kinds, in display order.</summary>
    public static IReadOnlyList<ChartErrorBarKindChoice> GetKindChoices() => KindCatalog;

    /// <summary>The selectable directions, in display order.</summary>
    public static IReadOnlyList<ChartErrorBarDirectionChoice> GetDirectionChoices() => DirectionCatalog;

    /// <summary>The English display label for <paramref name="kind"/> (falls back to the enum name).</summary>
    public static string DisplayName(ChartErrorBarKind kind)
    {
        foreach (var choice in KindCatalog)
        {
            if (choice.Kind == kind)
                return choice.DisplayName;
        }

        return kind.ToString();
    }

    /// <summary>The English display label for <paramref name="direction"/> (falls back to the enum name).</summary>
    public static string DisplayName(ChartErrorBarDirection direction)
    {
        foreach (var choice in DirectionCatalog)
        {
            if (choice.Direction == direction)
                return choice.DisplayName;
        }

        return direction.ToString();
    }

    /// <summary>True when <paramref name="type"/> can carry error bars (column/line/bar/scatter/bubble/area).</summary>
    public static bool SupportsErrorBars(ChartType type) => ChartTypeSupport.SupportsTrendlines(type);

    /// <summary>Reads the chart's current error-bar state into the dialog input shape.</summary>
    public static ChartErrorBarsInput Read(ChartModel chart) =>
        Normalize(new ChartErrorBarsInput(
            chart.ShowErrorBars,
            chart.ErrorBarKind,
            chart.ErrorBarDirection,
            chart.ErrorBarValue,
            chart.ErrorBarEndCaps));

    /// <summary>
    /// Normalizes error-bar dialog state: unknown kind/direction values fall back to Excel defaults, and the
    /// amount is clamped into Excel's accepted range.
    /// </summary>
    public static ChartErrorBarsInput Normalize(ChartErrorBarsInput input) =>
        input with
        {
            Kind = IsKnownKind(input.Kind) ? input.Kind : ChartErrorBarKind.StandardError,
            Direction = IsKnownDirection(input.Direction) ? input.Direction : ChartErrorBarDirection.Both,
            Value = ClampValue(input.Value),
        };

    /// <summary>
    /// Builds the <see cref="ChartLayoutOptions"/> delta for the edited error-bar state. An invalid/unknown
    /// kind falls back to standard error and an unknown direction to both; the amount is clamped into Excel's
    /// range. The kind, direction, amount, and end-cap toggle are always set (even when hiding) so re-showing
    /// keeps the chosen configuration.
    /// </summary>
    public static ChartLayoutOptions Plan(ChartErrorBarsInput input)
    {
        var normalized = Normalize(input);
        return new ChartLayoutOptions(
            ShowErrorBars: normalized.ShowErrorBars,
            ErrorBarKind: normalized.Kind,
            ErrorBarDirection: normalized.Direction,
            ErrorBarValue: normalized.Value,
            ErrorBarEndCaps: normalized.EndCaps);
    }

    private static double ClampValue(double value) =>
        Math.Clamp(double.IsFinite(value) ? value : 5, MinValue, MaxValue);

    private static bool IsKnownKind(ChartErrorBarKind kind)
    {
        foreach (var choice in KindCatalog)
        {
            if (choice.Kind == kind)
                return true;
        }

        return false;
    }

    private static bool IsKnownDirection(ChartErrorBarDirection direction)
    {
        foreach (var choice in DirectionCatalog)
        {
            if (choice.Direction == direction)
                return true;
        }

        return false;
    }
}
