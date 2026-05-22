using System.Text.Json;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

/// <summary>
/// Native JSON adapter for Freexcel.
/// Serializes the workbook to a simple, human-readable JSON format.
/// </summary>
public sealed partial class NativeJsonAdapter : IFileAdapter
{
    private const string NativeFileFormat = "Freexcel.NativeJsonWorkbook";
    private const int CurrentSchemaVersion = 1;
    private const int CurrentMinimumReaderVersion = 1;
    private const string NumberStoredAsTextCode = "NumberStoredAsText";
    private const string FormulaRefersToBlankCellsCode = "FormulaRefersToBlankCells";

    public string Extension => ".fxl";
    public string FormatName => "Freexcel Workbook";

    public Workbook Load(Stream stream)
    {
        var dto = JsonSerializer.Deserialize<WorkbookDto>(stream)
            ?? throw new InvalidDataException("Invalid Freexcel file");

        ValidateSchemaHeader(dto);

        var workbook = new Workbook(dto.Name);
        if (dto.Theme is { } theme)
            workbook.Theme = ToWorkbookTheme(theme);
        workbook.IsStructureProtected = dto.IsStructureProtected;
        workbook.StructureProtectionPassword = dto.IsStructureProtected ? dto.StructureProtectionPassword : null;
        if (dto.WindowArrangement is { } arrangement && Enum.IsDefined(arrangement))
            workbook.WindowArrangement = arrangement;
        ApplyCalculationOptions(dto, workbook);
        foreach (var errorCode in dto.DisabledFormulaErrorCodes ?? [])
            if (IsSupportedFormulaErrorCode(errorCode))
                workbook.DisabledFormulaErrorCodes.Add(errorCode);

        foreach (var sDto in dto.Sheets ?? [])
        {
            if (string.IsNullOrEmpty(sDto?.Name)) continue;
            var sheet = workbook.AddSheet(sDto.Name);
            sheet.IsHidden = sDto.IsHidden;
            sheet.TabColor = sDto.TabColor is { } tabColor ? ParseColor(tabColor) : null;
            sheet.IsProtected = sDto.IsProtected;
            sheet.ProtectionPassword = sDto.IsProtected ? sDto.ProtectionPassword : null;
            foreach (var property in sDto.CustomProperties ?? [])
            {
                if (string.IsNullOrWhiteSpace(property?.Name) || property.Id <= 0)
                    continue;

                sheet.CustomProperties.Add(new WorksheetCustomProperty(property.Name, property.Id));
            }
            foreach (var entry in sDto.RowHeights ?? [])
                if (NativeJsonValueSanitizer.IsValidRowIndex(entry.Index) && NativeJsonValueSanitizer.IsPositiveFinite(entry.Value))
                    sheet.RowHeights[entry.Index] = entry.Value;
            foreach (var entry in sDto.ColumnWidths ?? [])
                if (NativeJsonValueSanitizer.IsValidColumnIndex(entry.Index) && NativeJsonValueSanitizer.IsPositiveFinite(entry.Value))
                    sheet.ColumnWidths[entry.Index] = entry.Value;
            foreach (var row in sDto.HiddenRows ?? [])
                if (NativeJsonValueSanitizer.IsValidRowIndex(row))
                    sheet.HiddenRows.Add(row);
            foreach (var row in sDto.FilterHiddenRows ?? [])
                if (NativeJsonValueSanitizer.IsValidRowIndex(row))
                    sheet.FilterHiddenRows.Add(row);
            foreach (var column in sDto.HiddenCols ?? [])
                if (NativeJsonValueSanitizer.IsValidColumnIndex(column))
                    sheet.HiddenCols.Add(column);
            foreach (var entry in sDto.RowOutlineLevels ?? [])
                if (NativeJsonValueSanitizer.IsValidRowIndex(entry.Index) && NativeJsonValueSanitizer.IsValidOutlineLevel(entry.Value))
                    sheet.RowOutlineLevels[entry.Index] = entry.Value;
            foreach (var entry in sDto.ColOutlineLevels ?? [])
                if (NativeJsonValueSanitizer.IsValidColumnIndex(entry.Index) && NativeJsonValueSanitizer.IsValidOutlineLevel(entry.Value))
                    sheet.ColOutlineLevels[entry.Index] = entry.Value;
            foreach (var row in sDto.GroupHiddenRows ?? [])
                if (NativeJsonValueSanitizer.IsValidRowIndex(row))
                    sheet.GroupHiddenRows.Add(row);
            foreach (var column in sDto.GroupHiddenCols ?? [])
                if (NativeJsonValueSanitizer.IsValidColumnIndex(column))
                    sheet.GroupHiddenCols.Add(column);
            sheet.ViewMode = Enum.IsDefined(sDto.ViewMode) ? sDto.ViewMode : WorksheetViewMode.Normal;
            sheet.ShowGridlines = sDto.ShowGridlines ?? true;
            sheet.ShowHeadings = sDto.ShowHeadings ?? true;
            sheet.ShowRulers = sDto.ShowRulers ?? true;
            sheet.ZoomPercent = NativeJsonValueSanitizer.ValidZoomPercentOrDefault(sDto.ZoomPercent);
            sheet.ShowFormulas = sDto.ShowFormulas ?? false;
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
            if (!string.IsNullOrWhiteSpace(sDto.PrintArea))
            {
                try { sheet.PrintArea = GridRange.Parse(sDto.PrintArea, sheet.Id); }
                catch (FormatException) { /* skip unparseable print areas */ }
            }
            if (sDto.PageOrientation is { } orientation && Enum.IsDefined(orientation))
                sheet.PageOrientation = orientation;
            if (sDto.PaperSize is { } paperSize && Enum.IsDefined(paperSize))
                sheet.PaperSize = paperSize;
            if (sDto.PageMargins is { } margins)
                sheet.PageMargins = NativeJsonValueSanitizer.ValidPageMarginsOrDefault(
                    new WorksheetPageMargins(margins.Left, margins.Right, margins.Top, margins.Bottom),
                    WorksheetPageMargins.Narrow);
            sheet.HeaderMargin = NativeJsonValueSanitizer.NonNegativeFiniteOrDefault(sDto.HeaderMargin, 0.3);
            sheet.FooterMargin = NativeJsonValueSanitizer.NonNegativeFiniteOrDefault(sDto.FooterMargin, 0.3);
            sheet.PrintGridlines = sDto.PrintGridlines;
            sheet.PrintHeadings = sDto.PrintHeadings;
            sheet.PrintTitleRows = ToRepeatRange(sDto.PrintTitleRows);
            sheet.PrintTitleColumns = ToRepeatRange(sDto.PrintTitleColumns);
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
            sheet.CenterHorizontallyOnPage = sDto.CenterHorizontallyOnPage;
            sheet.CenterVerticallyOnPage = sDto.CenterVerticallyOnPage;
            if (sDto.PageOrder is { } pageOrder && Enum.IsDefined(pageOrder))
                sheet.PageOrder = pageOrder;
            sheet.FirstPageNumber = sDto.FirstPageNumber is > 0 ? sDto.FirstPageNumber : null;
            sheet.PrintBlackAndWhite = sDto.PrintBlackAndWhite;
            sheet.PrintDraftQuality = sDto.PrintDraftQuality;
            sheet.PrintQualityDpi = sDto.PrintQualityDpi is > 0 ? sDto.PrintQualityDpi : null;
            if (sDto.PrintErrorValue is { } printErrorValue && Enum.IsDefined(printErrorValue))
                sheet.PrintErrorValue = printErrorValue;
            if (sDto.PrintComments is { } printComments && Enum.IsDefined(printComments))
                sheet.PrintComments = printComments;
            if (sDto.ScaleToFit is { } scaleToFit)
                sheet.ScaleToFit = NativeJsonValueSanitizer.ValidScaleToFitOrDefault(
                    new WorksheetScaleToFit(scaleToFit.ScalePercent, scaleToFit.FitToPagesWide, scaleToFit.FitToPagesTall),
                    WorksheetScaleToFit.Default);
            foreach (var rowBreak in sDto.RowPageBreaks ?? [])
                if (rowBreak is >= 2 and <= CellAddress.MaxRow)
                    sheet.RowPageBreaks.Add(rowBreak);
            foreach (var columnBreak in sDto.ColumnPageBreaks ?? [])
                if (columnBreak is >= 2 and <= CellAddress.MaxCol)
                    sheet.ColumnPageBreaks.Add(columnBreak);
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
                    sheet.Comments[comment.Address] = comment.Text;
            }
            foreach (var hyperlinkDto in sDto.Hyperlinks ?? [])
            {
                if (TryLoadHyperlink(hyperlinkDto, sheet.Id) is { } hyperlink)
                {
                    sheet.Hyperlinks[hyperlink.Address] = hyperlink.Target;
                    sheet.HyperlinkMetadata[hyperlink.Address] = hyperlink.Metadata;
                }
            }
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
            sheet.BackgroundImage = TryLoadWorksheetBackground(sDto.BackgroundImage);
            foreach (var pictureDto in sDto.Pictures ?? [])
            {
                if (NativeJsonVisualDtoMapper.ToPicture(pictureDto, sheet.Id) is { } picture)
                    sheet.Pictures.Add(picture);
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
            foreach (var sparklineDto in sDto.Sparklines ?? [])
            {
                if (TryLoadSparkline(sparklineDto, sheet.Id) is { } sparkline)
                    sheet.Sparklines.Add(sparkline);
            }
            foreach (var chartDto in sDto.Charts ?? [])
            {
                if (TryLoadChart(chartDto, sheet.Id) is { } chart)
                    sheet.Charts.Add(chart);
            }

            foreach (var validationDto in sDto.DataValidations ?? [])
            {
                if (TryLoadDataValidation(validationDto, sheet.Id) is { } validation)
                    sheet.DataValidations.Add(validation);
            }

            foreach (var formatDto in sDto.ConditionalFormats ?? [])
            {
                if (string.IsNullOrWhiteSpace(formatDto?.AppliesTo))
                    continue;
                if (!IsSupportedConditionalFormat(formatDto))
                    continue;

                try
                {
                    sheet.ConditionalFormats.Add(new ConditionalFormat
                    {
                        AppliesTo = GridRange.Parse(formatDto.AppliesTo, sheet.Id),
                        Priority = formatDto.Priority < 1 ? 1 : formatDto.Priority,
                        RuleType = formatDto.RuleType,
                        Operator = formatDto.Operator,
                        Value1 = formatDto.Value1,
                        Value2 = formatDto.Value2,
                        FormatIfTrue = ToCellStyle(formatDto.FormatIfTrue),
                        MinColor = formatDto.MinColor,
                        MidColor = formatDto.MidColor,
                        MaxColor = formatDto.MaxColor,
                        UseThreeColorScale = formatDto.UseThreeColorScale,
                        DataBarColor = formatDto.DataBarColor,
                        AboveAverage = formatDto.AboveAverage,
                        FormulaText = formatDto.FormulaText,
                        TopBottomRank = formatDto.TopBottomRank,
                        TopBottomPercent = formatDto.TopBottomPercent,
                        TextRuleText = formatDto.TextRuleText,
                        DateOccurringPeriod = formatDto.DateOccurringPeriod,
                        StopIfTrue = formatDto.StopIfTrue,
                        NativeAttributes = formatDto.NativeAttributes,
                        NativeChildXmls = formatDto.NativeChildXmls,
                        NativePayloadAttributes = formatDto.NativePayloadAttributes,
                        NativePayloadChildXmls = formatDto.NativePayloadChildXmls,
                        NativeContainerAttributes = formatDto.NativeContainerAttributes,
                        NativeContainerChildXmls = formatDto.NativeContainerChildXmls
                    });
                }
                catch (FormatException) { /* skip conditional formats with unparseable ranges */ }
            }

            foreach (var cDto in sDto.Cells ?? [])
            {
                if (string.IsNullOrEmpty(cDto?.Address)) continue;
                try
                {
                    var addr = CellAddress.Parse(cDto.Address, sheet.Id);
                    var cell = cDto.Formula != null
                        ? Cell.FromFormula(cDto.Formula)
                        : Cell.FromValue(NativeJsonScalarValueMapper.Deserialize(cDto.Value, cDto.ValueType));
                    cell.IgnoreFormulaError = cDto.IgnoreFormulaError;
                    if (ToCellStyle(cDto.Style) is { } style)
                        cell.StyleId = workbook.RegisterStyle(style);
                    sheet.SetCell(addr, cell);
                }
                catch (FormatException) { /* skip cells with unparseable addresses */ }
            }

            foreach (var styleOnlyDto in sDto.StyleOnlyCells ?? [])
            {
                if (string.IsNullOrWhiteSpace(styleOnlyDto?.Address) || styleOnlyDto.Style is null)
                    continue;

                try
                {
                    var address = CellAddress.Parse(styleOnlyDto.Address, sheet.Id);
                    if (address.Sheet != sheet.Id)
                        continue;
                    if (ToCellStyle(styleOnlyDto.Style) is { } style)
                        sheet.SetStyleOnly(address.Row, address.Col, workbook.RegisterStyle(style));
                }
                catch (FormatException) { /* skip style-only entries with unparseable addresses */ }
            }
        }

        foreach (var namedRangeDto in dto.NamedRanges ?? [])
        {
            if (string.IsNullOrWhiteSpace(namedRangeDto?.Name) ||
                string.IsNullOrWhiteSpace(namedRangeDto.SheetName) ||
                string.IsNullOrWhiteSpace(namedRangeDto.Range))
            {
                continue;
            }

            var sheet = workbook.GetSheet(namedRangeDto.SheetName);
            if (sheet is null)
                continue;

            try
            {
                workbook.DefineNamedRange(
                    namedRangeDto.Name,
                    GridRange.Parse(namedRangeDto.Range, sheet.Id),
                    new NamedRangeMetadata(
                        string.IsNullOrWhiteSpace(namedRangeDto.Scope) ? "Workbook" : namedRangeDto.Scope.Trim(),
                        namedRangeDto.Comment?.Trim() ?? ""));
            }
            catch (ArgumentException) { /* skip invalid defined names */ }
            catch (FormatException) { /* skip unparseable named ranges */ }
        }

        foreach (var viewDto in dto.CustomViews ?? [])
        {
            if (string.IsNullOrWhiteSpace(viewDto?.Name)) continue;
            workbook.CustomViews.Add(new WorkbookCustomView(
                viewDto.Name,
                (viewDto.Sheets ?? []).Select(ToWorksheetCustomViewState).ToList(),
                string.IsNullOrWhiteSpace(viewDto.Id) ? null : viewDto.Id));
        }

        foreach (var watchDto in dto.WatchedCells ?? [])
        {
            if (string.IsNullOrWhiteSpace(watchDto?.SheetName) ||
                string.IsNullOrWhiteSpace(watchDto.Address))
            {
                continue;
            }

            var sheet = workbook.Sheets.FirstOrDefault(s => s.Name == watchDto.SheetName);
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

                var sheet = workbook.Sheets.FirstOrDefault(s => s.Name == changeDto.SheetName);
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
                workbook.Scenarios.Add(new WorkbookScenario(scenarioDto.Name, changes));
        }

        return workbook;
    }

    public void Save(Workbook workbook, Stream stream)
    {
        var dto = new WorkbookDto
        {
            FileFormat = NativeFileFormat,
            SchemaVersion = CurrentSchemaVersion,
            MinimumReaderVersion = CurrentMinimumReaderVersion,
            Name = workbook.Name,
            Theme = FromWorkbookTheme(workbook.Theme),
            IsStructureProtected = workbook.IsStructureProtected,
            StructureProtectionPassword = workbook.IsStructureProtected ? workbook.StructureProtectionPassword : null,
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
                Sheets = view.Sheets.Select(ToCustomViewSheetDto).ToList()
            }).ToList(),
            WatchedCells = workbook.WatchedCells.Select(address =>
            {
                var sheet = workbook.Sheets.FirstOrDefault(s => s.Id.Equals(address.Sheet));
                return sheet is null
                    ? null
                    : new WatchedCellDto { SheetName = sheet.Name, Address = address.ToA1() };
            }).OfType<WatchedCellDto>().ToList(),
            Scenarios = workbook.Scenarios.Select(scenario => new ScenarioDto
            {
                Name = scenario.Name,
                ChangingCells = scenario.ChangingCells.Select(change =>
                {
                    var sheet = workbook.Sheets.FirstOrDefault(s => s.Id.Equals(change.Address.Sheet));
                    return sheet is null
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
                ProtectionPassword = s.IsProtected ? s.ProtectionPassword : null,
                CustomProperties = s.CustomProperties
                    .Where(property => !string.IsNullOrWhiteSpace(property.Name) && property.Id > 0)
                    .Select(property => new WorksheetCustomPropertyDto
                    {
                        Name = property.Name,
                        Id = property.Id
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
                PrintArea = s.PrintArea?.ToString(),
                PageOrientation = NativeJsonValueSanitizer.ValidEnumOrDefault(s.PageOrientation, WorksheetPageOrientation.Portrait),
                PaperSize = NativeJsonValueSanitizer.ValidEnumOrDefault(s.PaperSize, WorksheetPaperSize.A4),
                PageMargins = FromPageMargins(NativeJsonValueSanitizer.ValidPageMarginsOrDefault(s.PageMargins, WorksheetPageMargins.Narrow)),
                HeaderMargin = NativeJsonValueSanitizer.NonNegativeFiniteOrDefault(s.HeaderMargin, 0.3),
                FooterMargin = NativeJsonValueSanitizer.NonNegativeFiniteOrDefault(s.FooterMargin, 0.3),
                PrintGridlines = s.PrintGridlines,
                PrintHeadings = s.PrintHeadings,
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
                CenterHorizontallyOnPage = s.CenterHorizontallyOnPage,
                CenterVerticallyOnPage = s.CenterVerticallyOnPage,
                PageOrder = NativeJsonValueSanitizer.ValidEnumOrDefault(s.PageOrder, WorksheetPageOrder.DownThenOver),
                FirstPageNumber = s.FirstPageNumber is > 0 ? s.FirstPageNumber : null,
                PrintBlackAndWhite = s.PrintBlackAndWhite,
                PrintDraftQuality = s.PrintDraftQuality,
                PrintQualityDpi = s.PrintQualityDpi is > 0 ? s.PrintQualityDpi : null,
                PrintErrorValue = NativeJsonValueSanitizer.ValidEnumOrDefault(s.PrintErrorValue, WorksheetPrintErrorValue.Displayed),
                PrintComments = NativeJsonValueSanitizer.ValidEnumOrDefault(s.PrintComments, WorksheetPrintComments.None),
                ScaleToFit = new ScaleToFitDto
                {
                    ScalePercent = NativeJsonValueSanitizer.ValidScaleToFitOrDefault(s.ScaleToFit, WorksheetScaleToFit.Default).ScalePercent,
                    FitToPagesWide = NativeJsonValueSanitizer.ValidScaleToFitOrDefault(s.ScaleToFit, WorksheetScaleToFit.Default).FitToPagesWide,
                    FitToPagesTall = NativeJsonValueSanitizer.ValidScaleToFitOrDefault(s.ScaleToFit, WorksheetScaleToFit.Default).FitToPagesTall
                },
                RowPageBreaks = s.RowPageBreaks.Where(rowBreak => rowBreak is >= 2 and <= CellAddress.MaxRow).ToList(),
                ColumnPageBreaks = s.ColumnPageBreaks.Where(columnBreak => columnBreak is >= 2 and <= CellAddress.MaxCol).ToList(),
                MergedRegions = s.MergedRegions
                    .Where(range => range.Start.Sheet == s.Id && range.End.Sheet == s.Id)
                    .Select(range => range.ToString())
                    .ToList(),
                Comments = s.Comments
                    .Where(pair => pair.Key.Sheet == s.Id && pair.Value is not null)
                    .Select(ToCommentDto)
                    .ToList(),
                Hyperlinks = s.Hyperlinks
                    .Where(pair => pair.Key.Sheet == s.Id && pair.Value is not null)
                    .Select(pair => ToHyperlinkDto(s, pair))
                    .ToList(),
                AllowEditRanges = s.AllowEditRanges
                    .Where(range => range.Start.Sheet == s.Id && range.End.Sheet == s.Id)
                    .Select(range => range.ToString())
                    .ToList(),
                BackgroundImage = ToWorksheetBackgroundDto(s.BackgroundImage),
                Pictures = s.Pictures.Select(NativeJsonVisualDtoMapper.FromPicture).ToList(),
                TextBoxes = s.TextBoxes.Select(NativeJsonVisualDtoMapper.FromTextBox).ToList(),
                DrawingShapes = s.DrawingShapes.Select(NativeJsonVisualDtoMapper.FromDrawingShape).ToList(),
                Sparklines = s.Sparklines
                    .Where(sparkline => IsSparklineOnSheet(sparkline, s.Id) && Enum.IsDefined(sparkline.Kind))
                    .Select(ToSparklineDto)
                    .ToList(),
                Charts = s.Charts.Select(ToChartDto).ToList(),
                DataValidations = s.DataValidations
                    .Where(IsSupportedDataValidation)
                    .Select(ToDataValidationDto)
                    .ToList(),
                ConditionalFormats = s.ConditionalFormats
                    .Where(format =>
                        format.AppliesTo.Start.Sheet == s.Id &&
                        format.AppliesTo.End.Sheet == s.Id &&
                        IsSupportedConditionalFormat(format))
                    .Select(format => new ConditionalFormatDto
                    {
                        AppliesTo = format.AppliesTo.ToString(),
                        Priority = format.Priority < 1 ? 1 : format.Priority,
                        RuleType = format.RuleType,
                        Operator = format.Operator,
                        Value1 = format.Value1,
                        Value2 = format.Value2,
                        FormatIfTrue = FromCellStyle(format.FormatIfTrue),
                        MinColor = format.MinColor,
                        MidColor = format.MidColor,
                        MaxColor = format.MaxColor,
                        UseThreeColorScale = format.UseThreeColorScale,
                        DataBarColor = format.DataBarColor,
                        AboveAverage = format.AboveAverage,
                        FormulaText = format.FormulaText,
                        TopBottomRank = format.TopBottomRank,
                        TopBottomPercent = format.TopBottomPercent,
                        TextRuleText = format.TextRuleText,
                        DateOccurringPeriod = format.DateOccurringPeriod,
                        StopIfTrue = format.StopIfTrue,
                        NativeAttributes = format.NativeAttributes,
                        NativeChildXmls = format.NativeChildXmls,
                        NativePayloadAttributes = format.NativePayloadAttributes,
                        NativePayloadChildXmls = format.NativePayloadChildXmls,
                        NativeContainerAttributes = format.NativeContainerAttributes,
                        NativeContainerChildXmls = format.NativeContainerChildXmls
                    }).ToList(),
                Cells = s.GetUsedCells().Select(pair => new CellDto
                {
                    Address   = pair.Key.ToA1(),
                    Value     = NativeJsonScalarValueMapper.Serialize(pair.Value.Value),
                    ValueType = NativeJsonScalarValueMapper.GetValueType(pair.Value.Value),
                    Formula   = pair.Value.HasFormula ? pair.Value.FormulaText : null,
                    IgnoreFormulaError = pair.Value.IgnoreFormulaError,
                    Style = FromCellStyle(workbook.GetStyle(pair.Value.StyleId))
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

        JsonSerializer.Serialize(stream, dto, new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool IsSupportedConditionalFormat(ConditionalFormat format) =>
        Enum.IsDefined(format.RuleType) && Enum.IsDefined(format.Operator);

    private static bool IsSupportedConditionalFormat(ConditionalFormatDto format) =>
        Enum.IsDefined(format.RuleType) && Enum.IsDefined(format.Operator);

    private static string FormatColor(CellColor color) => NativeJsonColorMapper.FormatColor(color);

    private static CellColor? ParseColor(string text) => NativeJsonColorMapper.ParseColor(text);
}
