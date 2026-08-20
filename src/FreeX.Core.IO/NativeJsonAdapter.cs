using System.Text.Json;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Native JSON adapter for FreeX.
/// Serializes the workbook to a simple, human-readable JSON format.
/// </summary>
public sealed partial class NativeJsonAdapter : IFileAdapter
{
    private const string NativeFileFormat = "FreeX.NativeJsonWorkbook";
    // Bumped 1 -> 2 (R72-meta-1): schema version 2 marks the point at which an explicit data-bar
    // Min/Max threshold type is trusted as-is on load. Only a file whose loaded schema version is
    // still < 2 (a genuine pre-r70/legacy save) has its legacy Min/Max data-bar endpoint migrated
    // to AutoMin/AutoMax -- see ToConditionalFormat in NativeJsonAdapter.ConditionalFormat.cs.
    private const int CurrentSchemaVersion = 2;
    private const int CurrentMinimumReaderVersion = 1;
    private const string NumberStoredAsTextCode = "NumberStoredAsText";
    private const string FormulaRefersToBlankCellsCode = "FormulaRefersToBlankCells";

    private static readonly JsonSerializerOptions LoadOptions = new()
    {
        // Default options — defined as a static field so the reflection cache is shared
        // across all load calls rather than being rebuilt on every invocation.
    };

    /// <summary>Exposed for unit tests to verify the static instance is reused.</summary>
    internal static JsonSerializerOptions LoadOptionsForTest => LoadOptions;

    public string Extension => ".fxl";
    public string FormatName => "FreeX Workbook";

    public Workbook Load(Stream stream)
    {
        var dto = JsonSerializer.Deserialize<WorkbookDto>(stream, LoadOptions)
            ?? throw new InvalidDataException("Invalid FreeX file");

        ValidateSchemaHeader(dto);
        // A missing SchemaVersion field (pre-schema-version legacy file) is treated as version 1,
        // the same as an explicit "SchemaVersion": 1 -- both predate the r70 AutoMin/AutoMax split.
        var loadedSchemaVersion = dto.SchemaVersion ?? 1;

        var workbook = ToCellStyle(dto.DefaultStyle) is { } customizedDefaultStyle
            ? new Workbook(dto.Name, customizedDefaultStyle)
            : new Workbook(dto.Name);
        if (dto.Theme is { } theme)
            workbook.Theme = ToWorkbookTheme(theme);
        workbook.Uses1904DateSystem = dto.Uses1904DateSystem;
        workbook.HasVbaProjectPackage = dto.HasVbaProjectPackage;
        workbook.ShowSheetTabs = dto.ShowSheetTabs;
        workbook.SheetTabRatio = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(dto.SheetTabRatio, 1000);
        workbook.FirstVisibleSheetIndex = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(dto.FirstVisibleSheetIndex, Math.Max(0, (dto.Sheets?.Count ?? 1) - 1));
        workbook.ActiveSheetIndex = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(dto.ActiveSheetIndex, Math.Max(0, (dto.Sheets?.Count ?? 1) - 1));
        workbook.FileVersion = ToWorkbookFileVersion(dto.FileVersion);
        workbook.CountrySettings = ToWorkbookCountrySettings(dto.CountrySettings);
        workbook.LegacyMenuSettings = ToWorkbookLegacyMenuSettings(dto.LegacyMenuSettings);
        workbook.LegacyWorkbookSettings = ToWorkbookLegacyWorkbookSettings(dto.LegacyWorkbookSettings);
        workbook.FileSharing = ToWorkbookFileSharing(dto.FileSharing);
        foreach (var fileRecoveryProperties in (dto.FileRecoveryProperties ?? []).Select(ToWorkbookFileRecoveryProperties).OfType<WorkbookFileRecoveryPropertiesModel>())
            workbook.FileRecoveryProperties.Add(fileRecoveryProperties);
        workbook.Properties = ToWorkbookProperties(dto.Properties);
        workbook.FunctionGroups = ToWorkbookFunctionGroups(dto.FunctionGroups);
        workbook.SmartTags = ToWorkbookSmartTags(dto.SmartTags);
        workbook.AdditionalViews = ToWorkbookAdditionalViews(dto.AdditionalViews);
        workbook.IsStructureProtected = dto.IsStructureProtected;
        // Not gated on IsStructureProtected -- see the matching note on the Save side
        // (NativeJsonAdapter.Save.cs): a stored password can be genuine Windows-only protection
        // (IsStructureProtected == false) and must survive the load unchanged, exactly like
        // XlsxWorkbookMetadataReader treats workbookPassword independent of lockStructure.
        workbook.StructureProtectionPassword = dto.StructureProtectionPassword;
        workbook.ProtectionMetadata = ToWorkbookProtectionMetadata(dto.ProtectionMetadata);
        if (dto.WindowArrangement is { } arrangement && Enum.IsDefined(arrangement))
            workbook.WindowArrangement = arrangement;
        ApplyCalculationOptions(dto, workbook);
        foreach (var errorCode in dto.DisabledFormulaErrorCodes ?? [])
            if (IsSupportedFormulaErrorCode(errorCode))
                workbook.DisabledFormulaErrorCodes.Add(errorCode);
        LoadPivotCaches(workbook, dto.PivotCaches);
        LoadSlicers(workbook, dto.Slicers);
        LoadTimelines(workbook, dto.Timelines);

        var loadedSheetsBySourceName = new Dictionary<string, Sheet>(StringComparer.OrdinalIgnoreCase);
        var pendingPivotTables = new List<(Sheet Sheet, SheetDto Dto)>();
        var pendingCrossSheetCharts = new List<(ChartModel Chart, string DataRangeSheetName)>();
        var pendingCrossSheetPictures = new List<(PictureModel Picture, string LinkedSourceSheetName)>();
        var cellStyleTable = LoadCellStyleTable(workbook, dto.CellStyles);
        Dictionary<CellStyleDto, StyleId>? styleIdCache = null;
        var sheetIndex = 1;
        foreach (var sDto in dto.Sheets ?? [])
        {
            if (sDto is null) continue;
            var sheet = workbook.AddSheet(UniqueSheetName(workbook, sDto.Name, sheetIndex++));
            pendingPivotTables.Add((sheet, sDto));
            if (!string.IsNullOrWhiteSpace(sDto.Name))
                loadedSheetsBySourceName.TryAdd(sDto.Name, sheet);
            sheet.IsHidden = sDto.IsHidden;
            sheet.IsVeryHidden = sDto.IsVeryHidden;
            sheet.Kind = NativeJsonValueSanitizer.ValidEnumOrDefault(sDto.Kind, SheetKind.Worksheet);
            sheet.TabColor = sDto.TabColor is { } tabColor ? ParseColor(tabColor) : null;
            sheet.IsProtected = sDto.IsProtected;
            sheet.ProtectionPassword = sDto.IsProtected ? sDto.ProtectionPassword : null;
            sheet.ProtectionMetadata = ToWorksheetProtectionMetadata(sDto.ProtectionMetadata);
            if (sDto.ProtectionPermissions is not null)
            {
                sheet.ProtectionPermissions.Clear();
                foreach (var permission in sDto.ProtectionPermissions.Where(Enum.IsDefined).Distinct())
                    sheet.ProtectionPermissions.Add(permission);
            }
            foreach (var property in sDto.CustomProperties ?? [])
            {
                if (property is null || string.IsNullOrWhiteSpace(property.Name) || property.Id <= 0)
                    continue;

                sheet.CustomProperties.Add(new WorksheetCustomProperty(
                    property.Name,
                    property.Id,
                    ToWorksheetCustomPropertyMetadata(property.Metadata)));
            }
            foreach (var entry in (sDto.RowHeights ?? []).OfType<UIntDoubleDto>())
                if (NativeJsonValueSanitizer.IsValidRowIndex(entry.Index) && NativeJsonValueSanitizer.IsPositiveFinite(entry.Value))
                    sheet.RowHeights[entry.Index] = entry.Value;
            foreach (var entry in (sDto.ColumnWidths ?? []).OfType<UIntDoubleDto>())
                if (NativeJsonValueSanitizer.IsValidColumnIndex(entry.Index) && NativeJsonValueSanitizer.IsPositiveFinite(entry.Value))
                    sheet.ColumnWidths[entry.Index] = entry.Value;
            foreach (var row in sDto.HiddenRows ?? [])
                if (NativeJsonValueSanitizer.IsValidRowIndex(row))
                    sheet.HiddenRows.Add(row);
            foreach (var row in sDto.FilterHiddenRows ?? [])
                if (NativeJsonValueSanitizer.IsValidRowIndex(row))
                    sheet.FilterHiddenRows.Add(row);
            // G32: restore the per-column value-filter state (and which FilterHiddenRows entries it
            // owns) so a reloaded workbook's next filter recompute has the same "AND across columns"
            // picture it had before saving, instead of treating every column as unfiltered.
            foreach (var entry in (sDto.ActiveValueFilterColumns ?? []).OfType<UIntStringListDto>())
                if (NativeJsonValueSanitizer.IsValidColumnIndex(entry.Index))
                    sheet.ActiveValueFilterColumns[entry.Index] = [.. entry.Values];
            foreach (var row in sDto.ValueFilterHiddenRows ?? [])
                if (NativeJsonValueSanitizer.IsValidRowIndex(row))
                    sheet.ValueFilterHiddenRows.Add(row);
            // R14-meta-1: restore which rows each column-owned filter mechanism (condition/Top-Bottom/
            // average/value-list) still owns, or the next un-hide decision on any OTHER column wrongly
            // treats every column's filter as inactive (see FilterCommand.cs, finding R14-meta-1).
            foreach (var entry in (sDto.ColumnFilterOwnedRows ?? []).OfType<UIntUintListDto>())
                if (NativeJsonValueSanitizer.IsValidColumnIndex(entry.Index))
                    sheet.ColumnFilterOwnedRows[entry.Index] =
                        [.. entry.Values.Where(NativeJsonValueSanitizer.IsValidRowIndex)];
            foreach (var column in sDto.HiddenCols ?? [])
                if (NativeJsonValueSanitizer.IsValidColumnIndex(column))
                    sheet.HiddenCols.Add(column);
            foreach (var entry in (sDto.RowOutlineLevels ?? []).OfType<UIntIntDto>())
                if (NativeJsonValueSanitizer.IsValidRowIndex(entry.Index) && NativeJsonValueSanitizer.IsValidOutlineLevel(entry.Value))
                    sheet.RowOutlineLevels[entry.Index] = entry.Value;
            foreach (var entry in (sDto.ColOutlineLevels ?? []).OfType<UIntIntDto>())
                if (NativeJsonValueSanitizer.IsValidColumnIndex(entry.Index) && NativeJsonValueSanitizer.IsValidOutlineLevel(entry.Value))
                    sheet.ColOutlineLevels[entry.Index] = entry.Value;
            sheet.OutlineSummaryBelow = sDto.OutlineSummaryBelow;
            sheet.OutlineSummaryRight = sDto.OutlineSummaryRight;
            sheet.ShowOutlineSymbols = sDto.ShowOutlineSymbols;
            sheet.ApplyOutlineStyles = sDto.ApplyOutlineStyles;
            sheet.SheetFormatMetadata = ToWorksheetSheetFormatMetadata(sDto.SheetFormatMetadata);
            sheet.DimensionMetadata = ToWorksheetDimensionMetadata(sDto.DimensionMetadata);
            sheet.SheetPropertiesMetadata = ToWorksheetSheetPropertiesMetadata(sDto.SheetPropertiesMetadata);
            foreach (var row in sDto.GroupHiddenRows ?? [])
                if (NativeJsonValueSanitizer.IsValidRowIndex(row))
                    sheet.GroupHiddenRows.Add(row);
            foreach (var column in sDto.GroupHiddenCols ?? [])
                if (NativeJsonValueSanitizer.IsValidColumnIndex(column))
                    sheet.GroupHiddenCols.Add(column);
            foreach (var row in sDto.CollapsedAnchorRows ?? [])
                if (NativeJsonValueSanitizer.IsValidRowIndex(row))
                    sheet.CollapsedAnchorRows.Add(row);
            foreach (var column in sDto.CollapsedAnchorCols ?? [])
                if (NativeJsonValueSanitizer.IsValidColumnIndex(column))
                    sheet.CollapsedAnchorCols.Add(column);
            sheet.ViewMode = Enum.IsDefined(sDto.ViewMode) ? sDto.ViewMode : WorksheetViewMode.Normal;
            sheet.ShowGridlines = sDto.ShowGridlines ?? true;
            sheet.ShowHeadings = sDto.ShowHeadings ?? true;
            sheet.ShowRulers = sDto.ShowRulers ?? true;
            sheet.ZoomPercent = NativeJsonValueSanitizer.ValidZoomPercentOrDefault(sDto.ZoomPercent);
            sheet.ShowFormulas = sDto.ShowFormulas ?? false;
            sheet.IsRightToLeft = sDto.IsRightToLeft ?? false;
            sheet.ShowZeros = sDto.ShowZeros ?? true;
            sheet.FullCalculationOnLoad = sDto.FullCalculationOnLoad;
            sheet.PhoneticProperties = ToWorksheetPhoneticProperties(sDto.PhoneticProperties);
            sheet.FrozenRows = NativeJsonValueSanitizer.ValidFrozenRowsOrZero(sDto.FrozenRows);
            sheet.FrozenCols = NativeJsonValueSanitizer.ValidFrozenColumnsOrZero(sDto.FrozenCols);
            sheet.ViewTopRow = NativeJsonValueSanitizer.ValidRowPaneOrNull(sDto.ViewTopRow);
            sheet.ViewLeftCol = NativeJsonValueSanitizer.ValidColumnPaneOrNull(sDto.ViewLeftCol);
            sheet.ActiveRow = NativeJsonValueSanitizer.ValidRowPaneOrNull(sDto.ActiveRow);
            sheet.ActiveCol = NativeJsonValueSanitizer.ValidColumnPaneOrNull(sDto.ActiveCol);
            sheet.SplitRow = sheet.FrozenRows > 0 || sheet.FrozenCols > 0
                ? null
                : NativeJsonValueSanitizer.ValidRowPaneOrNull(sDto.SplitRow);
            sheet.SplitColumn = sheet.FrozenRows > 0 || sheet.FrozenCols > 0
                ? null
                : NativeJsonValueSanitizer.ValidColumnPaneOrNull(sDto.SplitColumn);
            sheet.AutoFilter = ToWorksheetAutoFilter(sDto.AutoFilter, sheet.Id);
            sheet.SmartTags = ToWorksheetSmartTags(sDto.SmartTags);
            sheet.DataConsolidation = ToWorksheetDataConsolidation(sDto.DataConsolidation);
            sheet.SortState = ToWorksheetSortState(sDto.SortState, sheet.Id);
            sheet.SingleXmlCells = ToWorksheetSingleXmlCells(sDto.SingleXmlCells);
            sheet.CellWatchesMetadata = ToWorksheetCellWatchesMetadata(sDto.CellWatchesMetadata);
            sheet.IgnoredErrorsMetadata = ToWorksheetIgnoredErrorsMetadata(sDto.IgnoredErrorsMetadata);
            sheet.AdditionalViews = ToWorksheetAdditionalViews(sDto.AdditionalViews);
            sheet.PrimaryViewMetadata = ToWorksheetPrimaryViewMetadata(sDto.PrimaryViewMetadata);
            // Multi-area print areas (new field takes precedence; fall back to legacy single-area field).
            if (sDto.PrintAreas is { Length: > 0 })
            {
                var areas = new List<GridRange>(sDto.PrintAreas.Length);
                foreach (var raw in sDto.PrintAreas)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    try { areas.Add(GridRange.Parse(raw, sheet.Id)); }
                    catch (FormatException) { /* skip unparseable */ }
                }
                if (areas.Count > 0)
                    sheet.SetPrintAreas(areas);
            }
            else if (!string.IsNullOrWhiteSpace(sDto.PrintArea))
            {
                try { sheet.PrintArea = GridRange.Parse(sDto.PrintArea, sheet.Id); }
                catch (FormatException) { /* skip unparseable print areas */ }
            }
            if (sDto.PageOrientation is { } orientation && Enum.IsDefined(orientation))
                sheet.PageOrientation = orientation;
            if (sDto.PaperSizeCode is { } rawCode && rawCode > 0)
            {
                // Exotic code preserved in JSON: restore it and derive the enum best-effort.
                sheet.PaperSizeCode = rawCode;
                sheet.PaperSize = PaperSizeCodes.TryGetEnum(rawCode, out var mappedSize)
                    ? mappedSize
                    : WorksheetPaperSize.A4;
            }
            else if (sDto.PaperSize is { } paperSize && Enum.IsDefined(paperSize))
            {
                // Known size: enum is authoritative; derive code from it.
                sheet.PaperSize = paperSize;
                sheet.PaperSizeCode = PaperSizeCodes.GetCode(paperSize);
            }
            if (sDto.PageMargins is { } margins)
                sheet.PageMargins = NativeJsonValueSanitizer.ValidPageMarginsOrDefault(
                    new WorksheetPageMargins(margins.Left, margins.Right, margins.Top, margins.Bottom),
                    WorksheetPageMargins.Narrow);
            sheet.HeaderMargin = NativeJsonValueSanitizer.NonNegativeFiniteOrDefault(sDto.HeaderMargin, 0.3);
            sheet.FooterMargin = NativeJsonValueSanitizer.NonNegativeFiniteOrDefault(sDto.FooterMargin, 0.3);
            sheet.PageMarginsMetadata = ToWorksheetPageMarginsMetadata(sDto.PageMarginsMetadata);
            sheet.PrintGridlines = sDto.PrintGridlines;
            sheet.PrintHeadings = sDto.PrintHeadings;
            sheet.PrintOptionsMetadata = ToWorksheetPrintOptionsMetadata(sDto.PrintOptionsMetadata);
            sheet.PrintTitleRows = ToRepeatRange(sDto.PrintTitleRows, CellAddress.MaxRow);
            sheet.PrintTitleColumns = ToRepeatRange(sDto.PrintTitleColumns, CellAddress.MaxCol);
            sheet.PageHeader = ToHeaderFooter(sDto.PageHeader);
            sheet.PageFooter = ToHeaderFooter(sDto.PageFooter);
            sheet.FirstPageHeader = ToHeaderFooter(sDto.FirstPageHeader);
            sheet.FirstPageFooter = ToHeaderFooter(sDto.FirstPageFooter);
            sheet.EvenPageHeader = ToHeaderFooter(sDto.EvenPageHeader);
            sheet.EvenPageFooter = ToHeaderFooter(sDto.EvenPageFooter);
            sheet.PageHeaderPictures = ToHeaderFooterPictures(sDto.PageHeaderPictures);
            sheet.PageFooterPictures = ToHeaderFooterPictures(sDto.PageFooterPictures);
            sheet.FirstPageHeaderPictures = ToHeaderFooterPictures(sDto.FirstPageHeaderPictures);
            sheet.FirstPageFooterPictures = ToHeaderFooterPictures(sDto.FirstPageFooterPictures);
            sheet.EvenPageHeaderPictures = ToHeaderFooterPictures(sDto.EvenPageHeaderPictures);
            sheet.EvenPageFooterPictures = ToHeaderFooterPictures(sDto.EvenPageFooterPictures);
            sheet.DifferentFirstPageHeaderFooter = sDto.DifferentFirstPageHeaderFooter;
            sheet.DifferentOddEvenHeaderFooter = sDto.DifferentOddEvenHeaderFooter;
            sheet.HeaderFooterScaleWithDocument = sDto.HeaderFooterScaleWithDocument ?? true;
            sheet.HeaderFooterAlignWithMargins = sDto.HeaderFooterAlignWithMargins ?? true;
            sheet.HeaderFooterMetadata = ToWorksheetHeaderFooterMetadata(sDto.HeaderFooterMetadata);
            sheet.CenterHorizontallyOnPage = sDto.CenterHorizontallyOnPage;
            sheet.CenterVerticallyOnPage = sDto.CenterVerticallyOnPage;
            if (sDto.PageOrder is { } pageOrder && Enum.IsDefined(pageOrder))
                sheet.PageOrder = pageOrder;
            sheet.FirstPageNumber = sDto.FirstPageNumber is > 0 ? sDto.FirstPageNumber : null;
            sheet.UsePrinterDefaults = sDto.UsePrinterDefaults;
            sheet.PrintCopies = sDto.PrintCopies is > 0 ? sDto.PrintCopies : null;
            sheet.PrintBlackAndWhite = sDto.PrintBlackAndWhite;
            sheet.PrintDraftQuality = sDto.PrintDraftQuality;
            sheet.PrintQualityDpi = sDto.PrintQualityDpi is > 0 ? sDto.PrintQualityDpi : null;
            sheet.PrintQualityVerticalDpi = sDto.PrintQualityVerticalDpi is > 0 ? sDto.PrintQualityVerticalDpi : null;
            if (sDto.PrintErrorValue is { } printErrorValue && Enum.IsDefined(printErrorValue))
                sheet.PrintErrorValue = printErrorValue;
            if (sDto.PrintComments is { } printComments && Enum.IsDefined(printComments))
                sheet.PrintComments = printComments;
            sheet.LegacyPrintSize = sDto.LegacyPrintSize is > 0 and <= ushort.MaxValue ? sDto.LegacyPrintSize : null;
            sheet.PageSetupMetadata = ToWorksheetPageSetupMetadata(sDto.PageSetupMetadata);
            if (sDto.ScaleToFit is { } scaleToFit)
                sheet.ScaleToFit = NativeJsonValueSanitizer.ValidScaleToFitOrDefault(
                    new WorksheetScaleToFit(scaleToFit.ScalePercent, scaleToFit.FitToPagesWide, scaleToFit.FitToPagesTall),
                    WorksheetScaleToFit.Default);
            sheet.FitToPage = sDto.FitToPage;
            sheet.AutoPageBreaks = sDto.AutoPageBreaks;
            foreach (var rowBreak in sDto.RowPageBreaks ?? [])
                if (rowBreak is >= 2 and <= CellAddress.MaxRow)
                    sheet.RowPageBreaks.Add(rowBreak);
            sheet.RowPageBreaksMetadata = ToWorksheetPageBreaksMetadata(sDto.RowPageBreaksMetadata);
            foreach (var columnBreak in sDto.ColumnPageBreaks ?? [])
                if (columnBreak is >= 2 and <= CellAddress.MaxCol)
                    sheet.ColumnPageBreaks.Add(columnBreak);
            sheet.ColumnPageBreaksMetadata = ToWorksheetPageBreaksMetadata(sDto.ColumnPageBreaksMetadata);
            foreach (var mergedRegion in sDto.MergedRegions ?? [])
            {
                if (string.IsNullOrWhiteSpace(mergedRegion))
                    continue;

                try
                {
                    var range = GridRange.Parse(mergedRegion, sheet.Id);
                    if (range.Start.Sheet == sheet.Id && range.End.Sheet == sheet.Id)
                        sheet.AddMergedRegion(range);
                }
                catch (FormatException) { /* skip unparseable merged regions */ }
            }
            foreach (var commentDto in sDto.Comments ?? [])
            {
                if (TryLoadComment(commentDto, sheet.Id) is { } comment)
                {
                    sheet.Comments[comment.Address] = comment.Text;
                    if (!string.IsNullOrEmpty(comment.Author))
                        sheet.CommentAuthors[comment.Address] = comment.Author;
                    if (comment.IsShown)
                        sheet.ShownComments.Add(comment.Address);
                }
            }
            foreach (var threadedCommentDto in sDto.ThreadedComments ?? [])
            {
                if (TryLoadThreadedComment(threadedCommentDto, sheet.Id) is { } comment)
                    sheet.ThreadedComments[comment.Address] = comment.Comment;
            }
            foreach (var hyperlinkDto in sDto.Hyperlinks ?? [])
            {
                if (TryLoadHyperlink(hyperlinkDto, sheet.Id) is { } hyperlink)
                {
                    sheet.Hyperlinks[hyperlink.Address] = hyperlink.Target;
                    sheet.HyperlinkMetadata[hyperlink.Address] = hyperlink.Metadata;
                }
            }
            LoadRichTextRuns(sDto.RichTextRuns, sheet);
            LoadPhoneticGuides(sDto.CellPhoneticGuides, sheet);
            foreach (var allowEditRange in sDto.AllowEditRanges ?? [])
            {
                if (string.IsNullOrWhiteSpace(allowEditRange))
                    continue;

                try
                {
                    var range = GridRange.Parse(allowEditRange, sheet.Id);
                    if (range.Start.Sheet == sheet.Id && range.End.Sheet == sheet.Id)
                        sheet.AllowEditRanges.Add(range);
                }
                catch (FormatException) { /* skip unparseable allow-edit ranges */ }
            }
            foreach (var passwordDto in sDto.AllowEditRangePasswords ?? [])
            {
                if (string.IsNullOrWhiteSpace(passwordDto.Range) || string.IsNullOrEmpty(passwordDto.Password))
                    continue;

                try
                {
                    var range = GridRange.Parse(passwordDto.Range, sheet.Id);
                    if (range.Start.Sheet == sheet.Id && range.End.Sheet == sheet.Id &&
                        sheet.AllowEditRanges.Contains(range))
                    {
                        sheet.AllowEditRangePasswords[range] = passwordDto.Password;
                    }
                }
                catch (FormatException) { /* skip unparseable allow-edit range passwords */ }
            }
            sheet.BackgroundImage = TryLoadWorksheetBackground(sDto.BackgroundImage);
            foreach (var pictureDto in sDto.Pictures ?? [])
            {
                if (NativeJsonVisualDtoMapper.ToPicture(pictureDto, sheet.Id) is { } picture)
                {
                    sheet.Pictures.Add(picture);
                    // LinkedSourceRange was parsed against this (anchor) sheet by ToPicture; when the
                    // linked range actually lives on a different sheet (LinkedSourceSheetName differs
                    // from the anchor sheet's own name), rebind it once every sheet exists (P24).
                    if (picture.LinkedSourceRange is not null &&
                        !string.IsNullOrWhiteSpace(picture.LinkedSourceSheetName) &&
                        !string.Equals(picture.LinkedSourceSheetName, sheet.Name, StringComparison.OrdinalIgnoreCase))
                        pendingCrossSheetPictures.Add((picture, picture.LinkedSourceSheetName));
                }
            }
            foreach (var textBoxDto in sDto.TextBoxes ?? [])
            {
                if (NativeJsonVisualDtoMapper.ToTextBox(textBoxDto, sheet.Id) is { } textBox)
                    sheet.TextBoxes.Add(textBox);
            }
            foreach (var shapeDto in sDto.DrawingShapes ?? [])
            {
                if (NativeJsonVisualDtoMapper.ToDrawingShape(shapeDto, sheet.Id) is { } shape)
                    sheet.DrawingShapes.Add(shape);
            }
            foreach (var formControlDto in sDto.FormControls ?? [])
            {
                if (ToFormControl(formControlDto, sheet.Id) is { } formControl)
                    sheet.FormControls.Add(formControl);
            }
            LoadDrawingObjectZOrder(sheet, sDto.DrawingObjectZOrder);
            foreach (var sparklineDto in sDto.Sparklines ?? [])
            {
                if (TryLoadSparkline(sparklineDto, sheet.Id) is { } sparkline)
                    sheet.Sparklines.Add(sparkline);
            }
            foreach (var chartDto in sDto.Charts ?? [])
            {
                if (TryLoadChart(chartDto, sheet.Id) is { } chart)
                {
                    sheet.Charts.Add(chart);
                    if (!string.IsNullOrWhiteSpace(chartDto.DataRangeSheetName))
                        pendingCrossSheetCharts.Add((chart, chartDto.DataRangeSheetName));
                }
            }

            foreach (var validationDto in sDto.DataValidations ?? [])
            {
                if (TryLoadDataValidation(validationDto, sheet.Id) is { } validation)
                    sheet.DataValidations.Add(validation);
            }

            LoadConditionalFormats(sheet, sDto.ConditionalFormats, loadedSchemaVersion);

            var cellDtos = sDto.Cells ?? CellDtoSequence.Empty;
            sheet.EnsureCellCapacity(cellDtos.Count);
            foreach (var cDto in cellDtos)
            {
                if (cDto is null) continue;
                try
                {
                    if (!TryGetCellAddress(cDto, sheet.Id, out var addr))
                        continue;
                    var value = cDto.HasParsedNumericValue && cDto.ParsedValueType == 'n'
                        ? new NumberValue(cDto.ParsedNumericValue)
                        : cDto.HasParsedNumericValue && cDto.ParsedValueType == 'd'
                            ? new DateTimeValue(cDto.ParsedNumericValue)
                            : cDto.ParsedValueType == '\0'
                                ? NativeJsonScalarValueMapper.Deserialize(cDto.Value, cDto.ValueType)
                                : NativeJsonScalarValueMapper.Deserialize(cDto.Value, cDto.ParsedValueType);
                    var cell = cDto.Formula != null
                        ? Cell.FromFormula(NormalizeNativeFormulaText(cDto.Formula))
                        : Cell.FromValue(value);
                    if (cDto.Formula != null)
                        cell.ArrayMode = ParseFormulaArrayMode(cDto.FormulaArrayMode);
                    if (cDto.Formula != null && value is not BlankValue)
                        cell.Value = value;
                    cell.IgnoreFormulaError = cDto.IgnoreFormulaError;
                    if (ResolveCellStyleId(workbook, cellStyleTable, ref styleIdCache, cDto.StyleId, cDto.Style) is { } styleId)
                        cell.StyleId = styleId;
                    sheet.SetCell(addr, cell);
                }
                catch (FormatException) { /* skip cells with unparseable addresses */ }
            }

            foreach (var styleOnlyDto in sDto.StyleOnlyCells ?? StyleOnlyCellDtoSequence.Empty)
            {
                if (styleOnlyDto is null ||
                    (styleOnlyDto.StyleId is null && styleOnlyDto.Style is null))
                    continue;

                try
                {
                    if (!TryGetCellAddress(styleOnlyDto, sheet.Id, out var address))
                        continue;
                    if (address.Sheet != sheet.Id)
                        continue;
                    if (ResolveCellStyleId(workbook, cellStyleTable, ref styleIdCache, styleOnlyDto.StyleId, styleOnlyDto.Style) is { } styleId)
                        sheet.SetStyleOnly(address.Row, address.Col, styleId);
                }
                catch (FormatException) { /* skip style-only entries with unparseable addresses */ }
            }
        }

        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Sheet1");

        LoadPivotTables(workbook, loadedSheetsBySourceName, pendingPivotTables);

        // Rebind cross-sheet chart data ranges to their source sheet now that all sheets exist.
        // TryLoadChart parsed the range against the host sheet; restore the data-source sheet identity.
        foreach (var (chart, dataRangeSheetName) in pendingCrossSheetCharts)
        {
            var dataSheet = ResolveLoadedSheet(workbook, loadedSheetsBySourceName, dataRangeSheetName);
            if (dataSheet is null)
                continue;
            var range = chart.DataRange;
            chart.DataRange = new GridRange(
                range.Start with { Sheet = dataSheet.Id },
                range.End with { Sheet = dataSheet.Id });
        }

        // Rebind cross-sheet linked-picture source ranges the same way (P24): ToPicture parsed
        // LinkedSourceRange against the picture's anchor sheet, so restore the real source sheet now.
        foreach (var (picture, linkedSourceSheetName) in pendingCrossSheetPictures)
        {
            var sourceSheet = ResolveLoadedSheet(workbook, loadedSheetsBySourceName, linkedSourceSheetName);
            if (sourceSheet is null || picture.LinkedSourceRange is not { } linkedRange)
                continue;
            picture.LinkedSourceRange = new GridRange(
                linkedRange.Start with { Sheet = sourceSheet.Id },
                linkedRange.End with { Sheet = sourceSheet.Id });
        }

        var maxLoadedSheetIndex = Math.Max(0, workbook.Sheets.Count - 1);
        workbook.FirstVisibleSheetIndex = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(workbook.FirstVisibleSheetIndex, maxLoadedSheetIndex);
        workbook.ActiveSheetIndex = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(workbook.ActiveSheetIndex, maxLoadedSheetIndex);

        foreach (var namedRangeDto in dto.NamedRanges ?? [])
        {
            if (string.IsNullOrWhiteSpace(namedRangeDto?.Name))
                continue;

            // Sheet-scoped resolution for the defined NAME itself (Excel localSheetId), distinct
            // from SheetName (the sheet a plain range lives on). Absent/blank means workbook scope.
            SheetId? scopeSheetId = null;
            if (!string.IsNullOrWhiteSpace(namedRangeDto.ScopeSheetName))
            {
                var scopeSheet = ResolveLoadedSheet(workbook, loadedSheetsBySourceName, namedRangeDto.ScopeSheetName);
                if (scopeSheet is null)
                    continue;
                scopeSheetId = scopeSheet.Id;
            }

            if (!string.IsNullOrWhiteSpace(namedRangeDto.Formula))
            {
                try
                {
                    if (scopeSheetId is { } formulaScopeSheetId)
                        workbook.DefineNamedFormula(namedRangeDto.Name, namedRangeDto.Formula, formulaScopeSheetId);
                    else
                        workbook.NamedFormulas[namedRangeDto.Name] = namedRangeDto.Formula;
                }
                catch (ArgumentException) { /* skip invalid defined names */ }
                continue;
            }

            if (string.IsNullOrWhiteSpace(namedRangeDto.SheetName) ||
                string.IsNullOrWhiteSpace(namedRangeDto.Range))
            {
                continue;
            }

            var sheet = ResolveLoadedSheet(workbook, loadedSheetsBySourceName, namedRangeDto.SheetName);
            if (sheet is null)
                continue;

            try
            {
                var metadata = new NamedRangeMetadata(
                    string.IsNullOrWhiteSpace(namedRangeDto.Scope) ? "Workbook" : namedRangeDto.Scope.Trim(),
                    namedRangeDto.Comment?.Trim() ?? "");
                var range = GridRange.Parse(namedRangeDto.Range, sheet.Id);
                if (scopeSheetId is { } rangeScopeSheetId)
                    workbook.DefineNamedRange(namedRangeDto.Name, range, metadata, rangeScopeSheetId);
                else
                    workbook.DefineNamedRange(namedRangeDto.Name, range, metadata);
            }
            catch (ArgumentException) { /* skip invalid defined names */ }
            catch (FormatException) { /* skip unparseable named ranges */ }
        }

        foreach (var viewDto in dto.CustomViews ?? [])
        {
            if (string.IsNullOrWhiteSpace(viewDto?.Name)) continue;
            var sheets = (viewDto.Sheets ?? [])
                .Select(sheetDto => ToWorksheetCustomViewState(sheetDto, workbook, loadedSheetsBySourceName))
                .OfType<WorksheetCustomViewState>()
                .ToList();
            if (sheets.Count == 0)
                continue;

            workbook.CustomViews.Add(new WorkbookCustomView(
                viewDto.Name,
                sheets,
                string.IsNullOrWhiteSpace(viewDto.Id) ? null : viewDto.Id,
                viewDto.IncludePrintSettings ?? true,
                viewDto.IncludeHiddenRowsColumnsAndFilterSettings ?? true,
                NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(
                    viewDto.ActiveSheetIndex,
                    Math.Max(0, workbook.Sheets.Count - 1))));
        }

        foreach (var watchDto in dto.WatchedCells ?? [])
        {
            if (string.IsNullOrWhiteSpace(watchDto?.SheetName) ||
                string.IsNullOrWhiteSpace(watchDto.Address))
            {
                continue;
            }

            var sheet = ResolveLoadedSheet(workbook, loadedSheetsBySourceName, watchDto.SheetName);
            if (sheet is null)
                continue;

            try
            {
                workbook.WatchedCells.Add(CellAddress.Parse(watchDto.Address, sheet.Id));
            }
            catch (FormatException) { /* skip watches with unparseable addresses */ }
        }

        foreach (var scenarioDto in dto.Scenarios ?? [])
        {
            if (string.IsNullOrWhiteSpace(scenarioDto?.Name))
                continue;

            var changes = new List<ScenarioCellValue>();
            foreach (var changeDto in scenarioDto.ChangingCells ?? [])
            {
                if (string.IsNullOrWhiteSpace(changeDto?.SheetName) ||
                    string.IsNullOrWhiteSpace(changeDto.Address))
                {
                    continue;
                }

                var sheet = ResolveLoadedSheet(workbook, loadedSheetsBySourceName, changeDto.SheetName);
                if (sheet is null)
                    continue;

                try
                {
                    changes.Add(new ScenarioCellValue(
                        CellAddress.Parse(changeDto.Address, sheet.Id),
                        NativeJsonScalarValueMapper.Deserialize(changeDto.Value, changeDto.ValueType)));
                }
                catch (FormatException) { /* skip scenarios with unparseable addresses */ }
            }

            if (changes.Count > 0)
                workbook.Scenarios.Add(new WorkbookScenario(
                    scenarioDto.Name,
                    changes,
                    string.IsNullOrWhiteSpace(scenarioDto.Comment) ? null : scenarioDto.Comment,
                    scenarioDto.Hidden,
                    scenarioDto.Locked,
                    string.IsNullOrWhiteSpace(scenarioDto.User) ? null : scenarioDto.User));
        }

        return workbook;
    }

    private static bool TryGetCellAddress(CellDto dto, SheetId sheetId, out CellAddress address)
    {
        if (dto.ParsedAddress != 0)
        {
            address = new CellAddress(sheetId, (uint)(dto.ParsedAddress >> 32), (uint)dto.ParsedAddress);
            return true;
        }

        address = default;
        return !string.IsNullOrEmpty(dto.Address) &&
               CellAddress.TryParse(dto.Address, sheetId, out address);
    }

    private static bool TryGetCellAddress(StyleOnlyCellDto dto, SheetId sheetId, out CellAddress address)
    {
        if (dto.ParsedAddress != 0)
        {
            address = new CellAddress(sheetId, (uint)(dto.ParsedAddress >> 32), (uint)dto.ParsedAddress);
            return true;
        }

        address = default;
        return !string.IsNullOrWhiteSpace(dto.Address) &&
               CellAddress.TryParse(dto.Address, sheetId, out address);
    }

    private static FormulaArrayMode ParseFormulaArrayMode(string? value) =>
        Enum.TryParse<FormulaArrayMode>(value, ignoreCase: true, out var parsed) &&
        Enum.IsDefined(parsed)
            ? parsed
            : FormulaArrayMode.Dynamic;

    private static Sheet? ResolveLoadedSheet(
        Workbook workbook,
        IReadOnlyDictionary<string, Sheet> loadedSheetsBySourceName,
        string sheetName) =>
        workbook.GetSheet(sheetName) ??
        (loadedSheetsBySourceName.TryGetValue(sheetName, out var sheet) ? sheet : null);

    private static string UniqueSheetName(Workbook workbook, string? rawName, int index)
    {
        var baseName = string.IsNullOrWhiteSpace(rawName) ? $"Sheet{index}" : rawName;
        baseName = SanitizeSheetName(baseName);
        var candidate = baseName;
        var suffix = 1;
        while (workbook.ValidateSheetName(candidate) is not null)
        {
            var marker = $" ({suffix++})";
            candidate = string.Concat(baseName.AsSpan(0, Math.Min(baseName.Length, 31 - marker.Length)), marker);
        }

        return candidate;
    }

    private static string SanitizeSheetName(string value)
    {
        Span<char> invalid = [':', '\\', '/', '?', '*', '[', ']'];
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(invalid.Contains(ch) ? '_' : ch);

        var sanitized = builder.ToString().Trim('\'');
        if (string.IsNullOrWhiteSpace(sanitized))
            return "Sheet";

        return sanitized.Length <= 31 ? sanitized : sanitized[..31];
    }

    private static string FormatColor(CellColor color) => NativeJsonColorMapper.FormatColor(color);

    private static CellColor? ParseColor(string text) => NativeJsonColorMapper.ParseColor(text);
}

