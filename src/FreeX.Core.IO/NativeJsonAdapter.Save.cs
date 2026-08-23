using System.Text.Json;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = false,
    };

    /// <summary>Exposed for unit tests to verify the static instance is reused.</summary>
    internal static JsonSerializerOptions SaveOptionsForTest => SaveOptions;

    public void Save(Workbook workbook, Stream stream) =>
        Save(workbook, stream, includeCells: true, includeStyleOnlyCells: true, includeCellStyles: true, includeWorksheetFilterState: true);

    internal void SaveForFingerprint(Workbook workbook, Stream stream) =>
        Save(workbook, stream, includeCells: true, includeStyleOnlyCells: false, includeCellStyles: true, includeWorksheetFilterState: true);

    internal void SaveForPatchValidationFingerprint(Workbook workbook, Stream stream) =>
        Save(workbook, stream, includeCells: false, includeStyleOnlyCells: false, includeCellStyles: false, includeWorksheetFilterState: false);

    internal void SaveWorksheetAutoFilterFingerprint(Workbook workbook, Stream stream)
    {
        SaveStreamPreparer.TruncateFromCurrentPosition(stream);
        var filters = workbook.Sheets.Select(sheet => new
        {
            sheet.Name,
            sheet.AutoFilter,
        });
        JsonSerializer.Serialize(stream, filters, SaveOptions);
    }

    /// <summary>
    /// Returns the stored representation of a protection password for the .fxl format.
    /// <see cref="Sheet.ProtectionPassword"/>/<see cref="Workbook.StructureProtectionPassword"/>
    /// almost always already hold a hash by the time a save happens — the command layer
    /// (<c>ProtectSheetCommand</c>/<c>ProtectWorkbookCommand</c>) hashes a freshly-typed password
    /// into a legacy 4-hex-digit verifier immediately, and a workbook loaded from .xlsx carries
    /// its cached "iso29500:..." or legacy-hex hash straight through. Blindly re-hashing an
    /// already-hashed value with a native password hasher would produce
    /// sha256(&lt;hash&gt;) instead of sha256(&lt;plaintext&gt;), which
    /// <see cref="ProtectionPasswordHelper.VerifyStoredPassword"/> can never again verify against
    /// the real typed password — a permanent lockout. So any value already recognizable as one of
    /// the hash forms <c>VerifyStoredPassword</c> understands (iso29500, or legacy 4-hex shape) is
    /// stored verbatim; only a value with neither shape (genuine plaintext) is hashed here.
    /// </summary>
    private static string StoreProtectionPassword(string value) =>
        ProtectionPasswordHelper.IsIso29500Hash(value) || ProtectionPasswordHelper.IsLegacyPasswordHash(value)
            ? value
            : ProtectionPasswordHelper.HashNativePassword(value);

    private static void Save(
        Workbook workbook,
        Stream stream,
        bool includeCells,
        bool includeStyleOnlyCells,
        bool includeCellStyles,
        bool includeWorksheetFilterState)
    {
        SaveStreamPreparer.TruncateFromCurrentPosition(stream);

        var dto = new WorkbookDto
        {
            FileFormat = NativeFileFormat,
            SchemaVersion = CurrentSchemaVersion,
            MinimumReaderVersion = CurrentMinimumReaderVersion,
            Name = workbook.Name,
            Theme = FromWorkbookTheme(workbook.Theme),
            Uses1904DateSystem = workbook.Uses1904DateSystem,
            ShowInkAnnotations = workbook.ShowInkAnnotations,
            HasVbaProjectPackage = workbook.HasVbaProjectPackage,
            ShowSheetTabs = workbook.ShowSheetTabs,
            SheetTabRatio = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(workbook.SheetTabRatio, 1000),
            FirstVisibleSheetIndex = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(workbook.FirstVisibleSheetIndex, Math.Max(0, workbook.Sheets.Count - 1)),
            ActiveSheetIndex = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(workbook.ActiveSheetIndex, Math.Max(0, workbook.Sheets.Count - 1)),
            FileVersion = FromWorkbookFileVersion(workbook.FileVersion),
            CountrySettings = FromWorkbookCountrySettings(workbook.CountrySettings),
            LegacyMenuSettings = FromWorkbookLegacyMenuSettings(workbook.LegacyMenuSettings),
            LegacyWorkbookSettings = FromWorkbookLegacyWorkbookSettings(workbook.LegacyWorkbookSettings),
            FileSharing = FromWorkbookFileSharing(workbook.FileSharing),
            FileRecoveryProperties = workbook.FileRecoveryProperties
                .Select(FromWorkbookFileRecoveryProperties)
                .OfType<WorkbookFileRecoveryPropertiesDto>()
                .ToList(),
            Properties = FromWorkbookProperties(workbook.Properties),
            FunctionGroups = FromWorkbookFunctionGroups(workbook.FunctionGroups),
            SmartTags = FromWorkbookSmartTags(workbook.SmartTags),
            AdditionalViews = FromWorkbookAdditionalViews(workbook.AdditionalViews),
            IsStructureProtected = workbook.IsStructureProtected,
            // Not gated on IsStructureProtected: a password can legitimately be set while only
            // Windows (layout) protection is active and Structure protection is not -- Excel's
            // <workbookProtection lockWindows="1" workbookPassword="..."/> shape (lockStructure
            // absent). XlsxWorkbookMetadataReader/Writer already treat the two independently; this
            // adapter must too, or a round trip through the native .fxl format (autosave, crash
            // recovery, Save As to .fxl) silently strips the password while leaving Windows
            // protection nominally active -- a security downgrade, not just data loss.
            StructureProtectionPassword = workbook.StructureProtectionPassword is { } swp
                ? StoreProtectionPassword(swp)
                : null,
            ProtectionMetadata = FromWorkbookProtectionMetadata(workbook.ProtectionMetadata),
            WindowArrangement = NativeJsonValueSanitizer.ValidEnumOrDefault(workbook.WindowArrangement, WorkbookWindowArrangement.Tiled),
            DisabledFormulaErrorCodes = workbook.DisabledFormulaErrorCodes
                .Where(IsSupportedFormulaErrorCode)
                .OrderBy(code => code)
                .ToList(),
            NamedRanges = ToNamedRangeDtos(workbook),
            CustomViews = workbook.CustomViews
                .OfType<WorkbookCustomView>()
                .Select(view => new CustomViewDto
                {
                    Name = view.Name,
                    Id = view.Id,
                    IncludePrintSettings = view.IncludePrintSettings,
                    IncludeHiddenRowsColumnsAndFilterSettings = view.IncludeHiddenRowsColumnsAndFilterSettings,
                    ActiveSheetIndex = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(
                        view.ActiveSheetIndex,
                        Math.Max(0, workbook.Sheets.Count - 1)),
                    Sheets = (view.Sheets ?? [])
                        .OfType<WorksheetCustomViewState>()
                        .Select(ToCustomViewSheetDto)
                        .ToList()
                }).ToList(),
            WatchedCells = ToWatchedCellDtos(workbook),
            Scenarios = ToScenarioDtos(workbook),
            PivotCaches = workbook.PivotCaches
                .OfType<PivotCacheModel>()
                .Where(cache => cache.CacheId > 0)
                .Select(ToPivotCacheDto)
                .ToList(),
            Slicers = ToSlicerDtos(workbook),
            Timelines = ToTimelineDtos(workbook),
            CellStyles = includeCellStyles ? ToCellStyleTable(workbook) : null,
            DefaultStyle = ToCustomizedDefaultStyleDto(workbook),
            Sheets = workbook.Sheets.Select(s => new SheetDto
            {
                Name = s.Name,
                Kind = NativeJsonValueSanitizer.ValidEnumOrDefault(s.Kind, SheetKind.Worksheet),
                IsHidden = s.IsHidden,
                IsVeryHidden = s.IsVeryHidden,
                TabColor = s.TabColor is { } color ? FormatColor(color) : null,
                IsProtected = s.IsProtected,
                ProtectionPassword = s.IsProtected && s.ProtectionPassword is { } shp
                    ? StoreProtectionPassword(shp)
                    : null,
                ProtectionPermissions = s.ProtectionPermissions
                    .Where(Enum.IsDefined)
                    .Distinct()
                    .ToList(),
                ProtectionMetadata = FromWorksheetProtectionMetadata(s.ProtectionMetadata),
                CustomProperties = s.CustomProperties
                    .OfType<WorksheetCustomProperty>()
                    .Where(property => !string.IsNullOrWhiteSpace(property.Name) && property.Id > 0)
                    .Select(property => new WorksheetCustomPropertyDto
                    {
                        Name = property.Name,
                        Id = property.Id,
                        Metadata = FromWorksheetCustomPropertyMetadata(property.Metadata)
                    })
                    .ToList(),
                RowHeights = s.RowHeights
                    .Where(pair => NativeJsonValueSanitizer.IsValidRowIndex(pair.Key) && NativeJsonValueSanitizer.IsPositiveFinite(pair.Value))
                    .Select(pair => new UIntDoubleDto { Index = pair.Key, Value = pair.Value })
                    .ToList(),
                ColumnWidths = s.ColumnWidths
                    .Where(pair => NativeJsonValueSanitizer.IsValidColumnIndex(pair.Key) && NativeJsonValueSanitizer.IsPositiveFinite(pair.Value))
                    .Select(pair => new UIntDoubleDto { Index = pair.Key, Value = pair.Value })
                    .ToList(),
                HiddenRows = s.HiddenRows.Where(NativeJsonValueSanitizer.IsValidRowIndex).OrderBy(row => row).ToList(),
                FilterHiddenRows = includeWorksheetFilterState
                    ? s.FilterHiddenRows.Where(NativeJsonValueSanitizer.IsValidRowIndex).OrderBy(row => row).ToList()
                    : [],
                // G32: persist alongside FilterHiddenRows so a reload can reconstruct the same
                // AND-across-columns picture (see FreeX.Core.Commands.FilterCommand, findings F8/G7).
                ActiveValueFilterColumns = (includeWorksheetFilterState
                    ? s.ActiveValueFilterColumns
                    : [])
                    .Where(pair => NativeJsonValueSanitizer.IsValidColumnIndex(pair.Key))
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new UIntStringListDto { Index = pair.Key, Values = [.. pair.Value] })
                    .ToList(),
                ValueFilterHiddenRows = includeWorksheetFilterState
                    ? s.ValueFilterHiddenRows.Where(NativeJsonValueSanitizer.IsValidRowIndex).OrderBy(row => row).ToList()
                    : [],
                // R14-meta-1: persist alongside the siblings above so a reload can reconstruct which
                // rows each column-owned filter mechanism owns (see FilterCommand.cs, finding R14-meta-1).
                ColumnFilterOwnedRows = (includeWorksheetFilterState
                    ? s.ColumnFilterOwnedRows
                    : [])
                    .Where(pair => NativeJsonValueSanitizer.IsValidColumnIndex(pair.Key))
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new UIntUintListDto
                    {
                        Index = pair.Key,
                        Values = [.. pair.Value.Where(NativeJsonValueSanitizer.IsValidRowIndex).OrderBy(row => row)],
                    })
                    .ToList(),
                HiddenCols = s.HiddenCols.Where(NativeJsonValueSanitizer.IsValidColumnIndex).OrderBy(column => column).ToList(),
                RowOutlineLevels = s.RowOutlineLevels
                    .Where(pair => NativeJsonValueSanitizer.IsValidRowIndex(pair.Key) && NativeJsonValueSanitizer.IsValidOutlineLevel(pair.Value))
                    .Select(pair => new UIntIntDto { Index = pair.Key, Value = pair.Value })
                    .ToList(),
                ColOutlineLevels = s.ColOutlineLevels
                    .Where(pair => NativeJsonValueSanitizer.IsValidColumnIndex(pair.Key) && NativeJsonValueSanitizer.IsValidOutlineLevel(pair.Value))
                    .Select(pair => new UIntIntDto { Index = pair.Key, Value = pair.Value })
                    .ToList(),
                OutlineSummaryBelow = s.OutlineSummaryBelow,
                OutlineSummaryRight = s.OutlineSummaryRight,
                ShowOutlineSymbols = s.ShowOutlineSymbols,
                ApplyOutlineStyles = s.ApplyOutlineStyles,
                SheetFormatMetadata = FromWorksheetSheetFormatMetadata(s.SheetFormatMetadata),
                DimensionMetadata = FromWorksheetDimensionMetadata(s.DimensionMetadata),
                SheetPropertiesMetadata = FromWorksheetSheetPropertiesMetadata(s.SheetPropertiesMetadata),
                GroupHiddenRows = s.GroupHiddenRows.Where(NativeJsonValueSanitizer.IsValidRowIndex).OrderBy(row => row).ToList(),
                GroupHiddenCols = s.GroupHiddenCols.Where(NativeJsonValueSanitizer.IsValidColumnIndex).OrderBy(column => column).ToList(),
                CollapsedAnchorRows = s.CollapsedAnchorRows.Where(NativeJsonValueSanitizer.IsValidRowIndex).OrderBy(row => row).ToList(),
                CollapsedAnchorCols = s.CollapsedAnchorCols.Where(NativeJsonValueSanitizer.IsValidColumnIndex).OrderBy(column => column).ToList(),
                ViewMode = NativeJsonValueSanitizer.ValidEnumOrDefault(s.ViewMode, WorksheetViewMode.Normal),
                ShowGridlines = s.ShowGridlines,
                ShowHeadings = s.ShowHeadings,
                ShowRulers = s.ShowRulers,
                ZoomPercent = NativeJsonValueSanitizer.ValidZoomPercentOrDefault(s.ZoomPercent),
                ShowFormulas = s.ShowFormulas,
                IsRightToLeft = s.IsRightToLeft,
                ShowZeros = s.ShowZeros,
                FullCalculationOnLoad = s.FullCalculationOnLoad,
                PhoneticProperties = ToWorksheetPhoneticPropertiesDto(s.PhoneticProperties),
                FrozenRows = NativeJsonValueSanitizer.ValidFrozenRowsOrZero(s.FrozenRows),
                FrozenCols = NativeJsonValueSanitizer.ValidFrozenColumnsOrZero(s.FrozenCols),
                ViewTopRow = NativeJsonValueSanitizer.ValidRowPaneOrNull(s.ViewTopRow),
                ViewLeftCol = NativeJsonValueSanitizer.ValidColumnPaneOrNull(s.ViewLeftCol),
                ActiveRow = NativeJsonValueSanitizer.ValidRowPaneOrNull(s.ActiveRow),
                ActiveCol = NativeJsonValueSanitizer.ValidColumnPaneOrNull(s.ActiveCol),
                SplitRow = NativeJsonValueSanitizer.ValidFrozenRowsOrZero(s.FrozenRows) > 0 || NativeJsonValueSanitizer.ValidFrozenColumnsOrZero(s.FrozenCols) > 0
                    ? null
                    : NativeJsonValueSanitizer.ValidRowPaneOrNull(s.SplitRow),
                SplitColumn = NativeJsonValueSanitizer.ValidFrozenRowsOrZero(s.FrozenRows) > 0 || NativeJsonValueSanitizer.ValidFrozenColumnsOrZero(s.FrozenCols) > 0
                    ? null
                    : NativeJsonValueSanitizer.ValidColumnPaneOrNull(s.SplitColumn),
                AutoFilter = includeWorksheetFilterState
                    ? ToWorksheetAutoFilterDto(s.AutoFilter, s.Id)
                    : null,
                SmartTags = ToWorksheetSmartTagsDto(s.SmartTags),
                DataConsolidation = ToWorksheetDataConsolidationDto(s.DataConsolidation),
                SortState = ToWorksheetSortStateDto(s.SortState, s.Id),
                SingleXmlCells = ToWorksheetSingleXmlCellsDto(s.SingleXmlCells),
                CellWatchesMetadata = ToWorksheetCellWatchesMetadataDto(s.CellWatchesMetadata),
                IgnoredErrorsMetadata = ToWorksheetIgnoredErrorsMetadataDto(s.IgnoredErrorsMetadata),
                AdditionalViews = ToWorksheetAdditionalViewsDto(s.AdditionalViews),
                PrimaryViewMetadata = FromWorksheetPrimaryViewMetadata(s.PrimaryViewMetadata),
                // Persist as multi-area array (new) plus legacy single-area field for back-compat.
                PrintAreas = s.PrintAreas.Count > 0 ? s.PrintAreas.Select(r => r.ToString()).ToArray() : null,
                PrintArea = s.PrintArea?.ToString(),
                PageOrientation = NativeJsonValueSanitizer.ValidEnumOrDefault(s.PageOrientation, WorksheetPageOrientation.Portrait),
                PaperSize = NativeJsonValueSanitizer.ValidEnumOrDefault(s.PaperSize, WorksheetPaperSize.A4),
                // Only persist PaperSizeCode for exotic/unknown codes not in the standard map.
                // Known sizes (Letter/A4/Legal/A3/etc.) are reconstructed from PaperSize on load.
                PaperSizeCode = (s.PaperSizeCode > 0
                                 && !PaperSizeCodes.TryGetEnum(s.PaperSizeCode, out _))
                    ? s.PaperSizeCode
                    : null,
                PageMargins = FromPageMargins(NativeJsonValueSanitizer.ValidPageMarginsOrDefault(s.PageMargins, WorksheetPageMargins.Narrow)),
                HeaderMargin = NativeJsonValueSanitizer.NonNegativeFiniteOrDefault(s.HeaderMargin, 0.3),
                FooterMargin = NativeJsonValueSanitizer.NonNegativeFiniteOrDefault(s.FooterMargin, 0.3),
                PageMarginsMetadata = FromWorksheetPageMarginsMetadata(s.PageMarginsMetadata),
                PrintGridlines = s.PrintGridlines,
                PrintHeadings = s.PrintHeadings,
                PrintOptionsMetadata = FromWorksheetPrintOptionsMetadata(s.PrintOptionsMetadata),
                PrintTitleRows = FromValidRepeatRange(s.PrintTitleRows, CellAddress.MaxRow),
                PrintTitleColumns = FromValidRepeatRange(s.PrintTitleColumns, CellAddress.MaxCol),
                PageHeader = FromHeaderFooter(s.PageHeader),
                PageFooter = FromHeaderFooter(s.PageFooter),
                FirstPageHeader = FromHeaderFooter(s.FirstPageHeader),
                FirstPageFooter = FromHeaderFooter(s.FirstPageFooter),
                EvenPageHeader = FromHeaderFooter(s.EvenPageHeader),
                EvenPageFooter = FromHeaderFooter(s.EvenPageFooter),
                PageHeaderPictures = FromHeaderFooterPictures(s.PageHeaderPictures),
                PageFooterPictures = FromHeaderFooterPictures(s.PageFooterPictures),
                FirstPageHeaderPictures = FromHeaderFooterPictures(s.FirstPageHeaderPictures),
                FirstPageFooterPictures = FromHeaderFooterPictures(s.FirstPageFooterPictures),
                EvenPageHeaderPictures = FromHeaderFooterPictures(s.EvenPageHeaderPictures),
                EvenPageFooterPictures = FromHeaderFooterPictures(s.EvenPageFooterPictures),
                DifferentFirstPageHeaderFooter = s.DifferentFirstPageHeaderFooter,
                DifferentOddEvenHeaderFooter = s.DifferentOddEvenHeaderFooter,
                HeaderFooterScaleWithDocument = s.HeaderFooterScaleWithDocument,
                HeaderFooterAlignWithMargins = s.HeaderFooterAlignWithMargins,
                HeaderFooterMetadata = FromWorksheetHeaderFooterMetadata(s.HeaderFooterMetadata),
                CenterHorizontallyOnPage = s.CenterHorizontallyOnPage,
                CenterVerticallyOnPage = s.CenterVerticallyOnPage,
                PageOrder = NativeJsonValueSanitizer.ValidEnumOrDefault(s.PageOrder, WorksheetPageOrder.DownThenOver),
                FirstPageNumber = s.FirstPageNumber is > 0 ? s.FirstPageNumber : null,
                UsePrinterDefaults = s.UsePrinterDefaults,
                PrintCopies = s.PrintCopies is > 0 ? s.PrintCopies : null,
                PrintBlackAndWhite = s.PrintBlackAndWhite,
                PrintDraftQuality = s.PrintDraftQuality,
                PrintQualityDpi = s.PrintQualityDpi is > 0 ? s.PrintQualityDpi : null,
                PrintQualityVerticalDpi = s.PrintQualityVerticalDpi is > 0 ? s.PrintQualityVerticalDpi : null,
                PrintErrorValue = NativeJsonValueSanitizer.ValidEnumOrDefault(s.PrintErrorValue, WorksheetPrintErrorValue.Displayed),
                PrintComments = NativeJsonValueSanitizer.ValidEnumOrDefault(s.PrintComments, WorksheetPrintComments.None),
                LegacyPrintSize = s.LegacyPrintSize is > 0 and <= ushort.MaxValue ? s.LegacyPrintSize : null,
                PageSetupMetadata = FromWorksheetPageSetupMetadata(s.PageSetupMetadata),
                ScaleToFit = new ScaleToFitDto
                {
                    ScalePercent = NativeJsonValueSanitizer.ValidScaleToFitOrDefault(s.ScaleToFit, WorksheetScaleToFit.Default).ScalePercent,
                    FitToPagesWide = NativeJsonValueSanitizer.ValidScaleToFitOrDefault(s.ScaleToFit, WorksheetScaleToFit.Default).FitToPagesWide,
                    FitToPagesTall = NativeJsonValueSanitizer.ValidScaleToFitOrDefault(s.ScaleToFit, WorksheetScaleToFit.Default).FitToPagesTall
                },
                FitToPage = s.FitToPage,
                AutoPageBreaks = s.AutoPageBreaks,
                RowPageBreaks = s.RowPageBreaks.Where(rowBreak => rowBreak is >= 2 and <= CellAddress.MaxRow).ToList(),
                RowPageBreaksMetadata = FromWorksheetPageBreaksMetadata(s.RowPageBreaksMetadata),
                ColumnPageBreaks = s.ColumnPageBreaks.Where(columnBreak => columnBreak is >= 2 and <= CellAddress.MaxCol).ToList(),
                ColumnPageBreaksMetadata = FromWorksheetPageBreaksMetadata(s.ColumnPageBreaksMetadata),
                MergedRegions = s.MergedRegions
                    .Where(range => IsValidRangeOnSheet(range, s.Id))
                    .Select(range => range.ToString())
                    .ToList(),
                Comments = s.Comments
                    .Where(pair => IsValidAddressOnSheet(pair.Key, s.Id) && pair.Value is not null)
                    .Select(pair => ToCommentDto(s, pair))
                    .ToList(),
                ThreadedComments = s.ThreadedComments
                    .Where(pair => IsValidAddressOnSheet(pair.Key, s.Id) && pair.Value is not null)
                    .Select(ToThreadedCommentDto)
                    .ToList(),
                Hyperlinks = s.Hyperlinks
                    .Where(pair => IsValidAddressOnSheet(pair.Key, s.Id) && pair.Value is not null)
                    .Select(pair => ToHyperlinkDto(s, pair))
                    .ToList(),
                RichTextRuns = ToRichTextRunDtos(s),
                CellPhoneticGuides = ToPhoneticGuideDtos(s),
                AllowEditRanges = s.AllowEditRanges
                    .Where(range => IsValidRangeOnSheet(range, s.Id))
                    .Select(range => range.ToString())
                    .ToList(),
                AllowEditRangePasswords = s.AllowEditRanges
                    .Where(range => IsValidRangeOnSheet(range, s.Id))
                    .Where(range => !string.IsNullOrEmpty(s.AllowEditRangePasswords.GetValueOrDefault(range)))
                    .Select(range => new AllowEditRangePasswordDto
                    {
                        Range = range.ToString(),
                        Password = s.AllowEditRangePasswords[range]
                    })
                    .ToList(),
                BackgroundImage = ToWorksheetBackgroundDto(s.BackgroundImage),
                Pictures = s.Pictures
                    .OfType<PictureModel>()
                    .Where(picture => NativeJsonVisualDtoMapper.IsPictureOnSheet(picture, s.Id))
                    .Select(NativeJsonVisualDtoMapper.FromPicture)
                    .ToList(),
                TextBoxes = s.TextBoxes
                    .OfType<TextBoxModel>()
                    .Where(textBox => NativeJsonVisualDtoMapper.IsTextBoxOnSheet(textBox, s.Id))
                    .Select(NativeJsonVisualDtoMapper.FromTextBox)
                    .ToList(),
                DrawingShapes = s.DrawingShapes
                    .OfType<DrawingShapeModel>()
                    .Where(shape => NativeJsonVisualDtoMapper.IsDrawingShapeOnSheet(shape, s.Id))
                    .Select(NativeJsonVisualDtoMapper.FromDrawingShape)
                    .ToList(),
                FormControls = s.FormControls
                    .OfType<FormControlModel>()
                    .Where(control => IsFormControlOnSheet(control, s.Id))
                    .Select(ToFormControlDto)
                    .ToList(),
                DrawingObjectZOrder = ToDrawingObjectZOrderDtos(s),
                Sparklines = s.Sparklines
                    .OfType<SparklineModel>()
                    .Where(sparkline => IsSparklineOnSheet(sparkline, s.Id) && Enum.IsDefined(sparkline.Kind))
                    .Select(ToSparklineDto)
                    .ToList(),
                Charts = s.Charts
                    .OfType<ChartModel>()
                    .Select(chart => ToChartDto(workbook, s.Id, chart))
                    .ToList(),
                PivotTables = s.PivotTables
                    .OfType<PivotTableModel>()
                    .Select(pivot => ToPivotTableDto(workbook, s, pivot))
                    .OfType<PivotTableDto>()
                    .ToList(),
                DataValidations = s.DataValidations
                    .OfType<DataValidation>()
                    .Where(validation => IsDataValidationOnSheet(validation, s.Id) && IsSupportedDataValidation(validation))
                    .Select(validation => ToDataValidationDto(validation, s.Id))
                    .ToList(),
                ConditionalFormats = ToConditionalFormatDtos(s.ConditionalFormats, s.Id),
                Cells = includeCells ? new CellDtoSequence(s) : CellDtoSequence.Empty,
                StyleOnlyCells = includeStyleOnlyCells ? new StyleOnlyCellDtoSequence(s) : StyleOnlyCellDtoSequence.Empty
            }).ToList()
        };

        PopulateCalculationOptions(workbook, dto);

        JsonSerializer.Serialize(stream, dto, SaveOptions);
    }

    private static List<NamedRangeDto> ToNamedRangeDtos(Workbook workbook)
    {
        var result = new List<NamedRangeDto>();

        // Workbook-scoped plain named ranges.
        foreach (var pair in workbook.NamedRanges)
        {
            var sheet = workbook.GetSheet(pair.Value.Start.Sheet);
            if (sheet is null || !IsValidRangeOnSheet(pair.Value, sheet.Id))
                continue;

            var metadata = workbook.TryGetNamedRangeMetadata(pair.Key, out var savedMetadata)
                ? savedMetadata
                : NamedRangeMetadata.WorkbookScope;
            result.Add(new NamedRangeDto
            {
                Name = pair.Key,
                SheetName = sheet.Name,
                Range = pair.Value.ToString(),
                Scope = metadata.Scope,
                Comment = metadata.Comment
            });
        }

        // Sheet-scoped plain named ranges.
        foreach (var (key, range) in workbook.ScopedNamedRanges)
        {
            var scopeSheet = workbook.GetSheet(key.Sheet);
            var rangeSheet = workbook.GetSheet(range.Start.Sheet);
            if (scopeSheet is null || rangeSheet is null || !IsValidRangeOnSheet(range, rangeSheet.Id))
                continue;

            var metadata = workbook.TryGetScopedNamedRangeMetadata(key.Name, key.Sheet, out var savedMetadata)
                ? savedMetadata
                : NamedRangeMetadata.WorkbookScope;
            result.Add(new NamedRangeDto
            {
                Name = key.Name,
                SheetName = rangeSheet.Name,
                Range = range.ToString(),
                ScopeSheetName = scopeSheet.Name,
                Scope = metadata.Scope,
                Comment = metadata.Comment
            });
        }

        // Workbook-scoped named formulas.
        foreach (var (name, formulaText) in workbook.NamedFormulas)
        {
            if (string.IsNullOrWhiteSpace(formulaText))
                continue;

            result.Add(new NamedRangeDto
            {
                Name = name,
                Formula = formulaText
            });
        }

        // Sheet-scoped named formulas.
        foreach (var (key, formulaText) in workbook.ScopedNamedFormulas)
        {
            if (string.IsNullOrWhiteSpace(formulaText))
                continue;

            var scopeSheet = workbook.GetSheet(key.Sheet);
            if (scopeSheet is null)
                continue;

            result.Add(new NamedRangeDto
            {
                Name = key.Name,
                Formula = formulaText,
                ScopeSheetName = scopeSheet.Name
            });
        }

        return result;
    }

    private static List<WatchedCellDto> ToWatchedCellDtos(Workbook workbook)
    {
        if (workbook.WatchedCells.Count == 0)
            return [];

        var watchedCells = new List<WatchedCellDto>(workbook.WatchedCells.Count);
        foreach (var address in workbook.WatchedCells)
        {
            var sheet = workbook.GetSheet(address.Sheet);
            if (sheet is null || !IsValidAddressOnSheet(address, sheet.Id))
                continue;

            watchedCells.Add(new WatchedCellDto
            {
                SheetName = sheet.Name,
                Address = address.ToA1()
            });
        }

        return watchedCells;
    }

    private static List<ScenarioDto> ToScenarioDtos(Workbook workbook)
    {
        if (workbook.Scenarios.Count == 0)
            return [];

        var scenarios = new List<ScenarioDto>(workbook.Scenarios.Count);
        foreach (var scenario in workbook.Scenarios)
        {
            if (scenario is null)
                continue;

            IReadOnlyList<ScenarioCellValue> changes = scenario.ChangingCells ?? [];
            if (changes.Count == 0)
                continue;

            var changingCells = new List<ScenarioCellDto>(changes.Count);
            foreach (var change in changes)
            {
                if (change is null)
                    continue;

                var sheet = workbook.GetSheet(change.Address.Sheet);
                if (sheet is null || !IsValidAddressOnSheet(change.Address, sheet.Id))
                    continue;

                var serializedValue = NativeJsonScalarValueMapper.SerializeWithType(change.Value);
                changingCells.Add(new ScenarioCellDto
                {
                    SheetName = sheet.Name,
                    Address = change.Address.ToA1(),
                    Value = serializedValue.Value,
                    ValueType = serializedValue.ValueType
                });
            }

            if (changingCells.Count == 0)
                continue;

            scenarios.Add(new ScenarioDto
            {
                Name = scenario.Name,
                Comment = string.IsNullOrWhiteSpace(scenario.Comment) ? null : scenario.Comment,
                Hidden = scenario.Hidden,
                Locked = scenario.Locked,
                User = string.IsNullOrWhiteSpace(scenario.User) ? null : scenario.User,
                ChangingCells = changingCells
            });
        }

        return scenarios;
    }

    private static void WriteCellDtos(Utf8JsonWriter writer, Sheet sheet, JsonSerializerOptions options)
    {
        var cells = sheet.GetOccupiedCellMap();
        if (cells.Count == 0)
            return;

        foreach (var entry in cells)
        {
            var (row, col) = entry.Key;
            if (!NativeJsonValueSanitizer.IsValidRowIndex(row) ||
                !NativeJsonValueSanitizer.IsValidColumnIndex(col))
                continue;

            var cell = entry.Value;
            CellDtoJsonConverter.WriteCell(
                writer,
                cell.Value,
                cell.HasFormula ? NormalizeNativeFormulaText(cell.FormulaText!) : null,
                cell.HasFormula ? cell.ArrayMode : FormulaArrayMode.Dynamic,
                cell.IgnoreFormulaError,
                GetNativeStyleId(cell.StyleId),
                style: null,
                options,
                row,
                col);
        }
    }

    private static void WriteStyleOnlyCellDtos(Utf8JsonWriter writer, Sheet sheet, JsonSerializerOptions options)
    {
        if (!sheet.HasStyleOnlyCells)
            return;

        var dto = new StyleOnlyCellDto();
        foreach (var entry in sheet.GetStyleOnlyEntries())
        {
            var (row, col) = entry.Key;
            if (!NativeJsonValueSanitizer.IsValidRowIndex(row) ||
                !NativeJsonValueSanitizer.IsValidColumnIndex(col))
                continue;

            dto.StyleId = GetNativeStyleId(entry.StyleId, includeDefault: true);
            dto.Style = null;
            StyleOnlyCellDtoJsonConverter.WriteCell(writer, dto, options, row, col);
        }
    }

    /// <summary>
    /// Captures the workbook's customized default style (style 0) when it differs from the built-in
    /// <see cref="CellStyle.Default"/>, so it can be restored into slot 0 on load. Returns null when
    /// the workbook uses the hard-coded default (nothing to persist).
    /// </summary>
    private static CellStyleDto? ToCustomizedDefaultStyleDto(Workbook workbook)
    {
        var defaultStyle = workbook.GetStyle(StyleId.Default);
        if (defaultStyle.Equals(CellStyle.Default))
            return null;

        return FromCellStyle(defaultStyle);
    }

    private static List<CellStyleDto>? ToCellStyleTable(Workbook workbook)
    {
        if (workbook.StyleCount <= 1)
            return null;

        var styleDtoCache = new Dictionary<StyleId, CellStyleDto?>(workbook.StyleCount);
        var styles = new List<CellStyleDto>(workbook.StyleCount);
        for (var i = 0; i < workbook.StyleCount; i++)
            styles.Add(GetCachedCellStyleDto(workbook, styleDtoCache, new StyleId(i), includeDefault: true)!);

        return styles;
    }

    private static CellStyleDto? GetCachedCellStyleDto(
        Workbook workbook,
        Dictionary<StyleId, CellStyleDto?> styleDtoCache,
        StyleId styleId,
        bool includeDefault = false)
    {
        if (styleId == StyleId.Default && !includeDefault)
            return null;

        if (!styleDtoCache.TryGetValue(styleId, out var dto))
        {
            dto = FromCellStyle(workbook.GetStyle(styleId));
            styleDtoCache[styleId] = dto;
        }

        return dto;
    }

    private static int? GetNativeStyleId(StyleId styleId, bool includeDefault = false) =>
        styleId == StyleId.Default && !includeDefault ? null : Math.Max(0, styleId.Value);

    private static bool IsValidAddressOnSheet(CellAddress address, SheetId sheetId) =>
        address.Sheet == sheetId &&
        NativeJsonValueSanitizer.IsValidRowIndex(address.Row) &&
        NativeJsonValueSanitizer.IsValidColumnIndex(address.Col);

    private static bool IsValidRangeOnSheet(GridRange range, SheetId sheetId) =>
        IsValidAddressOnSheet(range.Start, sheetId) &&
        IsValidAddressOnSheet(range.End, sheetId);

    private static string NormalizeNativeFormulaText(string formulaText) =>
        formulaText.StartsWith("=", StringComparison.Ordinal) ? formulaText[1..] : formulaText;
}
