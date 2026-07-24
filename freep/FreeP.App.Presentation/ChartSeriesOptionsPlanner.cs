using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartSeriesOption(int Index, string Label);

public sealed record ChartMarkerSymbolOption(ChartMarkerSymbol Value, string Label);

public sealed record ChartSeriesOptionsSurfacePlan(
    string CommandId,
    string Title,
    string SeriesLabel,
    string SmoothLineLabel,
    string SecondaryAxisLabel,
    string LineWidthLabel,
    string MarkerLabel,
    string MarkerSizeLabel,
    string AutoHint,
    string OkLabel,
    string CancelLabel);

/// <summary>
/// Working-copy planner for per-series chart formatting backed by the existing model and
/// PowerPoint chart reader/writer.
/// </summary>
public sealed class ChartSeriesOptionsPlanner
{
    public const string CommandId = "freep.chart.series-options";
    public const string DialogTitle = "Chart Series Options";
    public const string SeriesLabel = "Series";
    public const string SmoothLineLabel = "Smooth line";
    public const string SecondaryAxisLabel = "Plot on secondary axis";
    public const string LineWidthLabel = "Line width (pt)";
    public const string MarkerLabel = "Marker";
    public const string MarkerSizeLabel = "Marker size (pt)";
    public const string AutoHint = "Blank values preserve automatic series formatting.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 440;
    public const double DefaultDialogHeight = 360;

    public static IReadOnlyList<ChartMarkerSymbolOption> MarkerOptions { get; } =
    [
        new(ChartMarkerSymbol.Auto, "Automatic"),
        new(ChartMarkerSymbol.Circle, "Circle"),
        new(ChartMarkerSymbol.Diamond, "Diamond"),
        new(ChartMarkerSymbol.Square, "Square"),
        new(ChartMarkerSymbol.Triangle, "Triangle"),
        new(ChartMarkerSymbol.Star, "Star"),
        new(ChartMarkerSymbol.Plus, "Plus"),
        new(ChartMarkerSymbol.X, "X"),
        new(ChartMarkerSymbol.None, "None"),
    ];

    private readonly ChartShape _chart;
    private int _seriesIndex;
    private bool _smoothLine;
    private bool _onSecondaryAxis;
    private double? _lineWidthPt;
    private ChartMarkerSymbol _markerSymbol;
    private double? _markerSizePt;

    private ChartSeriesOptionsPlanner(ChartShape chart)
    {
        _chart = chart;
        SetSeriesIndex(0);
    }

    public static ChartSeriesOptionsSurfacePlan BuildSurfacePlan() =>
        new(
            CommandId,
            DialogTitle,
            SeriesLabel,
            SmoothLineLabel,
            SecondaryAxisLabel,
            LineWidthLabel,
            MarkerLabel,
            MarkerSizeLabel,
            AutoHint,
            OkLabel,
            CancelLabel);

    public static ChartSeriesOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartSeriesOptionsPlanner(chart);
    }

    public IReadOnlyList<ChartSeriesOption> SeriesOptions =>
        _chart.Series.Select((series, index) => new ChartSeriesOption(index, SeriesLabelText(index, series))).ToArray();

    public int SeriesIndex => _seriesIndex;
    public string SeriesName => _seriesIndex >= 0 && _seriesIndex < _chart.Series.Count
        ? _chart.Series[_seriesIndex].Name
        : string.Empty;
    public bool SmoothLine => _smoothLine;
    public bool OnSecondaryAxis => _onSecondaryAxis;
    public double? LineWidthPt => _lineWidthPt;
    public ChartMarkerSymbol MarkerSymbol => _markerSymbol;
    public double? MarkerSizePt => _markerSizePt;

    public void SetSeriesIndex(int index)
    {
        if (_chart.Series.Count == 0)
        {
            _seriesIndex = 0;
            _smoothLine = false;
            _onSecondaryAxis = false;
            _lineWidthPt = null;
            _markerSymbol = ChartMarkerSymbol.Auto;
            _markerSizePt = null;
            return;
        }

        _seriesIndex = Math.Clamp(index, 0, _chart.Series.Count - 1);
        var series = _chart.Series[_seriesIndex];
        _smoothLine = series.SmoothLine ?? false;
        _onSecondaryAxis = series.OnSecondaryAxis;
        _lineWidthPt = series.LineStyle?.WidthPt;
        _markerSymbol = series.MarkerStyle?.Symbol ?? ChartMarkerSymbol.Auto;
        _markerSizePt = series.MarkerStyle?.SizePt;
    }

    public void SetSmoothLine(bool value) => _smoothLine = value;
    public void SetOnSecondaryAxis(bool value) => _onSecondaryAxis = value;
    public void SetLineWidth(double? value) => _lineWidthPt = value;
    public void SetMarkerSymbol(ChartMarkerSymbol value) => _markerSymbol = value;
    public void SetMarkerSize(double? value) => _markerSizePt = value;

    public ChartSeriesOptions BuildCommitPlan() => new(
        _seriesIndex,
        _smoothLine,
        _onSecondaryAxis,
        _lineWidthPt,
        _markerSymbol,
        _markerSizePt);

    private static string SeriesLabelText(int index, ChartSeries series) =>
        string.IsNullOrWhiteSpace(series.Name) ? $"Series {index + 1}" : series.Name;
}
