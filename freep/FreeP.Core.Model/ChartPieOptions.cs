namespace FreeP.Core.Model;

/// <summary>Undoable pie/doughnut layout settings already represented by the chart model.</summary>
public sealed record ChartPieOptions(
    int? FirstSliceAngleDegrees,
    int DoughnutHolePercent);
