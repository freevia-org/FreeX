using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartBubbleSizeRepresentationOption(BubbleSizeRepresentation Value, string Label);

public sealed record ChartBubbleOptionsSurfacePlan(
    string CommandId,
    string Title,
    string BubbleScaleLabel,
    string SizeRepresentsLabel,
    string ShowNegativeBubblesLabel,
    string Hint,
    string OkLabel,
    string CancelLabel);

/// <summary>Working-copy planner for PowerPoint bubble-chart sizing controls.</summary>
public sealed class ChartBubbleOptionsPlanner
{
    public const string CommandId = "freep.chart.bubble-options";
    public const string DialogTitle = "Bubble Chart Options";
    public const string BubbleScaleLabel = "Bubble scale (%)";
    public const string SizeRepresentsLabel = "Bubble size represents";
    public const string ShowNegativeBubblesLabel = "Show negative bubbles";
    public const string Hint = "Scale accepts 0-300. Bubble values can represent area or width.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 410;
    public const double DefaultDialogHeight = 270;

    public static IReadOnlyList<ChartBubbleSizeRepresentationOption> SizeRepresentsOptions { get; } =
    [
        new(BubbleSizeRepresentation.Area, "Area"),
        new(BubbleSizeRepresentation.Width, "Width"),
    ];

    private int _bubbleScalePercent;
    private BubbleSizeRepresentation _sizeRepresents;
    private bool _showNegativeBubbles;

    private ChartBubbleOptionsPlanner(ChartShape chart)
    {
        _bubbleScalePercent = Math.Clamp(chart.BubbleScalePercent, 0, 300);
        _sizeRepresents = chart.BubbleSizeRepresents;
        _showNegativeBubbles = chart.ShowNegativeBubbles;
    }

    public static ChartBubbleOptionsSurfacePlan BuildSurfacePlan() => new(
        CommandId,
        DialogTitle,
        BubbleScaleLabel,
        SizeRepresentsLabel,
        ShowNegativeBubblesLabel,
        Hint,
        OkLabel,
        CancelLabel);

    public static ChartBubbleOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartBubbleOptionsPlanner(chart);
    }

    public int BubbleScalePercent => _bubbleScalePercent;
    public BubbleSizeRepresentation SizeRepresents => _sizeRepresents;
    public bool ShowNegativeBubbles => _showNegativeBubbles;

    public void SetBubbleScalePercent(int value) => _bubbleScalePercent = Math.Clamp(value, 0, 300);
    public void SetSizeRepresents(BubbleSizeRepresentation value) => _sizeRepresents = value;
    public void SetShowNegativeBubbles(bool value) => _showNegativeBubbles = value;

    public ChartBubbleOptions BuildCommitPlan() => new(
        _bubbleScalePercent,
        _sizeRepresents,
        _showNegativeBubbles);
}
