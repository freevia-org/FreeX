namespace FreeP.Core.Model;

/// <summary>PowerPoint bubble-chart sizing and negative-value display options.</summary>
public sealed record ChartBubbleOptions(
    int BubbleScalePercent,
    BubbleSizeRepresentation SizeRepresents,
    bool ShowNegativeBubbles);
