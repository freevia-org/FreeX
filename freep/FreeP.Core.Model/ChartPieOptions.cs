namespace FreeP.Core.Model;

/// <summary>Undoable pie/doughnut layout settings already represented by the chart model.</summary>
public sealed record ChartPieOptions(
    int? FirstSliceAngleDegrees,
    int DoughnutHolePercent,
    OfPieType? OfPieType = null,
    OfPieSplitType? OfPieSplitType = null,
    double? OfPieSplitPosition = null,
    int? OfPieSecondPieSizePercent = null,
    IReadOnlyList<int>? OfPieCustomPointIndices = null,
    int? OfPieGapWidthPercent = null,
    bool? OfPieSeriesLines = null);
