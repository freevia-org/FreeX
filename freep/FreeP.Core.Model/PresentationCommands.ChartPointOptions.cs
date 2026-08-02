namespace FreeP.Core.Model;

/// <summary>Atomically updates supported formatting overrides for one chart point.</summary>
public sealed class SetChartPointOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartPointOptions _newOptions;

    private bool _hadPointColor;
    private ThemeAwareColor? _oldPointColor;
    private ChartPointStyle? _oldPointStyle;

    public SetChartPointOptionsCommand(int slideIndex, uint shapeId, ChartPointOptions options)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Label => "Set Chart Point Options";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        var series = ChartHelper.FindFormattingSeries(
            p, _slideIndex, _shapeId, _newOptions.SeriesIndex);
        if (chart is null || series is null || _newOptions.PointIndex < 0)
            return;

        _hadPointColor = series.PointColors.TryGetValue(_newOptions.PointIndex, out _oldPointColor);
        series.PointStyles.TryGetValue(_newOptions.PointIndex, out var currentStyle);
        _oldPointStyle = ClonePointStyle(currentStyle);

        if (_newOptions.FillColor is not null)
            series.PointColors[_newOptions.PointIndex] = _newOptions.FillColor;
        else
            series.PointColors.Remove(_newOptions.PointIndex);

        var style = ClonePointStyle(currentStyle) ?? new ChartPointStyle();
        style.FillColor = _newOptions.FillColor;
        style.Fill = _newOptions.Fill;
        style.StrokeColor = _newOptions.StrokeColor;
        style.StrokeWidthPt = _newOptions.StrokeWidthPt;
        style.DataLabels = CloneDataLabels(_newOptions.DataLabels);
        style.ExplosionPercent = _newOptions.ExplosionPercent.HasValue
            ? Math.Clamp(_newOptions.ExplosionPercent.Value, 0, 100)
            : null;
        if (style.DataLabels is not null)
            style.DataLabels.ShowLeaderLines = _newOptions.ShowLeaderLines ?? style.DataLabels.ShowLeaderLines;

        var symbol = _newOptions.MarkerSymbol;
        if (symbol is not null || _newOptions.MarkerSizePt is not null)
        {
            var marker = CloneMarkerStyle(style.Marker) ?? new ChartMarkerStyle();
            marker.Symbol = symbol;
            marker.SizePt = _newOptions.MarkerSizePt;
            style.Marker = marker;
        }
        else
        {
            style.Marker = null;
        }

        if (HasPointStyleContent(style))
            series.PointStyles[_newOptions.PointIndex] = style;
        else
            series.PointStyles.Remove(_newOptions.PointIndex);

        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        var series = chart is null
            ? null
            : ChartHelper.FindFormattingSeries(p, _slideIndex, _shapeId, _newOptions.SeriesIndex);
        if (chart is null || series is null || _newOptions.PointIndex < 0)
            return;

        if (_hadPointColor)
            series.PointColors[_newOptions.PointIndex] = _oldPointColor!;
        else
            series.PointColors.Remove(_newOptions.PointIndex);

        if (_oldPointStyle is not null)
            series.PointStyles[_newOptions.PointIndex] = ClonePointStyle(_oldPointStyle)!;
        else
            series.PointStyles.Remove(_newOptions.PointIndex);

        ChartHelper.MarkWorkbookDirty(chart);
    }

    private static bool HasPointStyleContent(ChartPointStyle style) =>
        style.FillColor is not null ||
        style.Fill is not null ||
        style.StrokeColor is not null ||
        style.StrokeWidthPt is not null ||
        style.DataLabels is not null ||
        style.ExplosionPercent is not null ||
        style.Marker is not null;

    private static ChartPointStyle? ClonePointStyle(ChartPointStyle? source) => source is null
        ? null
        : new ChartPointStyle
        {
            DataLabels = CloneDataLabels(source.DataLabels),
            FillColor = source.FillColor,
            Fill = source.Fill,
            StrokeColor = source.StrokeColor,
            StrokeWidthPt = source.StrokeWidthPt,
            ExplosionPercent = source.ExplosionPercent,
            Marker = CloneMarkerStyle(source.Marker),
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

    private static ChartDataLabels? CloneDataLabels(ChartDataLabels? source) => source is null
        ? null
        : new ChartDataLabels
        {
            Delete = source.Delete,
            ShowValue = source.ShowValue,
            ShowPercent = source.ShowPercent,
            ShowCategoryName = source.ShowCategoryName,
                ShowSeriesName = source.ShowSeriesName,
                ShowLegendKey = source.ShowLegendKey,
                ShowBubbleSize = source.ShowBubbleSize,
                ShowLeaderLines = source.ShowLeaderLines,
            Position = source.Position,
            NumberFormat = source.NumberFormat,
            Separator = source.Separator,
            TextStyle = source.TextStyle is null
                ? null
                : new ChartTextStyle
                {
                    IsImplicitDefault = source.TextStyle.IsImplicitDefault,
                    FontSizePt = source.TextStyle.FontSizePt,
                    Bold = source.TextStyle.Bold,
                    Italic = source.TextStyle.Italic,
                    Color = source.TextStyle.Color,
                    FontFamily = source.TextStyle.FontFamily,
                },
        };
}
