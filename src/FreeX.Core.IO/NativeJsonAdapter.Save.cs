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

    public void Save(Workbook workbook, Stream stream)
    {
        var dto = new WorkbookDto
        {
            FileFormat = NativeFileFormat,
            SchemaVersion = CurrentSchemaVersion,
            MinimumReaderVersion = CurrentMinimumReaderVersion,
            Name = workbook.Name,
            Theme = FromWorkbookTheme(workbook.Theme),
            Uses1904DateSystem = workbook.Uses1904DateSystem,
            ShowSheetTabs = workbook.ShowSheetTabs,
            SheetTabRatio = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(workbook.SheetTabRatio, 1000),
            FirstVisibleSheetIndex = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(workbook.FirstVisibleSheetIndex, Math.Max(0, workbook.Sheets.Count - 1)),
            ActiveSheetIndex = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(workbook.ActiveSheetIndex, Math.Max(0, workbook.Sheets.Count - 1)),
            FileVersion = FromWorkbookFileVersion(workbook.FileVersion),
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
            StructureProtectionPassword = workbook.IsStructureProtected && workbook.StructureProtectionPassword is { } swp
                ? NativePasswordHelper.HashPassword(swp)
                : null,
            ProtectionMetadata = FromWorkbookProtectionMetadata(workbook.ProtectionMetadata),
            WindowArrangement = NativeJsonValueSanitizer.ValidEnumOrDefault(workbook.WindowArrangement, WorkbookWindowArrangement.Tiled),
            DisabledFormulaErrorCodes = workbook.DisabledFormulaErrorCodes
                .Where(IsSupportedFormulaErrorCode)
                .OrderBy(code => code)
                .ToList(),
            NamedRanges = workbook.NamedRanges
                .Select(pair =>
                {
                    var sheet = workbook.GetSheet(pair.Value.Start.Sheet);
                    var metadata = workbook.TryGetNamedRangeMetadata(pair.Key, out var savedMetadata)
                        ? savedMetadata
                        : NamedRangeMetadata.WorkbookScope;
                    return sheet is null || pair.Value.End.Sheet != sheet.Id
                        ? null
                        : new NamedRangeDto
                        {
                            Name = pair.Key,
                            SheetName = sheet.Name,
                            Range = pair.Value.ToString(),
                            Scope = metadata.Scope,
                            Comment = metadata.Comment
                        };
                })
                .OfType<NamedRangeDto>()
                .ToList(),
            CustomViews = workbook.CustomViews.Select(view => new CustomViewDto
            {
                Name = view.Name,
                Id = view.Id,
                IncludePrintSettings = view.IncludePrintSettings,
                IncludeHiddenRowsColumnsAndFilterSettings = view.IncludeHiddenRowsColumnsAndFilterSettings,
                Sheets = view.Sheets.Select(ToCustomViewSheetDto).ToList()
            }).ToList(),
            WatchedCells = workbook.WatchedCells.Select(address =>
            {
                var sheet = workbook.Sheets.FirstOrDefault(s => s.Id.Equals(address.Sheet));
                return sheet is null || !IsValidAddressOnSheet(address, sheet.Id)
                    ? null
                    : new WatchedCellDto { SheetName = sheet.Name, Address = address.ToA1() };
            }).OfType<WatchedCellDto>().ToList(),
            Scenarios = workbook.Scenarios.Select(scenario => new ScenarioDto
            {
                Name = scenario.Name,
                Comment = string.IsNullOrWhiteSpace(scenario.Comment) ? null : scenario.Comment,
                Hidden = scenario.Hidden,
                Locked = scenario.Locked,
                User = string.IsNullOrWhiteSpace(scenario.User) ? null : scenario.User,
                ChangingCells = scenario.ChangingCells.Select(change =>
                {
                    var sheet = workbook.Sheets.FirstOrDefault(s => s.Id.Equals(change.Address.Sheet));
                    return sheet is null || !IsValidAddressOnSheet(change.Address, sheet.Id)
                        ? null
                        : new ScenarioCellDto
                        {
                            SheetName = sheet.Name,
                            Address = change.Address.ToA1(),
                            Value = NativeJsonScalarValueMapper.Serialize(change.Value),
                            ValueType = NativeJsonScalarValueMapper.GetValueType(change.Value)
                        };
                }).OfType<ScenarioCellDto>().ToList()
            }).Where(scenario => scenario.ChangingCells.Count > 0).ToList(),
            Sheets = workbook.Sheets.Select(s => new SheetDto
            {
                Name = s.Name,
                IsHidden = s.IsHidden,
                TabColor = s.TabColor is { } color ? FormatColor(color) : null,
                IsProtected = s.IsProtected,
                ProtectionPassword = s.IsProtected && s.ProtectionPassword is { } shp
                    ? NativePasswordHelper.HashPassword(shp)
                    : null,
                ProtectionPermissions = s.ProtectionPermissions
                    .Where(Enum.IsDefined)
                    .Distinct()
                    .ToList(),
                ProtectionMetadata = FromWorksheetProtectionMetadata(s.ProtectionMetadata),
                CustomProperties = s.CustomProperties
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
                FilterHiddenRows = s.FilterHiddenRows.Where(NativeJsonValueSanitizer.IsValidRowIndex).OrderBy(row => row).ToList(),
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
                ViewMode = NativeJsonValueSanitizer.ValidEnumOrDefault(s.ViewMode, WorksheetViewMode.Normal),
                ShowGridlines = s.ShowGridlines,
                ShowHeadings = s.ShowHeadings,
                ShowRulers = s.ShowRulers,
                ZoomPercent = NativeJsonValueSanitizer.ValidZoomPercentOrDefault(s.ZoomPercent),
                ShowFormulas = s.ShowFormulas,
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
                AutoFilter = ToWorksheetAutoFilterDto(s.AutoFilter),
                SmartTags = ToWorksheetSmartTagsDto(s.SmartTags),
                DataConsolidation = ToWorksheetDataConsolidationDto(s.DataConsolidation),
                SortState = ToWorksheetSortStateDto(s.SortState),
                SingleXmlCells = ToWorksheetSingleXmlCellsDto(s.SingleXmlCells),
                CellWatchesMetadata = ToWorksheetCellWatchesMetadataDto(s.CellWatchesMetadata),
                IgnoredErrorsMetadata = ToWorksheetIgnoredErrorsMetadataDto(s.IgnoredErrorsMetadata),
                AdditionalViews = ToWorksheetAdditionalViewsDto(s.AdditionalViews),
                PrimaryViewMetadata = FromWorksheetPrimaryViewMetadata(s.PrimaryViewMetadata),
                PrintArea = s.PrintArea?.ToString(),
                PageOrientation = NativeJsonValueSanitizer.ValidEnumOrDefault(s.PageOrientation, WorksheetPageOrientation.Portrait),
                PaperSize = NativeJsonValueSanitizer.ValidEnumOrDefault(s.PaperSize, WorksheetPaperSize.A4),
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
                    .Where(range => range.Start.Sheet == s.Id && range.End.Sheet == s.Id)
                    .Select(range => range.ToString())
                    .ToList(),
                Comments = s.Comments
                    .Where(pair => IsValidAddressOnSheet(pair.Key, s.Id) && pair.Value is not null)
                    .Select(ToCommentDto)
                    .ToList(),
                ThreadedComments = s.ThreadedComments
                    .Where(pair => IsValidAddressOnSheet(pair.Key, s.Id) && pair.Value is not null)
                    .Select(ToThreadedCommentDto)
                    .ToList(),
                Hyperlinks = s.Hyperlinks
                    .Where(pair => IsValidAddressOnSheet(pair.Key, s.Id) && pair.Value is not null)
                    .Select(pair => ToHyperlinkDto(s, pair))
                    .ToList(),
                AllowEditRanges = s.AllowEditRanges
                    .Where(range => range.Start.Sheet == s.Id && range.End.Sheet == s.Id)
                    .Select(range => range.ToString())
                    .ToList(),
                BackgroundImage = ToWorksheetBackgroundDto(s.BackgroundImage),
                Pictures = s.Pictures
                    .Where(picture => NativeJsonVisualDtoMapper.IsPictureOnSheet(picture, s.Id))
                    .Select(NativeJsonVisualDtoMapper.FromPicture)
                    .ToList(),
                TextBoxes = s.TextBoxes
                    .Where(textBox => NativeJsonVisualDtoMapper.IsTextBoxOnSheet(textBox, s.Id))
                    .Select(NativeJsonVisualDtoMapper.FromTextBox)
                    .ToList(),
                DrawingShapes = s.DrawingShapes
                    .Where(shape => NativeJsonVisualDtoMapper.IsDrawingShapeOnSheet(shape, s.Id))
                    .Select(NativeJsonVisualDtoMapper.FromDrawingShape)
                    .ToList(),
                Sparklines = s.Sparklines
                    .Where(sparkline => IsSparklineOnSheet(sparkline, s.Id) && Enum.IsDefined(sparkline.Kind))
                    .Select(ToSparklineDto)
                    .ToList(),
                Charts = s.Charts
                    .Where(chart => IsChartOnSheet(chart, s.Id))
                    .Select(ToChartDto)
                    .ToList(),
                DataValidations = s.DataValidations
                    .Where(validation => IsDataValidationOnSheet(validation, s.Id) && IsSupportedDataValidation(validation))
                    .Select(validation => ToDataValidationDto(validation, s.Id))
                    .ToList(),
                ConditionalFormats = ToConditionalFormatDtos(s.ConditionalFormats, s.Id),
                Cells = s.EnumerateCells()
                    .Where(entry => IsValidAddressOnSheet(entry.Address, s.Id))
                    .Select(entry => new CellDto
                    {
                        Address   = entry.Address.ToA1(),
                        Value     = NativeJsonScalarValueMapper.Serialize(entry.Cell.Value),
                        ValueType = NativeJsonScalarValueMapper.GetValueType(entry.Cell.Value),
                        Formula   = entry.Cell.HasFormula ? entry.Cell.FormulaText : null,
                        IgnoreFormulaError = entry.Cell.IgnoreFormulaError,
                        Style = FromCellStyle(workbook.GetStyle(entry.Cell.StyleId))
                    }).ToList(),
                StyleOnlyCells = s.GetStyleOnlyEntries()
                    .Where(entry => NativeJsonValueSanitizer.IsValidRowIndex(entry.Key.Row) && NativeJsonValueSanitizer.IsValidColumnIndex(entry.Key.Col))
                    .Select(entry => new StyleOnlyCellDto
                    {
                        Address = new CellAddress(s.Id, entry.Key.Row, entry.Key.Col).ToA1(),
                        Style = FromCellStyle(workbook.GetStyle(entry.StyleId))
                    }).ToList()
            }).ToList()
        };

        PopulateCalculationOptions(workbook, dto);

        JsonSerializer.Serialize(stream, dto, SaveOptions);
    }

    private static bool IsValidAddressOnSheet(CellAddress address, SheetId sheetId) =>
        address.Sheet == sheetId &&
        NativeJsonValueSanitizer.IsValidRowIndex(address.Row) &&
        NativeJsonValueSanitizer.IsValidColumnIndex(address.Col);
}
