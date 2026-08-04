using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartDisplayLegendOption(LegendPosition? Value, string Label);

public sealed record ChartDisplayLabelPositionOption(DataLabelPosition Value, string Label);

public sealed record ChartDisplayBlanksOption(ChartDisplayBlanksAs? Value, string Label);
public sealed record ChartDisplayStyleOption(int? Value, string Label);

public sealed record ChartDisplayOptionsSurfacePlan(
    string CommandId,
    string Title,
    string ChartTitleLabel,
    string TitleOverlayLabel,
    string PlotVisibleOnlyLabel,
    string RoundedCornersLabel,
    string ChartStyleLabel,
    string LegendLabel,
    string ValueLabelsLabel,
    string PercentLabelsLabel,
    string CategoryLabelsLabel,
    string SeriesLabelsLabel,
    string LegendKeysLabel,
    string BubbleSizeLabelsLabel,
    string LeaderLinesLabel,
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
    string CancelLabel,
    string WaterfallConnectorLinesLabel,
    string DropLinesLabel,
    string UpDownBarsLabel,
    string SeriesLinesLabel);

/// <summary>
/// Working-copy planner for the small set of chart display controls common to PowerPoint's
/// chart design/format workflow. The live chart is changed only when the host commits.
/// </summary>
public sealed class ChartDisplayOptionsPlanner
{
    public const string CommandId = "freep.chart.format-options";
    public const string DialogTitle = "Chart Options";
    public const string ChartTitleLabel = "Chart Title";
    public const string TitleOverlayLabel = "Overlay title on plot";
    public const string PlotVisibleOnlyLabel = "Plot visible cells only";
    public const string RoundedCornersLabel = "Rounded chart corners";
    public const string ChartStyleLabel = "Chart Style";
    public const string LegendLabel = "Legend";
    public const string ValueLabelsLabel = "Value Labels";
    public const string PercentLabelsLabel = "Percentage Labels";
    public const string CategoryLabelsLabel = "Category Labels";
    public const string SeriesLabelsLabel = "Series Labels";
    public const string LegendKeysLabel = "Legend Keys";
    public const string BubbleSizeLabelsLabel = "Bubble size labels";
    public const string LeaderLinesLabel = "Leader lines";
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
    public const string WaterfallConnectorLinesLabel = "Waterfall connector lines";
    public const string DropLinesLabel = "Drop lines";
    public const string UpDownBarsLabel = "Up/down bars";
    public const string SeriesLinesLabel = "Series lines";
    public const string PlotHint = "Bar gap width accepts 0-500; overlap accepts -100 to 100. Blank uses the chart default.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 420;
    public const double DefaultDialogHeight = 650;

    public static IReadOnlyList<ChartDisplayStyleOption> StyleOptions { get; } =
        [
            new(null, "Automatic (classic)"),
            .. Enumerable.Range(1, 48).Select(id => new ChartDisplayStyleOption(id, $"Style {id}")),
            .. Enumerable.Range(100, 7).Select(id => new ChartDisplayStyleOption(id, $"Style {id}")),
        ];

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
    private bool _titleOverlay;
    private bool _titleOverlayChanged;
    private bool _plotVisibleOnly;
    private bool _plotVisibleOnlyChanged;
    private bool _roundedCorners;
    private bool _roundedCornersChanged;
    private int? _styleId;
    private LegendPosition? _legend;
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
    private bool? _waterfallConnectorLines;
    private bool _supportsWaterfallConnectorLines;
    private bool? _dropLines;
    private bool _supportsDropLines;
    private bool? _upDownBars;
    private bool _supportsUpDownBars;
    private bool? _seriesLines;
    private bool _supportsSeriesLines;
    private IReadOnlyList<ChartDisplayStyleOption> _availableStyleOptions = StyleOptions;

    private ChartDisplayOptionsPlanner(ChartShape chart)
    {
        _title = chart.Title ?? string.Empty;
        _titleOverlay = chart.TitleOverlay == true;
        _plotVisibleOnly = chart.PlotVisibleOnly != false;
        _roundedCorners = chart.RoundedCorners == true;
        _styleId = chart.StyleId;
        _availableStyleOptions = StyleOptionsFor(chart.StyleId);
        _legend = chart.Legend;
        _showValueLabels = chart.DataLabels?.ShowValue == true;
        _showPercentLabels = chart.DataLabels?.ShowPercent == true;
        _showCategoryLabels = chart.DataLabels?.ShowCategoryName == true;
        _showSeriesLabels = chart.DataLabels?.ShowSeriesName == true;
        _showLegendKeys = chart.DataLabels?.ShowLegendKey == true;
        _showBubbleSize = chart.DataLabels?.ShowBubbleSize == true;
        _showLeaderLines = chart.DataLabels?.ShowLeaderLines;
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
        _supportsWaterfallConnectorLines = chart.ChartType == ChartType.Waterfall;
        _waterfallConnectorLines = _supportsWaterfallConnectorLines
            ? chart.ShowWaterfallConnectorLines
            : null;
        _supportsDropLines = SupportsLineDecorations(chart.ChartType);
        _dropLines = _supportsDropLines ? chart.ShowDropLines : null;
        _supportsUpDownBars = SupportsLineDecorations(chart.ChartType);
        _upDownBars = _supportsUpDownBars ? chart.ShowUpDownBars : null;
        _supportsSeriesLines = IsSeriesLinesSupported(chart.ChartType);
        _seriesLines = _supportsSeriesLines ? chart.SeriesLinesSpecified : null;
    }

    public static ChartDisplayOptionsSurfacePlan BuildSurfacePlan() =>
        new(
            CommandId,
            DialogTitle,
            ChartTitleLabel,
            TitleOverlayLabel,
            PlotVisibleOnlyLabel,
            RoundedCornersLabel,
            ChartStyleLabel,
            LegendLabel,
            ValueLabelsLabel,
            PercentLabelsLabel,
            CategoryLabelsLabel,
            SeriesLabelsLabel,
            LegendKeysLabel,
            BubbleSizeLabelsLabel,
            LeaderLinesLabel,
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
            CancelLabel,
            WaterfallConnectorLinesLabel,
            DropLinesLabel,
            UpDownBarsLabel,
            SeriesLinesLabel);

    public static ChartDisplayOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartDisplayOptionsPlanner(chart);
    }

    public string Title => _title;
    public bool TitleOverlay => _titleOverlay;
    public bool PlotVisibleOnly => _plotVisibleOnly;
    public bool RoundedCorners => _roundedCorners;
    public int? StyleId => _styleId;
    public IReadOnlyList<ChartDisplayStyleOption> AvailableStyleOptions => _availableStyleOptions;

    public static IReadOnlyList<ChartDisplayStyleOption> StyleOptionsFor(int? currentStyleId)
    {
        if (currentStyleId is null || StyleOptions.Any(option => option.Value == currentStyleId))
            return StyleOptions;

        return [.. StyleOptions, new ChartDisplayStyleOption(currentStyleId, $"Style {currentStyleId} (imported)")];
    }
    public LegendPosition? Legend => _legend;
    public bool ShowValueLabels => _showValueLabels;
    public bool ShowPercentLabels => _showPercentLabels;
    public bool ShowCategoryLabels => _showCategoryLabels;
    public bool ShowSeriesLabels => _showSeriesLabels;
    public bool ShowLegendKeys => _showLegendKeys;
    public bool ShowBubbleSize => _showBubbleSize;
    public bool? ShowLeaderLines => _showLeaderLines;
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
    public bool? WaterfallConnectorLines => _waterfallConnectorLines;
    public bool SupportsWaterfallConnectorLines => _supportsWaterfallConnectorLines;
    public bool? DropLines => _dropLines;
    public bool SupportsDropLines => _supportsDropLines;
    public bool? UpDownBars => _upDownBars;
    public bool SupportsUpDownBars => _supportsUpDownBars;
    public bool? SeriesLines => _seriesLines;
    public bool SupportsSeriesLines => _supportsSeriesLines;

    public static IReadOnlyList<ChartDisplayBlanksOption> DisplayBlanksOptions { get; } =
    [
        new(null, "Automatic"),
        new(ChartDisplayBlanksAs.Gap, "Gap"),
        new(ChartDisplayBlanksAs.Zero, "Zero"),
        new(ChartDisplayBlanksAs.Span, "Connect data points"),
    ];

    public void SetTitle(string? title) => _title = title ?? string.Empty;
    public void SetTitleOverlay(bool value)
    {
        _titleOverlay = value;
        _titleOverlayChanged = true;
    }
    public void SetPlotVisibleOnly(bool value)
    {
        _plotVisibleOnly = value;
        _plotVisibleOnlyChanged = true;
    }
    public void SetRoundedCorners(bool value)
    {
        _roundedCorners = value;
        _roundedCornersChanged = true;
    }
    public void SetStyleId(int? styleId)
    {
        if (styleId is not null && !StyleOptions.Any(option => option.Value == styleId) &&
            !_availableStyleOptions.Any(option => option.Value == styleId))
            throw new ArgumentOutOfRangeException(nameof(styleId), styleId, "The chart style is not a supported PowerPoint style ID.");
        _styleId = styleId;
    }
    public void SetLegend(LegendPosition? legend) => _legend = legend;
    public void SetShowValueLabels(bool show) => _showValueLabels = show;
    public void SetShowPercentLabels(bool show) => _showPercentLabels = show;
    public void SetShowCategoryLabels(bool show) => _showCategoryLabels = show;
    public void SetShowSeriesLabels(bool show) => _showSeriesLabels = show;
    public void SetShowLegendKeys(bool show) => _showLegendKeys = show;
    public void SetShowBubbleSize(bool show) => _showBubbleSize = show;
    public void SetShowLeaderLines(bool? show) => _showLeaderLines = show;
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
    public void SetWaterfallConnectorLines(bool? value) =>
        _waterfallConnectorLines = _supportsWaterfallConnectorLines ? value : null;
    public void SetDropLines(bool? value) => _dropLines = _supportsDropLines ? value : null;
    public void SetUpDownBars(bool? value) => _upDownBars = _supportsUpDownBars ? value : null;
    public void SetSeriesLines(bool? value) => _seriesLines = _supportsSeriesLines ? value : null;

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
        _showBubbleSize,
        _styleId,
        _showLeaderLines,
        _titleOverlayChanged ? _titleOverlay : null,
        _plotVisibleOnlyChanged ? _plotVisibleOnly : null,
        _roundedCornersChanged ? _roundedCorners : null,
        _supportsWaterfallConnectorLines ? _waterfallConnectorLines : null,
        _supportsDropLines ? _dropLines : null,
        _supportsUpDownBars ? _upDownBars : null,
        _supportsSeriesLines ? _seriesLines : null);

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

    private static bool SupportsLineDecorations(ChartType chartType) =>
        chartType is ChartType.Line or ChartType.LineMarkers or ChartType.Stock;

    private static bool IsSeriesLinesSupported(ChartType chartType) =>
        chartType is ChartType.ColumnStacked or ChartType.ColumnStacked100 or
            ChartType.BarStacked or ChartType.BarStacked100;
}
