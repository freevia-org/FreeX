using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>The bubble-chart sizing state read from a chart and edited back through the dialog.</summary>
public readonly record struct ChartBubbleFormatInput(
    int BubbleScale,
    bool ShowNegativeBubbles,
    ChartBubbleSizeRepresents BubbleSizeRepresents);

/// <summary>
/// Portable (no UI) planner for the "Format Bubble Chart" editing dialog: the bubble scale (percent), whether
/// negative-value bubbles are drawn, and whether the third column represents bubble area or width. Single-
/// sources the read/validate/project rules and maps an edited <see cref="ChartBubbleFormatInput"/> onto the
/// <see cref="ChartLayoutOptions"/> the shell hands to the Core <see cref="SetChartLayoutCommand"/>. Every
/// field already exists on <see cref="ChartModel"/> and is clamped by Core's <c>ApplyOptions</c>, so no Core
/// change is needed. Reused across every shell. (The WPF host's <c>ChartBubbleFormatDialog</c> is the
/// behavior reference.)
/// </summary>
public static class ChartBubbleFormatPlanner
{
    public const int MinBubbleScale = 1;
    public const int MaxBubbleScale = 300;

    /// <summary>True when the chart is a bubble chart that has these sizing options.</summary>
    public static bool Supports(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return chart.Type == ChartType.Bubble;
    }

    /// <summary>Reads the chart's current bubble sizing into the dialog input shape.</summary>
    public static ChartBubbleFormatInput Read(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartBubbleFormatInput(chart.BubbleScale, chart.ShowNegativeBubbles, chart.BubbleSizeRepresents);
    }

    /// <summary>Validates the edited input. Returns null when valid, else an English reason.</summary>
    public static string? Validate(ChartBubbleFormatInput input)
    {
        if (input.BubbleScale < MinBubbleScale || input.BubbleScale > MaxBubbleScale)
            return $"Enter a bubble scale between {MinBubbleScale} and {MaxBubbleScale}.";

        return null;
    }

    /// <summary>The bubble-size-represents choices the dialog should offer, in display order.</summary>
    public static IReadOnlyList<ChartBubbleSizeRepresents> GetSizeRepresentsChoices() =>
        Enum.GetValues<ChartBubbleSizeRepresents>();

    /// <summary>Builds the <see cref="ChartLayoutOptions"/> delta for the edited bubble sizing.</summary>
    public static ChartLayoutOptions Plan(ChartBubbleFormatInput input) =>
        new(
            BubbleScale: Math.Clamp(input.BubbleScale, MinBubbleScale, MaxBubbleScale),
            ShowNegativeBubbles: input.ShowNegativeBubbles,
            BubbleSizeRepresents: Enum.IsDefined(input.BubbleSizeRepresents)
                ? input.BubbleSizeRepresents
                : ChartBubbleSizeRepresents.Area);
}
