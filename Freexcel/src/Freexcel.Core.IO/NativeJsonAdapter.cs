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

    private static bool IsSupportedConditionalFormat(ConditionalFormat format) =>
        Enum.IsDefined(format.RuleType) && Enum.IsDefined(format.Operator);

    private static bool IsSupportedConditionalFormat(ConditionalFormatDto format) =>
        Enum.IsDefined(format.RuleType) && Enum.IsDefined(format.Operator);

    private static string FormatColor(CellColor color) => NativeJsonColorMapper.FormatColor(color);

    private static CellColor? ParseColor(string text) => NativeJsonColorMapper.ParseColor(text);

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
