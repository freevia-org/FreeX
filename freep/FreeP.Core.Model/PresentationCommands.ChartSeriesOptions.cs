namespace FreeP.Core.Model;

/// <summary>Atomically updates supported formatting options for one chart series.</summary>
public sealed class SetChartSeriesOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartSeriesOptions _newOptions;

    private bool? _oldSmoothLine;
    private bool _oldOnSecondaryAxis;
    private ChartAxis? _oldSecondaryValueAxis;
    private ChartLineStyle? _oldLineStyle;
    private ChartMarkerStyle? _oldMarkerStyle;
    private ThemeAwareColor? _oldFillColor;
    private ShapeFill? _oldFill;

    public SetChartSeriesOptionsCommand(int slideIndex, uint shapeId, ChartSeriesOptions options)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Label => "Set Chart Series Options";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null || _newOptions.SeriesIndex < 0 || _newOptions.SeriesIndex >= chart.Series.Count)
            return;
        var series = chart.Series[_newOptions.SeriesIndex];

        _oldSmoothLine = series.SmoothLine;
        _oldOnSecondaryAxis = series.OnSecondaryAxis;
        _oldSecondaryValueAxis = CloneAxis(chart.SecondaryValueAxis);
        _oldLineStyle = CloneLineStyle(series.LineStyle);
        _oldMarkerStyle = CloneMarkerStyle(series.MarkerStyle);
        _oldFillColor = series.FillColor;
        _oldFill = series.Fill;

        series.SmoothLine = _newOptions.SmoothLine;
        series.OnSecondaryAxis = _newOptions.OnSecondaryAxis;
        if (chart.Series.Any(item => item.OnSecondaryAxis))
            chart.SecondaryValueAxis ??= new ChartAxis();
        else if (_oldSecondaryValueAxis is null)
            chart.SecondaryValueAxis = null;
        else
            chart.SecondaryValueAxis = CloneAxis(_oldSecondaryValueAxis);
        series.FillColor = _newOptions.FillColor;
        series.Fill = _newOptions.Fill;

        if (_newOptions.LineColor is not null ||
            _newOptions.LineWidthPt.HasValue ||
            _newOptions.LineDash != OutlineDash.Solid ||
            _newOptions.NoLine ||
            series.LineStyle is not null)
        {
            var line = series.LineStyle ?? new ChartLineStyle();
            line.Color = _newOptions.LineColor;
            line.WidthPt = _newOptions.LineWidthPt;
            line.Dash = _newOptions.LineDash;
            line.NoFill = _newOptions.NoLine;
            series.LineStyle = line;
        }

        if (_newOptions.MarkerSizePt.HasValue || _newOptions.MarkerSymbol != ChartMarkerSymbol.Auto || series.MarkerStyle is not null)
        {
            var marker = series.MarkerStyle ?? new ChartMarkerStyle();
            marker.Symbol = _newOptions.MarkerSymbol;
            marker.SizePt = _newOptions.MarkerSizePt;
            series.MarkerStyle = marker;
        }

        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null || _newOptions.SeriesIndex < 0 || _newOptions.SeriesIndex >= chart.Series.Count)
            return;
        var series = chart.Series[_newOptions.SeriesIndex];

        series.SmoothLine = _oldSmoothLine;
        series.OnSecondaryAxis = _oldOnSecondaryAxis;
        chart.SecondaryValueAxis = CloneAxis(_oldSecondaryValueAxis);
        series.LineStyle = CloneLineStyle(_oldLineStyle);
        series.MarkerStyle = CloneMarkerStyle(_oldMarkerStyle);
        series.FillColor = _oldFillColor;
        series.Fill = _oldFill;
        ChartHelper.MarkWorkbookDirty(chart);
    }

    private static ChartLineStyle? CloneLineStyle(ChartLineStyle? source) => source is null
        ? null
        : new ChartLineStyle
        {
            Color = source.Color,
            WidthPt = source.WidthPt,
            Dash = source.Dash,
            NoFill = source.NoFill,
        };

    private static ChartMarkerStyle? CloneMarkerStyle(ChartMarkerStyle? source) => source is null
        ? null
        : new ChartMarkerStyle
        {
            Symbol = source.Symbol,
            SizePt = source.SizePt,
            FillColor = source.FillColor,
            Fill = source.Fill,
            StrokeColor = source.StrokeColor,
            StrokeWidthPt = source.StrokeWidthPt,
            NoFill = source.NoFill,
            NoStroke = source.NoStroke,
        };

    private static ChartAxis? CloneAxis(ChartAxis? source) => source is null
        ? null
        : new ChartAxis
        {
            Title = source.Title,
            NumberFormatCode = source.NumberFormatCode,
            NumberFormatSourceLinked = source.NumberFormatSourceLinked,
            Min = source.Min,
            Max = source.Max,
            MajorUnit = source.MajorUnit,
            MinorUnit = source.MinorUnit,
            HasMajorGridlines = source.HasMajorGridlines,
            MajorTickMark = source.MajorTickMark,
            MinorTickMark = source.MinorTickMark,
            TickLabelPosition = source.TickLabelPosition,
            LabelOffsetPercent = source.LabelOffsetPercent,
            NoMultiLevelLabels = source.NoMultiLevelLabels,
            CrossBetween = source.CrossBetween,
            AutoCrossing = source.AutoCrossing,
            LabelAlignment = source.LabelAlignment,
            Crosses = source.Crosses,
            CrossesAt = source.CrossesAt,
            Delete = source.Delete,
        };
}
