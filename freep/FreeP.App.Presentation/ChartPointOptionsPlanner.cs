using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartPointOption(int Index, string Label);

public sealed record ChartPointOptionsSurfacePlan(
    string CommandId,
    string Title,
    string SeriesLabel,
    string PointLabel,
    string FillColorLabel,
    string StrokeColorLabel,
    string StrokeWidthLabel,
    string MarkerLabel,
    string MarkerSizeLabel,
    string AutoHint,
    string OkLabel,
    string CancelLabel);

/// <summary>
/// Working-copy planner for per-point chart formatting. It exposes the existing
/// ChartPointStyle/PointColors model without introducing renderer-specific state.
/// </summary>
public sealed class ChartPointOptionsPlanner
{
    public const string CommandId = "freep.chart.point-options";
    public const string DialogTitle = "Chart Point Options";
    public const string SeriesLabel = "Series";
    public const string PointLabel = "Point";
    public const string FillColorLabel = "Fill color (#RRGGBB)";
    public const string StrokeColorLabel = "Outline color (#RRGGBB)";
    public const string StrokeWidthLabel = "Outline width (pt)";
    public const string MarkerLabel = "Marker";
    public const string MarkerSizeLabel = "Marker size (pt)";
    public const string AutoHint = "Blank colors and sizes preserve automatic point formatting.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 460;
    public const double DefaultDialogHeight = 420;

    public static IReadOnlyList<ChartMarkerSymbolOption> MarkerOptions =>
        ChartSeriesOptionsPlanner.MarkerOptions;

    private readonly ChartShape _chart;
    private int _seriesIndex;
    private int _pointIndex;
    private ThemeAwareColor? _fillColor;
    private ShapeFill? _fill;
    private ThemeAwareColor? _strokeColor;
    private double? _strokeWidthPt;
    private ChartMarkerSymbol? _markerSymbol;
    private double? _markerSizePt;

    private ChartPointOptionsPlanner(ChartShape chart)
    {
        _chart = chart;
        SetSeriesIndex(0);
    }

    public static ChartPointOptionsSurfacePlan BuildSurfacePlan() => new(
        CommandId,
        DialogTitle,
        SeriesLabel,
        PointLabel,
        FillColorLabel,
        StrokeColorLabel,
        StrokeWidthLabel,
        MarkerLabel,
        MarkerSizeLabel,
        AutoHint,
        OkLabel,
        CancelLabel);

    public static ChartPointOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartPointOptionsPlanner(chart);
    }

    public IReadOnlyList<ChartSeriesOption> SeriesOptions =>
        _chart.Series.Select((series, index) => new ChartSeriesOption(
            index,
            string.IsNullOrWhiteSpace(series.Name) ? $"Series {index + 1}" : series.Name)).ToArray();

    public IReadOnlyList<ChartPointOption> PointOptions =>
        Enumerable.Range(0, PointCount(_chart.Series.ElementAtOrDefault(_seriesIndex)))
            .Select(index => new ChartPointOption(index, PointLabelText(index)))
            .ToArray();

    public int SeriesIndex => _seriesIndex;
    public int PointIndex => _pointIndex;
    public string FillColorText => FormatColor(_fillColor);
    public string StrokeColorText => FormatColor(_strokeColor);
    public double? StrokeWidthPt => _strokeWidthPt;
    public ChartMarkerSymbol? MarkerSymbol => _markerSymbol;
    public double? MarkerSizePt => _markerSizePt;

    public void SetSeriesIndex(int index)
    {
        if (_chart.Series.Count == 0)
        {
            _seriesIndex = 0;
            SetPointIndex(0);
            return;
        }

        _seriesIndex = Math.Clamp(index, 0, _chart.Series.Count - 1);
        SetPointIndex(0);
    }

    public void SetPointIndex(int index)
    {
        var count = PointCount(_chart.Series.ElementAtOrDefault(_seriesIndex));
        _pointIndex = count == 0 ? 0 : Math.Clamp(index, 0, count - 1);
        LoadPoint();
    }

    public void SetFillColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _fillColor = null;
            return;
        }

        _fill = null;
        _fillColor = ParseColor(text, FillColorLabel);
    }
    public void SetStrokeColor(string? text) => _strokeColor = ParseColor(text, StrokeColorLabel);
    public void SetStrokeWidth(double? value) => _strokeWidthPt = value;
    public void SetMarkerSymbol(ChartMarkerSymbol? value) => _markerSymbol = value;
    public void SetMarkerSize(double? value) => _markerSizePt = value;

    public ChartPointOptions BuildCommitPlan() => new(
        _seriesIndex,
        _pointIndex,
        _fillColor,
        _fill,
        _strokeColor,
        _strokeWidthPt,
        _markerSymbol,
        _markerSizePt);

    public static ThemeAwareColor? ParseColor(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = text.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];
        if (normalized.Length != 6 ||
            !int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new FormatException($"{label} must be blank or a six-digit #RRGGBB color.");
        return new ThemeAwareColor(SrgbColor.FromRgb(rgb));
    }

    private void LoadPoint()
    {
        _fillColor = null;
        _fill = null;
        _strokeColor = null;
        _strokeWidthPt = null;
        _markerSymbol = null;
        _markerSizePt = null;

        var series = _chart.Series.ElementAtOrDefault(_seriesIndex);
        if (series is null)
            return;

        series.PointColors.TryGetValue(_pointIndex, out _fillColor);
        if (!series.PointStyles.TryGetValue(_pointIndex, out var style))
            return;

        _fillColor ??= style.FillColor;
        _fill = style.Fill;
        _strokeColor = style.StrokeColor;
        _strokeWidthPt = style.StrokeWidthPt;
        _markerSymbol = style.Marker?.Symbol;
        _markerSizePt = style.Marker?.SizePt;
    }

    private string PointLabelText(int index)
    {
        var categories = _chart.Categories;
        return index < categories.Count && !string.IsNullOrWhiteSpace(categories[index])
            ? $"{index + 1}: {categories[index]}"
            : $"Point {index + 1}";
    }

    private static int PointCount(ChartSeries? series) =>
        series is null ? 0 : Math.Max(series.Values.Count, series.XValues.Count);

    private static string FormatColor(ThemeAwareColor? color) =>
        color is null ? string.Empty : color.Resolved.ToString();
}
