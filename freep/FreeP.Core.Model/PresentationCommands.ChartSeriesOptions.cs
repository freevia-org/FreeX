namespace FreeP.Core.Model;

/// <summary>Atomically updates supported formatting options for one chart series.</summary>
public sealed class SetChartSeriesOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartSeriesOptions _newOptions;

    private bool? _oldSmoothLine;
    private bool _oldOnSecondaryAxis;
    private ChartLineStyle? _oldLineStyle;
    private ChartMarkerStyle? _oldMarkerStyle;

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
        _oldLineStyle = CloneLineStyle(series.LineStyle);
        _oldMarkerStyle = CloneMarkerStyle(series.MarkerStyle);

        series.SmoothLine = _newOptions.SmoothLine;
        series.OnSecondaryAxis = _newOptions.OnSecondaryAxis;

        if (_newOptions.LineWidthPt.HasValue || series.LineStyle is not null)
        {
            var line = series.LineStyle ?? new ChartLineStyle();
            line.WidthPt = _newOptions.LineWidthPt;
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
        series.LineStyle = CloneLineStyle(_oldLineStyle);
        series.MarkerStyle = CloneMarkerStyle(_oldMarkerStyle);
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
}
