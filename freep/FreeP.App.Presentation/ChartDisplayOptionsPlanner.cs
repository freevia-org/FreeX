using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartDisplayLegendOption(LegendPosition? Value, string Label);

public sealed record ChartDisplayLabelPositionOption(DataLabelPosition Value, string Label);

public sealed record ChartDisplayBlanksOption(ChartDisplayBlanksAs? Value, string Label);

public sealed record ChartDisplayOptionsSurfacePlan(
    string CommandId,
    string Title,
    string ChartTitleLabel,
    string LegendLabel,
    string ValueLabelsLabel,
    string PercentLabelsLabel,
    string CategoryLabelsLabel,
    string SeriesLabelsLabel,
    string LegendKeysLabel,
    string BubbleSizeLabelsLabel,
    string NumberFormatLabel,
    string SeparatorLabel,
    string FontFamilyLabel,
    string FontSizeLabel,
    string BoldLabel,
    string ItalicLabel,
    string LabelColorLabel,
    string LabelPositionLabel,
    string CategoryGridlinesLabel,
    string ValueGridlinesLabel,
    string BarGapWidthLabel,
    string BarOverlapLabel,
    string DisplayBlanksAsLabel,
    string ShowDataLabelsOverMaximumLabel,
    string VaryColorsLabel,
    string LegendOverlayLabel,
    string HighLowLinesLabel,
    string PlotHint,
    string OkLabel,
    string CancelLabel);

/// <summary>
/// Working-copy planner for the small set of chart display controls common to PowerPoint's
/// chart design/format workflow. The live chart is changed only when the host commits.
/// </summary>
public sealed class ChartDisplayOptionsPlanner
{
    public const string CommandId = "freep.chart.format-options";
    public const string DialogTitle = "Chart Options";
    public const string ChartTitleLabel = "Chart Title";
    public const string LegendLabel = "Legend";
    public const string ValueLabelsLabel = "Value Labels";
    public const string PercentLabelsLabel = "Percentage Labels";
    public const string CategoryLabelsLabel = "Category Labels";
    public const string SeriesLabelsLabel = "Series Labels";
    public const string LegendKeysLabel = "Legend Keys";
    public const string BubbleSizeLabelsLabel = "Bubble size labels";
    public const string NumberFormatLabel = "Number Format";
    public const string SeparatorLabel = "Separator";
    public const string FontFamilyLabel = "Font Family";
    public const string FontSizeLabel = "Font Size (pt)";
    public const string BoldLabel = "Bold";
    public const string ItalicLabel = "Italic";
    public const string LabelColorLabel = "Label color (#RRGGBB)";
    public const string LabelPositionLabel = "Label Position";
    public const string CategoryGridlinesLabel = "Category Gridlines";
    public const string ValueGridlinesLabel = "Value Gridlines";
    public const string BarGapWidthLabel = "Bar gap width (%)";
    public const string BarOverlapLabel = "Bar overlap (%)";
    public const string DisplayBlanksAsLabel = "Show empty cells as";
    public const string ShowDataLabelsOverMaximumLabel = "Show labels over maximum";
    public const string VaryColorsLabel = "Vary colors by point";
    public const string LegendOverlayLabel = "Overlay legend";
    public const string HighLowLinesLabel = "High-low lines";
    public const string PlotHint = "Bar gap width accepts 0-500; overlap accepts -100 to 100. Blank uses the chart default.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 420;
    public const double DefaultDialogHeight = 650;

    public static IReadOnlyList<ChartDisplayLegendOption> LegendOptions { get; } =
    [
        new(null, "Hidden"),
        new(LegendPosition.Right, "Right"),
        new(LegendPosition.Left, "Left"),
        new(LegendPosition.Top, "Top"),
        new(LegendPosition.Bottom, "Bottom"),
    ];

    public static IReadOnlyList<ChartDisplayLabelPositionOption> LabelPositionOptions { get; } =
    [
        new(DataLabelPosition.BestFit, "Best fit"),
        new(DataLabelPosition.Center, "Center"),
        new(DataLabelPosition.InsideEnd, "Inside end"),
        new(DataLabelPosition.OutsideEnd, "Outside end"),
        new(DataLabelPosition.InsideBase, "Inside base"),
        new(DataLabelPosition.Above, "Above"),
        new(DataLabelPosition.Below, "Below"),
        new(DataLabelPosition.Left, "Left"),
        new(DataLabelPosition.Right, "Right"),
    ];

    private string _title = string.Empty;
    private LegendPosition? _legend;
    private bool _showValueLabels;
    private bool _showPercentLabels;
    private bool _showCategoryLabels;
    private bool _showSeriesLabels;
    private bool _showLegendKeys;
    private bool _showBubbleSize;
    private DataLabelPosition _labelPosition = DataLabelPosition.OutsideEnd;
    private string _labelNumberFormat = string.Empty;
    private string _labelSeparator = string.Empty;
    private string _labelFontFamily = string.Empty;
    private double? _labelFontSizePt;
    private bool? _labelBold;
    private bool? _labelItalic;
    private ThemeAwareColor? _labelColor;
    private bool _categoryGridlines;
    private bool _valueGridlines;
    private int? _barGapWidthPercent;
    private int? _barOverlapPercent;
    private ChartDisplayBlanksAs? _displayBlanksAs;
    private bool? _showDataLabelsOverMaximum;
    private bool _varyColors;
    private bool? _legendOverlay;
    private bool? _highLowLines;
    private bool _supportsHighLowLines;

    private ChartDisplayOptionsPlanner(ChartShape chart)
    {
        _title = chart.Title ?? string.Empty;
        _legend = chart.Legend;
        _showValueLabels = chart.DataLabels?.ShowValue == true;
        _showPercentLabels = chart.DataLabels?.ShowPercent == true;
        _showCategoryLabels = chart.DataLabels?.ShowCategoryName == true;
        _showSeriesLabels = chart.DataLabels?.ShowSeriesName == true;
        _showLegendKeys = chart.DataLabels?.ShowLegendKey == true;
        _showBubbleSize = chart.DataLabels?.ShowBubbleSize == true;
        _labelPosition = chart.DataLabels?.Position ?? DataLabelPosition.OutsideEnd;
        _labelNumberFormat = chart.DataLabels?.NumberFormat ?? string.Empty;
        _labelSeparator = chart.DataLabels?.Separator ?? string.Empty;
        _labelFontFamily = chart.DataLabels?.TextStyle?.FontFamily ?? string.Empty;
        _labelFontSizePt = chart.DataLabels?.TextStyle?.FontSizePt;
        _labelBold = chart.DataLabels?.TextStyle?.Bold;
        _labelItalic = chart.DataLabels?.TextStyle?.Italic;
        _labelColor = chart.DataLabels?.TextStyle?.Color;
        _categoryGridlines = chart.CategoryAxis.HasMajorGridlines;
        _valueGridlines = chart.ValueAxis.HasMajorGridlines;
        _barGapWidthPercent = chart.BarGapWidthPercent;
        _barOverlapPercent = chart.BarOverlapPercent;
        _displayBlanksAs = chart.DisplayBlanksAs;
        _showDataLabelsOverMaximum = chart.ShowDataLabelsOverMaximum;
        _varyColors = chart.VaryColors;
        _legendOverlay = chart.LegendOverlay;
        _supportsHighLowLines = chart.ChartType == ChartType.Stock;
        _highLowLines = _supportsHighLowLines ? chart.HasHighLowLines : null;
    }

    public static ChartDisplayOptionsSurfacePlan BuildSurfacePlan() =>
        new(
            CommandId,
            DialogTitle,
            ChartTitleLabel,
            LegendLabel,
            ValueLabelsLabel,
            PercentLabelsLabel,
            CategoryLabelsLabel,
            SeriesLabelsLabel,
            LegendKeysLabel,
            BubbleSizeLabelsLabel,
            NumberFormatLabel,
            SeparatorLabel,
            FontFamilyLabel,
            FontSizeLabel,
            BoldLabel,
            ItalicLabel,
            LabelColorLabel,
            LabelPositionLabel,
            CategoryGridlinesLabel,
            ValueGridlinesLabel,
            BarGapWidthLabel,
            BarOverlapLabel,
            DisplayBlanksAsLabel,
            ShowDataLabelsOverMaximumLabel,
            VaryColorsLabel,
            LegendOverlayLabel,
            HighLowLinesLabel,
            PlotHint,
            OkLabel,
            CancelLabel);

    public static ChartDisplayOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartDisplayOptionsPlanner(chart);
    }

    public string Title => _title;
    public LegendPosition? Legend => _legend;
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
    public bool CategoryGridlines => _categoryGridlines;
    public bool ValueGridlines => _valueGridlines;
    public int? BarGapWidthPercent => _barGapWidthPercent;
    public int? BarOverlapPercent => _barOverlapPercent;
    public ChartDisplayBlanksAs? DisplayBlanksAs => _displayBlanksAs;
    public bool? ShowDataLabelsOverMaximum => _showDataLabelsOverMaximum;
    public bool VaryColors => _varyColors;
    public bool? LegendOverlay => _legendOverlay;
    public bool? HighLowLines => _highLowLines;
    public bool SupportsHighLowLines => _supportsHighLowLines;

    public static IReadOnlyList<ChartDisplayBlanksOption> DisplayBlanksOptions { get; } =
    [
        new(null, "Automatic"),
        new(ChartDisplayBlanksAs.Gap, "Gap"),
        new(ChartDisplayBlanksAs.Zero, "Zero"),
        new(ChartDisplayBlanksAs.Span, "Connect data points"),
    ];

    public void SetTitle(string? title) => _title = title ?? string.Empty;
    public void SetLegend(LegendPosition? legend) => _legend = legend;
    public void SetShowValueLabels(bool show) => _showValueLabels = show;
    public void SetShowPercentLabels(bool show) => _showPercentLabels = show;
    public void SetShowCategoryLabels(bool show) => _showCategoryLabels = show;
    public void SetShowSeriesLabels(bool show) => _showSeriesLabels = show;
    public void SetShowLegendKeys(bool show) => _showLegendKeys = show;
    public void SetShowBubbleSize(bool show) => _showBubbleSize = show;
    public void SetLabelPosition(DataLabelPosition position) => _labelPosition = position;
    public void SetLabelNumberFormat(string? format) => _labelNumberFormat = format ?? string.Empty;
    public void SetLabelSeparator(string? separator) => _labelSeparator = separator ?? string.Empty;
    public void SetLabelFontFamily(string? value) => _labelFontFamily = value?.Trim() ?? string.Empty;
    public void SetLabelFontSize(double? value) => _labelFontSizePt = value;
    public void SetLabelBold(bool? value) => _labelBold = value;
    public void SetLabelItalic(bool? value) => _labelItalic = value;
    public void SetLabelColor(string? text) => _labelColor = string.IsNullOrWhiteSpace(text)
        ? null
        : ChartPointOptionsPlanner.ParseColor(text, LabelColorLabel);
    public void SetCategoryGridlines(bool show) => _categoryGridlines = show;
    public void SetValueGridlines(bool show) => _valueGridlines = show;
    public void SetBarGapWidthPercent(int? value) => _barGapWidthPercent = Normalize(value, 0, 500);
    public void SetBarOverlapPercent(int? value) => _barOverlapPercent = Normalize(value, -100, 100);
    public void SetDisplayBlanksAs(ChartDisplayBlanksAs? value) => _displayBlanksAs = value;
    public void SetShowDataLabelsOverMaximum(bool? value) => _showDataLabelsOverMaximum = value;
    public void SetVaryColors(bool value) => _varyColors = value;
    public void SetLegendOverlay(bool? value) => _legendOverlay = value;
    public void SetHighLowLines(bool? value) => _highLowLines = _supportsHighLowLines ? value : null;

    public ChartDisplayOptions BuildCommitPlan() => new(
        string.IsNullOrWhiteSpace(_title) ? null : _title,
        _legend,
        _showValueLabels,
        _labelPosition,
        _categoryGridlines,
        _valueGridlines,
        _showPercentLabels,
        _showCategoryLabels,
        _showSeriesLabels,
        _showLegendKeys,
        string.IsNullOrWhiteSpace(_labelNumberFormat) ? null : _labelNumberFormat,
        string.IsNullOrEmpty(_labelSeparator) ? null : _labelSeparator,
        _barGapWidthPercent,
        _barOverlapPercent,
        _displayBlanksAs,
        _showDataLabelsOverMaximum,
        _varyColors,
        _legendOverlay,
        _highLowLines,
        BuildLabelTextStyle(),
        _showBubbleSize);

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

    private static string FormatColor(ThemeAwareColor? color) =>
        color is null ? string.Empty : color.Resolved.ToString();

    private static int? Normalize(int? value, int minimum, int maximum) =>
        value is null ? null : Math.Clamp(value.Value, minimum, maximum);
}
