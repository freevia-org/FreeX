using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartSeriesOption(int Index, string Label);

public sealed record ChartMarkerSymbolOption(ChartMarkerSymbol Value, string Label);

public sealed record ChartDashOption(OutlineDash Value, string Label);

public sealed record ChartSeriesOptionsSurfacePlan(
    string CommandId,
    string Title,
    string SeriesLabel,
    string SmoothLineLabel,
    string SecondaryAxisLabel,
    string LineWidthLabel,
    string LineColorLabel,
    string LineDashLabel,
    string NoLineLabel,
    string FillColorLabel,
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
    public const string LineColorLabel = "Line color (#RRGGBB)";
    public const string LineDashLabel = "Line dash";
    public const string NoLineLabel = "No line";
    public const string FillColorLabel = "Fill color (#RRGGBB)";
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

    public static IReadOnlyList<ChartDashOption> DashOptions { get; } =
    [
        new(OutlineDash.Solid, "Solid"),
        new(OutlineDash.Dash, "Dash"),
        new(OutlineDash.Dot, "Dot"),
        new(OutlineDash.DashDot, "Dash dot"),
        new(OutlineDash.LongDash, "Long dash"),
        new(OutlineDash.LongDashDot, "Long dash dot"),
        new(OutlineDash.LongDashDotDot, "Long dash dot dot"),
        new(OutlineDash.SystemDash, "System dash"),
        new(OutlineDash.SystemDot, "System dot"),
        new(OutlineDash.SystemDashDot, "System dash dot"),
    ];

    private readonly ChartShape _chart;
    private int _seriesIndex;
    private bool _smoothLine;
    private bool _onSecondaryAxis;
    private double? _lineWidthPt;
    private ThemeAwareColor? _lineColor;
    private OutlineDash _lineDash;
    private bool _noLine;
    private ThemeAwareColor? _fillColor;
    private ShapeFill? _fill;
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
            LineColorLabel,
            LineDashLabel,
            NoLineLabel,
            FillColorLabel,
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
    public string LineColorText => FormatColor(_lineColor);
    public OutlineDash LineDash => _lineDash;
    public bool NoLine => _noLine;
    public string FillColorText => FormatColor(_fillColor);
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
            _lineColor = null;
            _lineDash = OutlineDash.Solid;
            _noLine = false;
            _fillColor = null;
            _fill = null;
            _markerSymbol = ChartMarkerSymbol.Auto;
            _markerSizePt = null;
            return;
        }

        _seriesIndex = Math.Clamp(index, 0, _chart.Series.Count - 1);
        var series = _chart.Series[_seriesIndex];
        _smoothLine = series.SmoothLine ?? false;
        _onSecondaryAxis = series.OnSecondaryAxis;
        _lineWidthPt = series.LineStyle?.WidthPt;
        _lineColor = series.LineStyle?.Color;
        _lineDash = series.LineStyle?.Dash ?? OutlineDash.Solid;
        _noLine = series.LineStyle?.NoFill == true;
        _fill = series.Fill;
        _fillColor = series.FillColor ?? (series.Fill is ShapeFill.Solid solid ? solid.Color : null);
        _markerSymbol = series.MarkerStyle?.Symbol ?? ChartMarkerSymbol.Auto;
        _markerSizePt = series.MarkerStyle?.SizePt;
    }

    public void SetSmoothLine(bool value) => _smoothLine = value;
    public void SetOnSecondaryAxis(bool value) => _onSecondaryAxis = value;
    public void SetLineWidth(double? value) => _lineWidthPt = value;
    public void SetLineColor(string? text) => _lineColor = string.IsNullOrWhiteSpace(text)
        ? null
        : ChartPointOptionsPlanner.ParseColor(text, LineColorLabel);
    public void SetLineDash(OutlineDash value) => _lineDash = value;
    public void SetNoLine(bool value) => _noLine = value;
    public void SetFillColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _fillColor = null;
            return;
        }

        _fill = null;
        _fillColor = ChartPointOptionsPlanner.ParseColor(text, FillColorLabel);
    }
    public void SetMarkerSymbol(ChartMarkerSymbol value) => _markerSymbol = value;
    public void SetMarkerSize(double? value) => _markerSizePt = value;

    public ChartSeriesOptions BuildCommitPlan() => new(
        _seriesIndex,
        _smoothLine,
        _onSecondaryAxis,
        _lineWidthPt,
        _markerSymbol,
        _markerSizePt,
        _fillColor,
        _fill,
        _lineColor,
        _lineDash,
        _noLine);

    private static string FormatColor(ThemeAwareColor? color) =>
        color is null ? string.Empty : color.Resolved.ToString();

    private static string SeriesLabelText(int index, ChartSeries series) =>
        string.IsNullOrWhiteSpace(series.Name) ? $"Series {index + 1}" : series.Name;
}
