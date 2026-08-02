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
    string PointDataLabelsLabel,
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
    string ExplosionLabel,
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
    public const string PointDataLabelsLabel = "Use point data labels";
    public const string ValueLabelsLabel = "Value labels";
    public const string PercentLabelsLabel = "Percentage labels";
    public const string CategoryLabelsLabel = "Category labels";
    public const string SeriesLabelsLabel = "Series labels";
    public const string LegendKeysLabel = "Legend keys";
    public const string BubbleSizeLabelsLabel = "Bubble size labels";
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
    public const string ExplosionLabel = "Explosion (%)";
    public const string AutoHint = "Blank colors and sizes preserve automatic point formatting.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 460;
    public const double DefaultDialogHeight = 700;

    public static IReadOnlyList<ChartMarkerSymbolOption> MarkerOptions =>
        ChartSeriesOptionsPlanner.MarkerOptions;

    private readonly ChartShape _chart;
    private int _seriesIndex;
    private int _pointIndex;
    private ThemeAwareColor? _fillColor;
    private ShapeFill? _fill;
    private ThemeAwareColor? _strokeColor;
    private double? _strokeWidthPt;
    private bool _usePointDataLabels;
    private bool _showValueLabels;
    private bool _showPercentLabels;
    private bool _showCategoryLabels;
    private bool _showSeriesLabels;
    private bool _showLegendKeys;
    private bool _showBubbleSize;
    private bool? _showLeaderLines;
    private DataLabelPosition _labelPosition = DataLabelPosition.OutsideEnd;
    private string _labelNumberFormat = string.Empty;
    private string _labelSeparator = string.Empty;
    private string _labelFontFamily = string.Empty;
    private double? _labelFontSizePt;
    private bool? _labelBold;
    private bool? _labelItalic;
    private ThemeAwareColor? _labelColor;
    private ChartMarkerSymbol? _markerSymbol;
    private double? _markerSizePt;
    private int? _explosionPercent;

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
        PointDataLabelsLabel,
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
        ExplosionLabel,
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
    public bool UsePointDataLabels => _usePointDataLabels;
    public bool ShowValueLabels => _showValueLabels;
    public bool ShowPercentLabels => _showPercentLabels;
    public bool ShowCategoryLabels => _showCategoryLabels;
    public bool ShowSeriesLabels => _showSeriesLabels;
    public bool ShowLegendKeys => _showLegendKeys;
    public bool ShowBubbleSize => _showBubbleSize;
    public DataLabelPosition LabelPosition => _labelPosition;
    public string LabelNumberFormat => _labelNumberFormat;
    public string LabelSeparator => _labelSeparator;
    public string LabelFontFamily => _labelFontFamily;
    public double? LabelFontSizePt => _labelFontSizePt;
    public bool? LabelBold => _labelBold;
    public bool? LabelItalic => _labelItalic;
    public string LabelColorText => FormatColor(_labelColor);
    public ChartMarkerSymbol? MarkerSymbol => _markerSymbol;
    public double? MarkerSizePt => _markerSizePt;
    public int? ExplosionPercent => _explosionPercent;

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
    public void SetUsePointDataLabels(bool value) => _usePointDataLabels = value;
    public void SetShowValueLabels(bool value) => _showValueLabels = value;
    public void SetShowPercentLabels(bool value) => _showPercentLabels = value;
    public void SetShowCategoryLabels(bool value) => _showCategoryLabels = value;
    public void SetShowSeriesLabels(bool value) => _showSeriesLabels = value;
    public void SetShowLegendKeys(bool value) => _showLegendKeys = value;
    public void SetShowBubbleSize(bool value) => _showBubbleSize = value;
    public void SetLabelPosition(DataLabelPosition value) => _labelPosition = value;
    public void SetLabelNumberFormat(string? value) => _labelNumberFormat = value ?? string.Empty;
    public void SetLabelSeparator(string? value) => _labelSeparator = value ?? string.Empty;
    public void SetLabelFontFamily(string? value) => _labelFontFamily = value?.Trim() ?? string.Empty;
    public void SetLabelFontSize(double? value) => _labelFontSizePt = value;
    public void SetLabelBold(bool? value) => _labelBold = value;
    public void SetLabelItalic(bool? value) => _labelItalic = value;
    public void SetLabelColor(string? text) => _labelColor = ParseColor(text, LabelColorLabel);
    public void SetMarkerSymbol(ChartMarkerSymbol? value) => _markerSymbol = value;
    public void SetMarkerSize(double? value) => _markerSizePt = value;
    public void SetExplosionPercent(int? value) => _explosionPercent = value.HasValue
        ? Math.Clamp(value.Value, 0, 100)
        : null;

    public ChartPointOptions BuildCommitPlan() => new(
        _seriesIndex,
        _pointIndex,
        _fillColor,
        _fill,
        _strokeColor,
        _strokeWidthPt,
        _markerSymbol,
        _markerSizePt,
        _usePointDataLabels
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
        ExplosionPercent: _explosionPercent);

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
        _usePointDataLabels = false;
        _showValueLabels = false;
        _showPercentLabels = false;
        _showCategoryLabels = false;
        _showSeriesLabels = false;
        _showLegendKeys = false;
        _showBubbleSize = false;
        _labelPosition = DataLabelPosition.OutsideEnd;
        _labelNumberFormat = string.Empty;
        _labelSeparator = string.Empty;
        _labelFontFamily = string.Empty;
        _labelFontSizePt = null;
        _labelBold = null;
        _labelItalic = null;
        _labelColor = null;
        _markerSymbol = null;
        _markerSizePt = null;
        _explosionPercent = null;

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
        var labels = style.DataLabels;
        _usePointDataLabels = labels is not null;
        _showValueLabels = labels?.ShowValue == true;
        _showPercentLabels = labels?.ShowPercent == true;
        _showCategoryLabels = labels?.ShowCategoryName == true;
        _showSeriesLabels = labels?.ShowSeriesName == true;
        _showLegendKeys = labels?.ShowLegendKey == true;
        _showBubbleSize = labels?.ShowBubbleSize == true;
        _showLeaderLines = labels?.ShowLeaderLines;
        _labelPosition = labels?.Position ?? DataLabelPosition.OutsideEnd;
        _labelNumberFormat = labels?.NumberFormat ?? string.Empty;
        _labelSeparator = labels?.Separator ?? string.Empty;
        _labelFontFamily = labels?.TextStyle?.FontFamily ?? string.Empty;
        _labelFontSizePt = labels?.TextStyle?.FontSizePt;
        _labelBold = labels?.TextStyle?.Bold;
        _labelItalic = labels?.TextStyle?.Italic;
        _labelColor = labels?.TextStyle?.Color;
        _markerSymbol = style.Marker?.Symbol;
        _markerSizePt = style.Marker?.SizePt;
        _explosionPercent = style.ExplosionPercent;
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

    private ChartTextStyle? BuildLabelTextStyle()
    {
        if (string.IsNullOrWhiteSpace(_labelFontFamily) &&
            !_labelFontSizePt.HasValue &&
            !_labelBold.HasValue &&
            !_labelItalic.HasValue &&
            _labelColor is null)
        {
            return null;
        }

        return new ChartTextStyle
        {
            FontFamily = string.IsNullOrWhiteSpace(_labelFontFamily) ? null : _labelFontFamily,
            FontSizePt = _labelFontSizePt,
            Bold = _labelBold,
            Italic = _labelItalic,
            Color = _labelColor,
        };
    }
}
