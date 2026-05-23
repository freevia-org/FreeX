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
        public bool ShowPivotChartFieldButtons { get; set; } = true;
        public bool ShowPivotChartReportFilterButtons { get; set; } = true;
        public bool ShowPivotChartAxisFieldButtons { get; set; } = true;
        public bool ShowPivotChartValueFieldButtons { get; set; } = true;
        public bool FirstRowIsHeader { get; set; } = true;
        public bool FirstColIsCategories { get; set; } = true;
        public string? Title { get; set; }
        public string? XAxisTitle { get; set; }
        public string? YAxisTitle { get; set; }
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
        public ChartDataLabelNumberFormat XAxisNumberFormat { get; set; } = ChartDataLabelNumberFormat.General;
        public bool ShowXAxisMajorGridlines { get; set; }
        public bool ShowXAxisMinorGridlines { get; set; }
        public CellColor? XAxisMajorGridlineColor { get; set; }
        public CellColor? XAxisMinorGridlineColor { get; set; }
        public double XAxisGridlineThickness { get; set; } = 1;
        public ChartAxisTickStyle XAxisMajorTickStyle { get; set; } = ChartAxisTickStyle.Outside;
        public ChartAxisTickStyle XAxisMinorTickStyle { get; set; } = ChartAxisTickStyle.None;
        public bool ShowXAxisLabels { get; set; } = true;
        public CellColor? XAxisLabelTextColor { get; set; }
        public ThemeColorReferenceDto? XAxisLabelTextThemeColor { get; set; }
        public double XAxisLabelFontSize { get; set; } = 11;
        public double XAxisLabelAngle { get; set; }
        public CellColor? XAxisLineColor { get; set; }
        public double XAxisLineThickness { get; set; } = 1;
        public double? YAxisMinimum { get; set; }
        public double? YAxisMaximum { get; set; }
        public double? YAxisMajorUnit { get; set; }
        public double? YAxisMinorUnit { get; set; }
        public bool YAxisLogScale { get; set; }
        public ChartDataLabelNumberFormat YAxisNumberFormat { get; set; } = ChartDataLabelNumberFormat.General;
        public bool ShowYAxisMajorGridlines { get; set; }
        public bool ShowYAxisMinorGridlines { get; set; }
        public CellColor? YAxisMajorGridlineColor { get; set; }
        public CellColor? YAxisMinorGridlineColor { get; set; }
        public double YAxisGridlineThickness { get; set; } = 1;
        public ChartAxisTickStyle YAxisMajorTickStyle { get; set; } = ChartAxisTickStyle.Outside;
        public ChartAxisTickStyle YAxisMinorTickStyle { get; set; } = ChartAxisTickStyle.None;
        public bool ShowYAxisLabels { get; set; } = true;
        public CellColor? YAxisLabelTextColor { get; set; }
        public ThemeColorReferenceDto? YAxisLabelTextThemeColor { get; set; }
        public double YAxisLabelFontSize { get; set; } = 11;
        public double YAxisLabelAngle { get; set; }
        public CellColor? YAxisLineColor { get; set; }
        public double YAxisLineThickness { get; set; } = 1;
        public ChartDataTableModel? DataTable { get; set; }
        public int? BarGapWidth { get; set; }
        public int? BarOverlap { get; set; }
        public bool? VaryColorsByPoint { get; set; }
        public StockChartSubtype StockSubtype { get; set; } = StockChartSubtype.HighLowClose;
        public ChartLegendPosition LegendPosition { get; set; } = ChartLegendPosition.Right;
        public bool LegendOverlay { get; set; }
        public bool ShowLegend { get; set; } = true;
        public bool ShowDataLabels { get; set; }
        public ChartDataLabelPosition DataLabelPosition { get; set; } = ChartDataLabelPosition.BestFit;
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
        public bool ShowDropLines { get; set; }
        public bool ShowHighLowLines { get; set; }
        public bool ShowUpDownBars { get; set; }
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
}
