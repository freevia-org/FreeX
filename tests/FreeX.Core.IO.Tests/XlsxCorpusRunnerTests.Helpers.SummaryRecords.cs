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
    private sealed record WorkbookSummary(
        int SheetCount,
        IReadOnlyList<NamedRangeSummary> NamedRanges,
        int NamedRangeCount,
        bool IsStructureProtected,
        string StructureProtectionPassword,
        IReadOnlyList<PivotCacheSummary> PivotCaches,
        int PivotCacheCount,
        int PivotCacheFieldCount,
        IReadOnlyList<PivotTableStyleSummary> PivotTableStyles,
        int PivotTableStyleCount,
        int PivotTableStyleElementCount,
        IReadOnlyList<NumberFormatCatalogSummary> NumberFormatCatalog,
        IReadOnlyList<CustomViewSummary> CustomViews,
        int CustomViewCount,
        WorkbookMetadataSummary Metadata,
        WorkbookCalculationSummary Calculation,
        WorkbookThemeSummary Theme,
        IReadOnlyList<SheetSummary> Sheets);

    private sealed record WorkbookMetadataSummary(
        IReadOnlyList<SlicerSummary> Slicers,
        IReadOnlyList<TimelineSummary> Timelines,
        IReadOnlyList<ExternalLinkSummary> ExternalLinks,
        IReadOnlyList<WatchedCellSummary> WatchedCells,
        IReadOnlyList<ScenarioSummary> Scenarios);

    private sealed record SlicerSummary(
        string Name,
        string Caption,
        string CacheName,
        string SourcePivotTableName,
        string SourceFieldName,
        string StyleName,
        IReadOnlyList<string> SelectedItems,
        string PackagePart);

    private sealed record TimelineSummary(
        string Name,
        string Caption,
        string CacheName,
        string SourcePivotTableName,
        string SourceFieldName,
        string StyleName,
        string StartDate,
        string EndDate,
        string SelectedStartDate,
        string SelectedEndDate,
        string PackagePart);

    private sealed record ExternalLinkSummary(
        string PackagePart,
        string TargetUri,
        string TargetMode);

    private sealed record WatchedCellSummary(
        string SheetName,
        uint Row,
        uint Column);

    private sealed record ScenarioSummary(
        string Name,
        IReadOnlyList<ScenarioCellSummary> ChangingCells);

    private sealed record ScenarioCellSummary(
        string SheetName,
        uint Row,
        uint Column,
        ScalarValueSummary Value);

    private sealed record WorkbookCalculationSummary(
        WorkbookCalculationMode Mode,
        bool FullCalculationOnLoad,
        bool ForceFullCalculation,
        bool IterativeCalculation,
        int? MaxIterations,
        double? MaxChange);

    private sealed record WorkbookThemeSummary(
        string Name,
        string MajorFontName,
        string MinorFontName,
        string EffectsName,
        IReadOnlyList<ThemeColorSummary> Colors);

    private sealed record ThemeColorSummary(
        WorkbookThemeColorSlot Slot,
        string Color);

    private sealed record NamedRangeSummary(
        string Name,
        string Scope,
        string Comment,
        ChartRangeSummary Range);

    private sealed record SheetSummary(
        string Name,
        IReadOnlyList<CellSummary> Cells,
        int CellCount,
        int FormulaCount,
        int MergedRegionCount,
        IReadOnlyList<DataValidationSummary> DataValidations,
        int DataValidationCount,
        IReadOnlyList<ConditionalFormatSummary> ConditionalFormats,
        int ConditionalFormatCount,
        int ColorScaleConditionalFormatCount,
        int DataBarConditionalFormatCount,
        int IconSetConditionalFormatCount,
        IReadOnlyList<CommentSummary> Comments,
        int CommentCount,
        IReadOnlyList<HyperlinkSummary> Hyperlinks,
        int HyperlinkCount,
        IReadOnlyList<ChartSummary> Charts,
        int ChartCount,
        IReadOnlyList<PivotTableSummary> PivotTables,
        int PivotTableCount,
        int PivotTableFieldCount,
        IReadOnlyList<StructuredTableSummary> StructuredTables,
        int StructuredTableCount,
        int StructuredTableColumnCount,
        IReadOnlyList<SparklineSummary> Sparklines,
        int SparklineCount,
        IReadOnlyList<TextBoxSummary> TextBoxes,
        int TextBoxCount,
        IReadOnlyList<DrawingShapeSummary> DrawingShapes,
        int DrawingShapeCount,
        IReadOnlyList<PictureSummary> Pictures,
        int PictureCount,
        BackgroundImageSummary? BackgroundImage,
        bool HasBackgroundImage,
        bool IsProtected,
        string ProtectionPassword,
        IReadOnlyList<ChartRangeSummary> AllowEditRanges,
        int AllowEditRangeCount,
        ChartRangeSummary? PrintArea,
        bool HasPrintArea,
        RepeatRangeSummary? PrintTitleRows,
        bool HasPrintTitleRows,
        RepeatRangeSummary? PrintTitleColumns,
        bool HasPrintTitleColumns,
        WorksheetPageOrientation PageOrientation,
        WorksheetPaperSize PaperSize,
        WorksheetPageMargins PageMargins,
        double HeaderMargin,
        double FooterMargin,
        WorksheetScaleToFit ScaleToFit,
        bool PrintGridlines,
        bool PrintHeadings,
        HeaderFooterSummary PageHeader,
        bool HasPageHeader,
        HeaderFooterSummary PageFooter,
        bool HasPageFooter,
        HeaderFooterSummary FirstPageHeader,
        HeaderFooterSummary FirstPageFooter,
        HeaderFooterSummary EvenPageHeader,
        HeaderFooterSummary EvenPageFooter,
        bool DifferentFirstPageHeaderFooter,
        bool DifferentOddEvenHeaderFooter,
        bool HeaderFooterScaleWithDocument,
        bool HeaderFooterAlignWithMargins,
        HeaderFooterPictureSetSummary PageHeaderPictures,
        HeaderFooterPictureSetSummary PageFooterPictures,
        HeaderFooterPictureSetSummary FirstPageHeaderPictures,
        HeaderFooterPictureSetSummary FirstPageFooterPictures,
        HeaderFooterPictureSetSummary EvenPageHeaderPictures,
        HeaderFooterPictureSetSummary EvenPageFooterPictures,
        bool CenterHorizontallyOnPage,
        bool CenterVerticallyOnPage,
        WorksheetPageOrder PageOrder,
        int? FirstPageNumber,
        bool PrintBlackAndWhite,
        bool PrintDraftQuality,
        int? PrintQualityDpi,
        WorksheetPrintErrorValue PrintErrorValue,
        WorksheetPrintComments PrintComments,
        double DefaultColumnWidth,
        double DefaultRowHeight,
        IReadOnlyList<DimensionSummary> ColumnWidths,
        IReadOnlyList<DimensionSummary> RowHeights,
        IReadOnlyList<uint> RowPageBreaks,
        int RowPageBreakCount,
        IReadOnlyList<uint> ColumnPageBreaks,
        int ColumnPageBreakCount,
        uint FrozenRows,
        uint FrozenCols,
        uint? SplitRow,
        uint? SplitColumn,
        WorksheetViewMode ViewMode,
        uint? ViewTopRow,
        uint? ViewLeftColumn,
        uint? ActiveRow,
        uint? ActiveColumn,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        int ZoomPercent,
        bool ShowFormulas,
        bool FullCalculationOnLoad,
        PhoneticSummary? PhoneticProperties,
        bool IsHidden,
        bool IsVeryHidden,
        string CodeName,
        string TabColor,
        IReadOnlyList<WorksheetCustomPropertySummary> CustomProperties,
        IReadOnlyList<uint> HiddenRows,
        int HiddenRowCount,
        IReadOnlyList<uint> FilterHiddenRows,
        int FilterHiddenRowCount,
        IReadOnlyList<uint> HiddenColumns,
        int HiddenColumnCount,
        IReadOnlyList<OutlineLevelSummary> RowOutlineLevels,
        int RowOutlineLevelCount,
        IReadOnlyList<OutlineLevelSummary> ColumnOutlineLevels,
        int ColumnOutlineLevelCount,
        IReadOnlyList<uint> GroupHiddenRows,
        int GroupHiddenRowCount,
        IReadOnlyList<uint> GroupHiddenColumns,
        int GroupHiddenColumnCount,
        IReadOnlyList<StyleOnlyCellSummary> StyleOnlyCells,
        int StyleOnlyCellCount);

    private sealed record CellSummary(
        uint Row,
        uint Column,
        ScalarValueSummary Value,
        string FormulaText,
        bool IgnoreFormulaError,
        CellStyleSummary? Style);

    private sealed record FormulaCellSummary(
        string SheetName,
        uint Row,
        uint Column,
        string FormulaText,
        ScalarValueSummary CachedValue);

    private sealed record ScalarValueSummary(string Kind, string Value);

    private sealed record CustomViewSummary(
        string Name,
        bool IncludePrintSettings,
        bool IncludeHiddenRowsColumnsAndFilterSettings,
        IReadOnlyList<CustomViewSheetSummary> Sheets);

    private sealed record CustomViewSheetSummary(
        string SheetName,
        WorksheetViewMode ViewMode,
        uint FrozenRows,
        uint FrozenCols,
        uint? SplitRow,
        uint? SplitColumn,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        int ZoomPercent,
        bool ShowFormulas);

    private sealed record CommentSummary(uint Row, uint Column, string Text);

    private sealed record HyperlinkSummary(
        uint Row,
        uint Column,
        string Target,
        HyperlinkTargetKind LinkType,
        string ScreenTip,
        string Bookmark);

    private sealed record OutlineLevelSummary(uint Index, int Level);

    private sealed record StyleOnlyCellSummary(uint Row, uint Column, CellStyleSummary? Style);

    private sealed record DimensionSummary(uint Index, double Value);

    private sealed record PhoneticSummary(string FontId, string Type, string Alignment);

    private sealed record WorksheetCustomPropertySummary(string Name, int Id);

    private sealed record RepeatRangeSummary(uint Start, uint End);

    private sealed record BackgroundImageSummary(string ContentType, string FileName, int ImageByteCount);

    private sealed record HeaderFooterSummary(string Left, string Center, string Right)
    {
        public static HeaderFooterSummary Empty { get; } = new("", "", "");
    }

    private sealed record HeaderFooterPictureSetSummary(
        HeaderFooterPictureSummary? Left,
        HeaderFooterPictureSummary? Center,
        HeaderFooterPictureSummary? Right);

    private sealed record HeaderFooterPictureSummary(
        string ContentType,
        string FileName,
        int ByteLength,
        double Width,
        double Height);

    private sealed record ChartSummary(
        ChartType Type,
        string Title,
        string XAxisTitle,
        string YAxisTitle,
        ChartVisualSummary Visual,
        ChartAxisSummary XAxis,
        ChartAxisSummary YAxis,
        bool ShowLegend,
        bool IsPivotChart,
        int? PivotSourceFormatId,
        bool Uses1904DateSystem,
        string Language,
        int? ChartStyleId,
        bool RoundedCorners,
        ChartBlankDisplayMode BlankDisplayMode,
        bool ShowDataLabelsOverMaximum,
        bool AutoTitleDeleted,
        bool ShowDataInHiddenRowsAndColumns,
        ChartProtectionSummary? Protection,
        ChartPrintSettingsSummary? PrintSettings,
        ChartColorMapSummary? ColorMapOverride,
        ChartExternalDataSummary? ExternalData,
        ChartManualLayoutSummary? PlotAreaLayout,
        ChartManualLayoutSummary? LegendLayout,
        ChartLegendPosition LegendPosition,
        bool LegendOverlay,
        bool ShowDataLabels,
        bool ShowDataLabelValue,
        bool ShowDataLabelLegendKey,
        bool ShowDataLabelBubbleSize,
        bool ShowDataLabelCategoryName,
        bool ShowDataLabelSeriesName,
        bool ShowDataLabelPercentage,
        ChartDataLabelPosition DataLabelPosition,
        ChartDataLabelSeparator DataLabelSeparator,
        ChartDataLabelNumberFormat DataLabelNumberFormat,
        bool ShowDataLabelCallouts,
        string DataLabelFillColor,
        WorkbookThemeColorReference? DataLabelFillThemeColor,
        string DataLabelBorderColor,
        WorkbookThemeColorReference? DataLabelBorderThemeColor,
        string DataLabelTextColor,
        WorkbookThemeColorReference? DataLabelTextThemeColor,
        double DataLabelBorderThickness,
        double DataLabelFontSize,
        double DataLabelAngle,
        int? BarGapWidth,
        int? BarOverlap,
        bool? VaryColorsByPoint,
        int BubbleScale,
        bool ShowNegativeBubbles,
        ChartBubbleSizeRepresents BubbleSizeRepresents,
        ChartTrendlineSummary Trendline,
        ChartErrorBarSummary ErrorBars,
        ChartGuideLineSummary DropLines,
        StockChartSubtype StockSubtype,
        ChartGuideLineSummary HighLowLines,
        ChartGuideLineSummary SeriesLines,
        ChartUpDownBarsSummary UpDownBars,
        ChartDataTableSummary? DataTable,
        Chart3DViewSummary? ThreeDView,
        ChartSurfaceFormatSummary? FloorFormat,
        ChartSurfaceFormatSummary? SideWallFormat,
        ChartSurfaceFormatSummary? BackWallFormat,
        ChartRangeSummary DataRange);

    private sealed record ChartVisualSummary(
        string ChartTitleTextColor,
        WorkbookThemeColorReference? ChartTitleTextThemeColor,
        double ChartTitleFontSize,
        string AxisTitleTextColor,
        WorkbookThemeColorReference? AxisTitleTextThemeColor,
        double AxisTitleFontSize,
        string ChartAreaFillColor,
        WorkbookThemeColorReference? ChartAreaFillThemeColor,
        string PlotAreaFillColor,
        WorkbookThemeColorReference? PlotAreaFillThemeColor,
        string PlotAreaBorderColor,
        WorkbookThemeColorReference? PlotAreaBorderThemeColor,
        double PlotAreaBorderThickness,
        string LegendTextColor,
        WorkbookThemeColorReference? LegendTextThemeColor,
        string LegendFillColor,
        WorkbookThemeColorReference? LegendFillThemeColor,
        string LegendBorderColor,
        WorkbookThemeColorReference? LegendBorderThemeColor,
        double LegendBorderThickness,
        double LegendFontSize);

    private sealed record ChartAxisSummary(
        double? Minimum,
        double? Maximum,
        double? MajorUnit,
        double? MinorUnit,
        bool LogScale,
        ChartDataLabelNumberFormat NumberFormat,
        bool ShowMajorGridlines,
        bool ShowMinorGridlines,
        bool IsDateAxis,
        string MajorGridlineColor,
        string MinorGridlineColor,
        double GridlineThickness,
        ChartAxisTickStyle MajorTickStyle,
        ChartAxisTickStyle MinorTickStyle,
        bool ShowLabels,
        string LabelTextColor,
        WorkbookThemeColorReference? LabelTextThemeColor,
        double LabelFontSize,
        double LabelAngle,
        int LabelSkip,
        int TickMarkSkip,
        int LabelOffset,
        string LineColor,
        double LineThickness);

    private sealed record ChartTrendlineSummary(
        bool Show,
        ChartTrendlineType Type,
        int Period,
        int Order,
        bool ShowEquation,
        bool ShowRSquared,
        string Color,
        WorkbookThemeColorReference? ThemeColor,
        double Thickness,
        ChartLineDashStyle DashStyle);

    private sealed record ChartErrorBarSummary(
        bool Show,
        ChartErrorBarKind Kind,
        ChartErrorBarDirection Direction,
        double Value,
        bool EndCaps,
        string Color,
        WorkbookThemeColorReference? ThemeColor,
        double Thickness,
        ChartLineDashStyle DashStyle);

    private sealed record ChartGuideLineSummary(
        bool Show,
        string Color,
        WorkbookThemeColorReference? ThemeColor,
        double Thickness,
        ChartLineDashStyle DashStyle);

    private sealed record ChartUpDownBarsSummary(
        bool Show,
        int? GapWidth,
        ChartBarShapeSummary UpBars,
        ChartBarShapeSummary DownBars);

    private sealed record ChartBarShapeSummary(
        string FillColor,
        WorkbookThemeColorReference? FillThemeColor,
        string BorderColor,
        WorkbookThemeColorReference? BorderThemeColor,
        double? BorderThickness);

    private sealed record ChartColorMapSummary(
        bool UseMasterColorMapping,
        IReadOnlyList<ChartColorMapEntrySummary> OverrideMappings);

    private sealed record ChartColorMapEntrySummary(string Key, string Value);

    private sealed record ChartExternalDataSummary(
        string RelationshipId,
        string RelationshipType,
        string Target,
        string TargetMode,
        bool? AutoUpdate);

    private sealed record ChartManualLayoutSummary(
        string LayoutTarget,
        string XMode,
        string YMode,
        string WidthMode,
        string HeightMode,
        double? X,
        double? Y,
        double? Width,
        double? Height);

    private sealed record ChartDataTableSummary(
        bool? ShowHorizontalBorder,
        bool? ShowVerticalBorder,
        bool? ShowOutline,
        bool? ShowLegendKeys);

    private sealed record Chart3DViewSummary(
        int? RotationX,
        int? HeightPercent,
        int? RotationY,
        int? DepthPercent,
        bool? RightAngleAxes,
        int? Perspective);

    private sealed record ChartSurfaceFormatSummary(
        string FillColor,
        WorkbookThemeColorReference? FillThemeColor,
        string BorderColor,
        WorkbookThemeColorReference? BorderThemeColor,
        double? BorderThickness);

    private sealed record ChartProtectionSummary(
        bool? ChartObject,
        bool? Data,
        bool? Formatting,
        bool? Selection,
        bool? UserInterface);

    private sealed record ChartPrintSettingsSummary(
        ChartPageMarginsSummary? PageMargins,
        ChartPageSetupSummary? PageSetup);

    private sealed record ChartPageMarginsSummary(
        double? Left,
        double? Right,
        double? Top,
        double? Bottom,
        double? Header,
        double? Footer);

    private sealed record ChartPageSetupSummary(
        string PaperSize,
        string Orientation,
        int? Copies,
        bool? BlackAndWhite,
        bool? Draft);

    private sealed record ChartRangeSummary(
        uint StartRow,
        uint StartColumn,
        uint EndRow,
        uint EndColumn);

    private sealed record StructuredTableSummary(
        string Name,
        string DisplayName,
        string StyleName,
        bool HasAutoFilter,
        bool TotalsRowShown,
        bool ShowFirstColumn,
        bool ShowLastColumn,
        bool ShowRowStripes,
        bool ShowColumnStripes,
        string NativeSortStateXml,
        ChartRangeSummary Range,
        IReadOnlyList<StructuredTableColumnSummary> Columns,
        IReadOnlyList<StructuredTableFilterColumnSummary> FilterColumns);

    private sealed record StructuredTableColumnSummary(
        int Id,
        string Name,
        string TotalsRowLabel,
        string TotalsRowFunction,
        string CalculatedColumnFormula,
        string TotalsRowFormula);

    private sealed record StructuredTableFilterColumnSummary(
        int ColumnId,
        IReadOnlyList<string> Values,
        bool IncludeBlank,
        IReadOnlyList<StructuredTableCustomFilterSummary> CustomFilters,
        bool CustomFiltersAnd,
        string CustomFiltersAndRaw,
        IReadOnlyList<NativeAttributeSummary> NativeCustomFiltersAttributes,
        IReadOnlyList<string> NativeFilterXmls,
        IReadOnlyList<NativeAttributeSummary> NativeAttributes);

    private sealed record StructuredTableCustomFilterSummary(
        string Operator,
        string Value,
        IReadOnlyList<NativeAttributeSummary> NativeAttributes);

    private sealed record NativeAttributeSummary(string Name, string Value);

    private sealed record PivotTableSummary(
        string Name,
        int CacheId,
        ChartRangeSummary SourceRange,
        ChartRangeSummary TargetRange,
        bool DataOnRows,
        int FirstHeaderRow,
        int FirstDataRow,
        int FirstDataColumn,
        bool ShowSubtotals,
        PivotSubtotalPlacement SubtotalPlacement,
        bool ShowRowGrandTotals,
        bool ShowColumnGrandTotals,
        bool RepeatItemLabels,
        bool BlankLineAfterItems,
        PivotReportLayout ReportLayout,
        string StyleName,
        bool ShowRowHeaders,
        bool ShowColumnHeaders,
        bool ShowRowStripes,
        bool ShowColumnStripes,
        bool ShowFieldHeaders,
        bool ShowContextualTooltips,
        bool ShowPropertiesInTooltips,
        bool ShowClassicLayout,
        bool MergeAndCenterLabels,
        bool ShowItemsWithNoDataOnRows,
        bool ShowItemsWithNoDataOnColumns,
        bool PageOverThenDown,
        int PageWrap,
        string EmptyValueText,
        bool ApplyNumberFormats,
        bool ApplyBorderFormats,
        bool ApplyFontFormats,
        bool ApplyPatternFormats,
        bool AutofitColumnsOnUpdate,
        bool PreserveFormattingOnUpdate,
        bool ShowExpandCollapseButtons,
        bool EnableDrill,
        bool AsteriskTotals,
        bool MultipleFieldFilters,
        bool EnableFieldDialog,
        bool EnableFieldProperties,
        bool EnableDataValueEditing,
        bool PrintTitles,
        bool PrintExpandCollapseButtons,
        string AltTextTitle,
        string AltTextDescription,
        string DataCaption,
        string GrandTotalCaption,
        string MissingCaption,
        string ErrorCaption,
        IReadOnlyList<PivotFieldSummary> RowFields,
        IReadOnlyList<PivotFieldSummary> ColumnFields,
        IReadOnlyList<PivotFieldSummary> PageFields,
        IReadOnlyList<PivotDataFieldSummary> DataFields);

    private sealed record PivotCacheSummary(
        int CacheId,
        PivotCacheSourceType SourceType,
        string SourceSheetName,
        string SourceReference,
        string SourceTableName,
        int? ConnectionId,
        bool IsOlap,
        bool RefreshOnLoad,
        bool SaveData,
        bool EnableRefresh,
        bool PreserveSourceSortFilter,
        int? MissingItemsLimit,
        int? RecordCount,
        int? CreatedVersion,
        int? MinRefreshableVersion,
        int? RefreshedVersion,
        string RefreshedBy,
        string RefreshedDateIso,
        IReadOnlyList<PivotCacheFieldSummary> Fields);

    private sealed record PivotCacheFieldSummary(
        string Name,
        int? NumberFormatId,
        int? SharedItemCount,
        bool ContainsBlank,
        bool ContainsString,
        bool ContainsNumber,
        bool ContainsDate,
        bool ContainsMixedTypes,
        bool ContainsSemiMixedTypes,
        bool ContainsNonDate,
        bool ContainsInteger,
        bool ContainsLongText,
        double? MinValue,
        double? MaxValue,
        string MinDate,
        string MaxDate,
        IReadOnlyList<string> SharedItems);

    private sealed record PivotFieldSummary(
        int SourceFieldIndex,
        string SelectedItem,
        IReadOnlyList<string> SelectedItems,
        PivotFieldGrouping Grouping,
        double? GroupStart,
        double? GroupEnd,
        double? GroupInterval);

    private sealed record PivotDataFieldSummary(
        int SourceFieldIndex,
        string Name,
        string SummaryFunction,
        int? NumberFormatId,
        string CalculatedFieldName,
        PivotShowValuesAs ShowValuesAs,
        int? BaseFieldIndex,
        string BaseItem,
        string NumberFormatCode);

    private sealed record PivotTableStyleSummary(
        string Name,
        bool AppliesToPivotTables,
        bool AppliesToTables,
        IReadOnlyList<PivotTableStyleElementSummary> Elements);

    private sealed record PivotTableStyleElementSummary(
        string Type,
        int? DifferentialFormatId,
        int? Size);

    private sealed record NumberFormatCatalogSummary(int Id, string FormatCode);

    private sealed record SparklineSummary(
        SparklineKind Kind,
        ChartRangeSummary DataRange,
        uint LocationRow,
        uint LocationColumn);

    private sealed record TextBoxSummary(
        string Name,
        string Text,
        string Title,
        string AltText,
        uint AnchorRow,
        uint AnchorColumn,
        double Width,
        double Height,
        double RotationDegrees,
        bool IsVisible,
        CellColor? FillColor,
        CellColor? OutlineColor,
        WorkbookThemeColorReference? FillThemeColor,
        WorkbookThemeColorReference? OutlineThemeColor);

    private sealed record DrawingShapeSummary(
        string Name,
        DrawingShapeKind Kind,
        string Title,
        string AltText,
        uint AnchorRow,
        uint AnchorColumn,
        double Width,
        double Height,
        double RotationDegrees,
        bool IsVisible,
        CellColor? FillColor,
        CellColor? OutlineColor,
        CellColor? GradientFillEndColor,
        WorkbookThemeColorReference? FillThemeColor,
        WorkbookThemeColorReference? OutlineThemeColor,
        bool HasShadowEffect);

    private sealed record PictureSummary(
        string Name,
        PictureKind Kind,
        string Title,
        string AltText,
        uint AnchorRow,
        uint AnchorColumn,
        double Width,
        double Height,
        double RotationDegrees,
        bool IsVisible,
        string ContentType,
        int ImageByteCount,
        double CropLeft,
        double CropTop,
        double CropRight,
        double CropBottom,
        bool IsLinkedToSourceRange,
        ChartRangeSummary? LinkedSourceRange,
        string LinkedSourceSheetName,
        uint SourceRowCount,
        uint SourceColumnCount,
        IReadOnlyList<PictureCellSummary> Cells);

    private sealed record PictureCellSummary(uint RowOffset, uint ColumnOffset, string Text);

    private sealed record DataValidationSummary(
        DvType Type,
        DvOperator Operator,
        string Formula1,
        string Formula2,
        bool AllowBlank,
        bool ShowDropdown,
        DvAlertStyle AlertStyle,
        bool ShowInputMessage,
        bool ShowErrorMessage,
        string ErrorTitle,
        string ErrorMessage,
        string PromptTitle,
        string PromptMessage,
        ChartRangeSummary AppliesTo,
        IReadOnlyList<ChartRangeSummary> AdditionalRanges);

    private sealed record ConditionalFormatSummary(
        CfRuleType RuleType,
        int Priority,
        CfOperator Operator,
        string Value1,
        string Value2,
        CellStyleSummary? FormatIfTrue,
        RgbColor MinColor,
        RgbColor MidColor,
        RgbColor MaxColor,
        bool UseThreeColorScale,
        CfThresholdType MinThresholdType,
        string MinThresholdValue,
        CfThresholdType MidThresholdType,
        string MidThresholdValue,
        CfThresholdType MaxThresholdType,
        string MaxThresholdValue,
        RgbColor DataBarColor,
        CfThresholdType DataBarMinThresholdType,
        string DataBarMinThresholdValue,
        CfThresholdType DataBarMaxThresholdType,
        string DataBarMaxThresholdValue,
        bool DataBarShowValue,
        int? DataBarMinLength,
        int? DataBarMaxLength,
        bool DataBarGradient,
        bool DataBarBorder,
        string DataBarAxisPosition,
        RgbColor? DataBarAxisColor,
        RgbColor? DataBarNegativeFillColor,
        RgbColor? DataBarNegativeBorderColor,
        bool AboveAverage,
        string FormulaText,
        string IconSetStyle,
        bool IconSetShowValue,
        bool IconSetReverse,
        IReadOnlyList<ConditionalFormatThresholdSummary> IconSetThresholds,
        int TopBottomRank,
        bool TopBottomPercent,
        string TextRuleText,
        string DateOccurringPeriod,
        bool StopIfTrue,
        ChartRangeSummary AppliesTo);

    private sealed record ConditionalFormatThresholdSummary(CfThresholdType Type, string Value);

    private sealed record CellStyleSummary(
        string FontName,
        double FontSize,
        bool Bold,
        bool Italic,
        bool Underline,
        bool Strikethrough,
        CellColor FontColor,
        CellColor? FillColor,
        CellFillPatternStyle FillPatternStyle,
        CellColor? FillPatternColor,
        string NumberFormat);

    private sealed record PackagePartSummary(
        IReadOnlyList<string> CriticalParts,
        IReadOnlyList<string> CriticalRelationshipTargets,
        IReadOnlyList<string> CriticalRelationshipDetails,
        IReadOnlyList<string> CriticalContentTypeOverrides);

    private sealed record WorksheetSortFilterPackageXmlSummary(
        WorksheetElementXmlSummary AutoFilter,
        WorksheetElementXmlSummary SortState);

    private sealed record WorksheetIgnoredErrorsPackageXmlSummary(
        IReadOnlyList<NativeAttributeSummary> ContainerAttributes,
        IReadOnlyList<WorksheetIgnoredErrorXmlSummary> Errors);

    private sealed record WorksheetIgnoredErrorXmlSummary(
        string Sqref,
        bool HasModeledIgnoredError,
        IReadOnlyList<NativeAttributeSummary> RetainedNativeAttributes);

    private sealed record WorksheetElementXmlSummary(
        string Name,
        IReadOnlyList<NativeAttributeSummary> Attributes,
        string Text,
        IReadOnlyList<WorksheetElementXmlSummary> Children);

    private sealed record DataValidationPackageXmlSummary(
        string CountAttribute,
        IReadOnlyList<DataValidationRuleXmlSummary> Rules);

    private sealed record DataValidationRuleXmlSummary(
        string Type,
        string Operator,
        string Sqref,
        string Formula1,
        string Formula2);

}
