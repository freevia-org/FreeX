using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartScatterStyleOption(ScatterStyle Value, string Label);
public sealed record ChartRadarStyleOption(RadarStyle Value, string Label);

public sealed record ChartPlotStyleOptionsSurfacePlan(
    string CommandId,
    string Title,
    string ScatterStyleLabel,
    string RadarStyleLabel,
    string Hint,
    string OkLabel,
    string CancelLabel);

/// <summary>Working-copy planner for modeled Scatter and Radar plot styles.</summary>
public sealed class ChartPlotStyleOptionsPlanner
{
    public const string CommandId = "freep.chart.plot-style-options";
    public const string DialogTitle = "Chart Plot Style";
    public const string ScatterStyleLabel = "Scatter style";
    public const string RadarStyleLabel = "Radar style";
    public const string Hint = "Scatter and Radar controls apply only to their matching chart family.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 430;
    public const double DefaultDialogHeight = 270;

    public static IReadOnlyList<ChartScatterStyleOption> ScatterStyleOptions { get; } =
    [
        new(ScatterStyle.Marker, "Markers only"),
        new(ScatterStyle.LineMarker, "Lines and markers"),
        new(ScatterStyle.Line, "Lines only"),
        new(ScatterStyle.Smooth, "Smooth lines"),
        new(ScatterStyle.SmoothMarker, "Smooth lines and markers"),
    ];

    public static IReadOnlyList<ChartRadarStyleOption> RadarStyleOptions { get; } =
    [
        new(RadarStyle.Standard, "Standard"),
        new(RadarStyle.Marker, "Marker"),
        new(RadarStyle.Filled, "Filled"),
    ];

    private ScatterStyle _scatterStyle;
    private RadarStyle _radarStyle;

    private ChartPlotStyleOptionsPlanner(ChartShape chart)
    {
        _scatterStyle = chart.ScatterStyle;
        _radarStyle = chart.RadarStyle;
    }

    public static ChartPlotStyleOptionsSurfacePlan BuildSurfacePlan() => new(
        CommandId,
        DialogTitle,
        ScatterStyleLabel,
        RadarStyleLabel,
        Hint,
        OkLabel,
        CancelLabel);

    public static ChartPlotStyleOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (chart.ChartType is not (ChartType.Scatter or ChartType.Radar))
            throw new InvalidOperationException("Select a Scatter or Radar chart before editing plot style.");
        return new ChartPlotStyleOptionsPlanner(chart);
    }

    public ScatterStyle ScatterStyle => _scatterStyle;
    public RadarStyle RadarStyle => _radarStyle;

    public void SetScatterStyle(ScatterStyle value) => _scatterStyle = value;
    public void SetRadarStyle(RadarStyle value) => _radarStyle = value;

    public ChartPlotStyleOptions BuildCommitPlan() => new(_scatterStyle, _radarStyle);
}
