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
                    sheet.Hyperlinks[hyperlink.Address] = hyperlink.Target;
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
                workbook.DefineNamedRange(namedRangeDto.Name, GridRange.Parse(namedRangeDto.Range, sheet.Id));
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
                    return sheet is null || pair.Value.End.Sheet != sheet.Id
                        ? null
                        : new NamedRangeDto
                        {
                            Name = pair.Key,
                            SheetName = sheet.Name,
                            Range = pair.Value.ToString()
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
                    .Select(ToHyperlinkDto)
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

    private static void ValidateSchemaHeader(WorkbookDto dto)
    {
        if (dto.FileFormat is { Length: > 0 } fileFormat &&
            !string.Equals(fileFormat, NativeFileFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported Freexcel file format '{fileFormat}'.");
        }

        if (dto.SchemaVersion is > CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported Freexcel native JSON schema version {dto.SchemaVersion}.");

        if (dto.MinimumReaderVersion is > CurrentSchemaVersion)
            throw new InvalidDataException($"Freexcel native JSON requires reader schema version {dto.MinimumReaderVersion}.");
    }

    private static void ApplyCalculationOptions(WorkbookDto dto, Workbook workbook)
    {
        if (dto.CalculationMode is { } calculationMode && Enum.IsDefined(calculationMode))
            workbook.CalculationMode = calculationMode;

        workbook.FullCalculationOnLoad = dto.FullCalculationOnLoad;
        workbook.ForceFullCalculation = dto.ForceFullCalculation;
        workbook.IterativeCalculation = dto.IterativeCalculation;
        workbook.MaxCalculationIterations = dto.MaxCalculationIterations;
        workbook.MaxCalculationChange = dto.MaxCalculationChange;
    }

    private static void PopulateCalculationOptions(Workbook workbook, WorkbookDto dto)
    {
        dto.CalculationMode = NativeJsonValueSanitizer.ValidEnumOrDefault(workbook.CalculationMode, WorkbookCalculationMode.Automatic);
        dto.FullCalculationOnLoad = workbook.FullCalculationOnLoad;
        dto.ForceFullCalculation = workbook.ForceFullCalculation;
        dto.IterativeCalculation = workbook.IterativeCalculation;
        dto.MaxCalculationIterations = workbook.MaxCalculationIterations;
        dto.MaxCalculationChange = workbook.MaxCalculationChange;
    }

    private static ChartModel? TryLoadChart(ChartDto? chartDto, SheetId sheetId)
    {
        if (chartDto?.DataRange is null)
            return null;

        try
        {
            var chart = new ChartModel
            {
                Type = chartDto.Type,
                DataRange = GridRange.Parse(chartDto.DataRange, sheetId),
                IsVisible = chartDto.IsVisible,
                FirstRowIsHeader = chartDto.FirstRowIsHeader,
                FirstColIsCategories = chartDto.FirstColIsCategories,
                Title = chartDto.Title,
                XAxisTitle = chartDto.XAxisTitle,
                YAxisTitle = chartDto.YAxisTitle,
                ChartTitleTextColor = chartDto.ChartTitleTextColor,
                ChartTitleFontSize = chartDto.ChartTitleFontSize,
                AxisTitleTextColor = chartDto.AxisTitleTextColor,
                AxisTitleFontSize = chartDto.AxisTitleFontSize,
                ChartAreaFillColor = chartDto.ChartAreaFillColor,
                ChartAreaFillThemeColor = ToThemeColorReference(chartDto.ChartAreaFillThemeColor),
                PlotAreaFillColor = chartDto.PlotAreaFillColor,
                PlotAreaFillThemeColor = ToThemeColorReference(chartDto.PlotAreaFillThemeColor),
                PlotAreaBorderColor = chartDto.PlotAreaBorderColor,
                PlotAreaBorderThemeColor = ToThemeColorReference(chartDto.PlotAreaBorderThemeColor),
                PlotAreaBorderThickness = chartDto.PlotAreaBorderThickness,
                LegendTextColor = chartDto.LegendTextColor,
                LegendTextThemeColor = ToThemeColorReference(chartDto.LegendTextThemeColor),
                LegendFillColor = chartDto.LegendFillColor,
                LegendFillThemeColor = ToThemeColorReference(chartDto.LegendFillThemeColor),
                LegendBorderColor = chartDto.LegendBorderColor,
                LegendBorderThemeColor = ToThemeColorReference(chartDto.LegendBorderThemeColor),
                LegendBorderThickness = chartDto.LegendBorderThickness,
                LegendFontSize = chartDto.LegendFontSize,
                DoughnutHoleSize = chartDto.DoughnutHoleSize,
                FirstSliceAngle = chartDto.FirstSliceAngle,
                ExplodedSliceIndex = chartDto.ExplodedSliceIndex,
                ExplodedSliceDistance = chartDto.ExplodedSliceDistance,
                XAxisMinimum = chartDto.XAxisMinimum,
                XAxisMaximum = chartDto.XAxisMaximum,
                XAxisMajorUnit = chartDto.XAxisMajorUnit,
                XAxisMinorUnit = chartDto.XAxisMinorUnit,
                XAxisLogScale = chartDto.XAxisLogScale,
                XAxisNumberFormat = chartDto.XAxisNumberFormat,
                ShowXAxisMajorGridlines = chartDto.ShowXAxisMajorGridlines,
                ShowXAxisMinorGridlines = chartDto.ShowXAxisMinorGridlines,
                XAxisMajorGridlineColor = chartDto.XAxisMajorGridlineColor,
                XAxisMinorGridlineColor = chartDto.XAxisMinorGridlineColor,
                XAxisGridlineThickness = chartDto.XAxisGridlineThickness,
                XAxisMajorTickStyle = chartDto.XAxisMajorTickStyle,
                XAxisMinorTickStyle = chartDto.XAxisMinorTickStyle,
                ShowXAxisLabels = chartDto.ShowXAxisLabels,
                XAxisLabelTextColor = chartDto.XAxisLabelTextColor,
                XAxisLabelFontSize = chartDto.XAxisLabelFontSize,
                XAxisLabelAngle = chartDto.XAxisLabelAngle,
                XAxisLineColor = chartDto.XAxisLineColor,
                XAxisLineThickness = chartDto.XAxisLineThickness,
                YAxisMinimum = chartDto.YAxisMinimum,
                YAxisMaximum = chartDto.YAxisMaximum,
                YAxisMajorUnit = chartDto.YAxisMajorUnit,
                YAxisMinorUnit = chartDto.YAxisMinorUnit,
                YAxisLogScale = chartDto.YAxisLogScale,
                YAxisNumberFormat = chartDto.YAxisNumberFormat,
                ShowYAxisMajorGridlines = chartDto.ShowYAxisMajorGridlines,
                ShowYAxisMinorGridlines = chartDto.ShowYAxisMinorGridlines,
                YAxisMajorGridlineColor = chartDto.YAxisMajorGridlineColor,
                YAxisMinorGridlineColor = chartDto.YAxisMinorGridlineColor,
                YAxisGridlineThickness = chartDto.YAxisGridlineThickness,
                YAxisMajorTickStyle = chartDto.YAxisMajorTickStyle,
                YAxisMinorTickStyle = chartDto.YAxisMinorTickStyle,
                ShowYAxisLabels = chartDto.ShowYAxisLabels,
                YAxisLabelTextColor = chartDto.YAxisLabelTextColor,
                YAxisLabelFontSize = chartDto.YAxisLabelFontSize,
                YAxisLabelAngle = chartDto.YAxisLabelAngle,
                YAxisLineColor = chartDto.YAxisLineColor,
                YAxisLineThickness = chartDto.YAxisLineThickness,
                DataTable = chartDto.DataTable,
                BarGapWidth = chartDto.BarGapWidth,
                BarOverlap = chartDto.BarOverlap,
                VaryColorsByPoint = chartDto.VaryColorsByPoint,
                LegendPosition = chartDto.LegendPosition,
                LegendOverlay = chartDto.LegendOverlay,
                ShowLegend = chartDto.ShowLegend,
                ShowDataLabels = chartDto.ShowDataLabels,
                DataLabelPosition = chartDto.DataLabelPosition,
                ShowDataLabelCategoryName = chartDto.ShowDataLabelCategoryName,
                ShowDataLabelSeriesName = chartDto.ShowDataLabelSeriesName,
                ShowDataLabelPercentage = chartDto.ShowDataLabelPercentage,
                DataLabelSeparator = chartDto.DataLabelSeparator,
                DataLabelNumberFormat = chartDto.DataLabelNumberFormat,
                ShowDataLabelCallouts = chartDto.ShowDataLabelCallouts,
                DataLabelFillColor = chartDto.DataLabelFillColor,
                DataLabelFillThemeColor = ToThemeColorReference(chartDto.DataLabelFillThemeColor),
                DataLabelBorderColor = chartDto.DataLabelBorderColor,
                DataLabelBorderThemeColor = ToThemeColorReference(chartDto.DataLabelBorderThemeColor),
                DataLabelTextColor = chartDto.DataLabelTextColor,
                DataLabelTextThemeColor = ToThemeColorReference(chartDto.DataLabelTextThemeColor),
                DataLabelBorderThickness = chartDto.DataLabelBorderThickness,
                DataLabelFontSize = chartDto.DataLabelFontSize,
                DataLabelAngle = chartDto.DataLabelAngle,
                ShowLinearTrendline = chartDto.ShowLinearTrendline,
                TrendlineType = chartDto.TrendlineType,
                TrendlinePeriod = chartDto.TrendlinePeriod,
                TrendlineOrder = chartDto.TrendlineOrder,
                ShowTrendlineEquation = chartDto.ShowTrendlineEquation,
                ShowTrendlineRSquared = chartDto.ShowTrendlineRSquared,
                TrendlineColor = chartDto.TrendlineColor,
                TrendlineThemeColor = ToThemeColorReference(chartDto.TrendlineThemeColor),
                TrendlineThickness = chartDto.TrendlineThickness,
                TrendlineDashStyle = chartDto.TrendlineDashStyle,
                ShowErrorBars = chartDto.ShowErrorBars,
                ErrorBarKind = chartDto.ErrorBarKind,
                ErrorBarDirection = chartDto.ErrorBarDirection,
                ErrorBarValue = chartDto.ErrorBarValue,
                ErrorBarEndCaps = chartDto.ErrorBarEndCaps,
                ShowDropLines = chartDto.ShowDropLines,
                ShowHighLowLines = chartDto.ShowHighLowLines,
                ShowUpDownBars = chartDto.ShowUpDownBars,
                ShowSecondaryAxis = chartDto.ShowSecondaryAxis,
                SecondaryAxisSeriesIndexes = chartDto.SecondaryAxisSeriesIndexes ?? [],
                ComboLineSeriesIndexes = chartDto.ComboLineSeriesIndexes ?? [],
                SeriesFormats = chartDto.SeriesFormats ?? [],
                PointDataLabelFormats = chartDto.PointDataLabelFormats ?? [],
                UseComboLineForSecondarySeries = chartDto.UseComboLineForSecondarySeries,
                Left = chartDto.Left,
                Top = chartDto.Top,
                Width = NativeJsonValueSanitizer.PositiveFiniteOrDefault(chartDto.Width, 400),
                Height = NativeJsonValueSanitizer.PositiveFiniteOrDefault(chartDto.Height, 300)
            };
            SanitizeLoadedChart(chart);
            return chart;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static ChartDto ToChartDto(ChartModel chart) => new()
    {
        Type = chart.Type,
        DataRange = chart.DataRange.ToString(),
        IsVisible = chart.IsVisible,
        FirstRowIsHeader = chart.FirstRowIsHeader,
        FirstColIsCategories = chart.FirstColIsCategories,
        Title = chart.Title,
        XAxisTitle = chart.XAxisTitle,
        YAxisTitle = chart.YAxisTitle,
        ChartTitleTextColor = chart.ChartTitleTextColor,
        ChartTitleFontSize = chart.ChartTitleFontSize,
        AxisTitleTextColor = chart.AxisTitleTextColor,
        AxisTitleFontSize = chart.AxisTitleFontSize,
        ChartAreaFillColor = chart.ChartAreaFillColor,
        ChartAreaFillThemeColor = FromThemeColorReference(chart.ChartAreaFillThemeColor),
        PlotAreaFillColor = chart.PlotAreaFillColor,
        PlotAreaFillThemeColor = FromThemeColorReference(chart.PlotAreaFillThemeColor),
        PlotAreaBorderColor = chart.PlotAreaBorderColor,
        PlotAreaBorderThemeColor = FromThemeColorReference(chart.PlotAreaBorderThemeColor),
        PlotAreaBorderThickness = chart.PlotAreaBorderThickness,
        LegendTextColor = chart.LegendTextColor,
        LegendTextThemeColor = FromThemeColorReference(chart.LegendTextThemeColor),
        LegendFillColor = chart.LegendFillColor,
        LegendFillThemeColor = FromThemeColorReference(chart.LegendFillThemeColor),
        LegendBorderColor = chart.LegendBorderColor,
        LegendBorderThemeColor = FromThemeColorReference(chart.LegendBorderThemeColor),
        LegendBorderThickness = chart.LegendBorderThickness,
        LegendFontSize = chart.LegendFontSize,
        DoughnutHoleSize = chart.DoughnutHoleSize,
        FirstSliceAngle = chart.FirstSliceAngle,
        ExplodedSliceIndex = chart.ExplodedSliceIndex,
        ExplodedSliceDistance = chart.ExplodedSliceDistance,
        XAxisMinimum = chart.XAxisMinimum,
        XAxisMaximum = chart.XAxisMaximum,
        XAxisMajorUnit = chart.XAxisMajorUnit,
        XAxisMinorUnit = chart.XAxisMinorUnit,
        XAxisLogScale = chart.XAxisLogScale,
        XAxisNumberFormat = chart.XAxisNumberFormat,
        ShowXAxisMajorGridlines = chart.ShowXAxisMajorGridlines,
        ShowXAxisMinorGridlines = chart.ShowXAxisMinorGridlines,
        XAxisMajorGridlineColor = chart.XAxisMajorGridlineColor,
        XAxisMinorGridlineColor = chart.XAxisMinorGridlineColor,
        XAxisGridlineThickness = chart.XAxisGridlineThickness,
        XAxisMajorTickStyle = chart.XAxisMajorTickStyle,
        XAxisMinorTickStyle = chart.XAxisMinorTickStyle,
        ShowXAxisLabels = chart.ShowXAxisLabels,
        XAxisLabelTextColor = chart.XAxisLabelTextColor,
        XAxisLabelFontSize = chart.XAxisLabelFontSize,
        XAxisLabelAngle = chart.XAxisLabelAngle,
        XAxisLineColor = chart.XAxisLineColor,
        XAxisLineThickness = chart.XAxisLineThickness,
        YAxisMinimum = chart.YAxisMinimum,
        YAxisMaximum = chart.YAxisMaximum,
        YAxisMajorUnit = chart.YAxisMajorUnit,
        YAxisMinorUnit = chart.YAxisMinorUnit,
        YAxisLogScale = chart.YAxisLogScale,
        YAxisNumberFormat = chart.YAxisNumberFormat,
        ShowYAxisMajorGridlines = chart.ShowYAxisMajorGridlines,
        ShowYAxisMinorGridlines = chart.ShowYAxisMinorGridlines,
        YAxisMajorGridlineColor = chart.YAxisMajorGridlineColor,
        YAxisMinorGridlineColor = chart.YAxisMinorGridlineColor,
        YAxisGridlineThickness = chart.YAxisGridlineThickness,
        YAxisMajorTickStyle = chart.YAxisMajorTickStyle,
        YAxisMinorTickStyle = chart.YAxisMinorTickStyle,
        ShowYAxisLabels = chart.ShowYAxisLabels,
        YAxisLabelTextColor = chart.YAxisLabelTextColor,
        YAxisLabelFontSize = chart.YAxisLabelFontSize,
        YAxisLabelAngle = chart.YAxisLabelAngle,
        YAxisLineColor = chart.YAxisLineColor,
        YAxisLineThickness = chart.YAxisLineThickness,
        DataTable = chart.DataTable,
        BarGapWidth = chart.BarGapWidth,
        BarOverlap = chart.BarOverlap,
        VaryColorsByPoint = chart.VaryColorsByPoint,
        LegendPosition = chart.LegendPosition,
        LegendOverlay = chart.LegendOverlay,
        ShowLegend = chart.ShowLegend,
        ShowDataLabels = chart.ShowDataLabels,
        DataLabelPosition = chart.DataLabelPosition,
        ShowDataLabelCategoryName = chart.ShowDataLabelCategoryName,
        ShowDataLabelSeriesName = chart.ShowDataLabelSeriesName,
        ShowDataLabelPercentage = chart.ShowDataLabelPercentage,
        DataLabelSeparator = chart.DataLabelSeparator,
        DataLabelNumberFormat = chart.DataLabelNumberFormat,
        ShowDataLabelCallouts = chart.ShowDataLabelCallouts,
        DataLabelFillColor = chart.DataLabelFillColor,
        DataLabelFillThemeColor = FromThemeColorReference(chart.DataLabelFillThemeColor),
        DataLabelBorderColor = chart.DataLabelBorderColor,
        DataLabelBorderThemeColor = FromThemeColorReference(chart.DataLabelBorderThemeColor),
        DataLabelTextColor = chart.DataLabelTextColor,
        DataLabelTextThemeColor = FromThemeColorReference(chart.DataLabelTextThemeColor),
        DataLabelBorderThickness = chart.DataLabelBorderThickness,
        DataLabelFontSize = chart.DataLabelFontSize,
        DataLabelAngle = chart.DataLabelAngle,
        ShowLinearTrendline = chart.ShowLinearTrendline,
        TrendlineType = chart.TrendlineType,
        TrendlinePeriod = chart.TrendlinePeriod,
        TrendlineOrder = chart.TrendlineOrder,
        ShowTrendlineEquation = chart.ShowTrendlineEquation,
        ShowTrendlineRSquared = chart.ShowTrendlineRSquared,
        TrendlineColor = chart.TrendlineColor,
        TrendlineThemeColor = FromThemeColorReference(chart.TrendlineThemeColor),
        TrendlineThickness = chart.TrendlineThickness,
        TrendlineDashStyle = chart.TrendlineDashStyle,
        ShowErrorBars = chart.ShowErrorBars,
        ErrorBarKind = chart.ErrorBarKind,
        ErrorBarDirection = chart.ErrorBarDirection,
        ErrorBarValue = chart.ErrorBarValue,
        ErrorBarEndCaps = chart.ErrorBarEndCaps,
        ShowDropLines = chart.ShowDropLines,
        ShowHighLowLines = chart.ShowHighLowLines,
        ShowUpDownBars = chart.ShowUpDownBars,
        ShowSecondaryAxis = chart.ShowSecondaryAxis,
        SecondaryAxisSeriesIndexes = chart.SecondaryAxisSeriesIndexes.ToList(),
        ComboLineSeriesIndexes = chart.ComboLineSeriesIndexes.ToList(),
        SeriesFormats = chart.SeriesFormats.ToList(),
        PointDataLabelFormats = chart.PointDataLabelFormats.ToList(),
        UseComboLineForSecondarySeries = chart.UseComboLineForSecondarySeries,
        Left = chart.Left,
        Top = chart.Top,
        Width = chart.Width,
        Height = chart.Height
    };

    private static WorksheetBackgroundImage? TryLoadWorksheetBackground(WorksheetBackgroundDto? dto)
    {
        if (dto is not { ImageBase64.Length: > 0 })
            return null;

        try
        {
            return new WorksheetBackgroundImage(
                Convert.FromBase64String(dto.ImageBase64),
                string.IsNullOrWhiteSpace(dto.ContentType) ? "image/png" : dto.ContentType,
                dto.FileName);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static WorksheetBackgroundDto? ToWorksheetBackgroundDto(WorksheetBackgroundImage? background) =>
        background is null
            ? null
            : new WorksheetBackgroundDto
            {
                ImageBase64 = Convert.ToBase64String(background.ImageBytes),
                ContentType = background.ContentType,
                FileName = background.FileName
            };

    private static void SanitizeLoadedChart(ChartModel chart)
    {
        chart.Type = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.Type, ChartType.Column);
        chart.ChartTitleFontSize = Math.Clamp(chart.ChartTitleFontSize, 6, 72);
        chart.AxisTitleFontSize = Math.Clamp(chart.AxisTitleFontSize, 6, 72);
        if (!ChartTypeSupport.SupportsAxes(chart.Type))
        {
            chart.XAxisTitle = null;
            chart.YAxisTitle = null;
            chart.AxisTitleTextColor = null;
            chart.AxisTitleFontSize = 12;
        }
        chart.PlotAreaBorderThickness = Math.Clamp(chart.PlotAreaBorderThickness, 0, 10);
        chart.LegendBorderThickness = Math.Clamp(chart.LegendBorderThickness, 0, 10);
        chart.LegendFontSize = Math.Clamp(chart.LegendFontSize, 6, 72);
        chart.DoughnutHoleSize = Math.Clamp(chart.DoughnutHoleSize, 0.1, 0.9);
        chart.FirstSliceAngle = NormalizeChartAngle(chart.FirstSliceAngle);
        chart.ExplodedSliceDistance = Math.Clamp(chart.ExplodedSliceDistance, 0, 0.5);
        if (!ChartTypeSupport.SupportsDoughnutHoleSize(chart.Type))
            chart.DoughnutHoleSize = 0.55;
        if (!ChartTypeSupport.SupportsFirstSliceAngle(chart.Type))
            chart.FirstSliceAngle = 0;
        if (!ChartTypeSupport.SupportsExplodedSlices(chart.Type))
        {
            chart.ExplodedSliceIndex = -1;
            chart.ExplodedSliceDistance = 0.1;
        }
        chart.XAxisMajorUnit = ClampPositiveAxisUnit(chart.XAxisMajorUnit);
        chart.XAxisMinorUnit = ClampPositiveAxisUnit(chart.XAxisMinorUnit);
        chart.XAxisNumberFormat = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.XAxisNumberFormat, ChartDataLabelNumberFormat.General);
        chart.XAxisGridlineThickness = Math.Clamp(chart.XAxisGridlineThickness, 0.25, 10);
        chart.XAxisMajorTickStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.XAxisMajorTickStyle, ChartAxisTickStyle.Outside);
        chart.XAxisMinorTickStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.XAxisMinorTickStyle, ChartAxisTickStyle.None);
        chart.XAxisLabelFontSize = Math.Clamp(chart.XAxisLabelFontSize, 6, 72);
        chart.XAxisLabelAngle = Math.Clamp(chart.XAxisLabelAngle, -90, 90);
        chart.XAxisLineThickness = Math.Clamp(chart.XAxisLineThickness, 0.5, 10);
        chart.YAxisMajorUnit = ClampPositiveAxisUnit(chart.YAxisMajorUnit);
        chart.YAxisMinorUnit = ClampPositiveAxisUnit(chart.YAxisMinorUnit);
        chart.YAxisNumberFormat = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.YAxisNumberFormat, ChartDataLabelNumberFormat.General);
        chart.YAxisGridlineThickness = Math.Clamp(chart.YAxisGridlineThickness, 0.25, 10);
        chart.YAxisMajorTickStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.YAxisMajorTickStyle, ChartAxisTickStyle.Outside);
        chart.YAxisMinorTickStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.YAxisMinorTickStyle, ChartAxisTickStyle.None);
        chart.YAxisLabelFontSize = Math.Clamp(chart.YAxisLabelFontSize, 6, 72);
        chart.YAxisLabelAngle = Math.Clamp(chart.YAxisLabelAngle, -90, 90);
        chart.YAxisLineThickness = Math.Clamp(chart.YAxisLineThickness, 0.5, 10);
        if (!ChartTypeSupport.SupportsXAxisBounds(chart.Type))
            ClearXAxisBounds(chart);
        if (!ChartTypeSupport.SupportsYAxisBounds(chart.Type))
            ClearYAxisBounds(chart);
        chart.LegendPosition = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.LegendPosition, ChartLegendPosition.Right);
        chart.DataLabelPosition = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.DataLabelPosition, ChartDataLabelPosition.BestFit);
        chart.DataLabelSeparator = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.DataLabelSeparator, ChartDataLabelSeparator.Comma);
        chart.DataLabelNumberFormat = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.DataLabelNumberFormat, ChartDataLabelNumberFormat.General);
        chart.DataLabelBorderThickness = Math.Clamp(chart.DataLabelBorderThickness, 0, 10);
        chart.DataLabelFontSize = Math.Clamp(chart.DataLabelFontSize, 6, 72);
        chart.DataLabelAngle = Math.Clamp(chart.DataLabelAngle, -90, 90);
        if (!ChartTypeSupport.SupportsPercentageDataLabels(chart.Type))
            chart.ShowDataLabelPercentage = false;
        chart.TrendlineType = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.TrendlineType, ChartTrendlineType.Linear);
        chart.TrendlinePeriod = Math.Max(2, chart.TrendlinePeriod);
        chart.TrendlineOrder = Math.Clamp(chart.TrendlineOrder, 2, 6);
        chart.TrendlineThickness = Math.Clamp(chart.TrendlineThickness, 0.5, 10);
        chart.TrendlineDashStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.TrendlineDashStyle, ChartLineDashStyle.Dash);
        if (!ChartTypeSupport.SupportsTrendlines(chart.Type))
        {
            chart.ShowLinearTrendline = false;
            chart.TrendlineType = ChartTrendlineType.Linear;
            chart.TrendlinePeriod = 2;
            chart.TrendlineOrder = 2;
            chart.ShowTrendlineEquation = false;
            chart.ShowTrendlineRSquared = false;
            chart.TrendlineColor = null;
            chart.TrendlineThemeColor = null;
            chart.TrendlineThickness = 1.5;
            chart.TrendlineDashStyle = ChartLineDashStyle.Dash;
        }

        var dataPointCount = ChartTypeSupport.GetDataPointCount(chart);
        if (chart.ExplodedSliceIndex < 0 || chart.ExplodedSliceIndex >= dataPointCount)
            chart.ExplodedSliceIndex = -1;

        var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
        chart.SecondaryAxisSeriesIndexes = chart.SecondaryAxisSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .Order()
            .ToList();
        if (!ChartTypeSupport.SupportsSecondaryAxis(chart.Type)
            || (chart.ShowSecondaryAxis && chart.SecondaryAxisSeriesIndexes.Count == 0))
        {
            chart.ShowSecondaryAxis = false;
            chart.SecondaryAxisSeriesIndexes = [];
        }
        chart.ComboLineSeriesIndexes = chart.ComboLineSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .Order()
            .ToList();
        if (!ChartTypeSupport.SupportsComboLineOverlay(chart)
            || (chart.UseComboLineForSecondarySeries && chart.ComboLineSeriesIndexes.Count == 0))
        {
            chart.UseComboLineForSecondarySeries = false;
            chart.ComboLineSeriesIndexes = [];
        }
        chart.SeriesFormats = chart.SeriesFormats
            .Where(format => format.SeriesIndex >= 0 && format.SeriesIndex < seriesCount)
            .GroupBy(format => format.SeriesIndex)
            .Select(group => ClampSeriesFormat(chart.Type, group.Last()))
            .Where(HasSeriesFormatting)
            .OrderBy(format => format.SeriesIndex)
            .ToList();
        chart.PointDataLabelFormats = chart.PointDataLabelFormats
            .Where(format => format.SeriesIndex >= 0
                && format.SeriesIndex < seriesCount
                && format.PointIndex >= 0
            && format.PointIndex < dataPointCount)
            .GroupBy(format => (format.SeriesIndex, format.PointIndex))
            .Select(group => ClampPointDataLabelFormat(group.Last()))
            .Where(HasPointDataLabelFormatting)
            .OrderBy(format => format.SeriesIndex)
            .ThenBy(format => format.PointIndex)
            .ToList();
    }

    private static double? ClampPositiveAxisUnit(double? value) =>
        value is null ? null : Math.Max(double.Epsilon, value.Value);

    private static double NormalizeChartAngle(double value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static void ClearXAxisBounds(ChartModel chart)
    {
        chart.XAxisMinimum = null;
        chart.XAxisMaximum = null;
        chart.XAxisMajorUnit = null;
        chart.XAxisMinorUnit = null;
        chart.XAxisLogScale = false;
        chart.XAxisNumberFormat = ChartDataLabelNumberFormat.General;
        chart.ShowXAxisMajorGridlines = false;
        chart.ShowXAxisMinorGridlines = false;
        chart.XAxisMajorGridlineColor = null;
        chart.XAxisMinorGridlineColor = null;
        chart.XAxisGridlineThickness = 1;
        chart.XAxisMajorTickStyle = ChartAxisTickStyle.Outside;
        chart.XAxisMinorTickStyle = ChartAxisTickStyle.None;
        chart.ShowXAxisLabels = true;
        chart.XAxisLabelTextColor = null;
        chart.XAxisLabelFontSize = 11;
        chart.XAxisLabelAngle = 0;
        chart.XAxisLineColor = null;
        chart.XAxisLineThickness = 1;
    }

    private static void ClearYAxisBounds(ChartModel chart)
    {
        chart.YAxisMinimum = null;
        chart.YAxisMaximum = null;
        chart.YAxisMajorUnit = null;
        chart.YAxisMinorUnit = null;
        chart.YAxisLogScale = false;
        chart.YAxisNumberFormat = ChartDataLabelNumberFormat.General;
        chart.ShowYAxisMajorGridlines = false;
        chart.ShowYAxisMinorGridlines = false;
        chart.YAxisMajorGridlineColor = null;
        chart.YAxisMinorGridlineColor = null;
        chart.YAxisGridlineThickness = 1;
        chart.YAxisMajorTickStyle = ChartAxisTickStyle.Outside;
        chart.YAxisMinorTickStyle = ChartAxisTickStyle.None;
        chart.ShowYAxisLabels = true;
        chart.YAxisLabelTextColor = null;
        chart.YAxisLabelFontSize = 11;
        chart.YAxisLabelAngle = 0;
        chart.YAxisLineColor = null;
        chart.YAxisLineThickness = 1;
    }

    private static ChartSeriesFormat ClampSeriesFormat(ChartType chartType, ChartSeriesFormat format)
    {
        var supportsMarkers = ChartTypeSupport.SupportsSeriesMarkers(chartType);
        return format with
        {
            StrokeThickness = format.StrokeThickness is { } strokeThickness
                ? Math.Clamp(strokeThickness, 0.5, 10)
                : null,
            MarkerSize = supportsMarkers && format.MarkerSize is { } markerSize
                ? Math.Clamp(markerSize, 1, 30)
                : null,
            DashStyle = NativeJsonValueSanitizer.ValidNullableEnumOrNull(format.DashStyle),
            MarkerStyle = supportsMarkers ? NativeJsonValueSanitizer.ValidNullableEnumOrNull(format.MarkerStyle) : null
        };
    }

    private static bool HasSeriesFormatting(ChartSeriesFormat format) =>
        format.FillColor is not null
        || format.StrokeColor is not null
        || format.StrokeThickness is not null
        || format.DashStyle is not null
        || format.MarkerStyle is not null
        || format.MarkerSize is not null
        || format.FillThemeColor is not null
        || format.StrokeThemeColor is not null;

    private static bool IsSupportedFormulaErrorCode(string? errorCode) =>
        string.Equals(errorCode, ErrorValue.DivByZero.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.Value.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.Ref.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.Name.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.NA.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.Num.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.Null.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.Spill.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.Circular.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, NumberStoredAsTextCode, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, FormulaRefersToBlankCellsCode, StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedConditionalFormat(ConditionalFormat format) =>
        Enum.IsDefined(format.RuleType) && Enum.IsDefined(format.Operator);

    private static bool IsSupportedConditionalFormat(ConditionalFormatDto format) =>
        Enum.IsDefined(format.RuleType) && Enum.IsDefined(format.Operator);

    private static CellStyle? ToCellStyle(CellStyleDto? dto)
    {
        if (dto is null)
            return null;

        return new CellStyle
        {
            FontName = string.IsNullOrWhiteSpace(dto.FontName) ? CellStyle.Default.FontName : dto.FontName,
            FontSize = NativeJsonValueSanitizer.PositiveFiniteOrDefault(dto.FontSize, CellStyle.Default.FontSize),
            Bold = dto.Bold,
            Italic = dto.Italic,
            Underline = dto.Underline,
            Strikethrough = dto.Strikethrough,
            Superscript = dto.Superscript,
            Subscript = dto.Subscript,
            FontColor = dto.FontColor,
            FillColor = dto.FillColor,
            BorderTop = ToCellBorder(dto.BorderTop),
            BorderRight = ToCellBorder(dto.BorderRight),
            BorderBottom = ToCellBorder(dto.BorderBottom),
            BorderLeft = ToCellBorder(dto.BorderLeft),
            NumberFormat = string.IsNullOrWhiteSpace(dto.NumberFormat) ? CellStyle.Default.NumberFormat : dto.NumberFormat,
            HorizontalAlignment = NativeJsonValueSanitizer.ValidEnumOrDefault(dto.HorizontalAlignment, HorizontalAlignment.General),
            VerticalAlignment = NativeJsonValueSanitizer.ValidEnumOrDefault(dto.VerticalAlignment, VerticalAlignment.Bottom),
            WrapText = dto.WrapText,
            ShrinkToFit = dto.ShrinkToFit,
            DoubleUnderline = dto.DoubleUnderline,
            IndentLevel = Math.Clamp(dto.IndentLevel, 0, 15),
            TextRotation = NativeJsonValueSanitizer.ValidTextRotationOrDefault(dto.TextRotation),
            Locked = dto.Locked,
            NativeDifferentialAttributes = dto.NativeDifferentialAttributes,
            NativeDifferentialChildXmls = dto.NativeDifferentialChildXmls,
            NativeDifferentialElementXmls = dto.NativeDifferentialElementXmls
        };
    }

    private static CellStyleDto? FromCellStyle(CellStyle? style)
    {
        if (style is null)
            return null;

        var safeStyle = ToCellStyle(new CellStyleDto
        {
            FontName = style.FontName,
            FontSize = style.FontSize,
            Bold = style.Bold,
            Italic = style.Italic,
            Underline = style.Underline,
            Strikethrough = style.Strikethrough,
            Superscript = style.Superscript,
            Subscript = style.Subscript,
            FontColor = style.FontColor,
            FillColor = style.FillColor,
            BorderTop = FromCellBorder(style.BorderTop),
            BorderRight = FromCellBorder(style.BorderRight),
            BorderBottom = FromCellBorder(style.BorderBottom),
            BorderLeft = FromCellBorder(style.BorderLeft),
            NumberFormat = style.NumberFormat,
            HorizontalAlignment = style.HorizontalAlignment,
            VerticalAlignment = style.VerticalAlignment,
            WrapText = style.WrapText,
            ShrinkToFit = style.ShrinkToFit,
            DoubleUnderline = style.DoubleUnderline,
            IndentLevel = style.IndentLevel,
            TextRotation = style.TextRotation,
            Locked = style.Locked,
            NativeDifferentialAttributes = style.NativeDifferentialAttributes,
            NativeDifferentialChildXmls = style.NativeDifferentialChildXmls,
            NativeDifferentialElementXmls = style.NativeDifferentialElementXmls
        })!;

        return new CellStyleDto
        {
            FontName = safeStyle.FontName,
            FontSize = safeStyle.FontSize,
            Bold = safeStyle.Bold,
            Italic = safeStyle.Italic,
            Underline = safeStyle.Underline,
            Strikethrough = safeStyle.Strikethrough,
            Superscript = safeStyle.Superscript,
            Subscript = safeStyle.Subscript,
            FontColor = safeStyle.FontColor,
            FillColor = safeStyle.FillColor,
            BorderTop = FromCellBorder(safeStyle.BorderTop),
            BorderRight = FromCellBorder(safeStyle.BorderRight),
            BorderBottom = FromCellBorder(safeStyle.BorderBottom),
            BorderLeft = FromCellBorder(safeStyle.BorderLeft),
            NumberFormat = safeStyle.NumberFormat,
            HorizontalAlignment = safeStyle.HorizontalAlignment,
            VerticalAlignment = safeStyle.VerticalAlignment,
            WrapText = safeStyle.WrapText,
            ShrinkToFit = safeStyle.ShrinkToFit,
            DoubleUnderline = safeStyle.DoubleUnderline,
            IndentLevel = safeStyle.IndentLevel,
            TextRotation = safeStyle.TextRotation,
            Locked = safeStyle.Locked,
            NativeDifferentialAttributes = safeStyle.NativeDifferentialAttributes,
            NativeDifferentialChildXmls = safeStyle.NativeDifferentialChildXmls,
            NativeDifferentialElementXmls = safeStyle.NativeDifferentialElementXmls
        };
    }

    private static CellBorder ToCellBorder(CellBorderDto? border) =>
        border is null
            ? default
            : new CellBorder(NativeJsonValueSanitizer.ValidEnumOrDefault(border.Style, BorderStyle.None), border.Color);

    private static CellBorderDto FromCellBorder(CellBorder border) => new()
    {
        Style = NativeJsonValueSanitizer.ValidEnumOrDefault(border.Style, BorderStyle.None),
        Color = border.Color
    };

    private static WorksheetCustomViewState ToWorksheetCustomViewState(CustomViewSheetDto sheetDto)
    {
        var frozenRows = NativeJsonValueSanitizer.ValidFrozenRowsOrZero(sheetDto.FrozenRows);
        var frozenCols = NativeJsonValueSanitizer.ValidFrozenColumnsOrZero(sheetDto.FrozenCols);
        var hasFrozenPanes = frozenRows > 0 || frozenCols > 0;
        return new WorksheetCustomViewState(
            sheetDto.SheetName,
            Enum.IsDefined(sheetDto.ViewMode) ? sheetDto.ViewMode : WorksheetViewMode.Normal,
            frozenRows,
            frozenCols,
            hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidRowPaneOrNull(sheetDto.SplitRow),
            hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidColumnPaneOrNull(sheetDto.SplitColumn),
            sheetDto.ShowGridlines ?? true,
            sheetDto.ShowHeadings ?? true,
            sheetDto.ShowRulers ?? true,
            NativeJsonValueSanitizer.ValidZoomPercentOrDefault(sheetDto.ZoomPercent),
            sheetDto.ShowFormulas ?? false);
    }

    private static CustomViewSheetDto ToCustomViewSheetDto(WorksheetCustomViewState state)
    {
        var frozenRows = NativeJsonValueSanitizer.ValidFrozenRowsOrZero(state.FrozenRows);
        var frozenCols = NativeJsonValueSanitizer.ValidFrozenColumnsOrZero(state.FrozenCols);
        var hasFrozenPanes = frozenRows > 0 || frozenCols > 0;
        return new CustomViewSheetDto
        {
            SheetName = state.SheetName,
            ViewMode = NativeJsonValueSanitizer.ValidEnumOrDefault(state.ViewMode, WorksheetViewMode.Normal),
            FrozenRows = frozenRows,
            FrozenCols = frozenCols,
            SplitRow = hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidRowPaneOrNull(state.SplitRow),
            SplitColumn = hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidColumnPaneOrNull(state.SplitColumn),
            ShowGridlines = state.ShowGridlines,
            ShowHeadings = state.ShowHeadings,
            ShowRulers = state.ShowRulers,
            ZoomPercent = NativeJsonValueSanitizer.ValidZoomPercentOrDefault(state.ZoomPercent),
            ShowFormulas = state.ShowFormulas
        };
    }

    private static WorksheetPhoneticProperties? ToWorksheetPhoneticProperties(WorksheetPhoneticPropertiesDto? dto)
    {
        if (dto is null)
            return null;

        var fontId = string.IsNullOrWhiteSpace(dto.FontId) ? null : dto.FontId;
        var type = string.IsNullOrWhiteSpace(dto.Type) ? null : dto.Type;
        var alignment = string.IsNullOrWhiteSpace(dto.Alignment) ? null : dto.Alignment;
        return fontId is null && type is null && alignment is null
            ? null
            : new WorksheetPhoneticProperties(fontId, type, alignment);
    }

    private static WorksheetPhoneticPropertiesDto? ToWorksheetPhoneticPropertiesDto(WorksheetPhoneticProperties? properties)
    {
        if (properties is null)
            return null;

        var fontId = string.IsNullOrWhiteSpace(properties.FontId) ? null : properties.FontId;
        var type = string.IsNullOrWhiteSpace(properties.Type) ? null : properties.Type;
        var alignment = string.IsNullOrWhiteSpace(properties.Alignment) ? null : properties.Alignment;
        return fontId is null && type is null && alignment is null
            ? null
            : new WorksheetPhoneticPropertiesDto
            {
                FontId = fontId,
                Type = type,
                Alignment = alignment
            };
    }

    private static void NormalizePictureCrop(PictureModel picture)
    {
        if (picture.CropLeft + picture.CropRight >= 1)
        {
            picture.CropLeft = 0;
            picture.CropRight = 0;
        }

        if (picture.CropTop + picture.CropBottom >= 1)
        {
            picture.CropTop = 0;
            picture.CropBottom = 0;
        }
    }

    private static ChartPointDataLabelFormat ClampPointDataLabelFormat(ChartPointDataLabelFormat format) =>
        format with
        {
            BorderThickness = format.BorderThickness is { } borderThickness
                ? Math.Clamp(borderThickness, 0, 10)
                : null,
            FontSize = format.FontSize is { } fontSize
                ? Math.Clamp(fontSize, 6, 72)
                : null
        };

    private static bool HasPointDataLabelFormatting(ChartPointDataLabelFormat format) =>
        format.FillColor is not null
        || format.BorderColor is not null
        || format.BorderThickness is not null
        || format.TextColor is not null
        || format.FontSize is not null
        || format.FillThemeColor is not null
        || format.BorderThemeColor is not null
        || format.TextThemeColor is not null;

    private static string FormatColor(CellColor color) => NativeJsonColorMapper.FormatColor(color);

    private static CellColor? ParseColor(string text) => NativeJsonColorMapper.ParseColor(text);

    private static WorksheetRepeatRange? ToRepeatRange(RepeatRangeDto? dto) =>
        dto is null ? null : new WorksheetRepeatRange(dto.Start, dto.End);

    private static RepeatRangeDto? FromRepeatRange(WorksheetRepeatRange? range) =>
        range is null ? null : new RepeatRangeDto { Start = range.Value.Start, End = range.Value.End };

    private static RepeatRangeDto? FromValidRepeatRange(WorksheetRepeatRange? range, uint max) =>
        range is { } value && value.Start >= 1 && value.End >= value.Start && value.End <= max
            ? new RepeatRangeDto { Start = value.Start, End = value.End }
            : null;

    private static PageMarginsDto FromPageMargins(WorksheetPageMargins margins) =>
        new()
        {
            Left = margins.Left,
            Right = margins.Right,
            Top = margins.Top,
            Bottom = margins.Bottom
        };

    private static WorksheetHeaderFooter ToHeaderFooter(HeaderFooterDto? dto) =>
        dto is null
            ? new WorksheetHeaderFooter("", "", "")
            : new WorksheetHeaderFooter(dto.Left ?? "", dto.Center ?? "", dto.Right ?? "");

    private static HeaderFooterDto FromHeaderFooter(WorksheetHeaderFooter value) =>
        new() { Left = value.Left, Center = value.Center, Right = value.Right };

    private static WorkbookThemeColorReference? ToThemeColorReference(ThemeColorReferenceDto? dto) =>
        NativeJsonColorMapper.ToThemeColorReference(dto);

    private static ThemeColorReferenceDto? FromThemeColorReference(WorkbookThemeColorReference? reference) =>
        NativeJsonColorMapper.FromThemeColorReference(reference);

    private static WorkbookTheme ToWorkbookTheme(WorkbookThemeDto dto)
    {
        var theme = WorkbookTheme.Office
            .WithName(dto.Name ?? WorkbookTheme.Office.Name)
            .WithFonts(dto.MajorFontName ?? WorkbookTheme.Office.MajorFontName,
                dto.MinorFontName ?? WorkbookTheme.Office.MinorFontName)
            .WithEffects(dto.EffectsName ?? WorkbookTheme.Office.EffectsName);

        foreach (var color in dto.Colors ?? [])
        {
            if (Enum.IsDefined(color.Slot) && ParseColor(color.Color ?? "") is { } parsed)
                theme = theme.WithColor(color.Slot, parsed);
        }

        return theme;
    }

    private static WorkbookThemeDto FromWorkbookTheme(WorkbookTheme theme) =>
        new()
        {
            Name = theme.Name,
            MajorFontName = theme.MajorFontName,
            MinorFontName = theme.MinorFontName,
            EffectsName = theme.EffectsName,
            Colors = Enum.GetValues<WorkbookThemeColorSlot>()
                .Select(slot => new WorkbookThemeColorDto
                {
                    Slot = slot,
                    Color = FormatColor(theme.GetColor(slot))
                })
                .ToList()
        };

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
        public ChartType Type { get; set; } = ChartType.Column;
        public string? DataRange { get; set; }
        public bool IsVisible { get; set; } = true;
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
