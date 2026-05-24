using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private class ChartDto
    {
        public string? Name { get; set; }
        public ChartType Type { get; set; } = ChartType.Column;
        public string? DataRange { get; set; }
        public bool IsVisible { get; set; } = true;
        public bool IsPivotChart { get; set; }
        public string? PivotSourceSheetName { get; set; }
        public string? PivotTableName { get; set; }
        public int? PivotSourceFormatId { get; set; }
        public int? PivotCacheId { get; set; }
        public int? ChartStyleId { get; set; }
        public string? PivotFormatsXml { get; set; }
        public bool Uses1904DateSystem { get; set; }
        public string? Language { get; set; }
        public ChartColorMapOverrideModel? ColorMapOverride { get; set; }
        public ChartExternalDataModel? ExternalData { get; set; }
        public ChartManualLayoutModel? PlotAreaLayout { get; set; }
        public ChartManualLayoutModel? LegendLayout { get; set; }
        public bool RoundedCorners { get; set; }
        public ChartBlankDisplayMode BlankDisplayMode { get; set; } = ChartBlankDisplayMode.Gap;
        public bool ShowDataLabelsOverMaximum { get; set; }
        public bool AutoTitleDeleted { get; set; }
        public bool ShowDataInHiddenRowsAndColumns { get; set; }
        public ChartProtectionModel? Protection { get; set; }
        public ChartPrintSettingsModel? PrintSettings { get; set; }
        public Chart3DViewModel? ThreeDView { get; set; }
        public ChartSurfaceFormatDto? FloorFormat { get; set; }
        public ChartSurfaceFormatDto? SideWallFormat { get; set; }
        public ChartSurfaceFormatDto? BackWallFormat { get; set; }
        public bool ShowPivotChartFieldButtons { get; set; } = true;
        public bool ShowPivotChartReportFilterButtons { get; set; } = true;
        public bool ShowPivotChartAxisFieldButtons { get; set; } = true;
        public bool ShowPivotChartValueFieldButtons { get; set; } = true;
        public bool FirstRowIsHeader { get; set; } = true;
        public bool FirstColIsCategories { get; set; } = true;
        public string? Title { get; set; }
        public string? XAxisTitle { get; set; }
        public string? YAxisTitle { get; set; }
        public bool HideXAxis { get; set; }
        public bool HideYAxis { get; set; }
        public ChartAxisPosition XAxisPosition { get; set; } = ChartAxisPosition.Bottom;
        public ChartAxisPosition YAxisPosition { get; set; } = ChartAxisPosition.Left;
        public CellColor? ChartTitleTextColor { get; set; }
        public ThemeColorReferenceDto? ChartTitleTextThemeColor { get; set; }
        public double ChartTitleFontSize { get; set; } = 16;
        public CellColor? AxisTitleTextColor { get; set; }
        public ThemeColorReferenceDto? AxisTitleTextThemeColor { get; set; }
        public double AxisTitleFontSize { get; set; } = 12;
        public CellColor? ChartAreaFillColor { get; set; }
        public ThemeColorReferenceDto? ChartAreaFillThemeColor { get; set; }
        public CellColor? PlotAreaFillColor { get; set; }
        public ThemeColorReferenceDto? PlotAreaFillThemeColor { get; set; }
        public CellColor? PlotAreaBorderColor { get; set; }
        public ThemeColorReferenceDto? PlotAreaBorderThemeColor { get; set; }
        public double PlotAreaBorderThickness { get; set; } = 1;
        public CellColor? LegendTextColor { get; set; }
        public ThemeColorReferenceDto? LegendTextThemeColor { get; set; }
        public CellColor? LegendFillColor { get; set; }
        public ThemeColorReferenceDto? LegendFillThemeColor { get; set; }
        public CellColor? LegendBorderColor { get; set; }
        public ThemeColorReferenceDto? LegendBorderThemeColor { get; set; }
        public double LegendBorderThickness { get; set; }
        public double LegendFontSize { get; set; } = 12;
        public double DoughnutHoleSize { get; set; } = 0.55;
        public double FirstSliceAngle { get; set; }
        public int ExplodedSliceIndex { get; set; } = -1;
        public double ExplodedSliceDistance { get; set; } = 0.1;
        public double? XAxisMinimum { get; set; }
        public double? XAxisMaximum { get; set; }
        public double? XAxisMajorUnit { get; set; }
        public double? XAxisMinorUnit { get; set; }
        public bool XAxisLogScale { get; set; }
        public double? XAxisLogBase { get; set; }
        public bool XAxisReverseOrder { get; set; }
        public ChartDataLabelNumberFormat XAxisNumberFormat { get; set; } = ChartDataLabelNumberFormat.General;
        public bool ShowXAxisMajorGridlines { get; set; }
        public bool ShowXAxisMinorGridlines { get; set; }
        public bool XAxisIsDateAxis { get; set; }
        public CellColor? XAxisMajorGridlineColor { get; set; }
        public CellColor? XAxisMinorGridlineColor { get; set; }
        public double XAxisGridlineThickness { get; set; } = 1;
        public ChartAxisTickStyle XAxisMajorTickStyle { get; set; } = ChartAxisTickStyle.Outside;
        public ChartAxisTickStyle XAxisMinorTickStyle { get; set; } = ChartAxisTickStyle.None;
        public bool ShowXAxisLabels { get; set; } = true;
        public ChartAxisTickLabelPosition XAxisTickLabelPosition { get; set; } = ChartAxisTickLabelPosition.NextTo;
        public CellColor? XAxisLabelTextColor { get; set; }
        public ThemeColorReferenceDto? XAxisLabelTextThemeColor { get; set; }
        public double XAxisLabelFontSize { get; set; } = 11;
        public double XAxisLabelAngle { get; set; }
        public int XAxisLabelSkip { get; set; }
        public int XAxisTickMarkSkip { get; set; }
        public int XAxisLabelOffset { get; set; }
        public bool XAxisNoMultiLevelLabels { get; set; }
        public ChartAxisLabelAlignment XAxisLabelAlignment { get; set; } = ChartAxisLabelAlignment.Center;
        public ChartDateAxisUnit? XAxisBaseTimeUnit { get; set; }
        public ChartDateAxisUnit? XAxisMajorTimeUnit { get; set; }
        public ChartDateAxisUnit? XAxisMinorTimeUnit { get; set; }
        public CellColor? XAxisLineColor { get; set; }
        public double XAxisLineThickness { get; set; } = 1;
        public ChartAxisCrosses XAxisCrosses { get; set; } = ChartAxisCrosses.AutoZero;
        public double? XAxisCrossesAt { get; set; }
        public ChartAxisCrossBetween? XAxisCrossBetween { get; set; }
        public ChartAxisDisplayUnit? XAxisDisplayUnit { get; set; }
        public double? XAxisCustomDisplayUnit { get; set; }
        public double? YAxisMinimum { get; set; }
        public double? YAxisMaximum { get; set; }
        public double? YAxisMajorUnit { get; set; }
        public double? YAxisMinorUnit { get; set; }
        public bool YAxisLogScale { get; set; }
        public double? YAxisLogBase { get; set; }
        public bool YAxisReverseOrder { get; set; }
        public ChartDataLabelNumberFormat YAxisNumberFormat { get; set; } = ChartDataLabelNumberFormat.General;
        public bool ShowYAxisMajorGridlines { get; set; }
        public bool ShowYAxisMinorGridlines { get; set; }
        public CellColor? YAxisMajorGridlineColor { get; set; }
        public CellColor? YAxisMinorGridlineColor { get; set; }
        public double YAxisGridlineThickness { get; set; } = 1;
        public ChartAxisTickStyle YAxisMajorTickStyle { get; set; } = ChartAxisTickStyle.Outside;
        public ChartAxisTickStyle YAxisMinorTickStyle { get; set; } = ChartAxisTickStyle.None;
        public bool ShowYAxisLabels { get; set; } = true;
        public ChartAxisTickLabelPosition YAxisTickLabelPosition { get; set; } = ChartAxisTickLabelPosition.NextTo;
        public CellColor? YAxisLabelTextColor { get; set; }
        public ThemeColorReferenceDto? YAxisLabelTextThemeColor { get; set; }
        public double YAxisLabelFontSize { get; set; } = 11;
        public double YAxisLabelAngle { get; set; }
        public CellColor? YAxisLineColor { get; set; }
        public double YAxisLineThickness { get; set; } = 1;
        public ChartAxisCrosses YAxisCrosses { get; set; } = ChartAxisCrosses.AutoZero;
        public double? YAxisCrossesAt { get; set; }
        public ChartAxisCrossBetween? YAxisCrossBetween { get; set; }
        public ChartAxisDisplayUnit? YAxisDisplayUnit { get; set; }
        public double? YAxisCustomDisplayUnit { get; set; }
        public ChartDataTableModel? DataTable { get; set; }
        public int? BarGapWidth { get; set; }
        public int? BarOverlap { get; set; }
        public bool? VaryColorsByPoint { get; set; }
        public int BubbleScale { get; set; } = 100;
        public bool ShowNegativeBubbles { get; set; }
        public ChartBubbleSizeRepresents BubbleSizeRepresents { get; set; } = ChartBubbleSizeRepresents.Area;
        public StockChartSubtype StockSubtype { get; set; } = StockChartSubtype.HighLowClose;
        public ChartLegendPosition LegendPosition { get; set; } = ChartLegendPosition.Right;
        public bool LegendOverlay { get; set; }
        public bool ShowLegend { get; set; } = true;
        public bool ShowDataLabels { get; set; }
        public ChartDataLabelPosition DataLabelPosition { get; set; } = ChartDataLabelPosition.BestFit;
        public bool ShowDataLabelValue { get; set; } = true;
        public bool ShowDataLabelLegendKey { get; set; }
        public bool ShowDataLabelBubbleSize { get; set; }
        public bool ShowDataLabelCategoryName { get; set; }
        public bool ShowDataLabelSeriesName { get; set; }
        public bool ShowDataLabelPercentage { get; set; }
        public ChartDataLabelSeparator DataLabelSeparator { get; set; } = ChartDataLabelSeparator.Comma;
        public ChartDataLabelNumberFormat DataLabelNumberFormat { get; set; } = ChartDataLabelNumberFormat.General;
        public bool ShowDataLabelCallouts { get; set; }
        public CellColor? DataLabelFillColor { get; set; }
        public ThemeColorReferenceDto? DataLabelFillThemeColor { get; set; }
        public CellColor? DataLabelBorderColor { get; set; }
        public ThemeColorReferenceDto? DataLabelBorderThemeColor { get; set; }
        public CellColor? DataLabelTextColor { get; set; }
        public ThemeColorReferenceDto? DataLabelTextThemeColor { get; set; }
        public double DataLabelBorderThickness { get; set; }
        public double DataLabelFontSize { get; set; } = 11;
        public double DataLabelAngle { get; set; }
        public bool ShowLinearTrendline { get; set; }
        public ChartTrendlineType TrendlineType { get; set; } = ChartTrendlineType.Linear;
        public int TrendlinePeriod { get; set; } = 2;
        public int TrendlineOrder { get; set; } = 2;
        public bool ShowTrendlineEquation { get; set; }
        public bool ShowTrendlineRSquared { get; set; }
        public CellColor? TrendlineColor { get; set; }
        public ThemeColorReferenceDto? TrendlineThemeColor { get; set; }
        public double TrendlineThickness { get; set; } = 1.5;
        public ChartLineDashStyle TrendlineDashStyle { get; set; } = ChartLineDashStyle.Dash;
        public bool ShowErrorBars { get; set; }
        public ChartErrorBarKind ErrorBarKind { get; set; } = ChartErrorBarKind.StandardError;
        public ChartErrorBarDirection ErrorBarDirection { get; set; } = ChartErrorBarDirection.Both;
        public double ErrorBarValue { get; set; } = 5;
        public bool ErrorBarEndCaps { get; set; } = true;
        public CellColor? ErrorBarColor { get; set; }
        public ThemeColorReferenceDto? ErrorBarThemeColor { get; set; }
        public double ErrorBarThickness { get; set; } = 1;
        public ChartLineDashStyle ErrorBarDashStyle { get; set; } = ChartLineDashStyle.Solid;
        public bool ShowDropLines { get; set; }
        public CellColor? DropLineColor { get; set; }
        public ThemeColorReferenceDto? DropLineThemeColor { get; set; }
        public double DropLineThickness { get; set; } = 1;
        public ChartLineDashStyle DropLineDashStyle { get; set; } = ChartLineDashStyle.Solid;
        public bool ShowHighLowLines { get; set; }
        public CellColor? HighLowLineColor { get; set; }
        public ThemeColorReferenceDto? HighLowLineThemeColor { get; set; }
        public double HighLowLineThickness { get; set; } = 1;
        public ChartLineDashStyle HighLowLineDashStyle { get; set; } = ChartLineDashStyle.Solid;
        public bool ShowSeriesLines { get; set; }
        public CellColor? SeriesLineColor { get; set; }
        public ThemeColorReferenceDto? SeriesLineThemeColor { get; set; }
        public double SeriesLineThickness { get; set; } = 1;
        public ChartLineDashStyle SeriesLineDashStyle { get; set; } = ChartLineDashStyle.Solid;
        public bool ShowUpDownBars { get; set; }
        public int? UpDownBarGapWidth { get; set; }
        public CellColor? UpBarFillColor { get; set; }
        public ThemeColorReferenceDto? UpBarFillThemeColor { get; set; }
        public CellColor? UpBarBorderColor { get; set; }
        public ThemeColorReferenceDto? UpBarBorderThemeColor { get; set; }
        public double? UpBarBorderThickness { get; set; }
        public CellColor? DownBarFillColor { get; set; }
        public ThemeColorReferenceDto? DownBarFillThemeColor { get; set; }
        public CellColor? DownBarBorderColor { get; set; }
        public ThemeColorReferenceDto? DownBarBorderThemeColor { get; set; }
        public double? DownBarBorderThickness { get; set; }
        public bool ShowSecondaryAxis { get; set; }
        public List<int>? SecondaryAxisSeriesIndexes { get; set; }
        public List<int>? ComboLineSeriesIndexes { get; set; }
        public List<ChartSeriesFormat>? SeriesFormats { get; set; }
        public List<ChartPointDataLabelFormat>? PointDataLabelFormats { get; set; }
        public bool UseComboLineForSecondarySeries { get; set; }
        public double Left { get; set; } = 50;
        public double Top { get; set; } = 50;
        public double Width { get; set; } = 400;
        public double Height { get; set; } = 300;
    }

    private sealed class ChartSurfaceFormatDto
    {
        public CellColor? FillColor { get; set; }
        public ThemeColorReferenceDto? FillThemeColor { get; set; }
        public CellColor? BorderColor { get; set; }
        public ThemeColorReferenceDto? BorderThemeColor { get; set; }
        public double? BorderThickness { get; set; }
    }
}
