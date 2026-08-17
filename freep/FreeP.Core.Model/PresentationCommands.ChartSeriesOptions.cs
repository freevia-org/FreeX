namespace FreeP.Core.Model;

/// <summary>Atomically updates supported formatting options for one chart series.</summary>
public sealed class SetChartSeriesOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartSeriesOptions _newOptions;

    private bool? _oldSmoothLine;
    private bool _oldOnSecondaryAxis;
    private bool? _oldInvertIfNegative;
    private ChartType? _oldOverrideChartType;
    private ChartAxis? _oldSecondaryValueAxis;
    private ChartLineStyle? _oldLineStyle;
    private ChartMarkerStyle? _oldMarkerStyle;
    private ThemeAwareColor? _oldFillColor;
    private ShapeFill? _oldFill;
    private ChartDataLabels? _oldDataLabels;
    private ChartErrorBars? _oldErrorBars;
    private ChartTrendline? _oldTrendline;

    public SetChartSeriesOptionsCommand(int slideIndex, uint shapeId, ChartSeriesOptions options)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Label => "Set Chart Series Options";

    public int EstimatedBytes => PresentationCommandSizeEstimator.Combine(new[]
    {
        PresentationCommandSizeEstimator.EstimateBytes(_newOptions.Fill),
        PresentationCommandSizeEstimator.EstimateBytes(_oldFill),
    });

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        if (chart is null || _newOptions.SeriesIndex < 0 || _newOptions.SeriesIndex >= chart.Series.Count)
            return;
        var series = chart.Series[_newOptions.SeriesIndex];

        _oldSmoothLine = series.SmoothLine;
        _oldOnSecondaryAxis = series.OnSecondaryAxis;
        _oldInvertIfNegative = series.InvertIfNegative;
        _oldOverrideChartType = series.OverrideChartType;
        _oldSecondaryValueAxis = CloneAxis(chart.SecondaryValueAxis);
        _oldLineStyle = CloneLineStyle(series.LineStyle);
        _oldMarkerStyle = CloneMarkerStyle(series.MarkerStyle);
        _oldFillColor = series.FillColor;
        _oldFill = series.Fill;
        _oldDataLabels = CloneDataLabels(series.DataLabels);
        _oldErrorBars = CloneErrorBars(series.ErrorBars);
        _oldTrendline = CloneTrendline(series.Trendline);

        series.SmoothLine = _newOptions.SmoothLine;
        series.InvertIfNegative = _newOptions.InvertIfNegative;
        // The current writer emits combo overrides as a secondary line plot group.
        // Keep the model internally valid even when the dialog only changes the type.
        series.OnSecondaryAxis = _newOptions.OnSecondaryAxis || _newOptions.OverrideChartType.HasValue;
        series.OverrideChartType = _newOptions.OverrideChartType;
        if (chart.Series.Any(item => item.OnSecondaryAxis))
            chart.SecondaryValueAxis ??= new ChartAxis();
        else if (_oldSecondaryValueAxis is null)
            chart.SecondaryValueAxis = null;
        else
            chart.SecondaryValueAxis = CloneAxis(_oldSecondaryValueAxis);
        series.FillColor = _newOptions.FillColor;
        series.Fill = _newOptions.Fill;
        series.DataLabels = CloneDataLabels(_newOptions.DataLabels);
        series.ErrorBars = CloneErrorBars(_newOptions.ErrorBars);
        series.Trendline = CloneTrendline(_newOptions.Trendline);

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
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        if (chart is null || _newOptions.SeriesIndex < 0 || _newOptions.SeriesIndex >= chart.Series.Count)
            return;
        var series = chart.Series[_newOptions.SeriesIndex];

        series.SmoothLine = _oldSmoothLine;
        series.OnSecondaryAxis = _oldOnSecondaryAxis;
        series.InvertIfNegative = _oldInvertIfNegative;
        series.OverrideChartType = _oldOverrideChartType;
        chart.SecondaryValueAxis = CloneAxis(_oldSecondaryValueAxis);
        series.LineStyle = CloneLineStyle(_oldLineStyle);
        series.MarkerStyle = CloneMarkerStyle(_oldMarkerStyle);
        series.FillColor = _oldFillColor;
        series.Fill = _oldFill;
        series.DataLabels = CloneDataLabels(_oldDataLabels);
        series.ErrorBars = CloneErrorBars(_oldErrorBars);
        series.Trendline = CloneTrendline(_oldTrendline);
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

    private static ChartDataLabels? CloneDataLabels(ChartDataLabels? source) => source is null
        ? null
        : new ChartDataLabels
        {
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

    private static ChartErrorBars? CloneErrorBars(ChartErrorBars? source) => source is null
        ? null
        : new ChartErrorBars
        {
            Direction = source.Direction,
            BarType = source.BarType,
            ValueType = source.ValueType,
            Value = source.Value,
            NoEndCap = source.NoEndCap,
        };

    private static ChartTrendline? CloneTrendline(ChartTrendline? source) => source is null
        ? null
        : new ChartTrendline
        {
            Type = source.Type,
            PolynomialOrder = source.PolynomialOrder,
            MovingAveragePeriod = source.MovingAveragePeriod,
            Forward = source.Forward,
            Backward = source.Backward,
            DisplayEquation = source.DisplayEquation,
            DisplayRSquared = source.DisplayRSquared,
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
            HasMinorGridlines = source.HasMinorGridlines,
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
