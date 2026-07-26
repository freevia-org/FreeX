namespace FreeP.Core.Model;

/// <summary>Undoable scatter and radar plot-style settings already represented by the chart model.</summary>
public sealed record ChartPlotStyleOptions(
    ScatterStyle ScatterStyle,
    RadarStyle RadarStyle);
