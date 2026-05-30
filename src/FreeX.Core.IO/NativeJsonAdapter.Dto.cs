using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private class WorkbookDto
    {
        public string? FileFormat { get; set; }
        public int? SchemaVersion { get; set; }
        public int? MinimumReaderVersion { get; set; }
        public string Name { get; set; } = "";
        public WorkbookThemeDto? Theme { get; set; }
        public bool Uses1904DateSystem { get; set; }
        public bool? ShowSheetTabs { get; set; }
        public int? SheetTabRatio { get; set; }
        public int? FirstVisibleSheetIndex { get; set; }
        public int? ActiveSheetIndex { get; set; }
        public WorkbookFileVersionDto? FileVersion { get; set; }
        public WorkbookFileSharingDto? FileSharing { get; set; }
        public List<WorkbookFileRecoveryPropertiesDto> FileRecoveryProperties { get; set; } = [];
        public WorkbookPropertiesDto? Properties { get; set; }
        public WorkbookFunctionGroupsDto? FunctionGroups { get; set; }
        public WorkbookSmartTagMetadataDto? SmartTags { get; set; }
        public WorkbookAdditionalViewsDto? AdditionalViews { get; set; }
        public bool IsStructureProtected { get; set; }
        public string? StructureProtectionPassword { get; set; }
        public WorkbookProtectionMetadataDto? ProtectionMetadata { get; set; }
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
        public string? NativeColorSchemeXml { get; set; }
        public string? NativeFontSchemeXml { get; set; }
        public string? NativeFormatSchemeXml { get; set; }
        public string? NativeThemeSupplementXml { get; set; }
        public List<WorkbookThemeColorDto> Colors { get; set; } = [];
    }

    private class WorkbookThemeColorDto
    {
        public WorkbookThemeColorSlot Slot { get; set; }
        public string? Color { get; set; }
    }

    private class WorkbookFileSharingDto
    {
        public bool? ReadOnlyRecommended { get; set; }
        public string? UserName { get; set; }
        public string? ReservationPassword { get; set; }
    }

    private class WorkbookFileVersionDto
    {
        public string? AppName { get; set; }
        public string? LastEdited { get; set; }
        public string? LowestEdited { get; set; }
        public string? RupBuild { get; set; }
        public string? CodeName { get; set; }
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    }

    private class WorkbookPropertiesDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<string> NativeChildXmls { get; set; } = [];
    }

    private class WorkbookProtectionMetadataDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<string> NativeChildXmls { get; set; } = [];
    }

    private class WorkbookFileRecoveryPropertiesDto
    {
        public bool? AutoRecover { get; set; }
        public bool? CrashSave { get; set; }
        public bool? DataExtractLoad { get; set; }
        public bool? RepairLoad { get; set; }
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    }

    private class WorkbookFunctionGroupsDto
    {
        public string? BuiltInGroupCount { get; set; }
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<WorkbookFunctionGroupDto> Groups { get; set; } = [];
    }

    private class WorkbookFunctionGroupDto
    {
        public string? Name { get; set; }
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    }

    private class WorkbookSmartTagMetadataDto
    {
        public bool? Embed { get; set; }
        public string? Show { get; set; }
        public Dictionary<string, string> PropertiesNativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> TypesNativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<WorkbookSmartTagTypeDto> Types { get; set; } = [];
    }

    private class WorkbookSmartTagTypeDto
    {
        public string? NamespaceUri { get; set; }
        public string? Name { get; set; }
        public string? Url { get; set; }
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    }

    private class WorkbookAdditionalViewsDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<WorkbookAdditionalViewDto> Views { get; set; } = [];
    }

    private class WorkbookAdditionalViewDto
    {
        public string? NativeXml { get; set; }
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
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
        public string? Comment { get; set; }
        public bool Hidden { get; set; }
        public bool Locked { get; set; }
        public string? User { get; set; }
        public List<ScenarioCellDto> ChangingCells { get; set; } = [];
    }

    private class ScenarioCellDto
    {
        public string SheetName { get; set; } = "";
        public string Address { get; set; } = "";
        public string? Value { get; set; }
        public string? ValueType { get; set; }
    }

    private class WorksheetAutoFilterDto
    {
        public string? Reference { get; set; }
        public string? NativeXml { get; set; }
        public Dictionary<string, string>? NativeAttributes { get; set; }
        public List<string>? NativeChildXmls { get; set; }
        public List<WorksheetAutoFilterColumnDto> FilterColumns { get; set; } = [];
    }

    private class WorksheetAutoFilterColumnDto
    {
        public int ColumnId { get; set; }
        public List<string> Values { get; set; } = [];
        public bool IncludeBlank { get; set; }
        public List<WorksheetAutoFilterDateGroupItemDto> DateGroups { get; set; } = [];
        public Dictionary<string, string>? NativeFiltersAttributes { get; set; }
        public List<WorksheetAutoFilterCustomFilterDto> CustomFilters { get; set; } = [];
        public bool CustomFiltersAnd { get; set; }
        public string? CustomFiltersAndRaw { get; set; }
        public Dictionary<string, string>? NativeCustomFiltersAttributes { get; set; }
        public WorksheetAutoFilterTop10Dto? Top10 { get; set; }
        public WorksheetAutoFilterDynamicFilterDto? DynamicFilter { get; set; }
        public WorksheetAutoFilterColorFilterDto? ColorFilter { get; set; }
        public WorksheetAutoFilterIconFilterDto? IconFilter { get; set; }
        public List<string> NativeFilterXmls { get; set; } = [];
        public Dictionary<string, string>? NativeAttributes { get; set; }
    }

    private class WorksheetAutoFilterCustomFilterDto
    {
        public string? Operator { get; set; }
        public string? Value { get; set; }
        public Dictionary<string, string>? NativeAttributes { get; set; }
    }

    private class WorksheetAutoFilterDateGroupItemDto
    {
        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Day { get; set; }
        public int? Hour { get; set; }
        public int? Minute { get; set; }
        public int? Second { get; set; }
        public string? DateTimeGrouping { get; set; }
        public string? YearRaw { get; set; }
        public string? MonthRaw { get; set; }
        public string? DayRaw { get; set; }
        public string? HourRaw { get; set; }
        public string? MinuteRaw { get; set; }
        public string? SecondRaw { get; set; }
        public Dictionary<string, string>? NativeAttributes { get; set; }
    }

    private class WorksheetAutoFilterTop10Dto
    {
        public bool Top { get; set; } = true;
        public bool Percent { get; set; }
        public double? Value { get; set; }
        public double? FilterValue { get; set; }
        public string? TopRaw { get; set; }
        public string? PercentRaw { get; set; }
        public string? ValueRaw { get; set; }
        public string? FilterValueRaw { get; set; }
        public Dictionary<string, string>? NativeAttributes { get; set; }
    }

    private class WorksheetAutoFilterDynamicFilterDto
    {
        public string? Type { get; set; }
        public double? Value { get; set; }
        public double? MaxValue { get; set; }
        public string? ValueRaw { get; set; }
        public string? MaxValueRaw { get; set; }
        public Dictionary<string, string>? NativeAttributes { get; set; }
    }

    private class WorksheetAutoFilterColorFilterDto
    {
        public int? DifferentialFormatId { get; set; }
        public bool CellColor { get; set; } = true;
        public string? DifferentialFormatIdRaw { get; set; }
        public string? CellColorRaw { get; set; }
        public Dictionary<string, string>? NativeAttributes { get; set; }
    }

    private class WorksheetAutoFilterIconFilterDto
    {
        public string? IconSet { get; set; }
        public int? IconId { get; set; }
        public string? IconIdRaw { get; set; }
        public Dictionary<string, string>? NativeAttributes { get; set; }
    }

    private class WorksheetSmartTagsDto
    {
        public string? NativeXml { get; set; }
        public List<WorksheetCellSmartTagsDto> Cells { get; set; } = [];
    }

    private class WorksheetDataConsolidationDto
    {
        public string? Function { get; set; }
        public bool? LeftLabels { get; set; }
        public bool? TopLabels { get; set; }
        public bool? Link { get; set; }
        public string? NativeXml { get; set; }
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<WorksheetDataConsolidationReferenceDto> References { get; set; } = [];
    }

    private class WorksheetDataConsolidationReferenceDto
    {
        public string? Reference { get; set; }
        public string? Sheet { get; set; }
        public string? Name { get; set; }
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    }

    private class WorksheetSortStateDto
    {
        public string? Reference { get; set; }
        public bool? ColumnSort { get; set; }
        public bool? CaseSensitive { get; set; }
        public string? SortMethod { get; set; }
        public string? NativeXml { get; set; }
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<WorksheetSortConditionDto> Conditions { get; set; } = [];
    }

    private class WorksheetSortConditionDto
    {
        public string? Reference { get; set; }
        public bool? Descending { get; set; }
        public string? SortBy { get; set; }
        public string? CustomList { get; set; }
        public string? DxfId { get; set; }
        public string? IconSet { get; set; }
        public string? IconId { get; set; }
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    }

    private class WorksheetAdditionalViewsDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<WorksheetAdditionalViewDto> Views { get; set; } = [];
    }

    private class WorksheetAdditionalViewDto
    {
        public string? WorkbookViewId { get; set; }
        public string? NativeXml { get; set; }
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    }

    private class WorksheetCellSmartTagsDto
    {
        public string? Reference { get; set; }
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<WorksheetCellSmartTagDto> Tags { get; set; } = [];
    }

    private class WorksheetCellSmartTagDto
    {
        public string? Type { get; set; }
        public bool? Deleted { get; set; }
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<WorksheetCellSmartTagPropertyDto> Properties { get; set; } = [];
    }

    private class WorksheetCellSmartTagPropertyDto
    {
        public string? Key { get; set; }
        public string? Value { get; set; }
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
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
        public List<SheetProtectionPermission> ProtectionPermissions { get; set; } = [];
        public WorksheetProtectionMetadataDto? ProtectionMetadata { get; set; }
        public List<WorksheetCustomPropertyDto> CustomProperties { get; set; } = [];
        public List<UIntDoubleDto> RowHeights { get; set; } = [];
        public List<UIntDoubleDto> ColumnWidths { get; set; } = [];
        public List<uint> HiddenRows { get; set; } = [];
        public List<uint> FilterHiddenRows { get; set; } = [];
        public List<uint> HiddenCols { get; set; } = [];
        public List<UIntIntDto> RowOutlineLevels { get; set; } = [];
        public List<UIntIntDto> ColOutlineLevels { get; set; } = [];
        public bool? OutlineSummaryBelow { get; set; }
        public bool? OutlineSummaryRight { get; set; }
        public bool? ShowOutlineSymbols { get; set; }
        public bool? ApplyOutlineStyles { get; set; }
        public WorksheetSheetFormatMetadataDto? SheetFormatMetadata { get; set; }
        public WorksheetDimensionMetadataDto? DimensionMetadata { get; set; }
        public WorksheetSheetPropertiesMetadataDto? SheetPropertiesMetadata { get; set; }
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
        public WorksheetAutoFilterDto? AutoFilter { get; set; }
        public WorksheetSmartTagsDto? SmartTags { get; set; }
        public WorksheetDataConsolidationDto? DataConsolidation { get; set; }
        public WorksheetSortStateDto? SortState { get; set; }
        public WorksheetSingleXmlCellsDto? SingleXmlCells { get; set; }
        public WorksheetCellWatchesMetadataDto? CellWatchesMetadata { get; set; }
        public WorksheetIgnoredErrorsMetadataDto? IgnoredErrorsMetadata { get; set; }
        public WorksheetAdditionalViewsDto? AdditionalViews { get; set; }
        public WorksheetPrimaryViewMetadataDto? PrimaryViewMetadata { get; set; }
        public string? PrintArea { get; set; }
        public WorksheetPageOrientation? PageOrientation { get; set; }
        public WorksheetPaperSize? PaperSize { get; set; }
        public PageMarginsDto? PageMargins { get; set; }
        public double? HeaderMargin { get; set; }
        public double? FooterMargin { get; set; }
        public WorksheetPageMarginsMetadataDto? PageMarginsMetadata { get; set; }
        public bool PrintGridlines { get; set; }
        public bool PrintHeadings { get; set; }
        public WorksheetPrintOptionsMetadataDto? PrintOptionsMetadata { get; set; }
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
        public WorksheetHeaderFooterMetadataDto? HeaderFooterMetadata { get; set; }
        public bool CenterHorizontallyOnPage { get; set; }
        public bool CenterVerticallyOnPage { get; set; }
        public WorksheetPageOrder? PageOrder { get; set; }
        public int? FirstPageNumber { get; set; }
        public bool? UsePrinterDefaults { get; set; }
        public int? PrintCopies { get; set; }
        public bool PrintBlackAndWhite { get; set; }
        public bool PrintDraftQuality { get; set; }
        public int? PrintQualityDpi { get; set; }
        public int? PrintQualityVerticalDpi { get; set; }
        public WorksheetPrintErrorValue? PrintErrorValue { get; set; }
        public WorksheetPrintComments? PrintComments { get; set; }
        public WorksheetPageSetupMetadataDto? PageSetupMetadata { get; set; }
        public ScaleToFitDto? ScaleToFit { get; set; }
        public bool? FitToPage { get; set; }
        public bool? AutoPageBreaks { get; set; }
        public List<uint> RowPageBreaks { get; set; } = [];
        public WorksheetPageBreaksMetadataDto? RowPageBreaksMetadata { get; set; }
        public List<uint> ColumnPageBreaks { get; set; } = [];
        public WorksheetPageBreaksMetadataDto? ColumnPageBreaksMetadata { get; set; }
        public List<string> MergedRegions { get; set; } = [];
        public List<CommentDto> Comments { get; set; } = [];
        public List<ThreadedCommentDto> ThreadedComments { get; set; } = [];
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

    private class WorksheetProtectionMetadataDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<string> NativeChildXmls { get; set; } = [];
    }

    private class WorksheetPageSetupMetadataDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<string> NativeChildXmls { get; set; } = [];
    }

    private class WorksheetPrintOptionsMetadataDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<string> NativeChildXmls { get; set; } = [];
    }

    private class WorksheetSheetFormatMetadataDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<string> NativeChildXmls { get; set; } = [];
    }

    private class WorksheetDimensionMetadataDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    }

    private class WorksheetSheetPropertiesMetadataDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<string> NativeChildXmls { get; set; } = [];
    }

    private class WorksheetPrimaryViewMetadataDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<string> NativeChildXmls { get; set; } = [];
    }

    private class WorksheetPageBreaksMetadataDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<uint, Dictionary<string, string>> BreakNativeAttributes { get; set; } = [];
    }

    private class WorksheetSingleXmlCellsDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<WorksheetSingleXmlCellDto> Cells { get; set; } = [];
    }

    private class WorksheetSingleXmlCellDto
    {
        public int? Id { get; set; }
        public string? Reference { get; set; }
        public int? XmlCellPropertyId { get; set; }
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    }

    private class WorksheetCellWatchesMetadataDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, string>> WatchNativeAttributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private class WorksheetIgnoredErrorsMetadataDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, string>> ErrorNativeAttributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private class WorksheetPageMarginsMetadataDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<string> NativeChildXmls { get; set; } = [];
    }

    private class WorksheetHeaderFooterMetadataDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<string> NativeChildXmls { get; set; } = [];
    }

    private class WorksheetCustomPropertyDto
    {
        public string Name { get; set; } = "";
        public int Id { get; set; }
        public WorksheetCustomPropertyMetadataDto? Metadata { get; set; }
    }

    private class WorksheetCustomPropertyMetadataDto
    {
        public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
        public List<string> NativeChildXmls { get; set; } = [];
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
        public List<string>? AdditionalRanges { get; set; }
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
        public CfThresholdType MinThresholdType { get; set; } = CfThresholdType.Min;
        public string? MinThresholdValue { get; set; }
        public bool? MinThresholdGreaterThanOrEqual { get; set; }
        public CfThresholdType MidThresholdType { get; set; } = CfThresholdType.Percentile;
        public string? MidThresholdValue { get; set; }
        public bool? MidThresholdGreaterThanOrEqual { get; set; }
        public CfThresholdType MaxThresholdType { get; set; } = CfThresholdType.Max;
        public string? MaxThresholdValue { get; set; }
        public bool? MaxThresholdGreaterThanOrEqual { get; set; }
        public RgbColor DataBarColor { get; set; } = new(99, 142, 198);
        public CfThresholdType DataBarMinThresholdType { get; set; } = CfThresholdType.Min;
        public string? DataBarMinThresholdValue { get; set; }
        public CfThresholdType DataBarMaxThresholdType { get; set; } = CfThresholdType.Max;
        public string? DataBarMaxThresholdValue { get; set; }
        public bool DataBarShowValue { get; set; } = true;
        public int? DataBarMinLength { get; set; }
        public int? DataBarMaxLength { get; set; }
        public bool DataBarGradient { get; set; } = true;
        public bool DataBarBorder { get; set; }
        public string? DataBarAxisPosition { get; set; }
        public RgbColor? DataBarAxisColor { get; set; }
        public RgbColor? DataBarNegativeFillColor { get; set; }
        public RgbColor? DataBarNegativeBorderColor { get; set; }
        public bool AboveAverage { get; set; } = true;
        public string? FormulaText { get; set; }
        public string? IconSetStyle { get; set; }
        public bool IconSetShowValue { get; set; } = true;
        public bool IconSetReverse { get; set; }
        public List<CfThresholdModel> IconSetThresholds { get; set; } = [];
        public List<CfIconOverride> IconOverrides { get; set; } = [];
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
        public bool Hidden { get; set; }
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

    private class ThreadedCommentDto
    {
        public string? Address { get; set; }
        public string? Text { get; set; }
        public string? Author { get; set; }
        public bool IsResolved { get; set; }
        public List<CommentReplyDto> Replies { get; set; } = [];
    }

    private class CommentReplyDto
    {
        public string? Text { get; set; }
        public string? Author { get; set; }
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
