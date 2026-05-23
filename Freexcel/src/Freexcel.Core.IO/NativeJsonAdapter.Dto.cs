using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private class WorkbookDto
    {
        public string? FileFormat { get; set; }
        public int? SchemaVersion { get; set; }
        public int? MinimumReaderVersion { get; set; }
        public string Name { get; set; } = "";
        public WorkbookThemeDto? Theme { get; set; }
        public bool IsStructureProtected { get; set; }
        public string? StructureProtectionPassword { get; set; }
        public WorkbookWindowArrangement? WindowArrangement { get; set; }
        public WorkbookCalculationMode? CalculationMode { get; set; }
        public bool FullCalculationOnLoad { get; set; }
        public bool ForceFullCalculation { get; set; }
        public bool IterativeCalculation { get; set; }
        public int? MaxCalculationIterations { get; set; }
        public double? MaxCalculationChange { get; set; }
        public List<string> DisabledFormulaErrorCodes { get; set; } = [];
        public List<NamedRangeDto> NamedRanges { get; set; } = [];
        public List<CustomViewDto> CustomViews { get; set; } = [];
        public List<WatchedCellDto> WatchedCells { get; set; } = [];
        public List<ScenarioDto> Scenarios { get; set; } = [];
        public List<SheetDto> Sheets { get; set; } = [];
    }

    private class WorkbookThemeDto
    {
        public string? Name { get; set; }
        public string? MajorFontName { get; set; }
        public string? MinorFontName { get; set; }
        public string? EffectsName { get; set; }
        public List<WorkbookThemeColorDto> Colors { get; set; } = [];
    }

    private class WorkbookThemeColorDto
    {
        public WorkbookThemeColorSlot Slot { get; set; }
        public string? Color { get; set; }
    }

    private class NamedRangeDto
    {
        public string? Name { get; set; }
        public string? SheetName { get; set; }
        public string? Range { get; set; }
        public string? Scope { get; set; }
        public string? Comment { get; set; }
    }

    private class WatchedCellDto
    {
        public string SheetName { get; set; } = "";
        public string Address { get; set; } = "";
    }

    private class ScenarioDto
    {
        public string Name { get; set; } = "";
        public List<ScenarioCellDto> ChangingCells { get; set; } = [];
    }

    private class ScenarioCellDto
    {
        public string SheetName { get; set; } = "";
        public string Address { get; set; } = "";
        public string? Value { get; set; }
        public string? ValueType { get; set; }
    }

    private class CustomViewDto
    {
        public string Name { get; set; } = "";
        public string? Id { get; set; }
        public bool? IncludePrintSettings { get; set; }
        public bool? IncludeHiddenRowsColumnsAndFilterSettings { get; set; }
        public List<CustomViewSheetDto> Sheets { get; set; } = [];
    }

    private class CustomViewSheetDto
    {
        public string SheetName { get; set; } = "";
        public WorksheetViewMode ViewMode { get; set; } = WorksheetViewMode.Normal;
        public uint FrozenRows { get; set; }
        public uint FrozenCols { get; set; }
        public uint? SplitRow { get; set; }
        public uint? SplitColumn { get; set; }
        public bool? ShowGridlines { get; set; }
        public bool? ShowHeadings { get; set; }
        public bool? ShowRulers { get; set; }
        public int? ZoomPercent { get; set; }
        public bool? ShowFormulas { get; set; }
    }

    private class SheetDto
    {
        public string Name { get; set; } = "";
        public bool IsHidden { get; set; }
        public string? TabColor { get; set; }
        public bool IsProtected { get; set; }
        public string? ProtectionPassword { get; set; }
        public List<WorksheetCustomPropertyDto> CustomProperties { get; set; } = [];
        public List<UIntDoubleDto> RowHeights { get; set; } = [];
        public List<UIntDoubleDto> ColumnWidths { get; set; } = [];
        public List<uint> HiddenRows { get; set; } = [];
        public List<uint> FilterHiddenRows { get; set; } = [];
        public List<uint> HiddenCols { get; set; } = [];
        public List<UIntIntDto> RowOutlineLevels { get; set; } = [];
        public List<UIntIntDto> ColOutlineLevels { get; set; } = [];
        public List<uint> GroupHiddenRows { get; set; } = [];
        public List<uint> GroupHiddenCols { get; set; } = [];
        public WorksheetViewMode ViewMode { get; set; } = WorksheetViewMode.Normal;
        public bool? ShowGridlines { get; set; }
        public bool? ShowHeadings { get; set; }
        public bool? ShowRulers { get; set; }
        public int? ZoomPercent { get; set; }
        public bool? ShowFormulas { get; set; }
        public bool FullCalculationOnLoad { get; set; }
        public WorksheetPhoneticPropertiesDto? PhoneticProperties { get; set; }
        public uint FrozenRows { get; set; }
        public uint FrozenCols { get; set; }
        public uint? ViewTopRow { get; set; }
        public uint? ViewLeftCol { get; set; }
        public uint? ActiveRow { get; set; }
        public uint? ActiveCol { get; set; }
        public uint? SplitRow { get; set; }
        public uint? SplitColumn { get; set; }
        public string? PrintArea { get; set; }
        public WorksheetPageOrientation? PageOrientation { get; set; }
        public WorksheetPaperSize? PaperSize { get; set; }
        public PageMarginsDto? PageMargins { get; set; }
        public double? HeaderMargin { get; set; }
        public double? FooterMargin { get; set; }
        public bool PrintGridlines { get; set; }
        public bool PrintHeadings { get; set; }
        public RepeatRangeDto? PrintTitleRows { get; set; }
        public RepeatRangeDto? PrintTitleColumns { get; set; }
        public HeaderFooterDto? PageHeader { get; set; }
        public HeaderFooterDto? PageFooter { get; set; }
        public HeaderFooterDto? FirstPageHeader { get; set; }
        public HeaderFooterDto? FirstPageFooter { get; set; }
        public HeaderFooterDto? EvenPageHeader { get; set; }
        public HeaderFooterDto? EvenPageFooter { get; set; }
        public HeaderFooterPictureSetDto? PageHeaderPictures { get; set; }
        public HeaderFooterPictureSetDto? PageFooterPictures { get; set; }
        public HeaderFooterPictureSetDto? FirstPageHeaderPictures { get; set; }
        public HeaderFooterPictureSetDto? FirstPageFooterPictures { get; set; }
        public HeaderFooterPictureSetDto? EvenPageHeaderPictures { get; set; }
        public HeaderFooterPictureSetDto? EvenPageFooterPictures { get; set; }
        public bool DifferentFirstPageHeaderFooter { get; set; }
        public bool DifferentOddEvenHeaderFooter { get; set; }
        public bool? HeaderFooterScaleWithDocument { get; set; }
        public bool? HeaderFooterAlignWithMargins { get; set; }
        public bool CenterHorizontallyOnPage { get; set; }
        public bool CenterVerticallyOnPage { get; set; }
        public WorksheetPageOrder? PageOrder { get; set; }
        public int? FirstPageNumber { get; set; }
        public bool PrintBlackAndWhite { get; set; }
        public bool PrintDraftQuality { get; set; }
        public int? PrintQualityDpi { get; set; }
        public WorksheetPrintErrorValue? PrintErrorValue { get; set; }
        public WorksheetPrintComments? PrintComments { get; set; }
        public ScaleToFitDto? ScaleToFit { get; set; }
        public List<uint> RowPageBreaks { get; set; } = [];
        public List<uint> ColumnPageBreaks { get; set; } = [];
        public List<string> MergedRegions { get; set; } = [];
        public List<CommentDto> Comments { get; set; } = [];
        public List<HyperlinkDto> Hyperlinks { get; set; } = [];
        public List<string> AllowEditRanges { get; set; } = [];
        public WorksheetBackgroundDto? BackgroundImage { get; set; }
        public List<PictureDto> Pictures { get; set; } = [];
        public List<TextBoxDto> TextBoxes { get; set; } = [];
        public List<DrawingShapeDto> DrawingShapes { get; set; } = [];
        public List<SparklineDto> Sparklines { get; set; } = [];
        public List<ChartDto> Charts { get; set; } = [];
        public List<DataValidationDto> DataValidations { get; set; } = [];
        public List<ConditionalFormatDto> ConditionalFormats { get; set; } = [];
        public List<CellDto> Cells { get; set; } = [];
        public List<StyleOnlyCellDto> StyleOnlyCells { get; set; } = [];
    }

    private class WorksheetCustomPropertyDto
    {
        public string Name { get; set; } = "";
        public int Id { get; set; }
    }

    private class WorksheetPhoneticPropertiesDto
    {
        public string? FontId { get; set; }
        public string? Type { get; set; }
        public string? Alignment { get; set; }
    }

    private class DataValidationDto
    {
        public string? AppliesTo { get; set; }
        public DvType Type { get; set; } = DvType.Any;
        public DvOperator Operator { get; set; } = DvOperator.Between;
        public string? Formula1 { get; set; }
        public string? Formula2 { get; set; }
        public bool AllowBlank { get; set; } = true;
        public bool ShowDropdown { get; set; } = true;
        public DvAlertStyle AlertStyle { get; set; } = DvAlertStyle.Stop;
        public bool ShowInputMessage { get; set; } = true;
        public bool ShowErrorMessage { get; set; } = true;
        public string? ErrorTitle { get; set; }
        public string? ErrorMessage { get; set; }
        public string? PromptTitle { get; set; }
        public string? PromptMessage { get; set; }
        public Dictionary<string, string>? NativeAttributes { get; set; }
        public List<string>? NativeChildXmls { get; set; }
        public Dictionary<string, string>? NativeContainerAttributes { get; set; }
        public List<string>? NativeContainerChildXmls { get; set; }
    }

    private class ConditionalFormatDto
    {
        public string? AppliesTo { get; set; }
        public int Priority { get; set; } = 1;
        public CfRuleType RuleType { get; set; }
        public CfOperator Operator { get; set; }
        public string? Value1 { get; set; }
        public string? Value2 { get; set; }
        public CellStyleDto? FormatIfTrue { get; set; }
        public RgbColor MinColor { get; set; } = new(99, 190, 123);
        public RgbColor MidColor { get; set; } = new(255, 235, 132);
        public RgbColor MaxColor { get; set; } = new(248, 105, 107);
        public bool UseThreeColorScale { get; set; }
        public RgbColor DataBarColor { get; set; } = new(99, 142, 198);
        public bool AboveAverage { get; set; } = true;
        public string? FormulaText { get; set; }
        public int TopBottomRank { get; set; } = 10;
        public bool TopBottomPercent { get; set; }
        public string? TextRuleText { get; set; }
        public string? DateOccurringPeriod { get; set; }
        public bool StopIfTrue { get; set; }
        public IReadOnlyDictionary<string, string>? NativeAttributes { get; set; }
        public IReadOnlyList<string>? NativeChildXmls { get; set; }
        public IReadOnlyDictionary<string, string>? NativePayloadAttributes { get; set; }
        public IReadOnlyList<string>? NativePayloadChildXmls { get; set; }
        public IReadOnlyDictionary<string, string>? NativeContainerAttributes { get; set; }
        public IReadOnlyList<string>? NativeContainerChildXmls { get; set; }
    }

    private class CellStyleDto
    {
        public string FontName { get; set; } = "Calibri";
        public double FontSize { get; set; } = 11;
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public bool Strikethrough { get; set; }
        public bool Superscript { get; set; }
        public bool Subscript { get; set; }
        public CellColor FontColor { get; set; } = CellColor.Black;
        public CellColor? FillColor { get; set; }
        public CellFillPatternStyle FillPatternStyle { get; set; }
        public CellColor? FillPatternColor { get; set; }
        public CellBorderDto? BorderTop { get; set; }
        public CellBorderDto? BorderRight { get; set; }
        public CellBorderDto? BorderBottom { get; set; }
        public CellBorderDto? BorderLeft { get; set; }
        public string NumberFormat { get; set; } = "General";
        public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.General;
        public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Bottom;
        public bool WrapText { get; set; }
        public bool ShrinkToFit { get; set; }
        public bool DoubleUnderline { get; set; }
        public int IndentLevel { get; set; }
        public int TextRotation { get; set; }
        public bool Locked { get; set; } = true;
        public IReadOnlyDictionary<string, string>? NativeDifferentialAttributes { get; set; }
        public IReadOnlyList<string>? NativeDifferentialChildXmls { get; set; }
        public IReadOnlyDictionary<string, string>? NativeDifferentialElementXmls { get; set; }
    }

    private class CellBorderDto
    {
        public BorderStyle Style { get; set; }
        public CellColor Color { get; set; }
    }

    private class CommentDto
    {
        public string? Address { get; set; }
        public string? Text { get; set; }
    }

    private class HyperlinkDto
    {
        public string? Address { get; set; }
        public string? Target { get; set; }
        public HyperlinkTargetKind? LinkType { get; set; }
        public string? ScreenTip { get; set; }
        public string? Bookmark { get; set; }
    }

    private class UIntDoubleDto
    {
        public uint Index { get; set; }
        public double Value { get; set; }
    }

    private class UIntIntDto
    {
        public uint Index { get; set; }
        public int Value { get; set; }
    }

    private class PageMarginsDto
    {
        public double Left { get; set; }
        public double Right { get; set; }
        public double Top { get; set; }
        public double Bottom { get; set; }
    }

    private class RepeatRangeDto
    {
        public uint Start { get; set; }
        public uint End { get; set; }
    }

    private class ScaleToFitDto
    {
        public int? ScalePercent { get; set; }
        public int? FitToPagesWide { get; set; }
        public int? FitToPagesTall { get; set; }
    }

    private class HeaderFooterDto
    {
        public string? Left { get; set; }
        public string? Center { get; set; }
        public string? Right { get; set; }
    }

    private class HeaderFooterPictureSetDto
    {
        public HeaderFooterPictureDto? Left { get; set; }
        public HeaderFooterPictureDto? Center { get; set; }
        public HeaderFooterPictureDto? Right { get; set; }
    }

    private class HeaderFooterPictureDto
    {
        public string ImageBase64 { get; set; } = "";
        public string ContentType { get; set; } = "image/png";
        public string? FileName { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private class WorksheetBackgroundDto
    {
        public string ImageBase64 { get; set; } = "";
        public string ContentType { get; set; } = "image/png";
        public string? FileName { get; set; }
    }

    private class SparklineDto
    {
        public string? DataRange { get; set; }
        public string? Location { get; set; }
        public SparklineKind Kind { get; set; } = SparklineKind.Line;
    }

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
        public double ChartTitleFontSize { get; set; } = 16;
        public CellColor? AxisTitleTextColor { get; set; }
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

    private class CellDto
    {
        public string Address { get; set; } = "";
        public string? Value { get; set; }
        public string? ValueType { get; set; }
        public string? Formula { get; set; }
        public bool IgnoreFormulaError { get; set; }
        public CellStyleDto? Style { get; set; }
    }

    private class StyleOnlyCellDto
    {
        public string? Address { get; set; }
        public CellStyleDto? Style { get; set; }
    }
}
