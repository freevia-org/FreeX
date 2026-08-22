using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        public bool? ShowInkAnnotations { get; set; }
        // R82-services-autosave-recovery-5-2: carries Workbook.HasVbaProjectPackage across a .fxl
        // round-trip (autosave/crash-recovery snapshots go through this adapter exclusively) so a
        // recovered macro-enabled workbook still reports itself as macro-enabled. The actual
        // xl/vbaProject.bin bytes live only in XlsxFileAdapter's SourcePackages side-table (never
        // part of the Workbook model), so they still cannot survive an .fxl round-trip -- this flag
        // is the one piece of macro state that IS part of the model and was being silently dropped.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool HasVbaProjectPackage { get; set; }
        public bool? ShowSheetTabs { get; set; }
        public int? SheetTabRatio { get; set; }
        public int? FirstVisibleSheetIndex { get; set; }
        public int? ActiveSheetIndex { get; set; }
        public WorkbookFileVersionDto? FileVersion { get; set; }
        public WorkbookCountrySettingsDto? CountrySettings { get; set; }
        public WorkbookLegacyMenuSettingsDto? LegacyMenuSettings { get; set; }
        public WorkbookLegacyWorkbookSettingsDto? LegacyWorkbookSettings { get; set; }
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
        // R90-io-workbook-calc-settings-5-1: precision-as-displayed (File > Options > Advanced >
        // "Set precision as displayed", calcPr/@fullPrecision on the XLSX side). Default true matches
        // Workbook.FullPrecision's default so an omitted/older .fxl still loads as "full precision".
        public bool FullPrecision { get; set; } = true;
        public List<string> DisabledFormulaErrorCodes { get; set; } = [];
        public List<NamedRangeDto> NamedRanges { get; set; } = [];
        public List<CustomViewDto> CustomViews { get; set; } = [];
        public List<WatchedCellDto> WatchedCells { get; set; } = [];
        public List<ScenarioDto> Scenarios { get; set; } = [];
        public List<PivotCacheDto> PivotCaches { get; set; } = [];
        public List<SlicerDto> Slicers { get; set; } = [];
        public List<TimelineDto> Timelines { get; set; } = [];
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<CellStyleDto>? CellStyles { get; set; }

        /// <summary>
        /// The workbook's customized default style (style 0) when it differs from the hard-coded
        /// <see cref="CellStyle.Default"/> (e.g. an XLSX whose workbook default font is "Aptos Narrow"
        /// with FontScheme=Minor). Persisted so style-0 cells keep their font across an fxl round-trip;
        /// absent/null means the workbook uses the built-in Calibri default.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CellStyleDto? DefaultStyle { get; set; }

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
        public bool HasObjectDefaults { get; set; }
        public WorkbookThemeObjectDefaultsDto? ObjectDefaults { get; set; }
        public List<WorkbookThemeAlternateColorSchemeDto> AlternateColorSchemes { get; set; } = [];
        public List<WorkbookThemeColorDto> Colors { get; set; } = [];
    }

    private class WorkbookThemeAlternateColorSchemeDto
    {
        public string? Name { get; set; }
        public string? NativeColorSchemeXml { get; set; }
        public List<WorkbookThemeColorDto> Colors { get; set; } = [];
    }

    private class WorkbookThemeObjectDefaultsDto
    {
        public WorkbookThemeShapeObjectDefaultDto? Shape { get; set; }
        public WorkbookThemeLineObjectDefaultDto? Line { get; set; }
        public WorkbookThemeTextObjectDefaultDto? Text { get; set; }
        public string? NativeObjectDefaultsXml { get; set; }
    }

    private class WorkbookThemeShapeObjectDefaultDto
    {
        public ThemeColorReferenceDto? FillThemeColor { get; set; }
        public string? FillColor { get; set; }
        public ThemeColorReferenceDto? OutlineThemeColor { get; set; }
        public string? OutlineColor { get; set; }
        public double? OutlineWidthPoints { get; set; }
    }

    private class WorkbookThemeLineObjectDefaultDto
    {
        public ThemeColorReferenceDto? StrokeThemeColor { get; set; }
        public string? StrokeColor { get; set; }
        public double? StrokeWidthPoints { get; set; }
    }

    private class WorkbookThemeTextObjectDefaultDto
    {
        public ThemeColorReferenceDto? TextThemeColor { get; set; }
        public string? TextColor { get; set; }
        public string? Typeface { get; set; }
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

    private class WorkbookCountrySettingsDto
    {
        public int? DefaultCountryId { get; set; }
        public int? CurrentCountryId { get; set; }
    }

    private class WorkbookLegacyMenuSettingsDto
    {
        public int? AddMenuCount { get; set; }
        public int? DeleteMenuCount { get; set; }
    }

    private class WorkbookLegacyWorkbookSettingsDto
    {
        public List<int> SheetTabIds { get; set; } = [];
        public bool? UseNaturalLanguageFormulas { get; set; }
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

        /// <summary>
        /// The raw refers-to formula text (without the leading '=') when this defined name is a
        /// named FORMULA (e.g. <c>MyRate = 0.08*Sheet1!$B$1</c>) rather than a plain cell/range
        /// reference. Null for plain named ranges; when set, <see cref="Range"/> is ignored on load.
        /// </summary>
        public string? Formula { get; set; }

        /// <summary>
        /// Name of the sheet this defined NAME itself is scoped to (Excel's <c>localSheetId</c>),
        /// distinct from <see cref="SheetName"/> (which for plain ranges identifies the sheet the
        /// range lives on). Null means workbook scope. Absent on files written before this field
        /// existed, which always meant workbook scope.
        /// </summary>
        public string? ScopeSheetName { get; set; }
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
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ActiveSheetIndex { get; set; }
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
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public uint? ActiveRow { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public uint? ActiveCol { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public uint? ViewTopRow { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public uint? ViewLeftCol { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<uint>? HiddenRows { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<uint>? HiddenCols { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<uint>? FilterHiddenRows { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public WorksheetAutoFilterDto? AutoFilter { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? PrintAreas { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public WorksheetPageOrientation? PageOrientation { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public WorksheetPaperSize? PaperSize { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? PaperSizeCode { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PageMarginsDto? PageMargins { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? HeaderMargin { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? FooterMargin { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? PrintGridlines { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? PrintHeadings { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ScaleToFitDto? ScaleToFit { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? FitToPage { get; set; }
    }

    private class SheetDto
    {
        public string Name { get; set; } = "";
        public SheetKind Kind { get; set; } = SheetKind.Worksheet;
        public bool IsHidden { get; set; }
        public bool IsVeryHidden { get; set; }
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
        // G32: sheet.ActiveValueFilterColumns/ValueFilterHiddenRows are the per-column value-filter
        // state and its FilterHiddenRows-ownership bookkeeping (see FreeX.Core.Commands.FilterCommand,
        // findings F8/G7). Without persisting them, reloading a workbook with more than one active
        // AutoFilter value-list column leaves FilterHiddenRows populated but the per-column state
        // empty, so the next filter recompute treats it as "no active filters" and unhides everything.
        public List<UIntStringListDto> ActiveValueFilterColumns { get; set; } = [];
        public List<uint> ValueFilterHiddenRows { get; set; } = [];
        // R14-meta-1: sheet.ColumnFilterOwnedRows is the row-ownership bookkeeping every column-owned
        // filter mechanism (condition/Top-Bottom/average AND the value-list side above) relies on to
        // decide whether a row another column still owns may be un-hidden (see
        // FreeX.Core.Commands.FilterCommand.IsHiddenByAnyColumnOwnedFilter/
        // IsHiddenByAnyOtherActiveMechanism). Without persisting it, a reload leaves it empty even
        // though FilterHiddenRows/ActiveValueFilterColumns/ValueFilterHiddenRows all survive, so the
        // next un-hide decision on ANY column wrongly treats every OTHER column's filter as inactive.
        public List<UIntUintListDto> ColumnFilterOwnedRows { get; set; } = [];
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
        public List<uint> CollapsedAnchorRows { get; set; } = [];
        public List<uint> CollapsedAnchorCols { get; set; } = [];
        public WorksheetViewMode ViewMode { get; set; } = WorksheetViewMode.Normal;
        public bool? ShowGridlines { get; set; }
        public bool? ShowHeadings { get; set; }
        public bool? ShowRulers { get; set; }
        public int? ZoomPercent { get; set; }
        public bool? ShowFormulas { get; set; }
        public bool? IsRightToLeft { get; set; }
        public bool? ShowZeros { get; set; }
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
        /// <summary>Legacy single-range field; superseded by <see cref="PrintAreas"/>. Kept for backward-compatible JSON round-trips.</summary>
        public string? PrintArea { get; set; }
        /// <summary>Multi-area print areas; takes precedence over <see cref="PrintArea"/> when present.</summary>
        public string[]? PrintAreas { get; set; }
        public WorksheetPageOrientation? PageOrientation { get; set; }
        public WorksheetPaperSize? PaperSize { get; set; }
        public int? PaperSizeCode { get; set; }
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
        public int? LegacyPrintSize { get; set; }
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
        public List<RichTextRunDto> RichTextRuns { get; set; } = [];
        public List<CellPhoneticGuideDto> CellPhoneticGuides { get; set; } = [];
        public List<string> AllowEditRanges { get; set; } = [];
        public List<AllowEditRangePasswordDto> AllowEditRangePasswords { get; set; } = [];
        public WorksheetBackgroundDto? BackgroundImage { get; set; }
        public List<PictureDto> Pictures { get; set; } = [];
        public List<TextBoxDto> TextBoxes { get; set; } = [];
        public List<DrawingShapeDto> DrawingShapes { get; set; } = [];
        public List<FormControlDto> FormControls { get; set; } = [];
        public List<DrawingObjectZOrderEntryDto> DrawingObjectZOrder { get; set; } = [];
        public List<SparklineDto> Sparklines { get; set; } = [];
        public List<ChartDto> Charts { get; set; } = [];
        public List<PivotTableDto> PivotTables { get; set; } = [];
        public List<DataValidationDto> DataValidations { get; set; } = [];
        public List<ConditionalFormatDto> ConditionalFormats { get; set; } = [];
        public CellDtoSequence Cells { get; set; } = CellDtoSequence.Empty;
        public StyleOnlyCellDtoSequence StyleOnlyCells { get; set; } = StyleOnlyCellDtoSequence.Empty;
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
        public bool IsX14 { get; set; }
        public Dictionary<string, string>? NativeAttributes { get; set; }
        public List<string>? NativeChildXmls { get; set; }
        public Dictionary<string, string>? NativeContainerAttributes { get; set; }
        public List<string>? NativeContainerChildXmls { get; set; }
    }

    private class ConditionalFormatDto
    {
        public string? AppliesTo { get; set; }
        /// <summary>Additional non-contiguous ranges beyond the first, preserved from the original sqref list.</summary>
        public List<string>? AdditionalRanges { get; set; }
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
        public RgbColor? DataBarBorderColor { get; set; }
        public string? DataBarAxisPosition { get; set; }
        public RgbColor? DataBarAxisColor { get; set; }
        public RgbColor? DataBarNegativeFillColor { get; set; }
        public RgbColor? DataBarNegativeBorderColor { get; set; }
        public bool AboveAverage { get; set; } = true;
        public bool EqualAverage { get; set; }
        public int? StdDevCount { get; set; }
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

    private class DrawingObjectZOrderEntryDto
    {
        public SelectionPaneObjectKind Kind { get; set; }
        public Guid Id { get; set; }
    }

    private class DrawingAnchorRangeDto
    {
        public DrawingAnchorPointDto From { get; set; } = new();
        public DrawingAnchorPointDto To { get; set; } = new();
    }

    private class DrawingAnchorPointDto
    {
        public uint Column { get; set; }
        public long ColumnOffsetEmu { get; set; }
        public uint Row { get; set; }
        public long RowOffsetEmu { get; set; }
    }

    private class SlicerDto
    {
        public string Name { get; set; } = "";
        public string? Caption { get; set; }
        public string CacheName { get; set; } = "";
        public string? SourcePivotTableName { get; set; }

        /// <summary>Mirrors <see cref="SlicerModel.ConnectedPivotTableNames"/> (R133-io-slicer-timeline-multipivot).</summary>
        public List<string>? ConnectedPivotTableNames { get; set; }
        public string? SourceFieldName { get; set; }
        public string? StyleName { get; set; }
        public List<string> SelectedItems { get; set; } = [];
        public DrawingAnchorRangeDto? DrawingAnchor { get; set; }
        public string? DrawingShapeName { get; set; }
        public int ColumnCount { get; set; } = 1;
        public bool ShowCaption { get; set; } = true;
        public string? SourceSheetName { get; set; }
        public int? SourceTableId { get; set; }
        public int? SourceTableColumnId { get; set; }

        /// <summary>
        /// R17-slicer-timeline-cache-2: a pivot slicer's available tiles (<see cref="SlicerModel.CacheItems"/>)
        /// so a native .fxl round-trip keeps every unselected tile, not just the ones in
        /// <see cref="SelectedItems"/> (the pivot-item resolver gates entirely on CacheItems.Count > 0).
        /// </summary>
        public List<SlicerCacheItemDto>? CacheItems { get; set; }

        /// <summary>Mirrors <see cref="SlicerModel.SelectionCaptured"/> so a cleared filter round-trips too.</summary>
        public bool SelectionCaptured { get; set; }
    }

    private class SlicerCacheItemDto
    {
        public int Index { get; set; }
        public bool IsSelected { get; set; }
    }

    private class TimelineDto
    {
        public string Name { get; set; } = "";
        public string? Caption { get; set; }
        public string CacheName { get; set; } = "";
        public string? SourcePivotTableName { get; set; }

        /// <summary>Mirrors <see cref="TimelineModel.ConnectedPivotTableNames"/> (R133-io-slicer-timeline-multipivot).</summary>
        public List<string>? ConnectedPivotTableNames { get; set; }
        public string? SourceFieldName { get; set; }
        public string? StyleName { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public string? SelectedStartDate { get; set; }
        public string? SelectedEndDate { get; set; }
        public DrawingAnchorRangeDto? DrawingAnchor { get; set; }
        public string? DrawingShapeName { get; set; }
        public string? SourceSheetName { get; set; }
        public int? Level { get; set; }
        public int? SelectionLevel { get; set; }
        public string? ScrollPosition { get; set; }
    }

    // Internal (not private): shared with NativeJsonVisualDtoMapper so picture-cell snapshots can
    // round-trip their captured CellStyle (see PictureCellDto.Style, P26).
    internal class CellStyleDto
    {
        public string FontName { get; set; } = "Calibri";
        public double FontSize { get; set; } = 11;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public CellFontScheme FontScheme { get; set; } = CellFontScheme.None;
        // Not JsonIgnore(WhenWritingDefault): the business default is 1 (not the CLR default of 0
        // that attribute checks against), so that condition would never actually suppress anything.
        public int Charset { get; set; } = 1;
        // Not JsonIgnore(WhenWritingDefault): the business default is 2 (not the CLR default of 0
        // that attribute checks against), so that condition would never actually suppress anything.
        public int FontFamily { get; set; } = 2;
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public bool Strikethrough { get; set; }
        public bool Superscript { get; set; }
        public bool Subscript { get; set; }
        public CellColor FontColor { get; set; } = CellColor.Black;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ThemeColorReferenceDto? FontThemeColor { get; set; }
        public CellColor? FillColor { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ThemeColorReferenceDto? FillThemeColor { get; set; }
        public CellFillPatternStyle FillPatternStyle { get; set; }
        public CellColor? FillPatternColor { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ThemeColorReferenceDto? FillPatternThemeColor { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CellGradientFillDto? GradientFill { get; set; }
        public CellBorderDto? BorderTop { get; set; }
        public CellBorderDto? BorderRight { get; set; }
        public CellBorderDto? BorderBottom { get; set; }
        public CellBorderDto? BorderLeft { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CellBorderDto? BorderDiagonalDown { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CellBorderDto? BorderDiagonalUp { get; set; }
        public string NumberFormat { get; set; } = "General";
        public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.General;
        public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Bottom;
        public bool WrapText { get; set; }
        public bool ShrinkToFit { get; set; }
        public bool DoubleUnderline { get; set; }
        public int IndentLevel { get; set; }
        public int TextRotation { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public CellReadingOrder ReadingOrder { get; set; } = CellReadingOrder.Context;
        public bool Locked { get; set; } = true;
        public bool Hidden { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? DxfBold { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? DxfItalic { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? DxfUnderline { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? DxfStrikethrough { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CellColor? DxfFontColor { get; set; }
        public IReadOnlyDictionary<string, string>? NativeDifferentialAttributes { get; set; }
        public IReadOnlyList<string>? NativeDifferentialChildXmls { get; set; }
        public IReadOnlyDictionary<string, string>? NativeDifferentialElementXmls { get; set; }
    }

    // Internal (not private): exposed as a property type on the now-internal CellStyleDto (shared
    // with NativeJsonVisualDtoMapper for picture-cell snapshot styles, P26).
    internal class CellBorderDto
    {
        public BorderStyle Style { get; set; }
        public CellColor Color { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ThemeColorReferenceDto? ThemeColor { get; set; }
    }

    // Internal (not private): exposed as a property type on the now-internal CellStyleDto (P26).
    internal class CellGradientFillDto
    {
        public CellGradientFillType Type { get; set; }
        public double Degree { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double Left { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double Right { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double Top { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double Bottom { get; set; }
        public List<CellGradientStopDto> Stops { get; set; } = [];
    }

    // Internal (not private): exposed transitively via CellGradientFillDto.Stops on the now-internal
    // CellStyleDto (P26).
    internal class CellGradientStopDto
    {
        public double Position { get; set; }
        public CellColor Color { get; set; }
    }

    private class CommentDto
    {
        public string? Address { get; set; }
        public string? Text { get; set; }

        /// <summary>Legacy note author (Sheet.CommentAuthors). Null/absent means no recorded author.</summary>
        public string? Author { get; set; }

        /// <summary>
        /// Whether this legacy note's comment box is pinned "always shown" (Sheet.ShownComments).
        /// Absent on files written before this field existed, which always meant not pinned.
        /// </summary>
        public bool IsShown { get; set; }
    }

    private class ThreadedCommentDto
    {
        public string? Address { get; set; }
        public string? Text { get; set; }
        public string? Author { get; set; }
        public bool IsResolved { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTimeOffset? CreatedAtUtc { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTimeOffset? ModifiedAtUtc { get; set; }
        /// <summary>See <see cref="ThreadedComment.RootTextEditedAtUtc"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTimeOffset? RootTextEditedAtUtc { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Id { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MentionsXml { get; set; }
        /// <summary>
        /// The source <c>&lt;threadedComment&gt;/@personId</c> this root comment was loaded with
        /// (see <see cref="ThreadedComment.SourcePersonId"/>); round-tripped so the mention ids
        /// inside <see cref="MentionsXml"/> keep resolving after a native-JSON round trip and a
        /// later re-save to XLSX.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SourcePersonId { get; set; }
        /// <summary>See <see cref="ThreadedComment.MentionedPersonDisplayNames"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? MentionedPersonDisplayNames { get; set; }
        public List<CommentReplyDto> Replies { get; set; } = [];
    }

    private class CommentReplyDto
    {
        public string? Text { get; set; }
        public string? Author { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTimeOffset? CreatedAtUtc { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTimeOffset? ModifiedAtUtc { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Id { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MentionsXml { get; set; }
        /// <summary>See <see cref="ThreadedCommentDto.SourcePersonId"/> (reply variant of the same concept).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SourcePersonId { get; set; }
        /// <summary>See <see cref="CommentReply.MentionedPersonDisplayNames"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? MentionedPersonDisplayNames { get; set; }
    }

    private class HyperlinkDto
    {
        public string? Address { get; set; }
        public string? Target { get; set; }
        public HyperlinkTargetKind? LinkType { get; set; }
        public string? ScreenTip { get; set; }
        public string? Bookmark { get; set; }
    }

    /// <summary>
    /// Per-range "Range Password" for an entry in <see cref="SheetDto.AllowEditRanges"/> (see
    /// <see cref="Sheet.AllowEditRangePasswords"/>). <see cref="Range"/> is the same
    /// <c>GridRange.ToString()</c> form used in <see cref="SheetDto.AllowEditRanges"/>, so it can be
    /// matched back up on load.
    /// </summary>
    private class AllowEditRangePasswordDto
    {
        public string? Range { get; set; }
        public string? Password { get; set; }
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

    private class UIntStringListDto
    {
        public uint Index { get; set; }
        public List<string> Values { get; set; } = [];
    }

    private class UIntUintListDto
    {
        public uint Index { get; set; }
        public List<uint> Values { get; set; } = [];
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

        // Group identity (for multi-group round-trips)
        public int GroupId { get; set; }

        // Show-flags
        public bool ShowMarkers { get; set; }
        public bool ShowHighPoint { get; set; }
        public bool ShowLowPoint { get; set; }
        public bool ShowFirstPoint { get; set; }
        public bool ShowLastPoint { get; set; }
        public bool ShowNegativePoints { get; set; }
        public bool ShowAxis { get; set; }
        public bool DisplayHidden { get; set; }
        public bool RightToLeft { get; set; }

        // Colors (stored as "RRGGBB" hex strings; null = not set)
        public string? SeriesColor { get; set; }
        public string? NegativeColor { get; set; }
        public string? AxisColor { get; set; }
        public string? MarkersColor { get; set; }
        public string? HighPointColor { get; set; }
        public string? LowPointColor { get; set; }
        public string? FirstPointColor { get; set; }
        public string? LastPointColor { get; set; }

        // Appearance
        public double? LineWeight { get; set; }

        // Axis scaling
        public SparklineAxisScaling MinAxisType { get; set; } = SparklineAxisScaling.Individual;
        public SparklineAxisScaling MaxAxisType { get; set; } = SparklineAxisScaling.Individual;
        public double? ManualMin { get; set; }
        public double? ManualMax { get; set; }

        // Empty-cell handling
        public SparklineEmptyCellDisplay DisplayEmptyCellsAs { get; set; } = SparklineEmptyCellDisplay.Gap;
    }

    private class PivotCacheDto
    {
        public int CacheId { get; set; }
        public PivotCacheSourceType SourceType { get; set; } = PivotCacheSourceType.Unknown;
        public string? SourceSheetName { get; set; }
        public string? SourceReference { get; set; }
        public string? SourceTableName { get; set; }
        /// <summary>
        /// R109: mirrors <see cref="Model.PivotCacheModel.SourceTableId"/> -- previously NOT present on
        /// this DTO at all, so the id-based table binding r104 established (and r107/r108's structured-table
        /// id watermark protects against reuse of) was silently discarded on every native .fxl save and
        /// came back null after every native reload, regardless of what was pinned in memory at save time.
        /// See PivotCacheModel.SourceTableId's own doc comment for why the id anchor matters more than the
        /// name.
        /// </summary>
        public int? SourceTableId { get; set; }
        public int? ConnectionId { get; set; }
        public bool IsOlap { get; set; }
        public string PackagePart { get; set; } = "";
        public bool RefreshOnLoad { get; set; } = true;
        public bool SaveData { get; set; } = true;
        public bool EnableRefresh { get; set; } = true;
        public bool PreserveSourceSortFilter { get; set; } = true;
        public int? MissingItemsLimit { get; set; }
        public int? RecordCount { get; set; }
        public int? CreatedVersion { get; set; }
        public int? MinRefreshableVersion { get; set; }
        public int? RefreshedVersion { get; set; }
        public string? RefreshedBy { get; set; }
        public string? RefreshedDateIso { get; set; }
        /// <summary>See <see cref="PivotCacheModel.RawRecordsXml"/>.</summary>
        public string? RawRecordsXml { get; set; }
        public List<PivotCacheFieldDto> Fields { get; set; } = [];
    }

    private class PivotCacheFieldDto
    {
        public string Name { get; set; } = "";
        public int? NumberFormatId { get; set; }
        public int? SharedItemCount { get; set; }
        public bool ContainsBlank { get; set; }
        public bool ContainsString { get; set; }
        public bool ContainsNumber { get; set; }
        public bool ContainsDate { get; set; }
        public bool ContainsMixedTypes { get; set; }
        public bool ContainsSemiMixedTypes { get; set; }
        public bool ContainsNonDate { get; set; }
        public bool ContainsInteger { get; set; }
        public bool ContainsLongText { get; set; }
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
        public string? MinDate { get; set; }
        public string? MaxDate { get; set; }
        public List<string>? SharedItems { get; set; }
        /// <summary>
        /// Element kind for each entry in <see cref="SharedItems"/> ('s', 'n', 'd', 'b'), mirroring
        /// <see cref="PivotCacheFieldModel.SharedItemKinds"/>. Preserving this prevents a shared item
        /// (e.g. a boolean serialized as "1"/"0") from being misclassified by kind-inference on a later
        /// XLSX export after round-tripping through the native JSON format.
        /// </summary>
        public List<string>? SharedItemKinds { get; set; }
        public string? Formula { get; set; }
        public bool IsDatabaseField { get; set; } = true;
    }

    private class PivotTableDto
    {
        public string Name { get; set; } = "";
        public int CacheId { get; set; }
        public string? SourceSheetName { get; set; }
        public string? SourceRange { get; set; }
        public string? TargetRange { get; set; }
        public string? LastRenderedRange { get; set; }
        public string PackagePart { get; set; } = "";
        public int? CreatedVersion { get; set; }
        public int? UpdatedVersion { get; set; }
        public int? MinRefreshableVersion { get; set; }
        public bool DataOnRows { get; set; } = true;
        public int FirstHeaderRow { get; set; } = 1;
        public int FirstDataRow { get; set; } = 1;
        public int FirstDataColumn { get; set; } = 1;
        public bool ShowSubtotals { get; set; }
        public PivotSubtotalPlacement SubtotalPlacement { get; set; } = PivotSubtotalPlacement.Bottom;
        public bool ShowRowGrandTotals { get; set; } = true;
        public bool ShowColumnGrandTotals { get; set; } = true;
        public bool RepeatItemLabels { get; set; } = true;
        public bool BlankLineAfterItems { get; set; }
        public PivotReportLayout ReportLayout { get; set; } = PivotReportLayout.Tabular;
        public int CompactRowLabelIndent { get; set; } = 1;
        public string StyleName { get; set; } = "PivotStyleLight16";
        public bool ShowRowHeaders { get; set; } = true;
        public bool ShowColumnHeaders { get; set; } = true;
        public bool ShowRowStripes { get; set; }
        public bool ShowColumnStripes { get; set; }
        public bool ShowFieldHeaders { get; set; } = true;
        public bool ShowContextualTooltips { get; set; } = true;
        public bool ShowPropertiesInTooltips { get; set; } = true;
        public bool ShowClassicLayout { get; set; }
        public bool MergeAndCenterLabels { get; set; }
        public bool ShowItemsWithNoDataOnRows { get; set; }
        public bool ShowItemsWithNoDataOnColumns { get; set; }
        public bool PageOverThenDown { get; set; }
        public int PageWrap { get; set; }
        public string? EmptyValueText { get; set; }
        public bool ApplyNumberFormats { get; set; } = true;
        public bool ApplyBorderFormats { get; set; } = true;
        public bool ApplyFontFormats { get; set; } = true;
        public bool ApplyPatternFormats { get; set; } = true;
        public bool AutofitColumnsOnUpdate { get; set; } = true;
        public bool PreserveFormattingOnUpdate { get; set; } = true;
        public bool ShowExpandCollapseButtons { get; set; } = true;
        public bool EnableDrill { get; set; } = true;
        public bool AsteriskTotals { get; set; }
        public bool MultipleFieldFilters { get; set; } = true;
        public bool EnableFieldDialog { get; set; } = true;
        public bool EnableFieldProperties { get; set; } = true;
        public bool EnableDataValueEditing { get; set; }
        public bool PrintTitles { get; set; }
        public bool PrintExpandCollapseButtons { get; set; }
        public string? AltTextTitle { get; set; }
        public string? AltTextDescription { get; set; }
        public string? DataCaption { get; set; }
        public string? GrandTotalCaption { get; set; }
        public string? MissingCaption { get; set; }
        public string? ErrorCaption { get; set; }
        public List<PivotFieldDto> RowFields { get; set; } = [];
        public List<PivotFieldDto> ColumnFields { get; set; } = [];
        public List<PivotFieldDto> PageFields { get; set; } = [];
        public List<PivotDataFieldDto> DataFields { get; set; } = [];
        public List<PivotCalculatedFieldDto> CalculatedFields { get; set; } = [];
        public List<PivotCalculatedItemDto> CalculatedItems { get; set; } = [];
        public List<PivotLabelFilterDto> LabelFilters { get; set; } = [];
        public List<PivotValueFilterDto> ValueFilters { get; set; } = [];
        public List<PivotSortDto> Sorts { get; set; } = [];
    }

    private class PivotFieldDto
    {
        public int SourceFieldIndex { get; set; }
        public string? SelectedItem { get; set; }
        public List<string>? SelectedItems { get; set; }
        public PivotFieldGrouping Grouping { get; set; } = PivotFieldGrouping.None;
        public double? GroupStart { get; set; }
        public double? GroupEnd { get; set; }
        public double? GroupInterval { get; set; }
        public bool? ShowAll { get; set; }
        public bool? IncludeNewItemsInFilter { get; set; }
        public bool? MultipleItemSelectionAllowed { get; set; }
        public bool? DragToRow { get; set; }
        public bool? DragToColumn { get; set; }
        public bool? DragToPage { get; set; }
        public bool? DragToData { get; set; }
        public bool? ShowDropDowns { get; set; }
    }

    private class PivotDataFieldDto
    {
        public int SourceFieldIndex { get; set; }
        public string Name { get; set; } = "";
        public string SummaryFunction { get; set; } = "sum";
        public int? NumberFormatId { get; set; }
        public string? CalculatedFieldName { get; set; }
        public PivotShowValuesAs ShowValuesAs { get; set; } = PivotShowValuesAs.None;
        public int? BaseFieldIndex { get; set; }
        public string? BaseItem { get; set; }
        public string? NumberFormatCode { get; set; }
    }

    private class PivotCalculatedFieldDto
    {
        public string Name { get; set; } = "";
        public string Formula { get; set; } = "";
    }

    private class PivotCalculatedItemDto
    {
        public int SourceFieldIndex { get; set; }
        public string Name { get; set; } = "";
        public string Formula { get; set; } = "";
    }

    private class PivotLabelFilterDto
    {
        public int SourceFieldIndex { get; set; }
        public PivotLabelFilterKind Kind { get; set; } = PivotLabelFilterKind.Equals;
        public string Value { get; set; } = "";
        public string? Value2 { get; set; }
    }

    private class PivotValueFilterDto
    {
        public int DataFieldIndex { get; set; }
        public PivotValueFilterKind Kind { get; set; } = PivotValueFilterKind.Top;
        public int Count { get; set; }
        public double? ComparisonValue { get; set; }
        public double? ComparisonValue2 { get; set; }
        public int? SourceFieldIndex { get; set; }
    }

    private class PivotSortDto
    {
        public PivotSortTarget Target { get; set; } = PivotSortTarget.Label;
        public PivotSortDirection Direction { get; set; } = PivotSortDirection.Ascending;
        public int DataFieldIndex { get; set; }
        public int FieldIndex { get; set; }
    }

}
