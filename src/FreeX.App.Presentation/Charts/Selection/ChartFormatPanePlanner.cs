using System.Globalization;
using Free.Shared.Localization;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Selection;

/// <summary>How a row of the docked Format pane is edited.</summary>
public enum ChartFormatRowKind
{
    /// <summary>Free text (a title).</summary>
    Text,

    /// <summary>A number, entered as text; blank is ignored.</summary>
    Number,

    /// <summary>A checkbox.</summary>
    Toggle,

    /// <summary>One of <see cref="ChartFormatPaneRow.Choices"/>.</summary>
    Choice,

    /// <summary>A push button that performs an action when invoked.</summary>
    Button,
}

public sealed record ChartFormatPaneChoice(string Id, string LabelKey);

/// <summary>One editable option of the Format pane. <see cref="Value"/> is the current value as text ("true"/"false" for toggles).</summary>
public sealed record ChartFormatPaneRow(
    string Id,
    string LabelKey,
    ChartFormatRowKind Kind,
    string Value = "",
    IReadOnlyList<ChartFormatPaneChoice>? Choices = null);

/// <summary>What the docked Format pane shows for the selected chart element.</summary>
public sealed record ChartFormatPaneModel(
    ChartElementRef Element,
    LocalizedTextDescriptor ElementName,
    IReadOnlyList<ChartFormatPaneRow> Rows,
    ChartElementFormatTarget MoreOptionsTarget);

/// <summary>
/// The shared model behind Excel's docked "Format Chart Area / Title / Legend / Axis / Series..." task
/// pane. <see cref="Build"/> lists the options of the selected element; <see cref="Plan"/> turns one
/// edit into a <see cref="ChartLayoutOptions"/> the host executes through <see cref="SetChartLayoutCommand"/>
/// (the same command the existing dialogs use, so each edit is one undo step). The pane's
/// "More Options" button opens the existing full dialog for anything not listed here.
/// </summary>
public static class ChartFormatPanePlanner
{
    public const string TitleResourceKey = "ChartFormatPane_Title";
    public const string MoreOptionsResourceKey = "ChartFormatPane_MoreOptions";
    public const string CloseResourceKey = "ChartFormatPane_Close";

    public const string RowText = "text";
    public const string RowFontSize = "fontSize";
    public const string RowResetPosition = "resetPosition";
    public const string RowShowLegend = "showLegend";
    public const string RowLegendPosition = "legendPosition";
    public const string RowLegendOverlay = "legendOverlay";
    public const string RowShowDataLabels = "showDataLabels";
    public const string RowShowValue = "showValue";
    public const string RowShowCategory = "showCategory";
    public const string RowShowSeries = "showSeries";
    public const string RowShowPercentage = "showPercentage";
    public const string RowLabelPosition = "labelPosition";
    public const string RowShowLabels = "showLabels";
    public const string RowLabelFontSize = "labelFontSize";
    public const string RowGridlines = "gridlines";
    public const string RowMinimum = "minimum";
    public const string RowMaximum = "maximum";
    public const string RowMajorUnit = "majorUnit";
    public const string RowLogScale = "logScale";
    public const string RowResetBounds = "resetBounds";
    public const string RowBorderThickness = "borderThickness";
    public const string RowShowTrendline = "showTrendline";
    public const string RowTrendlineEquation = "trendlineEquation";
    public const string RowTrendlineRSquared = "trendlineRSquared";
    public const string RowShowErrorBars = "showErrorBars";
    public const string RowErrorBarCaps = "errorBarCaps";

    /// <summary>The pane's content for <paramref name="element"/> of <paramref name="chart"/>.</summary>
    public static ChartFormatPaneModel Build(ChartModel chart, ChartElementRef element, string? seriesName = null)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var rows = new List<ChartFormatPaneRow>();
        switch (element.Kind)
        {
            case ChartElementKind.ChartArea:
                rows.Add(Text(RowText, "ChartFormatPane_ChartTitleText", chart.Title));
                rows.Add(Toggle(RowShowLegend, "ChartFormatPane_ShowLegend", chart.ShowLegend));
                rows.Add(Toggle(RowShowDataLabels, "ChartFormatPane_ShowDataLabels", chart.ShowDataLabels));
                break;
            case ChartElementKind.PlotArea:
                rows.Add(Number(RowBorderThickness, "ChartFormatPane_BorderThickness", chart.PlotAreaBorderThickness));
                rows.Add(Button(RowResetPosition, "ChartFormatPane_ResetPosition"));
                break;
            case ChartElementKind.ChartTitle:
                rows.Add(Text(RowText, "ChartFormatPane_TitleText", chart.Title));
                rows.Add(Number(RowFontSize, "ChartFormatPane_FontSize", chart.ChartTitleFontSize));
                rows.Add(Button(RowResetPosition, "ChartFormatPane_ResetPosition"));
                break;
            case ChartElementKind.Legend:
            case ChartElementKind.LegendEntry:
                rows.Add(Toggle(RowShowLegend, "ChartFormatPane_ShowLegend", chart.ShowLegend));
                rows.Add(new ChartFormatPaneRow(
                    RowLegendPosition,
                    "ChartFormatPane_LegendPosition",
                    ChartFormatRowKind.Choice,
                    (chart.LegendPosition == ChartLegendPosition.None ? ChartLegendPosition.Right : chart.LegendPosition).ToString(),
                    LegendChoices));
                rows.Add(Toggle(RowLegendOverlay, "ChartFormatPane_LegendOverlay", chart.LegendOverlay));
                rows.Add(Number(RowFontSize, "ChartFormatPane_FontSize", chart.LegendFontSize));
                rows.Add(Button(RowResetPosition, "ChartFormatPane_ResetPosition"));
                break;
            case ChartElementKind.CategoryAxisTitle:
            case ChartElementKind.ValueAxisTitle:
                rows.Add(Text(
                    RowText,
                    "ChartFormatPane_AxisTitleText",
                    ChartElementCommandPlanner.IsHorizontalAxis(chart, element.Kind) ? chart.XAxisTitle : chart.YAxisTitle));
                rows.Add(Number(RowFontSize, "ChartFormatPane_FontSize", chart.AxisTitleFontSize));
                rows.Add(Button(RowResetPosition, "ChartFormatPane_ResetPosition"));
                break;
            case ChartElementKind.SecondaryAxisTitle:
                rows.Add(Text(RowText, "ChartFormatPane_AxisTitleText", chart.SecondaryAxisTitle));
                break;
            case ChartElementKind.CategoryAxis:
            case ChartElementKind.ValueAxis:
            case ChartElementKind.SecondaryValueAxis:
            case ChartElementKind.CategoryGridlines:
            case ChartElementKind.ValueGridlines:
                AddAxisRows(rows, chart, element);
                break;
            case ChartElementKind.DataLabels:
                AddDataLabelRows(rows, chart);
                break;
            case ChartElementKind.Trendline:
            {
                // Per-series: show the selected series' own trendline settings (the chart-wide ones when the
                // element carries no series).
                var trend = element.SeriesIndex >= 0 ? chart.ResolveTrendline(element.SeriesIndex) : null;
                var source = trend ?? chart;
                rows.Add(Toggle(RowShowTrendline, "ChartFormatPane_ShowTrendline",
                    element.SeriesIndex >= 0 ? trend is not null : chart.ShowLinearTrendline));
                rows.Add(Toggle(RowTrendlineEquation, "ChartFormatPane_TrendlineEquation", source.ShowTrendlineEquation));
                rows.Add(Toggle(RowTrendlineRSquared, "ChartFormatPane_TrendlineRSquared", source.ShowTrendlineRSquared));
                break;
            }
            case ChartElementKind.ErrorBars:
            {
                var bars = element.SeriesIndex >= 0 ? chart.ResolveErrorBars(element.SeriesIndex) : null;
                var source = bars ?? chart;
                rows.Add(Toggle(RowShowErrorBars, "ChartFormatPane_ShowErrorBars",
                    element.SeriesIndex >= 0 ? bars is not null : chart.ShowErrorBars));
                rows.Add(Toggle(RowErrorBarCaps, "ChartFormatPane_ErrorBarCaps", source.ErrorBarEndCaps));
                break;
            }
        }

        return new ChartFormatPaneModel(
            element,
            ChartElementCommandPlanner.Describe(element, seriesName),
            rows,
            ChartElementCommandPlanner.GetFormatTarget(chart, element));
    }

    /// <summary>
    /// Turns the edit of row <paramref name="rowId"/> to <paramref name="value"/> into the layout change
    /// that applies it, or null when the value is unusable or nothing would change.
    /// </summary>
    public static ChartLayoutOptions? Plan(ChartModel chart, ChartElementRef element, string rowId, string value)
    {
        ArgumentNullException.ThrowIfNull(chart);
        value ??= string.Empty;
        var on = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        var horizontal = ChartElementCommandPlanner.IsHorizontalAxis(chart, element.Kind);

        switch (rowId)
        {
            case RowText:
                return element.Kind switch
                {
                    ChartElementKind.ChartTitle or ChartElementKind.ChartArea => new ChartLayoutOptions(Title: value),
                    ChartElementKind.SecondaryAxisTitle => new ChartLayoutOptions { SecondaryAxisTitle = value },
                    ChartElementKind.CategoryAxisTitle or ChartElementKind.ValueAxisTitle =>
                        horizontal ? new ChartLayoutOptions(XAxisTitle: value) : new ChartLayoutOptions(YAxisTitle: value),
                    _ => null,
                };
            case RowFontSize:
                if (!TryNumber(value, 6, 72, out var size))
                    return null;
                return element.Kind switch
                {
                    ChartElementKind.ChartTitle => new ChartLayoutOptions(ChartTitleFontSize: size),
                    ChartElementKind.Legend or ChartElementKind.LegendEntry => new ChartLayoutOptions(LegendFontSize: size),
                    ChartElementKind.CategoryAxisTitle or ChartElementKind.ValueAxisTitle => new ChartLayoutOptions(AxisTitleFontSize: size),
                    _ => null,
                };
            case RowResetPosition:
                return element.Kind switch
                {
                    ChartElementKind.ChartTitle => new ChartLayoutOptions { ClearManualLayouts = ChartManualLayoutTargets.Title },
                    ChartElementKind.Legend or ChartElementKind.LegendEntry => new ChartLayoutOptions { ClearManualLayouts = ChartManualLayoutTargets.Legend },
                    ChartElementKind.PlotArea => new ChartLayoutOptions { ClearManualLayouts = ChartManualLayoutTargets.PlotArea },
                    ChartElementKind.CategoryAxisTitle or ChartElementKind.ValueAxisTitle => new ChartLayoutOptions
                    {
                        ClearManualLayouts = horizontal ? ChartManualLayoutTargets.XAxisTitle : ChartManualLayoutTargets.YAxisTitle,
                    },
                    _ => null,
                };
            case RowShowLegend:
                return new ChartLayoutOptions(ShowLegend: on);
            case RowLegendPosition:
                return Enum.TryParse<ChartLegendPosition>(value, out var position) && position != ChartLegendPosition.None
                    ? new ChartLayoutOptions(LegendPosition: position)
                    : null;
            case RowLegendOverlay:
                return new ChartLayoutOptions(LegendOverlay: on);
            case RowShowDataLabels:
                return new ChartLayoutOptions(ShowDataLabels: on);
            case RowShowValue:
                return new ChartLayoutOptions(ShowDataLabelValue: on);
            case RowShowCategory:
                return new ChartLayoutOptions(ShowDataLabelCategoryName: on);
            case RowShowSeries:
                return new ChartLayoutOptions(ShowDataLabelSeriesName: on);
            case RowShowPercentage:
                return new ChartLayoutOptions(ShowDataLabelPercentage: on);
            case RowLabelPosition:
                return Enum.TryParse<ChartDataLabelPosition>(value, out var labelPosition)
                    ? new ChartLayoutOptions(DataLabelPosition: labelPosition)
                    : null;
            case RowBorderThickness:
                return TryNumber(value, 0, 10, out var thickness) ? new ChartLayoutOptions(PlotAreaBorderThickness: thickness) : null;
            case RowShowLabels:
                return horizontal ? new ChartLayoutOptions(ShowXAxisLabels: on) : new ChartLayoutOptions(ShowYAxisLabels: on);
            case RowLabelFontSize:
                return TryNumber(value, 6, 72, out var labelSize)
                    ? horizontal ? new ChartLayoutOptions(XAxisLabelFontSize: labelSize) : new ChartLayoutOptions(YAxisLabelFontSize: labelSize)
                    : null;
            case RowGridlines:
                return horizontal ? new ChartLayoutOptions(ShowXAxisMajorGridlines: on) : new ChartLayoutOptions(ShowYAxisMajorGridlines: on);
            case RowMinimum:
                return TryNumber(value, double.MinValue, double.MaxValue, out var min)
                    ? horizontal ? new ChartLayoutOptions(XAxisMinimum: min) : new ChartLayoutOptions(YAxisMinimum: min)
                    : null;
            case RowMaximum:
                return TryNumber(value, double.MinValue, double.MaxValue, out var max)
                    ? horizontal ? new ChartLayoutOptions(XAxisMaximum: max) : new ChartLayoutOptions(YAxisMaximum: max)
                    : null;
            case RowMajorUnit:
                return TryNumber(value, double.Epsilon, double.MaxValue, out var unit)
                    ? horizontal ? new ChartLayoutOptions(XAxisMajorUnit: unit) : new ChartLayoutOptions(YAxisMajorUnit: unit)
                    : null;
            case RowLogScale:
                return horizontal ? new ChartLayoutOptions(XAxisLogScale: on) : new ChartLayoutOptions(YAxisLogScale: on);
            case RowResetBounds:
                return horizontal ? new ChartLayoutOptions(ClearXAxisBounds: true) : new ChartLayoutOptions(ClearYAxisBounds: true);
            case RowShowTrendline:
                return new ChartLayoutOptions(ShowLinearTrendline: on) { TrendlineTargetSeriesIndex = TargetSeries(element) };
            case RowTrendlineEquation:
                return new ChartLayoutOptions(ShowTrendlineEquation: on) { TrendlineTargetSeriesIndex = TargetSeries(element) };
            case RowTrendlineRSquared:
                return new ChartLayoutOptions(ShowTrendlineRSquared: on) { TrendlineTargetSeriesIndex = TargetSeries(element) };
            case RowShowErrorBars:
                return new ChartLayoutOptions(ShowErrorBars: on) { ErrorBarTargetSeriesIndex = TargetSeries(element) };
            case RowErrorBarCaps:
                return new ChartLayoutOptions(ErrorBarEndCaps: on) { ErrorBarTargetSeriesIndex = TargetSeries(element) };
            default:
                return null;
        }
    }

    /// <summary>The series a trendline / error-bar pane row edits (null = the chart-wide "every series" meaning).</summary>
    private static int? TargetSeries(ChartElementRef element) =>
        element.SeriesIndex >= 0 ? element.SeriesIndex : null;

    private static void AddAxisRows(List<ChartFormatPaneRow> rows, ChartModel chart, ChartElementRef element)
    {
        var horizontal = ChartElementCommandPlanner.IsHorizontalAxis(chart, element.Kind);
        rows.Add(Toggle(RowShowLabels, "ChartFormatPane_ShowAxisLabels", horizontal ? chart.ShowXAxisLabels : chart.ShowYAxisLabels));
        rows.Add(Number(RowLabelFontSize, "ChartFormatPane_LabelFontSize", horizontal ? chart.XAxisLabelFontSize : chart.YAxisLabelFontSize));
        rows.Add(Toggle(RowGridlines, "ChartFormatPane_MajorGridlines", horizontal ? chart.ShowXAxisMajorGridlines : chart.ShowYAxisMajorGridlines));

        var supportsBounds = horizontal
            ? ChartTypeSupport.SupportsXAxisBounds(chart.Type)
            : ChartTypeSupport.SupportsYAxisBounds(chart.Type);
        if (!supportsBounds)
            return;

        rows.Add(Number(RowMinimum, "ChartFormatPane_AxisMinimum", horizontal ? chart.XAxisMinimum : chart.YAxisMinimum));
        rows.Add(Number(RowMaximum, "ChartFormatPane_AxisMaximum", horizontal ? chart.XAxisMaximum : chart.YAxisMaximum));
        rows.Add(Number(RowMajorUnit, "ChartFormatPane_AxisMajorUnit", horizontal ? chart.XAxisMajorUnit : chart.YAxisMajorUnit));
        rows.Add(Toggle(RowLogScale, "ChartFormatPane_LogScale", horizontal ? chart.XAxisLogScale : chart.YAxisLogScale));
        rows.Add(Button(RowResetBounds, "ChartFormatPane_ResetBounds"));
    }

    private static void AddDataLabelRows(List<ChartFormatPaneRow> rows, ChartModel chart)
    {
        rows.Add(Toggle(RowShowDataLabels, "ChartFormatPane_ShowDataLabels", chart.ShowDataLabels));
        rows.Add(Toggle(RowShowValue, "ChartFormatPane_LabelValue", chart.ShowDataLabelValue));
        rows.Add(Toggle(RowShowCategory, "ChartFormatPane_LabelCategory", chart.ShowDataLabelCategoryName));
        rows.Add(Toggle(RowShowSeries, "ChartFormatPane_LabelSeries", chart.ShowDataLabelSeriesName));
        if (ChartTypeSupport.SupportsPercentageDataLabels(chart.Type))
            rows.Add(Toggle(RowShowPercentage, "ChartFormatPane_LabelPercentage", chart.ShowDataLabelPercentage));
        rows.Add(new ChartFormatPaneRow(
            RowLabelPosition,
            "ChartFormatPane_LabelPosition",
            ChartFormatRowKind.Choice,
            chart.DataLabelPosition.ToString(),
            LabelPositionChoices));
    }

    private static readonly IReadOnlyList<ChartFormatPaneChoice> LegendChoices =
    [
        new(nameof(ChartLegendPosition.Top), "ChartFormatPane_PositionTop"),
        new(nameof(ChartLegendPosition.Bottom), "ChartFormatPane_PositionBottom"),
        new(nameof(ChartLegendPosition.Left), "ChartFormatPane_PositionLeft"),
        new(nameof(ChartLegendPosition.Right), "ChartFormatPane_PositionRight"),
        new(nameof(ChartLegendPosition.TopRight), "ChartFormatPane_PositionTopRight"),
    ];

    private static readonly IReadOnlyList<ChartFormatPaneChoice> LabelPositionChoices =
    [
        new(nameof(ChartDataLabelPosition.BestFit), "ChartFormatPane_PositionBestFit"),
        new(nameof(ChartDataLabelPosition.Center), "ChartFormatPane_PositionCenter"),
        new(nameof(ChartDataLabelPosition.InsideEnd), "ChartFormatPane_PositionInsideEnd"),
        new(nameof(ChartDataLabelPosition.OutsideEnd), "ChartFormatPane_PositionOutsideEnd"),
    ];

    private static ChartFormatPaneRow Text(string id, string key, string? value) =>
        new(id, key, ChartFormatRowKind.Text, value ?? string.Empty);

    private static ChartFormatPaneRow Toggle(string id, string key, bool value) =>
        new(id, key, ChartFormatRowKind.Toggle, value ? "true" : "false");

    private static ChartFormatPaneRow Number(string id, string key, double? value) =>
        new(id, key, ChartFormatRowKind.Number, value is { } v ? v.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty);

    private static ChartFormatPaneRow Button(string id, string key) => new(id, key, ChartFormatRowKind.Button);

    private static bool TryNumber(string text, double min, double max, out double number)
    {
        number = 0;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
               double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed)
            ? double.IsFinite(parsed) && parsed >= min && parsed <= max && (number = parsed) == parsed
            : false;
    }
}
