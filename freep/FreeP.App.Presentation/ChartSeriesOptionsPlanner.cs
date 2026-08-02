using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartSeriesOption(int Index, string Label);
public sealed record ChartSeriesChartTypeOption(ChartType? Value, string Label);

public sealed record ChartMarkerSymbolOption(ChartMarkerSymbol Value, string Label);

public sealed record ChartDashOption(OutlineDash Value, string Label);

public sealed record ChartErrorDirectionOption(ChartErrorDirection Value, string Label);
public sealed record ChartErrorBarTypeOption(ChartErrorBarType Value, string Label);
public sealed record ChartErrorValueTypeOption(ChartErrorValueType Value, string Label);
public sealed record ChartTrendlineTypeOption(ChartTrendlineType Value, string Label);

public sealed record ChartSeriesOptionsSurfacePlan(
    string CommandId,
    string Title,
    string SeriesLabel,
    string SeriesChartTypeLabel,
    string SmoothLineLabel,
    string SecondaryAxisLabel,
    string InvertIfNegativeLabel,
    string LineWidthLabel,
    string LineColorLabel,
    string LineDashLabel,
    string NoLineLabel,
    string FillColorLabel,
    string SeriesDataLabelsLabel,
    string ValueLabelsLabel,
    string PercentLabelsLabel,
    string CategoryLabelsLabel,
    string SeriesLabelsLabel,
    string LegendKeysLabel,
    string BubbleSizeLabelsLabel,
    string LabelPositionLabel,
    string NumberFormatLabel,
    string SeparatorLabel,
    string FontFamilyLabel,
    string FontSizeLabel,
    string BoldLabel,
    string ItalicLabel,
    string LabelColorLabel,
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
    public const string SeriesChartTypeLabel = "Series chart type";
    public const string SmoothLineLabel = "Smooth line";
    public const string SecondaryAxisLabel = "Plot on secondary axis";
    public const string InvertIfNegativeLabel = "Invert negative values";
    public const string LineWidthLabel = "Line width (pt)";
    public const string LineColorLabel = "Line color (#RRGGBB)";
    public const string LineDashLabel = "Line dash";
    public const string NoLineLabel = "No line";
    public const string FillColorLabel = "Fill color (#RRGGBB)";
    public const string SeriesDataLabelsLabel = "Use series data labels";
    public const string ValueLabelsLabel = "Value labels";
    public const string PercentLabelsLabel = "Percentage labels";
    public const string CategoryLabelsLabel = "Category labels";
    public const string SeriesLabelsLabel = "Series labels";
    public const string LegendKeysLabel = "Legend keys";
    public const string BubbleSizeLabelsLabel = "Bubble size labels";
    public const string ErrorBarsLabel = "Error bars";
    public const string ErrorDirectionLabel = "Direction";
    public const string ErrorBarTypeLabel = "Error amount";
    public const string ErrorValueTypeLabel = "Value type";
    public const string ErrorValueLabel = "Value";
    public const string ErrorNoEndCapLabel = "No end cap";
    public const string TrendlineLabel = "Trendline";
    public const string TrendlineTypeLabel = "Trendline type";
    public const string TrendlineOrderLabel = "Polynomial order";
    public const string TrendlinePeriodLabel = "Moving average period";
    public const string TrendlineForwardLabel = "Forecast forward";
    public const string TrendlineBackwardLabel = "Forecast backward";
    public const string TrendlineEquationLabel = "Display equation";
    public const string TrendlineRSquaredLabel = "Display R-squared";
    public const string LabelPositionLabel = "Label position";
    public const string NumberFormatLabel = "Number format";
    public const string SeparatorLabel = "Separator";
    public const string FontFamilyLabel = "Font family";
    public const string FontSizeLabel = "Font size (pt)";
    public const string BoldLabel = "Bold";
    public const string ItalicLabel = "Italic";
    public const string LabelColorLabel = "Label color (#RRGGBB)";
    public const string MarkerLabel = "Marker";
    public const string MarkerSizeLabel = "Marker size (pt)";
    public const string AutoHint = "Blank values preserve automatic series formatting.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 440;
    public const double DefaultDialogHeight = 700;

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

    public static IReadOnlyList<ChartSeriesChartTypeOption> SeriesChartTypeOptions { get; } =
    [
        new(null, "Same as chart"),
        new(ChartType.Line, "Line"),
        new(ChartType.LineMarkers, "Line with markers"),
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

    public static IReadOnlyList<ChartErrorDirectionOption> ErrorDirectionOptions { get; } =
    [
        new(ChartErrorDirection.Y, "Vertical (Y)"),
        new(ChartErrorDirection.X, "Horizontal (X)"),
    ];

    public static IReadOnlyList<ChartErrorBarTypeOption> ErrorBarTypeOptions { get; } =
    [
        new(ChartErrorBarType.Both, "Plus and minus"),
        new(ChartErrorBarType.Minus, "Minus only"),
        new(ChartErrorBarType.Plus, "Plus only"),
    ];

    public static IReadOnlyList<ChartErrorValueTypeOption> ErrorValueTypeOptions { get; } =
    [
        new(ChartErrorValueType.Fixed, "Fixed value"),
        new(ChartErrorValueType.Percentage, "Percentage"),
    ];

    public static IReadOnlyList<ChartTrendlineTypeOption> TrendlineTypeOptions { get; } =
    [
        new(ChartTrendlineType.Linear, "Linear"),
        new(ChartTrendlineType.Exponential, "Exponential"),
        new(ChartTrendlineType.Logarithmic, "Logarithmic"),
        new(ChartTrendlineType.Polynomial, "Polynomial"),
        new(ChartTrendlineType.Power, "Power"),
        new(ChartTrendlineType.MovingAverage, "Moving average"),
    ];

    private readonly ChartShape _chart;
    private int _seriesIndex;
    private bool _smoothLine;
    private bool _onSecondaryAxis;
    private bool? _invertIfNegative;
    private ChartType? _overrideChartType;
    private double? _lineWidthPt;
    private ThemeAwareColor? _lineColor;
    private OutlineDash _lineDash;
    private bool _noLine;
    private ThemeAwareColor? _fillColor;
    private ShapeFill? _fill;
    private bool _useSeriesDataLabels;
    private bool _showValueLabels;
    private bool _showPercentLabels;
    private bool _showCategoryLabels;
    private bool _showSeriesLabels;
    private bool _showLegendKeys;
    private bool _showBubbleSize;
    private bool? _showLeaderLines;
    private bool _errorBarsEnabled;
    private ChartErrorDirection _errorDirection;
    private ChartErrorBarType _errorBarType;
    private ChartErrorValueType _errorValueType;
    private double _errorValue;
    private bool _errorNoEndCap;
    private bool _trendlineEnabled;
    private ChartTrendlineType _trendlineType;
    private int? _trendlineOrder;
    private int? _trendlinePeriod;
    private double? _trendlineForward;
    private double? _trendlineBackward;
    private bool _trendlineEquation;
    private bool _trendlineRSquared;
    private DataLabelPosition _labelPosition = DataLabelPosition.OutsideEnd;
    private string _labelNumberFormat = string.Empty;
    private string _labelSeparator = string.Empty;
    private string _labelFontFamily = string.Empty;
    private double? _labelFontSizePt;
    private bool? _labelBold;
    private bool? _labelItalic;
    private ThemeAwareColor? _labelColor;
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
            SeriesChartTypeLabel,
            SmoothLineLabel,
            SecondaryAxisLabel,
            InvertIfNegativeLabel,
            LineWidthLabel,
            LineColorLabel,
            LineDashLabel,
            NoLineLabel,
            FillColorLabel,
            SeriesDataLabelsLabel,
            ValueLabelsLabel,
            PercentLabelsLabel,
            CategoryLabelsLabel,
            SeriesLabelsLabel,
            LegendKeysLabel,
            BubbleSizeLabelsLabel,
            LabelPositionLabel,
            NumberFormatLabel,
            SeparatorLabel,
            FontFamilyLabel,
            FontSizeLabel,
            BoldLabel,
            ItalicLabel,
            LabelColorLabel,
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
    public bool? InvertIfNegative => _invertIfNegative;
    public ChartType? OverrideChartType => _overrideChartType;
    public double? LineWidthPt => _lineWidthPt;
    public string LineColorText => FormatColor(_lineColor);
    public OutlineDash LineDash => _lineDash;
    public bool NoLine => _noLine;
    public string FillColorText => FormatColor(_fillColor);
    public bool UseSeriesDataLabels => _useSeriesDataLabels;
    public bool ShowValueLabels => _showValueLabels;
    public bool ShowPercentLabels => _showPercentLabels;
    public bool ShowCategoryLabels => _showCategoryLabels;
    public bool ShowSeriesLabels => _showSeriesLabels;
    public bool ShowLegendKeys => _showLegendKeys;
    public bool ShowBubbleSize => _showBubbleSize;
    public bool ErrorBarsEnabled => _errorBarsEnabled;
    public ChartErrorDirection ErrorDirection => _errorDirection;
    public ChartErrorBarType ErrorBarType => _errorBarType;
    public ChartErrorValueType ErrorValueType => _errorValueType;
    public double ErrorValue => _errorValue;
    public bool ErrorNoEndCap => _errorNoEndCap;
    public bool TrendlineEnabled => _trendlineEnabled;
    public ChartTrendlineType TrendlineType => _trendlineType;
    public int? TrendlineOrder => _trendlineOrder;
    public int? TrendlinePeriod => _trendlinePeriod;
    public double? TrendlineForward => _trendlineForward;
    public double? TrendlineBackward => _trendlineBackward;
    public bool TrendlineEquation => _trendlineEquation;
    public bool TrendlineRSquared => _trendlineRSquared;
    public DataLabelPosition LabelPosition => _labelPosition;
    public string LabelNumberFormat => _labelNumberFormat;
    public string LabelSeparator => _labelSeparator;
    public string LabelFontFamily => _labelFontFamily;
    public double? LabelFontSizePt => _labelFontSizePt;
    public bool? LabelBold => _labelBold;
    public bool? LabelItalic => _labelItalic;
    public string LabelColorText => FormatColor(_labelColor);
    public ChartMarkerSymbol MarkerSymbol => _markerSymbol;
    public double? MarkerSizePt => _markerSizePt;

    public void SetSeriesIndex(int index)
    {
        if (_chart.Series.Count == 0)
        {
            _seriesIndex = 0;
            _smoothLine = false;
            _onSecondaryAxis = false;
            _invertIfNegative = null;
            _overrideChartType = null;
            _lineWidthPt = null;
            _lineColor = null;
            _lineDash = OutlineDash.Solid;
            _noLine = false;
            _fillColor = null;
            _fill = null;
            _useSeriesDataLabels = false;
            _showValueLabels = false;
            _showPercentLabels = false;
            _showCategoryLabels = false;
            _showSeriesLabels = false;
            _showLegendKeys = false;
            _showBubbleSize = false;
            _errorBarsEnabled = false;
            _errorDirection = ChartErrorDirection.Y;
            _errorBarType = ChartErrorBarType.Both;
            _errorValueType = ChartErrorValueType.Fixed;
            _errorValue = 0;
            _errorNoEndCap = false;
            _trendlineEnabled = false;
            _trendlineType = ChartTrendlineType.Linear;
            _trendlineOrder = null;
            _trendlinePeriod = null;
            _trendlineForward = null;
            _trendlineBackward = null;
            _trendlineEquation = false;
            _trendlineRSquared = false;
            _labelPosition = DataLabelPosition.OutsideEnd;
            _labelNumberFormat = string.Empty;
            _labelSeparator = string.Empty;
            _labelFontFamily = string.Empty;
            _labelFontSizePt = null;
            _labelBold = null;
            _labelItalic = null;
            _labelColor = null;
            _markerSymbol = ChartMarkerSymbol.Auto;
            _markerSizePt = null;
            return;
        }

        _seriesIndex = Math.Clamp(index, 0, _chart.Series.Count - 1);
        var series = _chart.Series[_seriesIndex];
        _smoothLine = series.SmoothLine ?? false;
        _onSecondaryAxis = series.OnSecondaryAxis;
        _invertIfNegative = series.InvertIfNegative;
        _overrideChartType = series.OverrideChartType;
        _lineWidthPt = series.LineStyle?.WidthPt;
        _lineColor = series.LineStyle?.Color;
        _lineDash = series.LineStyle?.Dash ?? OutlineDash.Solid;
        _noLine = series.LineStyle?.NoFill == true;
        _fill = series.Fill;
        _fillColor = series.FillColor ?? (series.Fill is ShapeFill.Solid solid ? solid.Color : null);
        var labels = series.DataLabels;
        _useSeriesDataLabels = labels is not null;
        _showValueLabels = labels?.ShowValue == true;
        _showPercentLabels = labels?.ShowPercent == true;
        _showCategoryLabels = labels?.ShowCategoryName == true;
        _showSeriesLabels = labels?.ShowSeriesName == true;
        _showLegendKeys = labels?.ShowLegendKey == true;
        _showBubbleSize = labels?.ShowBubbleSize == true;
        _showLeaderLines = labels?.ShowLeaderLines;
        var errorBars = series.ErrorBars;
        _errorBarsEnabled = errorBars is not null;
        _errorDirection = errorBars?.Direction ?? ChartErrorDirection.Y;
        _errorBarType = errorBars?.BarType ?? ChartErrorBarType.Both;
        _errorValueType = errorBars?.ValueType ?? ChartErrorValueType.Fixed;
        _errorValue = errorBars?.Value ?? 0;
        _errorNoEndCap = errorBars?.NoEndCap == true;
        var trendline = series.Trendline;
        _trendlineEnabled = trendline is not null;
        _trendlineType = trendline?.Type ?? ChartTrendlineType.Linear;
        _trendlineOrder = trendline?.PolynomialOrder;
        _trendlinePeriod = trendline?.MovingAveragePeriod;
        _trendlineForward = trendline?.Forward;
        _trendlineBackward = trendline?.Backward;
        _trendlineEquation = trendline?.DisplayEquation == true;
        _trendlineRSquared = trendline?.DisplayRSquared == true;
        _labelPosition = labels?.Position ?? DataLabelPosition.OutsideEnd;
        _labelNumberFormat = labels?.NumberFormat ?? string.Empty;
        _labelSeparator = labels?.Separator ?? string.Empty;
        _labelFontFamily = labels?.TextStyle?.FontFamily ?? string.Empty;
        _labelFontSizePt = labels?.TextStyle?.FontSizePt;
        _labelBold = labels?.TextStyle?.Bold;
        _labelItalic = labels?.TextStyle?.Italic;
        _labelColor = labels?.TextStyle?.Color;
        _markerSymbol = series.MarkerStyle?.Symbol ?? ChartMarkerSymbol.Auto;
        _markerSizePt = series.MarkerStyle?.SizePt;
    }

    public void SetSmoothLine(bool value) => _smoothLine = value;
    public void SetOnSecondaryAxis(bool value) => _onSecondaryAxis = value;
    public void SetInvertIfNegative(bool? value) => _invertIfNegative = value;
    public void SetOverrideChartType(ChartType? value) => _overrideChartType = value switch
    {
        null or ChartType.Line or ChartType.LineMarkers => value,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Only line combo overrides are supported."),
    };
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
    public void SetUseSeriesDataLabels(bool value) => _useSeriesDataLabels = value;
    public void SetShowValueLabels(bool value) => _showValueLabels = value;
    public void SetShowPercentLabels(bool value) => _showPercentLabels = value;
    public void SetShowCategoryLabels(bool value) => _showCategoryLabels = value;
    public void SetShowSeriesLabels(bool value) => _showSeriesLabels = value;
    public void SetShowLegendKeys(bool value) => _showLegendKeys = value;
    public void SetShowBubbleSize(bool value) => _showBubbleSize = value;
    public void SetErrorBarsEnabled(bool value) => _errorBarsEnabled = value;
    public void SetErrorDirection(ChartErrorDirection value) => _errorDirection = value;
    public void SetErrorBarType(ChartErrorBarType value) => _errorBarType = value;
    public void SetErrorValueType(ChartErrorValueType value) => _errorValueType = value;
    public void SetErrorValue(double value) => _errorValue = Math.Max(0, value);
    public void SetErrorNoEndCap(bool value) => _errorNoEndCap = value;
    public void SetTrendlineEnabled(bool value) => _trendlineEnabled = value;
    public void SetTrendlineType(ChartTrendlineType value) => _trendlineType = value;
    public void SetTrendlineOrder(int? value) => _trendlineOrder = value is null ? null : Math.Clamp(value.Value, 2, 6);
    public void SetTrendlinePeriod(int? value) => _trendlinePeriod = value is null ? null : Math.Max(2, value.Value);
    public void SetTrendlineForward(double? value) => _trendlineForward = NormalizeForecast(value);
    public void SetTrendlineBackward(double? value) => _trendlineBackward = NormalizeForecast(value);
    public void SetTrendlineEquation(bool value) => _trendlineEquation = value;
    public void SetTrendlineRSquared(bool value) => _trendlineRSquared = value;
    public void SetLabelPosition(DataLabelPosition value) => _labelPosition = value;
    public void SetLabelNumberFormat(string? value) => _labelNumberFormat = value ?? string.Empty;
    public void SetLabelSeparator(string? value) => _labelSeparator = value ?? string.Empty;
    public void SetLabelFontFamily(string? value) => _labelFontFamily = value?.Trim() ?? string.Empty;
    public void SetLabelFontSize(double? value) => _labelFontSizePt = value;
    public void SetLabelBold(bool? value) => _labelBold = value;
    public void SetLabelItalic(bool? value) => _labelItalic = value;
    public void SetLabelColor(string? text) => _labelColor = string.IsNullOrWhiteSpace(text)
        ? null
        : ChartPointOptionsPlanner.ParseColor(text, LabelColorLabel);
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
        _noLine,
        _useSeriesDataLabels
            ? new ChartDataLabels
            {
                ShowValue = _showValueLabels,
                ShowPercent = _showPercentLabels,
                ShowCategoryName = _showCategoryLabels,
                ShowSeriesName = _showSeriesLabels,
                ShowLegendKey = _showLegendKeys,
                ShowBubbleSize = _showBubbleSize,
                ShowLeaderLines = _showLeaderLines,
                Position = _labelPosition,
                NumberFormat = string.IsNullOrWhiteSpace(_labelNumberFormat) ? null : _labelNumberFormat,
                Separator = string.IsNullOrEmpty(_labelSeparator) ? null : _labelSeparator,
                TextStyle = BuildLabelTextStyle(),
            }
            : null,
        ErrorBars: _errorBarsEnabled
            ? new ChartErrorBars
            {
                Direction = _errorDirection,
                BarType = _errorBarType,
                ValueType = _errorValueType,
                Value = _errorValue,
                NoEndCap = _errorNoEndCap,
            }
            : null,
        Trendline: _trendlineEnabled
            ? new ChartTrendline
            {
                Type = _trendlineType,
                PolynomialOrder = _trendlineType == ChartTrendlineType.Polynomial ? _trendlineOrder : null,
                MovingAveragePeriod = _trendlineType == ChartTrendlineType.MovingAverage ? _trendlinePeriod : null,
                Forward = _trendlineForward,
                Backward = _trendlineBackward,
                DisplayEquation = _trendlineEquation,
                DisplayRSquared = _trendlineRSquared,
            }
            : null,
        OverrideChartType: _overrideChartType,
        InvertIfNegative: _invertIfNegative);

    private static string FormatColor(ThemeAwareColor? color) =>
        color is null ? string.Empty : color.Resolved.ToString();

    private static double? NormalizeForecast(double? value) =>
        value is { } number && double.IsFinite(number) && number >= 0 ? number : null;

    private ChartTextStyle? BuildLabelTextStyle()
    {
        if (string.IsNullOrWhiteSpace(_labelFontFamily) &&
            !_labelFontSizePt.HasValue &&
            !_labelBold.HasValue &&
            !_labelItalic.HasValue &&
            _labelColor is null)
            return null;

        return new ChartTextStyle
        {
            FontFamily = string.IsNullOrWhiteSpace(_labelFontFamily) ? null : _labelFontFamily,
            FontSizePt = _labelFontSizePt,
            Bold = _labelBold,
            Italic = _labelItalic,
            Color = _labelColor,
        };
    }

    private static string SeriesLabelText(int index, ChartSeries series) =>
        string.IsNullOrWhiteSpace(series.Name) ? $"Series {index + 1}" : series.Name;
}
