using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartDisplayLegendOption(LegendPosition? Value, string Label);

public sealed record ChartDisplayLabelPositionOption(DataLabelPosition Value, string Label);

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
    string NumberFormatLabel,
    string SeparatorLabel,
    string LabelPositionLabel,
    string CategoryGridlinesLabel,
    string ValueGridlinesLabel,
    string BarGapWidthLabel,
    string BarOverlapLabel,
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
    public const string NumberFormatLabel = "Number Format";
    public const string SeparatorLabel = "Separator";
    public const string LabelPositionLabel = "Label Position";
    public const string CategoryGridlinesLabel = "Category Gridlines";
    public const string ValueGridlinesLabel = "Value Gridlines";
    public const string BarGapWidthLabel = "Bar gap width (%)";
    public const string BarOverlapLabel = "Bar overlap (%)";
    public const string PlotHint = "Bar gap width accepts 0-500; overlap accepts -100 to 100. Blank uses the chart default.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 420;
    public const double DefaultDialogHeight = 470;

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
    private DataLabelPosition _labelPosition = DataLabelPosition.OutsideEnd;
    private string _labelNumberFormat = string.Empty;
    private string _labelSeparator = string.Empty;
    private bool _categoryGridlines;
    private bool _valueGridlines;
    private int? _barGapWidthPercent;
    private int? _barOverlapPercent;

    private ChartDisplayOptionsPlanner(ChartShape chart)
    {
        _title = chart.Title ?? string.Empty;
        _legend = chart.Legend;
        _showValueLabels = chart.DataLabels?.ShowValue == true;
        _showPercentLabels = chart.DataLabels?.ShowPercent == true;
        _showCategoryLabels = chart.DataLabels?.ShowCategoryName == true;
        _showSeriesLabels = chart.DataLabels?.ShowSeriesName == true;
        _showLegendKeys = chart.DataLabels?.ShowLegendKey == true;
        _labelPosition = chart.DataLabels?.Position ?? DataLabelPosition.OutsideEnd;
        _labelNumberFormat = chart.DataLabels?.NumberFormat ?? string.Empty;
        _labelSeparator = chart.DataLabels?.Separator ?? string.Empty;
        _categoryGridlines = chart.CategoryAxis.HasMajorGridlines;
        _valueGridlines = chart.ValueAxis.HasMajorGridlines;
        _barGapWidthPercent = chart.BarGapWidthPercent;
        _barOverlapPercent = chart.BarOverlapPercent;
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
            NumberFormatLabel,
            SeparatorLabel,
            LabelPositionLabel,
            CategoryGridlinesLabel,
            ValueGridlinesLabel,
            BarGapWidthLabel,
            BarOverlapLabel,
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
    public DataLabelPosition LabelPosition => _labelPosition;
    public string LabelNumberFormat => _labelNumberFormat;
    public string LabelSeparator => _labelSeparator;
    public bool CategoryGridlines => _categoryGridlines;
    public bool ValueGridlines => _valueGridlines;
    public int? BarGapWidthPercent => _barGapWidthPercent;
    public int? BarOverlapPercent => _barOverlapPercent;

    public void SetTitle(string? title) => _title = title ?? string.Empty;
    public void SetLegend(LegendPosition? legend) => _legend = legend;
    public void SetShowValueLabels(bool show) => _showValueLabels = show;
    public void SetShowPercentLabels(bool show) => _showPercentLabels = show;
    public void SetShowCategoryLabels(bool show) => _showCategoryLabels = show;
    public void SetShowSeriesLabels(bool show) => _showSeriesLabels = show;
    public void SetShowLegendKeys(bool show) => _showLegendKeys = show;
    public void SetLabelPosition(DataLabelPosition position) => _labelPosition = position;
    public void SetLabelNumberFormat(string? format) => _labelNumberFormat = format ?? string.Empty;
    public void SetLabelSeparator(string? separator) => _labelSeparator = separator ?? string.Empty;
    public void SetCategoryGridlines(bool show) => _categoryGridlines = show;
    public void SetValueGridlines(bool show) => _valueGridlines = show;
    public void SetBarGapWidthPercent(int? value) => _barGapWidthPercent = Normalize(value, 0, 500);
    public void SetBarOverlapPercent(int? value) => _barOverlapPercent = Normalize(value, -100, 100);

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
        _barOverlapPercent);

    private static int? Normalize(int? value, int minimum, int maximum) =>
        value is null ? null : Math.Clamp(value.Value, minimum, maximum);
}
