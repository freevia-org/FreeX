using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace FreeX.Core.IO.Tests;

public partial class XlsxCorpusRunnerTests
{
    private static ChartSummary CaptureChartSummary(ChartModel chart) =>
        new(
            chart.Type,
            chart.Title ?? "",
            chart.XAxisTitle ?? "",
            chart.YAxisTitle ?? "",
            CaptureChartVisualSummary(chart),
            CaptureChartAxisSummary(chart, isXAxis: true),
            CaptureChartAxisSummary(chart, isXAxis: false),
            chart.ShowLegend,
            chart.IsPivotChart,
            chart.PivotSourceFormatId,
            chart.Uses1904DateSystem,
            chart.Language ?? "",
            chart.ChartStyleId,
            chart.RoundedCorners,
            chart.BlankDisplayMode,
            chart.ShowDataLabelsOverMaximum,
            chart.AutoTitleDeleted,
            chart.ShowDataInHiddenRowsAndColumns,
            CaptureChartProtectionSummary(chart.Protection),
            CaptureChartPrintSettingsSummary(chart.PrintSettings),
            CaptureChartColorMapSummary(chart.ColorMapOverride),
            CaptureChartExternalDataSummary(chart.ExternalData),
            CaptureChartManualLayoutSummary(chart.PlotAreaLayout),
            CaptureChartManualLayoutSummary(chart.LegendLayout),
            chart.LegendPosition,
            chart.LegendOverlay,
            chart.ShowDataLabels,
            chart.ShowDataLabelValue,
            chart.ShowDataLabelLegendKey,
            chart.ShowDataLabelBubbleSize,
            chart.ShowDataLabelCategoryName,
            chart.ShowDataLabelSeriesName,
            chart.ShowDataLabelPercentage,
            chart.DataLabelPosition,
            chart.DataLabelSeparator,
            chart.DataLabelNumberFormat,
            chart.ShowDataLabelCallouts,
            chart.DataLabelFillColor is null ? "" : ToColorSummary(chart.DataLabelFillColor.Value),
            chart.DataLabelFillThemeColor,
            chart.DataLabelBorderColor is null ? "" : ToColorSummary(chart.DataLabelBorderColor.Value),
            chart.DataLabelBorderThemeColor,
            chart.DataLabelTextColor is null ? "" : ToColorSummary(chart.DataLabelTextColor.Value),
            chart.DataLabelTextThemeColor,
            chart.DataLabelBorderThickness,
            chart.DataLabelFontSize,
            chart.DataLabelAngle,
            chart.BarGapWidth,
            chart.BarOverlap,
            chart.VaryColorsByPoint,
            chart.BubbleScale,
            chart.ShowNegativeBubbles,
            chart.BubbleSizeRepresents,
            CaptureChartTrendlineSummary(chart),
            CaptureChartErrorBarSummary(chart),
            CaptureChartGuideLineSummary(
                chart.ShowDropLines,
                chart.DropLineColor,
                chart.DropLineThemeColor,
                chart.DropLineThickness,
                chart.DropLineDashStyle),
            chart.StockSubtype,
            CaptureChartGuideLineSummary(
                chart.ShowHighLowLines,
                chart.HighLowLineColor,
                chart.HighLowLineThemeColor,
                chart.HighLowLineThickness,
                chart.HighLowLineDashStyle),
            CaptureChartGuideLineSummary(
                chart.ShowSeriesLines,
                chart.SeriesLineColor,
                chart.SeriesLineThemeColor,
                chart.SeriesLineThickness,
                chart.SeriesLineDashStyle),
            CaptureChartUpDownBarsSummary(chart),
            CaptureChartDataTableSummary(chart.DataTable),
            CaptureChart3DViewSummary(chart.ThreeDView),
            CaptureChartSurfaceFormatSummary(chart.FloorFormat),
            CaptureChartSurfaceFormatSummary(chart.SideWallFormat),
            CaptureChartSurfaceFormatSummary(chart.BackWallFormat),
            new ChartRangeSummary(
                chart.DataRange.Start.Row,
                chart.DataRange.Start.Col,
                chart.DataRange.End.Row,
                chart.DataRange.End.Col));

    private static ChartDataTableSummary? CaptureChartDataTableSummary(ChartDataTableModel? dataTable) =>
        dataTable is null
            ? null
            : new ChartDataTableSummary(
                dataTable.ShowHorizontalBorder,
                dataTable.ShowVerticalBorder,
                dataTable.ShowOutline,
                dataTable.ShowLegendKeys);

    private static ChartProtectionSummary? CaptureChartProtectionSummary(ChartProtectionModel? protection) =>
        protection is null
            ? null
            : new ChartProtectionSummary(
                protection.ChartObject,
                protection.Data,
                protection.Formatting,
                protection.Selection,
                protection.UserInterface);

    private static ChartPrintSettingsSummary? CaptureChartPrintSettingsSummary(ChartPrintSettingsModel? printSettings) =>
        printSettings is null
            ? null
            : new ChartPrintSettingsSummary(
                CaptureChartPageMarginsSummary(printSettings.PageMargins),
                CaptureChartPageSetupSummary(printSettings.PageSetup));

    private static ChartPageMarginsSummary? CaptureChartPageMarginsSummary(ChartPageMarginsModel? pageMargins) =>
        pageMargins is null
            ? null
            : new ChartPageMarginsSummary(
                pageMargins.Left,
                pageMargins.Right,
                pageMargins.Top,
                pageMargins.Bottom,
                pageMargins.Header,
                pageMargins.Footer);

    private static ChartPageSetupSummary? CaptureChartPageSetupSummary(ChartPageSetupModel? pageSetup) =>
        pageSetup is null
            ? null
            : new ChartPageSetupSummary(
                pageSetup.PaperSize ?? "",
                pageSetup.Orientation ?? "",
                pageSetup.Copies,
                pageSetup.BlackAndWhite,
                pageSetup.Draft);

    private static ChartTrendlineSummary CaptureChartTrendlineSummary(ChartModel chart) =>
        new(
            chart.ShowLinearTrendline,
            chart.TrendlineType,
            chart.TrendlinePeriod,
            chart.TrendlineOrder,
            chart.ShowTrendlineEquation,
            chart.ShowTrendlineRSquared,
            chart.TrendlineColor is null ? "" : ToColorSummary(chart.TrendlineColor.Value),
            chart.TrendlineThemeColor,
            chart.TrendlineThickness,
            chart.TrendlineDashStyle);

    private static ChartErrorBarSummary CaptureChartErrorBarSummary(ChartModel chart) =>
        new(
            chart.ShowErrorBars,
            chart.ErrorBarKind,
            chart.ErrorBarDirection,
            chart.ErrorBarValue,
            chart.ErrorBarEndCaps,
            chart.ErrorBarColor is null ? "" : ToColorSummary(chart.ErrorBarColor.Value),
            chart.ErrorBarThemeColor,
            chart.ErrorBarThickness,
            chart.ErrorBarDashStyle);
    private static ChartGuideLineSummary CaptureChartGuideLineSummary(
        bool show,
        CellColor? color,
        WorkbookThemeColorReference? themeColor,
        double thickness,
        ChartLineDashStyle dashStyle) =>
        new(
            show,
            color is null ? "" : ToColorSummary(color.Value),
            themeColor,
            thickness,
            dashStyle);

    private static ChartUpDownBarsSummary CaptureChartUpDownBarsSummary(ChartModel chart) =>
        new(
            chart.ShowUpDownBars,
            chart.UpDownBarGapWidth,
            CaptureChartBarShapeSummary(
                chart.UpBarFillColor,
                chart.UpBarFillThemeColor,
                chart.UpBarBorderColor,
                chart.UpBarBorderThemeColor,
                chart.UpBarBorderThickness),
            CaptureChartBarShapeSummary(
                chart.DownBarFillColor,
                chart.DownBarFillThemeColor,
                chart.DownBarBorderColor,
                chart.DownBarBorderThemeColor,
                chart.DownBarBorderThickness));

    private static ChartBarShapeSummary CaptureChartBarShapeSummary(
        CellColor? fillColor,
        WorkbookThemeColorReference? fillThemeColor,
        CellColor? borderColor,
        WorkbookThemeColorReference? borderThemeColor,
        double? borderThickness) =>
        new(
            fillColor is null ? "" : ToColorSummary(fillColor.Value),
            fillThemeColor,
            borderColor is null ? "" : ToColorSummary(borderColor.Value),
            borderThemeColor,
            borderThickness);

    private static ChartVisualSummary CaptureChartVisualSummary(ChartModel chart) =>
        new(
            chart.ChartTitleTextColor is null ? "" : ToColorSummary(chart.ChartTitleTextColor.Value),
            chart.ChartTitleTextThemeColor,
            chart.ChartTitleFontSize,
            chart.AxisTitleTextColor is null ? "" : ToColorSummary(chart.AxisTitleTextColor.Value),
            chart.AxisTitleTextThemeColor,
            chart.AxisTitleFontSize,
            chart.ChartAreaFillColor is null ? "" : ToColorSummary(chart.ChartAreaFillColor.Value),
            chart.ChartAreaFillThemeColor,
            chart.PlotAreaFillColor is null ? "" : ToColorSummary(chart.PlotAreaFillColor.Value),
            chart.PlotAreaFillThemeColor,
            chart.PlotAreaBorderColor is null ? "" : ToColorSummary(chart.PlotAreaBorderColor.Value),
            chart.PlotAreaBorderThemeColor,
            chart.PlotAreaBorderThickness,
            chart.LegendTextColor is null ? "" : ToColorSummary(chart.LegendTextColor.Value),
            chart.LegendTextThemeColor,
            chart.LegendFillColor is null ? "" : ToColorSummary(chart.LegendFillColor.Value),
            chart.LegendFillThemeColor,
            chart.LegendBorderColor is null ? "" : ToColorSummary(chart.LegendBorderColor.Value),
            chart.LegendBorderThemeColor,
            chart.LegendBorderThickness,
            chart.LegendFontSize);

    private static ChartAxisSummary CaptureChartAxisSummary(ChartModel chart, bool isXAxis) =>
        isXAxis
            ? new ChartAxisSummary(
                chart.XAxisMinimum,
                chart.XAxisMaximum,
                chart.XAxisMajorUnit,
                chart.XAxisMinorUnit,
                chart.XAxisLogScale,
                chart.XAxisNumberFormat,
                chart.ShowXAxisMajorGridlines,
                chart.ShowXAxisMinorGridlines,
                chart.XAxisIsDateAxis,
                chart.XAxisMajorGridlineColor is null ? "" : ToColorSummary(chart.XAxisMajorGridlineColor.Value),
                chart.XAxisMinorGridlineColor is null ? "" : ToColorSummary(chart.XAxisMinorGridlineColor.Value),
                chart.XAxisGridlineThickness,
                chart.XAxisMajorTickStyle,
                chart.XAxisMinorTickStyle,
                chart.ShowXAxisLabels,
                chart.XAxisLabelTextColor is null ? "" : ToColorSummary(chart.XAxisLabelTextColor.Value),
                chart.XAxisLabelTextThemeColor,
                chart.XAxisLabelFontSize,
                chart.XAxisLabelAngle,
                chart.XAxisLabelSkip,
                chart.XAxisTickMarkSkip,
                chart.XAxisLabelOffset,
                chart.XAxisLineColor is null ? "" : ToColorSummary(chart.XAxisLineColor.Value),
                chart.XAxisLineThickness)
            : new ChartAxisSummary(
                chart.YAxisMinimum,
                chart.YAxisMaximum,
                chart.YAxisMajorUnit,
                chart.YAxisMinorUnit,
                chart.YAxisLogScale,
                chart.YAxisNumberFormat,
                chart.ShowYAxisMajorGridlines,
                chart.ShowYAxisMinorGridlines,
                false,
                chart.YAxisMajorGridlineColor is null ? "" : ToColorSummary(chart.YAxisMajorGridlineColor.Value),
                chart.YAxisMinorGridlineColor is null ? "" : ToColorSummary(chart.YAxisMinorGridlineColor.Value),
                chart.YAxisGridlineThickness,
                chart.YAxisMajorTickStyle,
                chart.YAxisMinorTickStyle,
                chart.ShowYAxisLabels,
                chart.YAxisLabelTextColor is null ? "" : ToColorSummary(chart.YAxisLabelTextColor.Value),
                chart.YAxisLabelTextThemeColor,
                chart.YAxisLabelFontSize,
                chart.YAxisLabelAngle,
                0,
                0,
                0,
                chart.YAxisLineColor is null ? "" : ToColorSummary(chart.YAxisLineColor.Value),
                chart.YAxisLineThickness);

    private static ChartColorMapSummary? CaptureChartColorMapSummary(ChartColorMapOverrideModel? colorMap) =>
        colorMap is null
            ? null
            : new ChartColorMapSummary(
                colorMap.UseMasterColorMapping,
                colorMap.OverrideMappings
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new ChartColorMapEntrySummary(pair.Key, pair.Value))
                    .ToArray());

    private static ChartExternalDataSummary? CaptureChartExternalDataSummary(ChartExternalDataModel? externalData) =>
        externalData is null
            ? null
            : new ChartExternalDataSummary(
                externalData.RelationshipId ?? "",
                externalData.RelationshipType ?? "",
                externalData.Target ?? "",
                externalData.TargetMode ?? "",
                externalData.AutoUpdate);

    private static ChartManualLayoutSummary? CaptureChartManualLayoutSummary(ChartManualLayoutModel? layout) =>
        layout is null
            ? null
            : new ChartManualLayoutSummary(
                layout.LayoutTarget ?? "",
                layout.XMode ?? "",
                layout.YMode ?? "",
                layout.WidthMode ?? "",
                layout.HeightMode ?? "",
                layout.X,
                layout.Y,
                layout.Width,
                layout.Height);

    private static Chart3DViewSummary? CaptureChart3DViewSummary(Chart3DViewModel? view) =>
        view is null
            ? null
            : new Chart3DViewSummary(
                view.RotationX,
                view.HeightPercent,
                view.RotationY,
                view.DepthPercent,
                view.RightAngleAxes,
                view.Perspective);

    private static ChartSurfaceFormatSummary? CaptureChartSurfaceFormatSummary(ChartSurfaceFormatModel? format) =>
        format is null
            ? null
            : new ChartSurfaceFormatSummary(
                format.FillColor is null ? "" : ToColorSummary(format.FillColor.Value),
                format.FillThemeColor,
                format.BorderColor is null ? "" : ToColorSummary(format.BorderColor.Value),
                format.BorderThemeColor,
                format.BorderThickness);

}
