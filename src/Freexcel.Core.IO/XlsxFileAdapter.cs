using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using ClosedXML.Excel;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

/// <summary>
/// XLSX file adapter using ClosedXML.
/// Supports standard Excel .xlsx files.
/// </summary>
public sealed class XlsxFileAdapter : IFileAdapter
{
    private static readonly ConditionalWeakTable<Workbook, XlsxSourcePackage> SourcePackages = new();
    private static readonly HashSet<string> ModeledPrintOptionsAttributes = new(StringComparer.Ordinal)
    {
        "gridLines",
        "headings",
        "horizontalCentered",
        "verticalCentered"
    };
    private static readonly HashSet<string> ModeledPageMarginsAttributes = new(StringComparer.Ordinal)
    {
        "left",
        "right",
        "top",
        "bottom"
    };
    private static readonly HashSet<string> ModeledPageSetupAttributes = new(StringComparer.Ordinal)
    {
        "paperSize",
        "scale",
        "firstPageNumber",
        "fitToWidth",
        "fitToHeight",
        "pageOrder",
        "orientation",
        "useFirstPageNumber",
        "blackAndWhite",
        "draft",
        "cellComments",
        "errors",
        "horizontalDpi",
        "verticalDpi"
    };

    private const int CategoryAxisId = 48650112;
    private const int ValueAxisId = 48672768;
    private const int SecondaryValueAxisId = 48672769;

    public string Extension => ".xlsx";
    public string FormatName => "Excel Workbook";

    public Workbook Load(Stream stream)
    {
        using var packageStream = new MemoryStream();
        stream.CopyTo(packageStream);

        packageStream.Position = 0;
        var sheetXmlLayout = LoadSheetXmlLayout(packageStream);
        packageStream.Position = 0;
        var workbookTheme = XlsxWorkbookThemeReader.Load(packageStream);
        packageStream.Position = 0;
        var workbookProtection = XlsxWorkbookMetadataReader.LoadProtection(packageStream);
        packageStream.Position = 0;
        var calculationProperties = XlsxWorkbookMetadataReader.LoadCalculationProperties(packageStream);
        packageStream.Position = 0;
        var numberFormatCatalog = XlsxWorkbookMetadataReader.LoadNumberFormatCatalog(packageStream);
        packageStream.Position = 0;
        var pivotMetadata = XlsxPivotTableReader.Load(packageStream, numberFormatCatalog);
        packageStream.Position = 0;
        var slicerTimelineMetadata = XlsxSlicerTimelineMetadataReader.Load(packageStream);
        packageStream.Position = 0;
        var externalLinkMetadata = XlsxExternalLinkMetadataReader.Load(packageStream);
        packageStream.Position = 0;
        var structuredTableMetadata = XlsxStructuredTableMetadataReader.Load(packageStream);
        packageStream.Position = 0;
        var pivotTableStyleMetadata = XlsxPivotTableStyleMetadataReader.Load(packageStream);
        packageStream.Position = 0;
        var xlsxCustomViews = XlsxWorkbookMetadataReader.LoadCustomViews(packageStream);

        packageStream.Position = 0;
        using var closedXmlPackageStream = XlsxClosedXmlLoadPackageSanitizer.Create(packageStream);
        using var xlWorkbook = new XLWorkbook(closedXmlPackageStream);
        var workbook = new Workbook("Untitled");
        SourcePackages.Remove(workbook);
        SourcePackages.Add(workbook, new XlsxSourcePackage(packageStream.ToArray()));
        workbook.Theme = workbookTheme;
        workbook.IsStructureProtected = workbookProtection.IsStructureProtected;
        workbook.StructureProtectionPassword = workbookProtection.PasswordHash;
        workbook.CalculationMode = xlWorkbook.CalculateMode == XLCalculateMode.Manual
            ? WorkbookCalculationMode.Manual
            : WorkbookCalculationMode.Automatic;
        if (calculationProperties.Mode is { } calculationMode)
            workbook.CalculationMode = calculationMode;
        workbook.FullCalculationOnLoad = calculationProperties.FullCalculationOnLoad;
        workbook.ForceFullCalculation = calculationProperties.ForceFullCalculation;
        workbook.IterativeCalculation = calculationProperties.IterativeCalculation;
        workbook.MaxCalculationIterations = calculationProperties.MaxIterations;
        workbook.MaxCalculationChange = calculationProperties.MaxChange;
        foreach (var (numberFormatId, formatCode) in numberFormatCatalog)
            workbook.NumberFormatCatalog[numberFormatId] = formatCode;
        foreach (var pivotCache in pivotMetadata.PivotCaches)
            workbook.PivotCaches.Add(pivotCache);
        foreach (var slicer in slicerTimelineMetadata.Slicers)
            workbook.Slicers.Add(slicer);
        foreach (var timeline in slicerTimelineMetadata.Timelines)
            workbook.Timelines.Add(timeline);
        foreach (var externalLink in externalLinkMetadata)
            workbook.ExternalLinks.Add(externalLink);
        foreach (var pivotTableStyle in pivotTableStyleMetadata)
            workbook.PivotTableStyles.Add(pivotTableStyle);

        var loadedScenarioNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var customViewStatesById = new Dictionary<string, List<WorksheetCustomViewState>>(StringComparer.OrdinalIgnoreCase);
        foreach (var xlSheet in xlWorkbook.Worksheets)
        {
            var sheet = workbook.AddSheet(xlSheet.Name);
            sheet.IsVeryHidden = xlSheet.Visibility == XLWorksheetVisibility.VeryHidden;
            sheet.IsHidden = xlSheet.Visibility != XLWorksheetVisibility.Visible;
            if (xlSheet.TabColor.HasValue)
            {
                var color = xlSheet.TabColor.Color;
                sheet.TabColor = new CellColor(color.R, color.G, color.B);
            }

            foreach (var xlCell in xlSheet.CellsUsed())
            {
                var addr = new CellAddress(sheet.Id, (uint)xlCell.Address.RowNumber, (uint)xlCell.Address.ColumnNumber);

                Cell cell;
                if (xlCell.HasFormula)
                {
                    cell = Cell.FromFormula(XlsxClosedXmlCellMapper.NormalizeFormulaText(xlCell.FormulaA1));
                    // Preserve the cached formula result so callers see the last-calculated value
                    // without needing to recalculate immediately.
                    var cached = XlsxClosedXmlCellMapper.MapValue(xlCell);
                    if (cached is not BlankValue)
                        cell.Value = cached;
                }
                else
                {
                    cell = Cell.FromValue(XlsxClosedXmlCellMapper.MapValue(xlCell));
                }

                var style = XlsxClosedXmlCellMapper.MapStyle(xlCell.Style, workbook.Theme);
                if (!style.Equals(CellStyle.Default))
                    cell.StyleId = workbook.RegisterStyle(style);

                sheet.SetCell(addr, cell);
            }

            foreach (var xlCell in xlSheet.CellsUsed(XLCellsUsedOptions.All))
            {
                try
                {
                    var comment = xlCell.GetComment();
                    if (comment.Length == 0) continue;

                    var addr = new CellAddress(sheet.Id, (uint)xlCell.Address.RowNumber, (uint)xlCell.Address.ColumnNumber);
                    sheet.Comments[addr] = comment.Text;
                }
                catch
                {
                    // Skip cells without comments or comments ClosedXML cannot expose.
                }
            }

            foreach (var hyperlink in xlSheet.Hyperlinks)
            {
                try
                {
                    var cell = hyperlink.Cell;
                    if (cell is null) continue;

                    var target = hyperlink.ExternalAddress?.ToString() ??
                                 hyperlink.InternalAddress ??
                                 string.Empty;
                    if (string.IsNullOrEmpty(target)) continue;

                    var addr = new CellAddress(sheet.Id, (uint)cell.Address.RowNumber, (uint)cell.Address.ColumnNumber);
                    sheet.Hyperlinks[addr] = target;
                }
                catch
                {
                    // Skip hyperlinks ClosedXML cannot expose.
                }
            }

            foreach (var row in xlSheet.RowsUsed(XLCellsUsedOptions.AllFormats))
            {
                var rowNumber = (uint)row.RowNumber();
                if (row.Height > 0)
                    sheet.RowHeights[rowNumber] = row.Height * (96.0 / 72.0);
                if (row.IsHidden)
                    sheet.HiddenRows.Add(rowNumber);
            }

            foreach (var col in xlSheet.ColumnsUsed(XLCellsUsedOptions.AllFormats))
            {
                var colNumber = (uint)col.ColumnNumber();
                if (col.Width > 0)
                    sheet.ColumnWidths[colNumber] = col.Width;
                if (col.IsHidden)
                    sheet.HiddenCols.Add(colNumber);
            }

            if (sheetXmlLayout.TryGetValue(xlSheet.Name, out var layout))
            {
                sheet.HiddenRows.UnionWith(layout.HiddenRows);
                sheet.HiddenCols.UnionWith(layout.HiddenCols);
                sheet.IsProtected = layout.IsProtected;
                sheet.ProtectionPassword = layout.ProtectionPasswordHash;
                foreach (var range in layout.AllowEditRanges)
                    sheet.AllowEditRanges.Add(new GridRange(
                        new CellAddress(sheet.Id, range.Start.Row, range.Start.Col),
                        new CellAddress(sheet.Id, range.End.Row, range.End.Col)));
                sheet.ViewMode = layout.ViewMode;
                sheet.ShowGridlines = layout.ShowGridlines;
                sheet.ShowHeadings = layout.ShowHeadings;
                sheet.ShowRulers = layout.ShowRulers;
                sheet.ZoomPercent = layout.ZoomPercent;
                sheet.ShowFormulas = layout.ShowFormulas;
                sheet.BackgroundImage = layout.BackgroundImage;
                sheet.CodeName = layout.CodeName;

                foreach (var (rowNum, level) in layout.RowOutlineLevels)
                    sheet.RowOutlineLevels[rowNum] = level;
                foreach (var (colNum, level) in layout.ColOutlineLevels)
                    sheet.ColOutlineLevels[colNum] = level;
                sheet.GroupHiddenRows.UnionWith(layout.GroupHiddenRows);
                sheet.GroupHiddenCols.UnionWith(layout.GroupHiddenCols);
                foreach (var chartPart in layout.ChartParts)
                {
                    if (XlsxChartPartReader.TryReadSupportedChart(chartPart.Xml, sheet.Id, out var chart))
                    {
                        chart.Name = chartPart.Name;
                    XlsxDrawingAnchorApplier.ApplyToChart(chart, chartPart.Anchor, sheet);
                        ApplyChartExternalDataRelationshipMetadata(chart, chartPart);
                        sheet.Charts.Add(chart);
                    }
                }
                foreach (var picturePart in layout.PictureParts)
                {
                    var picture = new PictureModel
                    {
                        Anchor = new CellAddress(
                            sheet.Id,
                            picturePart.Anchor?.FromRowZeroBased + 1 ?? 1,
                            picturePart.Anchor?.FromColumnZeroBased + 1 ?? 1),
                        Kind = PictureKind.Image,
                        Name = picturePart.Name,
                        ImageBytes = picturePart.ImageBytes.ToArray(),
                        ContentType = picturePart.ContentType,
                        AltText = picturePart.AltText,
                        CropLeft = picturePart.CropLeft,
                        CropTop = picturePart.CropTop,
                        CropRight = picturePart.CropRight,
                        CropBottom = picturePart.CropBottom
                    };
                    XlsxDrawingAnchorApplier.ApplyToPicture(picture, picturePart.Anchor, sheet);
                    sheet.Pictures.Add(picture);
                }
                foreach (var textBoxPart in layout.TextBoxParts)
                {
                    var textBox = new TextBoxModel
                    {
                        Anchor = new CellAddress(
                            sheet.Id,
                            textBoxPart.Anchor?.FromRowZeroBased + 1 ?? 1,
                            textBoxPart.Anchor?.FromColumnZeroBased + 1 ?? 1),
                        Text = textBoxPart.Text,
                        Name = textBoxPart.Name,
                        AltText = textBoxPart.AltText,
                        RotationDegrees = textBoxPart.RotationDegrees,
                        FillColor = textBoxPart.FillColor,
                        OutlineColor = textBoxPart.OutlineColor
                    };
                XlsxDrawingAnchorApplier.ApplyToTextBox(textBox, textBoxPart.Anchor, sheet);
                    sheet.TextBoxes.Add(textBox);
                }
                foreach (var shapePart in layout.ShapeParts)
                {
                    var shape = new DrawingShapeModel
                    {
                        Anchor = new CellAddress(
                            sheet.Id,
                            shapePart.Anchor?.FromRowZeroBased + 1 ?? 1,
                            shapePart.Anchor?.FromColumnZeroBased + 1 ?? 1),
                        Kind = shapePart.Kind,
                        Name = shapePart.Name,
                        AltText = shapePart.AltText,
                        RotationDegrees = shapePart.RotationDegrees,
                        FillColor = shapePart.FillColor,
                        OutlineColor = shapePart.OutlineColor,
                        GradientFillEndColor = shapePart.GradientFillEndColor,
                        HasShadowEffect = shapePart.HasShadowEffect
                    };
                XlsxDrawingAnchorApplier.ApplyToShape(shape, shapePart.Anchor, sheet);
                    sheet.DrawingShapes.Add(shape);
                }
                foreach (var sparkline in layout.Sparklines)
                {
                    sheet.Sparklines.Add(new SparklineModel
                    {
                        DataRange = new GridRange(
                            new CellAddress(sheet.Id, sparkline.DataRange.Start.Row, sparkline.DataRange.Start.Col),
                            new CellAddress(sheet.Id, sparkline.DataRange.End.Row, sparkline.DataRange.End.Col)),
                        Location = new CellAddress(sheet.Id, sparkline.Location.Row, sparkline.Location.Col),
                        Kind = sparkline.Kind
                    });
                }
                foreach (var conditionalFormat in layout.AdvancedConditionalFormats)
                    sheet.ConditionalFormats.Add(RemapConditionalFormat(conditionalFormat, sheet.Id));
                foreach (var ignoredErrorAddress in layout.IgnoredErrors.ExpandedCells)
                {
                    var address = new CellAddress(sheet.Id, ignoredErrorAddress.Row, ignoredErrorAddress.Col);
                    var cell = sheet.GetCell(address);
                    if (cell is null)
                    {
                        cell = Cell.FromValue(BlankValue.Instance);
                        sheet.SetCell(address, cell);
                    }

                    cell.IgnoreFormulaError = true;
                }
                if (layout.IgnoredErrors.ExistingCellOnlyRanges.Count > 0)
                {
                    foreach (var (address, cell) in sheet.GetUsedCells())
                    {
                        var comparableAddress = new CellAddress(
                            layout.IgnoredErrors.ExistingCellOnlyRanges[0].Start.Sheet,
                            address.Row,
                            address.Col);
                        if (layout.IgnoredErrors.ExistingCellOnlyRanges.Any(range => range.Contains(comparableAddress)))
                            cell.IgnoreFormulaError = true;
                    }
                }
                foreach (var watchedCell in layout.CellWatches)
                {
                    var address = new CellAddress(sheet.Id, watchedCell.Row, watchedCell.Col);
                    if (!workbook.WatchedCells.Contains(address))
                        workbook.WatchedCells.Add(address);
                }
                foreach (var scenario in layout.Scenarios)
                {
                    var remappedScenario = new WorkbookScenario(
                        scenario.Name,
                        scenario.ChangingCells
                            .Select(change => new ScenarioCellValue(
                                new CellAddress(sheet.Id, change.Address.Row, change.Address.Col),
                                change.Value))
                            .ToList());

                    if (loadedScenarioNames.Add(remappedScenario.Name))
                    {
                        workbook.Scenarios.Add(remappedScenario);
                        continue;
                    }

                    var existingIndex = workbook.Scenarios.FindIndex(existing =>
                        string.Equals(existing.Name, remappedScenario.Name, StringComparison.OrdinalIgnoreCase));
                    if (existingIndex >= 0)
                    {
                        workbook.Scenarios[existingIndex] = workbook.Scenarios[existingIndex] with
                        {
                            ChangingCells = workbook.Scenarios[existingIndex].ChangingCells
                                .Concat(remappedScenario.ChangingCells)
                                .Distinct()
                                .ToList()
                        };
                    }
                }
                foreach (var customView in layout.CustomViews)
                {
                    if (!customViewStatesById.TryGetValue(customView.Id, out var states))
                    {
                        states = [];
                        customViewStatesById[customView.Id] = states;
                    }

                    states.Add(customView.State with { SheetName = sheet.Name });
                }
                foreach (var property in layout.CustomProperties)
                    sheet.CustomProperties.Add(property);
                sheet.FullCalculationOnLoad = layout.FullCalculationOnLoad;
                sheet.PhoneticProperties = layout.PhoneticProperties;
            }
            if (pivotMetadata.PivotTablesBySheetName.TryGetValue(xlSheet.Name, out var pivotTables))
            {
                foreach (var pivotTable in pivotTables)
                    sheet.PivotTables.Add(pivotTable.ToPivotTableModel(sheet.Id));
            }
            if (structuredTableMetadata.TablesBySheetName.TryGetValue(xlSheet.Name, out var structuredTables))
            {
                foreach (var structuredTable in structuredTables)
                {
                    var table = XlsxStructuredTableModelMapper.ToModel(structuredTable, sheet.Id);
                    sheet.StructuredTables.Add(table);
                    XlsxStructuredTableModelMapper.MaterializeFilters(sheet, table);
                }
            }

            if (layout?.PaneState is "frozen" or "frozenSplit")
            {
                sheet.FrozenRows = layout.PaneRowSplit ?? 0;
                sheet.FrozenCols = layout.PaneColumnSplit ?? 0;
            }
            else
            {
                var splitRow = xlSheet.SheetView.SplitRow > 0
                    ? (uint)xlSheet.SheetView.SplitRow
                    : layout?.PaneRowSplit;
                var splitColumn = xlSheet.SheetView.SplitColumn > 0
                    ? (uint)xlSheet.SheetView.SplitColumn
                    : layout?.PaneColumnSplit;
                if (splitRow > 0)
                    sheet.SplitRow = splitRow;
                if (splitColumn > 0)
                    sheet.SplitColumn = splitColumn;
            }
            sheet.ViewTopRow = layout?.ViewTopRow;
            sheet.ViewLeftCol = layout?.ViewLeftCol;
            sheet.ActiveRow = layout?.ActiveRow;
            sheet.ActiveCol = layout?.ActiveCol;

            try { XlsxWorksheetPageSetupMapper.LoadPrintArea(xlSheet, sheet); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Print-area load failed: {ex.Message}"); }

            sheet.PageOrientation = xlSheet.PageSetup.PageOrientation == XLPageOrientation.Landscape
                ? WorksheetPageOrientation.Landscape
                : WorksheetPageOrientation.Portrait;
            sheet.PaperSize = xlSheet.PageSetup.PaperSize switch
            {
                XLPaperSize.LetterPaper => WorksheetPaperSize.Letter,
                XLPaperSize.LegalPaper => WorksheetPaperSize.Legal,
                _ => WorksheetPaperSize.A4
            };
            sheet.PageMargins = new WorksheetPageMargins(
                xlSheet.PageSetup.Margins.Left,
                xlSheet.PageSetup.Margins.Right,
                xlSheet.PageSetup.Margins.Top,
                xlSheet.PageSetup.Margins.Bottom);
            sheet.HeaderMargin = xlSheet.PageSetup.Margins.Header;
            sheet.FooterMargin = xlSheet.PageSetup.Margins.Footer;
            sheet.PrintGridlines = xlSheet.PageSetup.ShowGridlines;
            sheet.PrintHeadings = xlSheet.PageSetup.ShowRowAndColumnHeadings;
            sheet.CenterHorizontallyOnPage = xlSheet.PageSetup.CenterHorizontally;
            sheet.CenterVerticallyOnPage = xlSheet.PageSetup.CenterVertically;
            sheet.PageOrder = xlSheet.PageSetup.PageOrder == XLPageOrderValues.OverThenDown
                ? WorksheetPageOrder.OverThenDown
                : WorksheetPageOrder.DownThenOver;
            sheet.FirstPageNumber = xlSheet.PageSetup.FirstPageNumber == 0
                ? null
                : xlSheet.PageSetup.FirstPageNumber;
            sheet.PrintBlackAndWhite = xlSheet.PageSetup.BlackAndWhite;
            sheet.PrintDraftQuality = xlSheet.PageSetup.DraftQuality;
            sheet.PrintQualityDpi = xlSheet.PageSetup.HorizontalDpi > 0
                ? xlSheet.PageSetup.HorizontalDpi
                : xlSheet.PageSetup.VerticalDpi > 0 ? xlSheet.PageSetup.VerticalDpi : null;
            sheet.PrintErrorValue = XlsxWorksheetPageSetupMapper.FromPrintErrorValue(xlSheet.PageSetup.PrintErrorValue);
            sheet.PrintComments = XlsxWorksheetPageSetupMapper.FromPrintComments(xlSheet.PageSetup.ShowComments);
            sheet.DifferentFirstPageHeaderFooter = xlSheet.PageSetup.DifferentFirstPageOnHF;
            sheet.DifferentOddEvenHeaderFooter = xlSheet.PageSetup.DifferentOddEvenPagesOnHF;
            sheet.HeaderFooterScaleWithDocument = xlSheet.PageSetup.ScaleHFWithDocument;
            sheet.HeaderFooterAlignWithMargins = xlSheet.PageSetup.AlignHFWithMargins;
            sheet.PageHeader = new WorksheetHeaderFooter(
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Header.Left, XLHFOccurrence.OddPages, XLHFOccurrence.AllPages)),
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Header.Center, XLHFOccurrence.OddPages, XLHFOccurrence.AllPages)),
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Header.Right, XLHFOccurrence.OddPages, XLHFOccurrence.AllPages)));
            sheet.PageFooter = new WorksheetHeaderFooter(
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Footer.Left, XLHFOccurrence.OddPages, XLHFOccurrence.AllPages)),
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Footer.Center, XLHFOccurrence.OddPages, XLHFOccurrence.AllPages)),
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Footer.Right, XLHFOccurrence.OddPages, XLHFOccurrence.AllPages)));
            sheet.FirstPageHeader = new WorksheetHeaderFooter(
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Header.Left, XLHFOccurrence.FirstPage)),
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Header.Center, XLHFOccurrence.FirstPage)),
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Header.Right, XLHFOccurrence.FirstPage)));
            sheet.FirstPageFooter = new WorksheetHeaderFooter(
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Footer.Left, XLHFOccurrence.FirstPage)),
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Footer.Center, XLHFOccurrence.FirstPage)),
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Footer.Right, XLHFOccurrence.FirstPage)));
            sheet.EvenPageHeader = new WorksheetHeaderFooter(
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Header.Left, XLHFOccurrence.EvenPages)),
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Header.Center, XLHFOccurrence.EvenPages)),
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Header.Right, XLHFOccurrence.EvenPages)));
            sheet.EvenPageFooter = new WorksheetHeaderFooter(
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Footer.Left, XLHFOccurrence.EvenPages)),
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Footer.Center, XLHFOccurrence.EvenPages)),
                XlsxWorksheetPageSetupMapper.FromHeaderFooterText(XlsxWorksheetPageSetupMapper.GetHeaderFooterText(xlSheet.PageSetup.Footer.Right, XLHFOccurrence.EvenPages)));
            if (xlSheet.PageSetup.FirstRowToRepeatAtTop > 0 && xlSheet.PageSetup.LastRowToRepeatAtTop > 0)
            {
                sheet.PrintTitleRows = new WorksheetRepeatRange(
                    (uint)xlSheet.PageSetup.FirstRowToRepeatAtTop,
                    (uint)xlSheet.PageSetup.LastRowToRepeatAtTop);
            }
            if (xlSheet.PageSetup.FirstColumnToRepeatAtLeft > 0 && xlSheet.PageSetup.LastColumnToRepeatAtLeft > 0)
            {
                sheet.PrintTitleColumns = new WorksheetRepeatRange(
                    (uint)xlSheet.PageSetup.FirstColumnToRepeatAtLeft,
                    (uint)xlSheet.PageSetup.LastColumnToRepeatAtLeft);
            }
            foreach (var rowBreak in xlSheet.PageSetup.RowBreaks)
                if (rowBreak > 0) sheet.RowPageBreaks.Add((uint)rowBreak);
            foreach (var columnBreak in xlSheet.PageSetup.ColumnBreaks)
                if (columnBreak > 0) sheet.ColumnPageBreaks.Add((uint)columnBreak);
            sheet.ScaleToFit = xlSheet.PageSetup.PagesWide > 0 || xlSheet.PageSetup.PagesTall > 0
                ? new WorksheetScaleToFit(null,
                    xlSheet.PageSetup.PagesWide > 0 ? xlSheet.PageSetup.PagesWide : null,
                    xlSheet.PageSetup.PagesTall > 0 ? xlSheet.PageSetup.PagesTall : null)
                : new WorksheetScaleToFit(xlSheet.PageSetup.Scale, null, null);

            // Load CellIs conditional format rules (best-effort; skip anything we can't map)
            try { XlsxConditionalFormatClosedXmlMapper.Load(xlSheet, sheet, workbook.Theme, XlsxClosedXmlCellMapper.MapStyle); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] CF load failed: {ex.Message}"); }

            // Load data validation rules (best-effort)
            try { XlsxDataValidationClosedXmlMapper.Load(xlSheet, sheet); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] DV load failed: {ex.Message}"); }
            if (layout is not null)
                XlsxDataValidationNativeMetadataMapper.Apply(sheet, layout.DataValidationNativeMetadata);

            // Load merged regions (best-effort)
            try { LoadMergedRegions(xlSheet, sheet); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Merge load failed: {ex.Message}"); }
        }

        ResolvePivotChartCacheBindings(workbook);

        // Load named ranges (best-effort; skip any we cannot map)
        try { XlsxNamedRangeMapper.Load(xlWorkbook, workbook); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Named-range load failed: {ex.Message}"); }

        foreach (var customView in xlsxCustomViews)
        {
            if (customViewStatesById.TryGetValue(customView.Id, out var states) && states.Count > 0)
                workbook.CustomViews.Add(new WorkbookCustomView(customView.Name, states, customView.Id));
        }

        return workbook;
    }

    private static void ResolvePivotChartCacheBindings(Workbook workbook)
    {
        foreach (var chartSheet in workbook.Sheets)
        {
            foreach (var chart in chartSheet.Charts.Where(chart =>
                         chart.IsPivotChart &&
                         chart.PivotCacheId is null &&
                         !string.IsNullOrWhiteSpace(chart.PivotTableName)))
            {
                var sourceSheet = string.IsNullOrWhiteSpace(chart.PivotSourceSheetName)
                    ? chartSheet
                    : workbook.Sheets.FirstOrDefault(sheet =>
                        string.Equals(sheet.Name, chart.PivotSourceSheetName, StringComparison.OrdinalIgnoreCase));
                var pivot = sourceSheet?.PivotTables.FirstOrDefault(pivot =>
                    string.Equals(pivot.Name, chart.PivotTableName, StringComparison.OrdinalIgnoreCase));
                if (pivot is not null)
                    chart.PivotCacheId = pivot.CacheId;
            }
        }
    }

    private sealed record SheetXmlLayout(
        HashSet<uint> HiddenRows,
        HashSet<uint> HiddenCols,
        bool IsProtected,
        string? ProtectionPasswordHash,
        IReadOnlyList<GridRange> AllowEditRanges,
        WorksheetViewMode ViewMode,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        int ZoomPercent,
        bool ShowFormulas,
        bool FullCalculationOnLoad,
        WorksheetPhoneticProperties? PhoneticProperties,
        string? PaneState,
        uint? PaneRowSplit,
        uint? PaneColumnSplit,
        uint? ViewTopRow,
        uint? ViewLeftCol,
        uint? ActiveRow,
        uint? ActiveCol,
        WorksheetBackgroundImage? BackgroundImage,
        Dictionary<uint, int> RowOutlineLevels,
        Dictionary<uint, int> ColOutlineLevels,
        HashSet<uint> GroupHiddenRows,
        HashSet<uint> GroupHiddenCols,
        IReadOnlyList<XlsxChartPackagePart> ChartParts,
        IReadOnlyList<XlsxPicturePackagePart> PictureParts,
        IReadOnlyList<XlsxTextBoxPackagePart> TextBoxParts,
        IReadOnlyList<XlsxShapePackagePart> ShapeParts,
        IReadOnlyList<SparklineModel> Sparklines,
        IReadOnlyList<ConditionalFormat> AdvancedConditionalFormats,
        IReadOnlyList<DataValidationNativeMetadata> DataValidationNativeMetadata,
        IgnoredErrorLayout IgnoredErrors,
        IReadOnlyList<CellAddress> CellWatches,
        IReadOnlyList<WorkbookScenario> Scenarios,
        IReadOnlyList<XlsxWorksheetCustomViewState> CustomViews,
        IReadOnlyList<WorksheetCustomProperty> CustomProperties,
        string? CodeName);

    private static Dictionary<string, SheetXmlLayout> LoadSheetXmlLayout(Stream xlsxStream)
    {
        var result = new Dictionary<string, SheetXmlLayout>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbookEntry is null || relsEntry is null)
                return result;

            var workbookXml = LoadXml(workbookEntry);
            var relsXml = LoadXml(relsEntry);

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var relTargets = XlsxRelationshipReader.ReadTargets(
                relsXml,
                packageRelNs,
                XlsxPackagePath.NormalizeWorkbookTarget);

            var differentialStyles = XlsxDifferentialStyleReader.ReadAll(archive, workbookNs);

            foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
            {
                var name = sheetElement.Attribute("name")?.Value;
                var relId = sheetElement.Attribute(relNs + "id")?.Value;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId))
                    continue;
                if (!relTargets.TryGetValue(relId, out var worksheetPath))
                    continue;

                var worksheetEntry = archive.GetEntry(worksheetPath);
                if (worksheetEntry is null)
                    continue;

                result[name] = ReadHiddenSheetLayout(archive, worksheetPath, worksheetEntry, differentialStyles);
            }
        }
        catch
        {
            // Worksheet XML metadata is best-effort; ClosedXML still loads workbook content.
        }

        return result;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument document)
        => XlsxPackageXmlEditor.ReplaceXml(archive, entryName, document);

    private static Dictionary<string, string> LoadRelationshipTargets(
        ZipArchive archive,
        string relsPath,
        string sourcePart,
        XNamespace packageRelNs) =>
        XlsxRelationshipReader.LoadTargets(archive, relsPath, sourcePart, packageRelNs);

    private static int? ReadIntAttribute(XElement element, string attributeName) =>
        XlsxXmlAttributeReader.ReadIntAttribute(element, attributeName);

    private static double? ReadDoubleAttribute(XElement element, string attributeName) =>
        XlsxXmlAttributeReader.ReadDoubleAttribute(element, attributeName);

    private static bool ReadBoolAttribute(XElement? element, string attributeName, bool defaultValue = false) =>
        XlsxXmlAttributeReader.ReadBoolAttribute(element, attributeName, defaultValue);

    private static CfThresholdType FromCfvoType(string? type) =>
        type?.ToLowerInvariant() switch
        {
            "max" => CfThresholdType.Max,
            "num" => CfThresholdType.Number,
            "percent" => CfThresholdType.Percent,
            "percentile" => CfThresholdType.Percentile,
            "formula" => CfThresholdType.Formula,
            _ => CfThresholdType.Min
        };

    private static void EnsureContentType(ZipArchive archive, string extension, string contentType)
        => XlsxPackageXmlEditor.EnsureDefaultContentType(archive, extension, contentType);

    private static void EnsureSpecificContentType(ZipArchive archive, string partName, string contentType)
        => XlsxPackageXmlEditor.EnsureSpecificContentType(archive, partName, contentType);

    private static SheetXmlLayout ReadHiddenSheetLayout(
        ZipArchive archive,
        string worksheetPath,
        ZipArchiveEntry worksheetEntry,
        IReadOnlyList<CellStyle> differentialStyles)
    {
        var hiddenRows = new HashSet<uint>();
        var hiddenCols = new HashSet<uint>();
        var rowOutlineLevels = new Dictionary<uint, int>();
        var colOutlineLevels = new Dictionary<uint, int>();
        var groupHiddenRows = new HashSet<uint>();
        var groupHiddenCols = new HashSet<uint>();
        var worksheetXml = LoadXml(worksheetEntry);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var row in worksheetXml.Descendants(worksheetNs + "row"))
        {
            if (!uint.TryParse(row.Attribute("r")?.Value, out var rowNumber))
                continue;

            if (IsTruthy(row.Attribute("hidden")?.Value))
                hiddenRows.Add(rowNumber);

            var outlineStr = row.Attribute("outlineLevel")?.Value;
            if (int.TryParse(outlineStr, out var outlineLevel) && outlineLevel > 0)
            {
                rowOutlineLevels[rowNumber] = outlineLevel;
                if (IsTruthy(row.Attribute("collapsed")?.Value))
                    groupHiddenRows.Add(rowNumber);
            }
        }

        foreach (var col in worksheetXml.Descendants(worksheetNs + "col"))
        {
            if (!uint.TryParse(col.Attribute("min")?.Value, out var min))
                continue;
            if (!uint.TryParse(col.Attribute("max")?.Value, out var max))
                continue;

            if (IsTruthy(col.Attribute("hidden")?.Value))
            {
                for (var colNumber = min; colNumber <= max; colNumber++)
                    hiddenCols.Add(colNumber);
            }

            var colOutlineStr = col.Attribute("outlineLevel")?.Value;
            if (int.TryParse(colOutlineStr, out var colOutlineLevel) && colOutlineLevel > 0)
            {
                var collapsed = IsTruthy(col.Attribute("collapsed")?.Value);
                for (var colNumber = min; colNumber <= max; colNumber++)
                {
                    colOutlineLevels[colNumber] = colOutlineLevel;
                    if (collapsed)
                        groupHiddenCols.Add(colNumber);
                }
            }
        }

        var protection = worksheetXml.Root?.Element(worksheetNs + "sheetProtection");
        var isProtected = IsTruthy(protection?.Attribute("sheet")?.Value);
        var passwordHash =
            protection?.Attribute("password")?.Value ??
            protection?.Attribute("hashValue")?.Value;
        var allowEditRanges = XlsxAllowEditRangeMapper.Read(worksheetXml, worksheetNs);

        var sheetView = worksheetXml.Root?
            .Element(worksheetNs + "sheetViews")?
            .Elements(worksheetNs + "sheetView")
            .FirstOrDefault();
        var sheetCalcPr = worksheetXml.Root?.Element(worksheetNs + "sheetCalcPr");
        var phoneticPr = worksheetXml.Root?.Element(worksheetNs + "phoneticPr");
        var pane = sheetView?.Element(worksheetNs + "pane");
        var viewTopLeft = ParseOptionalCellReference(sheetView?.Attribute("topLeftCell")?.Value);
        var activeCell = ParseOptionalCellReference(
            sheetView?
                .Elements(worksheetNs + "selection")
                .Select(selection => selection.Attribute("activeCell")?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
        var background = XlsxWorksheetBackgroundReaderWriter.Read(archive, worksheetPath, worksheetXml);
        var chartParts = XlsxWorksheetDrawingPartReader.ReadChartParts(archive, worksheetPath, worksheetXml);
        var pictureParts = XlsxWorksheetDrawingPartReader.ReadPictureParts(archive, worksheetPath, worksheetXml);
        var (textBoxParts, shapeParts) = XlsxWorksheetDrawingPartReader.ReadShapeParts(archive, worksheetPath, worksheetXml);
        var sparklines = XlsxSparklineMapper.Read(worksheetXml);
        var advancedConditionalFormats = ReadAdvancedConditionalFormats(worksheetXml, worksheetNs, differentialStyles);
        var dataValidationNativeMetadata = XlsxDataValidationNativeMetadataMapper.Read(worksheetXml, worksheetNs);
        var ignoredErrors = XlsxWorksheetDiagnosticsMapper.ReadIgnoredErrors(worksheetXml, worksheetNs);
        var cellWatches = XlsxWorksheetDiagnosticsMapper.ReadCellWatches(worksheetXml, worksheetNs);
        var scenarios = XlsxWorksheetScenarioMapper.Read(worksheetXml, worksheetNs);
        var customViews = XlsxCustomViewMapper.ReadWorksheetViews(worksheetXml, worksheetNs);
        var customProperties = XlsxWorksheetCustomPropertyMapper.Read(worksheetXml, worksheetNs);
        var codeName = worksheetXml.Root?
            .Element(worksheetNs + "sheetPr")?
            .Attribute("codeName")?
            .Value;

        return new SheetXmlLayout(
            hiddenRows,
            hiddenCols,
            isProtected,
            passwordHash,
            allowEditRanges,
            ParseWorksheetViewMode(sheetView?.Attribute("view")?.Value),
            !IsFalse(sheetView?.Attribute("showGridLines")?.Value),
            !IsFalse(sheetView?.Attribute("showRowColHeaders")?.Value),
            !IsFalse(sheetView?.Attribute("showRuler")?.Value),
            ParseZoomPercent(sheetView?.Attribute("zoomScale")?.Value),
            IsTruthy(sheetView?.Attribute("showFormulas")?.Value),
            XlsxWorksheetCalculationPropertyMapper.ReadFullCalculationOnLoad(sheetCalcPr),
            XlsxWorksheetPhoneticPropertyMapper.Read(phoneticPr),
            pane?.Attribute("state")?.Value,
            ParsePaneSplit(pane?.Attribute("ySplit")?.Value),
            ParsePaneSplit(pane?.Attribute("xSplit")?.Value),
            viewTopLeft?.Row,
            viewTopLeft?.Col,
            activeCell?.Row,
            activeCell?.Col,
            background,
            rowOutlineLevels,
            colOutlineLevels,
            groupHiddenRows,
            groupHiddenCols,
            chartParts,
            pictureParts,
            textBoxParts,
            shapeParts,
            sparklines,
            advancedConditionalFormats,
            dataValidationNativeMetadata,
            ignoredErrors,
            cellWatches,
            scenarios,
            customViews,
            customProperties,
            codeName);
    }

    private static bool TryParseSqrefToken(string token, SheetId sheet, out GridRange range)
    {
        range = default;
        var parts = token.Split(':');
        if (parts.Length == 1)
        {
            if (!CellAddress.TryParse(parts[0], sheet, out var address))
                return false;

            range = new GridRange(address, address);
            return true;
        }

        if (parts.Length == 2 &&
            CellAddress.TryParse(parts[0], sheet, out var start) &&
            CellAddress.TryParse(parts[1], sheet, out var end))
        {
            range = new GridRange(start, end);
            return true;
        }

        return false;
    }

    private static IReadOnlyList<ConditionalFormat> ReadAdvancedConditionalFormats(
        XDocument worksheetXml,
        XNamespace worksheetNs,
        IReadOnlyList<CellStyle> differentialStyles)
    {
        var result = new List<ConditionalFormat>();
        var tempSheet = SheetId.New();
        foreach (var conditionalFormatting in worksheetXml.Root?.Elements(worksheetNs + "conditionalFormatting") ?? [])
        {
            var sqref = conditionalFormatting.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref))
                continue;

            GridRange appliesTo;
            try
            {
                var firstRef = sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).First();
                appliesTo = firstRef.Contains(':', StringComparison.Ordinal)
                    ? GridRange.Parse(firstRef, tempSheet)
                    : new GridRange(CellAddress.Parse(firstRef, tempSheet), CellAddress.Parse(firstRef, tempSheet));
            }
            catch
            {
                continue;
            }

            foreach (var rule in conditionalFormatting.Elements(worksheetNs + "cfRule"))
            {
                var type = rule.Attribute("type")?.Value;
                var priority = ReadIntAttribute(rule, "priority") ?? 1;
                var formatIfTrue = ReadIntAttribute(rule, "dxfId") is { } dxfId &&
                    dxfId >= 0 &&
                    dxfId < differentialStyles.Count
                    ? differentialStyles[dxfId].Clone()
                    : null;
                if (string.Equals(type, "colorScale", StringComparison.OrdinalIgnoreCase) &&
                    rule.Element(worksheetNs + "colorScale") is { } colorScale)
                {
                    var format = ReadColorScaleConditionalFormat(colorScale, appliesTo, priority, worksheetNs);
                    format.FormatIfTrue = formatIfTrue;
                    ApplyNativeConditionalFormatRuleMetadata(format, rule, worksheetNs);
                    ApplyNativeConditionalFormattingContainerMetadata(format, conditionalFormatting, worksheetNs);
                    result.Add(format);
                }
                else if (string.Equals(type, "dataBar", StringComparison.OrdinalIgnoreCase) &&
                         rule.Element(worksheetNs + "dataBar") is { } dataBar)
                {
                    var format = ReadDataBarConditionalFormat(dataBar, appliesTo, priority, worksheetNs);
                    format.FormatIfTrue = formatIfTrue;
                    ApplyNativeConditionalFormatRuleMetadata(format, rule, worksheetNs);
                    ApplyNativeConditionalFormattingContainerMetadata(format, conditionalFormatting, worksheetNs);
                    result.Add(format);
                }
                else if (string.Equals(type, "iconSet", StringComparison.OrdinalIgnoreCase) &&
                         rule.Element(worksheetNs + "iconSet") is { } iconSet)
                {
                    var format = new ConditionalFormat
                    {
                        AppliesTo = appliesTo,
                        Priority = priority,
                        RuleType = CfRuleType.IconSet,
                        IconSetStyle = iconSet.Attribute("iconSet")?.Value,
                        IconSetShowValue = !IsFalse(iconSet.Attribute("showValue")?.Value),
                        IconSetReverse = IsTruthy(iconSet.Attribute("reverse")?.Value),
                        FormatIfTrue = formatIfTrue
                    };
                    format.IconSetThresholds.AddRange(ReadCfvoThresholds(iconSet, worksheetNs));
                    ApplyNativeConditionalFormatPayloadMetadata(format, iconSet, worksheetNs);
                    ApplyNativeConditionalFormatRuleMetadata(format, rule, worksheetNs);
                    ApplyNativeConditionalFormattingContainerMetadata(format, conditionalFormatting, worksheetNs);
                    result.Add(format);
                }
                else if (TryMapLongTailConditionalFormatRule(type, out var mappedType))
                {
                    var format = new ConditionalFormat
                    {
                        AppliesTo = appliesTo,
                        Priority = priority,
                        RuleType = mappedType,
                        AboveAverage = mappedType == CfRuleType.Top10
                            ? !IsTruthy(rule.Attribute("bottom")?.Value)
                            : !IsFalse(rule.Attribute("aboveAverage")?.Value),
                        TopBottomRank = ReadIntAttribute(rule, "rank") ?? 10,
                        TopBottomPercent = IsTruthy(rule.Attribute("percent")?.Value),
                        TextRuleText = rule.Attribute("text")?.Value,
                        DateOccurringPeriod = rule.Attribute("timePeriod")?.Value,
                        StopIfTrue = IsTruthy(rule.Attribute("stopIfTrue")?.Value),
                        FormulaText = rule.Element(worksheetNs + "formula")?.Value,
                        FormatIfTrue = formatIfTrue
                    };
                    ApplyNativeConditionalFormatRuleMetadata(format, rule, worksheetNs);
                    ApplyNativeConditionalFormattingContainerMetadata(format, conditionalFormatting, worksheetNs);
                    result.Add(format);
                }
            }
        }

        return result;
    }

    private static void ApplyNativeConditionalFormatRuleMetadata(
        ConditionalFormat format,
        XElement rule,
        XNamespace worksheetNs)
    {
        var nativeAttributes = ReadNativeConditionalFormatRuleAttributes(rule);
        if (nativeAttributes.Count > 0)
            format.NativeAttributes = nativeAttributes;

        var nativeChildren = ReadNativeConditionalFormatRuleChildXmls(rule, worksheetNs);
        if (nativeChildren.Count > 0)
            format.NativeChildXmls = nativeChildren;
    }

    private static void ApplyNativeConditionalFormattingContainerMetadata(
        ConditionalFormat format,
        XElement conditionalFormatting,
        XNamespace worksheetNs)
    {
        var nativeAttributes = ReadNativeConditionalFormattingContainerAttributes(conditionalFormatting);
        if (nativeAttributes.Count > 0)
            format.NativeContainerAttributes = nativeAttributes;

        var nativeChildren = ReadNativeConditionalFormattingContainerChildXmls(conditionalFormatting, worksheetNs);
        if (nativeChildren.Count > 0)
            format.NativeContainerChildXmls = nativeChildren;
    }

    private static Dictionary<string, string> ReadNativeConditionalFormatRuleAttributes(XElement rule)
    {
        string[] modeledAttributes = ["type", "priority", "dxfId", "stopIfTrue"];
        return rule.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && !modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
    }

    private static List<string> ReadNativeConditionalFormatRuleChildXmls(XElement rule, XNamespace worksheetNs)
    {
        XName[] modeledChildren =
        [
            worksheetNs + "colorScale",
            worksheetNs + "dataBar",
            worksheetNs + "iconSet",
            worksheetNs + "formula"
        ];
        return rule.Elements()
            .Where(element => !modeledChildren.Contains(element.Name))
            .Select(element => element.ToString(System.Xml.Linq.SaveOptions.DisableFormatting))
            .ToList();
    }

    private static Dictionary<string, string> ReadNativeConditionalFormattingContainerAttributes(XElement conditionalFormatting)
    {
        string[] modeledAttributes = ["sqref"];
        return conditionalFormatting.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && !modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
    }

    private static List<string> ReadNativeConditionalFormattingContainerChildXmls(
        XElement conditionalFormatting,
        XNamespace worksheetNs) =>
        conditionalFormatting.Elements()
            .Where(element => element.Name != worksheetNs + "cfRule")
            .Select(element => element.ToString(System.Xml.Linq.SaveOptions.DisableFormatting))
            .ToList();

    private static void ApplyNativeConditionalFormatPayloadMetadata(
        ConditionalFormat format,
        XElement payload,
        XNamespace worksheetNs)
    {
        var nativeAttributes = ReadNativeConditionalFormatPayloadAttributes(format.RuleType, payload);
        if (nativeAttributes.Count > 0)
            format.NativePayloadAttributes = nativeAttributes;

        var nativeChildren = ReadNativeConditionalFormatPayloadChildXmls(format.RuleType, payload, worksheetNs);
        if (nativeChildren.Count > 0)
            format.NativePayloadChildXmls = nativeChildren;
    }

    private static Dictionary<string, string> ReadNativeConditionalFormatPayloadAttributes(
        CfRuleType ruleType,
        XElement payload)
    {
        string[] modeledAttributes = ruleType switch
        {
            CfRuleType.DataBar => ["showValue", "minLength", "maxLength"],
            CfRuleType.IconSet => ["iconSet", "showValue", "reverse"],
            _ => []
        };
        return payload.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && !modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
    }

    private static List<string> ReadNativeConditionalFormatPayloadChildXmls(
        CfRuleType ruleType,
        XElement payload,
        XNamespace worksheetNs)
    {
        XName[] modeledChildren = ruleType switch
        {
            CfRuleType.ColorScale => [worksheetNs + "cfvo", worksheetNs + "color"],
            CfRuleType.DataBar => [worksheetNs + "cfvo", worksheetNs + "color"],
            CfRuleType.IconSet => [worksheetNs + "cfvo"],
            _ => []
        };
        return payload.Elements()
            .Where(element => !modeledChildren.Contains(element.Name))
            .Select(element => element.ToString(System.Xml.Linq.SaveOptions.DisableFormatting))
            .ToList();
    }

    private static bool TryMapLongTailConditionalFormatRule(string? type, out CfRuleType ruleType)
    {
        ruleType = type switch
        {
            "aboveAverage" => CfRuleType.AboveAverage,
            "top10" => CfRuleType.Top10,
            "uniqueValues" => CfRuleType.UniqueValues,
            "duplicateValues" => CfRuleType.DuplicateValues,
            "containsText" => CfRuleType.ContainsText,
            "notContainsText" => CfRuleType.NotContainsText,
            "beginsWith" => CfRuleType.BeginsWith,
            "endsWith" => CfRuleType.EndsWith,
            "timePeriod" => CfRuleType.DateOccurring,
            "containsBlanks" => CfRuleType.Blanks,
            "notContainsBlanks" => CfRuleType.NoBlanks,
            "containsErrors" => CfRuleType.Errors,
            "notContainsErrors" => CfRuleType.NoErrors,
            _ => default
        };
        return type is "aboveAverage" or "top10" or "uniqueValues" or "duplicateValues" or
            "containsText" or "notContainsText" or "beginsWith" or "endsWith" or "timePeriod" or
            "containsBlanks" or "notContainsBlanks" or "containsErrors" or "notContainsErrors";
    }

    private static ConditionalFormat ReadColorScaleConditionalFormat(
        XElement colorScale,
        GridRange appliesTo,
        int priority,
        XNamespace worksheetNs)
    {
        var thresholds = colorScale.Elements(worksheetNs + "cfvo").ToList();
        var colors = colorScale.Elements(worksheetNs + "color").ToList();
        var format = new ConditionalFormat
        {
            AppliesTo = appliesTo,
            Priority = priority,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = thresholds.Count >= 3 && colors.Count >= 3
        };

        ApplyThreshold(thresholds.ElementAtOrDefault(0), value =>
        {
            format.MinThresholdType = value.Type;
            format.MinThresholdValue = value.Value;
        });
        ApplyThreshold(thresholds.ElementAtOrDefault(1), value =>
        {
            if (format.UseThreeColorScale)
            {
                format.MidThresholdType = value.Type;
                format.MidThresholdValue = value.Value;
            }
            else
            {
                format.MaxThresholdType = value.Type;
                format.MaxThresholdValue = value.Value;
            }
        });
        if (format.UseThreeColorScale)
        {
            ApplyThreshold(thresholds.ElementAtOrDefault(2), value =>
            {
                format.MaxThresholdType = value.Type;
                format.MaxThresholdValue = value.Value;
            });
        }

        if (XlsxColorReader.TryReadRgbColor(colors.ElementAtOrDefault(0), out var minColor))
            format.MinColor = minColor;
        if (format.UseThreeColorScale && XlsxColorReader.TryReadRgbColor(colors.ElementAtOrDefault(1), out var midColor))
            format.MidColor = midColor;
        if (XlsxColorReader.TryReadRgbColor(colors.ElementAtOrDefault(format.UseThreeColorScale ? 2 : 1), out var maxColor))
            format.MaxColor = maxColor;

        ApplyNativeConditionalFormatPayloadMetadata(format, colorScale, worksheetNs);
        return format;
    }

    private static ConditionalFormat ReadDataBarConditionalFormat(
        XElement dataBar,
        GridRange appliesTo,
        int priority,
        XNamespace worksheetNs)
    {
        var thresholds = dataBar.Elements(worksheetNs + "cfvo").ToList();
        var format = new ConditionalFormat
        {
            AppliesTo = appliesTo,
            Priority = priority,
            RuleType = CfRuleType.DataBar,
            DataBarShowValue = !IsFalse(dataBar.Attribute("showValue")?.Value),
            DataBarMinLength = ReadIntAttribute(dataBar, "minLength"),
            DataBarMaxLength = ReadIntAttribute(dataBar, "maxLength")
        };
        ApplyThreshold(thresholds.ElementAtOrDefault(0), value =>
        {
            format.DataBarMinThresholdType = value.Type;
            format.DataBarMinThresholdValue = value.Value;
        });
        ApplyThreshold(thresholds.ElementAtOrDefault(1), value =>
        {
            format.DataBarMaxThresholdType = value.Type;
            format.DataBarMaxThresholdValue = value.Value;
        });
        if (XlsxColorReader.TryReadRgbColor(dataBar.Element(worksheetNs + "color"), out var color))
            format.DataBarColor = color;
        ApplyNativeConditionalFormatPayloadMetadata(format, dataBar, worksheetNs);
        return format;
    }

    private static void ApplyThreshold(XElement? element, Action<(CfThresholdType Type, string? Value)> apply)
    {
        if (element is null)
            return;
        apply((FromCfvoType(element.Attribute("type")?.Value), element.Attribute("val")?.Value));
    }

    private static IReadOnlyList<CfThresholdModel> ReadCfvoThresholds(XElement parent, XNamespace worksheetNs) =>
        parent
            .Elements(worksheetNs + "cfvo")
            .Select(element => new CfThresholdModel(
                FromCfvoType(element.Attribute("type")?.Value),
                element.Attribute("val")?.Value))
            .ToList();

    private static ConditionalFormat RemapConditionalFormat(ConditionalFormat source, SheetId sheetId)
    {
        var format = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, source.AppliesTo.Start.Row, source.AppliesTo.Start.Col),
                new CellAddress(sheetId, source.AppliesTo.End.Row, source.AppliesTo.End.Col)),
            Priority = source.Priority,
            RuleType = source.RuleType,
            Operator = source.Operator,
            Value1 = source.Value1,
            Value2 = source.Value2,
            FormatIfTrue = source.FormatIfTrue?.Clone(),
            MinColor = source.MinColor,
            MidColor = source.MidColor,
            MaxColor = source.MaxColor,
            UseThreeColorScale = source.UseThreeColorScale,
            MinThresholdType = source.MinThresholdType,
            MinThresholdValue = source.MinThresholdValue,
            MidThresholdType = source.MidThresholdType,
            MidThresholdValue = source.MidThresholdValue,
            MaxThresholdType = source.MaxThresholdType,
            MaxThresholdValue = source.MaxThresholdValue,
            DataBarColor = source.DataBarColor,
            DataBarMinThresholdType = source.DataBarMinThresholdType,
            DataBarMinThresholdValue = source.DataBarMinThresholdValue,
            DataBarMaxThresholdType = source.DataBarMaxThresholdType,
            DataBarMaxThresholdValue = source.DataBarMaxThresholdValue,
            DataBarShowValue = source.DataBarShowValue,
            DataBarMinLength = source.DataBarMinLength,
            DataBarMaxLength = source.DataBarMaxLength,
            AboveAverage = source.AboveAverage,
            FormulaText = source.FormulaText,
            IconSetStyle = source.IconSetStyle,
            IconSetShowValue = source.IconSetShowValue,
            IconSetReverse = source.IconSetReverse,
            TopBottomRank = source.TopBottomRank,
            TopBottomPercent = source.TopBottomPercent,
            TextRuleText = source.TextRuleText,
            DateOccurringPeriod = source.DateOccurringPeriod,
            StopIfTrue = source.StopIfTrue,
            NativeAttributes = source.NativeAttributes,
            NativeChildXmls = source.NativeChildXmls,
            NativePayloadAttributes = source.NativePayloadAttributes,
            NativePayloadChildXmls = source.NativePayloadChildXmls,
            NativeContainerAttributes = source.NativeContainerAttributes,
            NativeContainerChildXmls = source.NativeContainerChildXmls
        };
        format.IconSetThresholds.AddRange(source.IconSetThresholds);
        return format;
    }

    private static uint? ParsePaneSplit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (uint.TryParse(value, out var integer))
            return integer;

        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var floating) &&
            floating > 0 &&
            floating <= uint.MaxValue)
            return (uint)Math.Round(floating);

        return null;
    }

    private static CellAddress? ParseOptionalCellReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        return CellAddress.TryParse(reference.Split(':')[0], SheetId.New(), out var address)
            ? address
            : null;
    }

    private static bool IsTruthy(string? value) =>
        value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsFalse(string? value) =>
        value is "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    private static int ParseZoomPercent(string? value) =>
        int.TryParse(value, out var zoom) && zoom is >= 10 and <= 400 ? zoom : 100;

    private static WorksheetViewMode ParseWorksheetViewMode(string? value) =>
        value switch
        {
            "pageBreakPreview" => WorksheetViewMode.PageBreakPreview,
            "pageLayout" => WorksheetViewMode.PageLayout,
            _ => WorksheetViewMode.Normal
        };

    private static bool IsValidWorksheetRow(uint row) =>
        row is >= 1 and <= CellAddress.MaxRow;

    private static bool IsValidWorksheetColumn(uint column) =>
        column is >= 1 and <= CellAddress.MaxCol;

    private static bool IsValidRepeatRange(WorksheetRepeatRange range, uint max) =>
        range.Start >= 1 && range.End >= range.Start && range.End <= max;

    private static bool IsSupportedTextRotation(int rotation) =>
        rotation == 255 || rotation is >= -90 and <= 90;

    private static uint ValidFrozenRowsOrZero(uint row) =>
        row <= CellAddress.MaxRow ? row : 0;

    private static uint ValidFrozenColumnsOrZero(uint column) =>
        column <= CellAddress.MaxCol ? column : 0;

    private static bool IsSupportedFontSize(double fontSize) =>
        double.IsFinite(fontSize) && fontSize is >= 1 and <= 409;

    private static void ApplyChartExternalDataRelationshipMetadata(ChartModel chart, XlsxChartPackagePart chartPart)
    {
        if (chart.ExternalData?.RelationshipId is not { Length: > 0 } relationshipId)
            return;

        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationship = chartPart.Relationships?.Root?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("Id")?.Value,
                relationshipId,
                StringComparison.Ordinal));
        if (relationship is null)
            return;

        chart.ExternalData.RelationshipType = relationship.Attribute("Type")?.Value;
        chart.ExternalData.Target = relationship.Attribute("Target")?.Value;
        chart.ExternalData.TargetMode = relationship.Attribute("TargetMode")?.Value;
    }

    public void Save(Workbook workbook, Stream stream)
    {
        using var xlWorkbook = new XLWorkbook();
        xlWorkbook.CalculateMode = workbook.CalculationMode == WorkbookCalculationMode.Manual
            ? XLCalculateMode.Manual
            : XLCalculateMode.Auto;

        foreach (var sheet in workbook.Sheets)
        {
            var xlSheet = xlWorkbook.Worksheets.Add(sheet.Name);
            xlSheet.Visibility = sheet.IsVeryHidden
                ? XLWorksheetVisibility.VeryHidden
                : sheet.IsHidden ? XLWorksheetVisibility.Hidden : XLWorksheetVisibility.Visible;
            if (sheet.TabColor is { } tabColor)
                xlSheet.TabColor = XLColor.FromArgb(tabColor.R, tabColor.G, tabColor.B);

            foreach (var pair in sheet.GetUsedCells())
            {
                var cell = pair.Value;

                // Skip blank cells that carry no style
                if (cell.Value is BlankValue && !cell.HasFormula && cell.StyleId == StyleId.Default)
                    continue;

                var xlCell = xlSheet.Cell((int)pair.Key.Row, (int)pair.Key.Col);

                if (cell.HasFormula)
                {
                    xlCell.FormulaA1 = cell.FormulaText;
                }
                else if (cell.Value is not BlankValue)
                {
                    xlCell.Value = XlsxClosedXmlCellMapper.MapValueInverse(cell.Value);
                }

                var style = workbook.GetStyle(cell.StyleId);
                XlsxClosedXmlCellMapper.ApplyStyle(xlCell, style);
            }

            foreach (var (rowNum, height) in sheet.RowHeights)
            {
                if (IsValidWorksheetRow(rowNum) && double.IsFinite(height) && height > 0)
                    xlSheet.Row((int)rowNum).Height = height * (72.0 / 96.0);
            }

            foreach (var rowNum in sheet.HiddenRows)
            {
                if (IsValidWorksheetRow(rowNum))
                    xlSheet.Row((int)rowNum).Hide();
            }

            foreach (var (rowNum, level) in sheet.RowOutlineLevels)
            {
                if (IsValidWorksheetRow(rowNum))
                    xlSheet.Row((int)rowNum).OutlineLevel = level;
            }

            foreach (var rowNum in sheet.GroupHiddenRows)
            {
                if (IsValidWorksheetRow(rowNum))
                    xlSheet.Row((int)rowNum).Collapse();
            }

            foreach (var (colNum, width) in sheet.ColumnWidths)
            {
                if (IsValidWorksheetColumn(colNum) && double.IsFinite(width) && width > 0)
                    xlSheet.Column((int)colNum).Width = width;
            }

            foreach (var colNum in sheet.HiddenCols)
            {
                if (IsValidWorksheetColumn(colNum))
                    xlSheet.Column((int)colNum).Hide();
            }

            foreach (var (colNum, level) in sheet.ColOutlineLevels)
            {
                if (IsValidWorksheetColumn(colNum))
                    xlSheet.Column((int)colNum).OutlineLevel = level;
            }

            foreach (var colNum in sheet.GroupHiddenCols)
            {
                if (IsValidWorksheetColumn(colNum))
                    xlSheet.Column((int)colNum).Collapse();
            }

            foreach (var (address, commentText) in sheet.Comments)
            {
                try
                {
                    xlSheet.Cell((int)address.Row, (int)address.Col)
                        .CreateComment()
                        .AddText(commentText);
                }
                catch
                {
                    // Skip comments ClosedXML cannot serialize.
                }
            }

            foreach (var (address, target) in sheet.Hyperlinks)
            {
                try
                {
                    xlSheet.Cell((int)address.Row, (int)address.Col)
                        .SetHyperlink(new XLHyperlink(target));
                }
                catch
                {
                    // Skip hyperlinks ClosedXML cannot serialize.
                }
            }

            var frozenRows = ValidFrozenRowsOrZero(sheet.FrozenRows);
            var frozenCols = ValidFrozenColumnsOrZero(sheet.FrozenCols);
            if (frozenRows > 0 || frozenCols > 0)
                xlSheet.SheetView.Freeze((int)frozenRows, (int)frozenCols);

            if (sheet.PrintArea is { } printArea)
            {
                xlSheet.PageSetup.PrintAreas.Clear();
                xlSheet.PageSetup.PrintAreas.Add(
                    (int)printArea.Start.Row,
                    (int)printArea.Start.Col,
                    (int)printArea.End.Row,
                    (int)printArea.End.Col);
            }

            var pageOrientation = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.PageOrientation, WorksheetPageOrientation.Portrait);
            var paperSize = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.PaperSize, WorksheetPaperSize.A4);
            var pageMargins = XlsxWorksheetValueSanitizer.ValidPageMarginsOrDefault(sheet.PageMargins, WorksheetPageMargins.Narrow);
            var headerMargin = XlsxWorksheetValueSanitizer.NonNegativeFiniteOrDefault(sheet.HeaderMargin, 0.3);
            var footerMargin = XlsxWorksheetValueSanitizer.NonNegativeFiniteOrDefault(sheet.FooterMargin, 0.3);
            var scaleToFit = XlsxWorksheetValueSanitizer.ValidScaleToFitOrDefault(sheet.ScaleToFit, WorksheetScaleToFit.Default);
            var pageOrder = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.PageOrder, WorksheetPageOrder.DownThenOver);
            var printErrorValue = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.PrintErrorValue, WorksheetPrintErrorValue.Displayed);
            var printComments = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.PrintComments, WorksheetPrintComments.None);

            xlSheet.PageSetup.PageOrientation = pageOrientation == WorksheetPageOrientation.Landscape
                ? XLPageOrientation.Landscape
                : XLPageOrientation.Portrait;
            xlSheet.PageSetup.PaperSize = paperSize switch
            {
                WorksheetPaperSize.Letter => XLPaperSize.LetterPaper,
                WorksheetPaperSize.Legal => XLPaperSize.LegalPaper,
                _ => XLPaperSize.A4Paper
            };
            xlSheet.PageSetup.Margins.Left = pageMargins.Left;
            xlSheet.PageSetup.Margins.Right = pageMargins.Right;
            xlSheet.PageSetup.Margins.Top = pageMargins.Top;
            xlSheet.PageSetup.Margins.Bottom = pageMargins.Bottom;
            xlSheet.PageSetup.Margins.Header = headerMargin;
            xlSheet.PageSetup.Margins.Footer = footerMargin;
            xlSheet.PageSetup.ShowGridlines = sheet.PrintGridlines;
            xlSheet.PageSetup.ShowRowAndColumnHeadings = sheet.PrintHeadings;
            xlSheet.PageSetup.CenterHorizontally = sheet.CenterHorizontallyOnPage;
            xlSheet.PageSetup.CenterVertically = sheet.CenterVerticallyOnPage;
            xlSheet.PageSetup.PageOrder = pageOrder == WorksheetPageOrder.OverThenDown
                ? XLPageOrderValues.OverThenDown
                : XLPageOrderValues.DownThenOver;
            if (sheet.FirstPageNumber is { } firstPageNumber && firstPageNumber > 0)
                xlSheet.PageSetup.FirstPageNumber = firstPageNumber;
            xlSheet.PageSetup.BlackAndWhite = sheet.PrintBlackAndWhite;
            xlSheet.PageSetup.DraftQuality = sheet.PrintDraftQuality;
            if (sheet.PrintQualityDpi is { } printQualityDpi && printQualityDpi > 0)
            {
                xlSheet.PageSetup.HorizontalDpi = printQualityDpi;
                xlSheet.PageSetup.VerticalDpi = printQualityDpi;
            }
            xlSheet.PageSetup.PrintErrorValue = XlsxWorksheetPageSetupMapper.ToPrintErrorValue(printErrorValue);
            xlSheet.PageSetup.ShowComments = XlsxWorksheetPageSetupMapper.ToPrintComments(printComments);
            xlSheet.PageSetup.DifferentFirstPageOnHF = sheet.DifferentFirstPageHeaderFooter;
            xlSheet.PageSetup.DifferentOddEvenPagesOnHF = sheet.DifferentOddEvenHeaderFooter;
            xlSheet.PageSetup.ScaleHFWithDocument = sheet.HeaderFooterScaleWithDocument;
            xlSheet.PageSetup.AlignHFWithMargins = sheet.HeaderFooterAlignWithMargins;
            XlsxWorksheetPageSetupMapper.SetHeaderFooter(
                xlSheet.PageSetup.Header,
                sheet.PageHeader,
                sheet.FirstPageHeader,
                sheet.EvenPageHeader,
                sheet.DifferentFirstPageHeaderFooter,
                sheet.DifferentOddEvenHeaderFooter);
            XlsxWorksheetPageSetupMapper.SetHeaderFooter(
                xlSheet.PageSetup.Footer,
                sheet.PageFooter,
                sheet.FirstPageFooter,
                sheet.EvenPageFooter,
                sheet.DifferentFirstPageHeaderFooter,
                sheet.DifferentOddEvenHeaderFooter);
            if (scaleToFit.ScalePercent is { } scalePercent)
                xlSheet.PageSetup.Scale = scalePercent;
            else if (scaleToFit.FitToPagesWide.HasValue || scaleToFit.FitToPagesTall.HasValue)
                xlSheet.PageSetup.FitToPages(scaleToFit.FitToPagesWide ?? 1, scaleToFit.FitToPagesTall ?? 1);
            if (sheet.PrintTitleRows is { } titleRows && IsValidRepeatRange(titleRows, CellAddress.MaxRow))
                xlSheet.PageSetup.SetRowsToRepeatAtTop((int)titleRows.Start, (int)titleRows.End);
            if (sheet.PrintTitleColumns is { } titleColumns && IsValidRepeatRange(titleColumns, CellAddress.MaxCol))
                xlSheet.PageSetup.SetColumnsToRepeatAtLeft((int)titleColumns.Start, (int)titleColumns.End);
            foreach (var rowBreak in sheet.RowPageBreaks)
                if (rowBreak is >= 2 and <= CellAddress.MaxRow)
                    xlSheet.PageSetup.AddHorizontalPageBreak((int)rowBreak);
            foreach (var columnBreak in sheet.ColumnPageBreaks)
                if (columnBreak is >= 2 and <= CellAddress.MaxCol)
                    xlSheet.PageSetup.AddVerticalPageBreak((int)columnBreak);

            if (sheet.IsProtected)
            {
                if (string.IsNullOrEmpty(sheet.ProtectionPassword))
                    xlSheet.Protect(XLProtectionAlgorithm.Algorithm.SimpleHash);
                else
                    xlSheet.Protect(sheet.ProtectionPassword, XLProtectionAlgorithm.Algorithm.SimpleHash);
            }

            // Save CellValue conditional format rules back to XLSX
            XlsxConditionalFormatClosedXmlMapper.Save(sheet, xlSheet);

            // Save data validation rules back to XLSX
            try { XlsxDataValidationClosedXmlMapper.Save(sheet, xlSheet); }
            catch { /* ignore DV save failures */ }

            // Save merged regions
            foreach (var region in sheet.MergedRegions)
            {
                try
                {
                    var rangeStr = $"{CellAddress.NumberToColumnName(region.Start.Col)}{region.Start.Row}" +
                                   $":{CellAddress.NumberToColumnName(region.End.Col)}{region.End.Row}";
                    xlSheet.Range(rangeStr).Merge();
                }
                catch { /* ignore individual merge failures */ }
            }
        }

        // Save named ranges
        try { XlsxNamedRangeMapper.Save(workbook, xlWorkbook); }
        catch { /* ignore named-range save failures */ }

        using var packageStream = new MemoryStream();
        xlWorkbook.SaveAs(packageStream);

        if (workbook.IsStructureProtected)
        {
            packageStream.Position = 0;
            XlsxWorkbookMetadataWriter.SaveProtection(packageStream, workbook);
        }

        packageStream.Position = 0;
        XlsxWorkbookMetadataWriter.SaveCalculationProperties(packageStream, workbook);

        if (workbook.Sheets.Any(sheet => sheet.FullCalculationOnLoad))
        {
            packageStream.Position = 0;
            XlsxWorksheetCalculationPropertyMapper.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.PhoneticProperties is not null))
        {
            packageStream.Position = 0;
            XlsxWorksheetPhoneticPropertyMapper.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.AllowEditRanges.Count > 0))
        {
            packageStream.Position = 0;
            XlsxAllowEditRangeMapper.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.DataValidations.Any(XlsxDataValidationNativeMetadataMapper.HasNativeMetadata)))
        {
            packageStream.Position = 0;
            XlsxDataValidationNativeMetadataMapper.Save(packageStream, workbook);
        }

        if (XlsxAdvancedConditionalFormatWriter.HasAdvancedConditionalFormats(workbook))
        {
            packageStream.Position = 0;
            XlsxAdvancedConditionalFormatWriter.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.Sparklines.Count > 0))
        {
            packageStream.Position = 0;
            XlsxSparklineMapper.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.BackgroundImage is not null))
        {
            packageStream.Position = 0;
            XlsxWorksheetBackgroundReaderWriter.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(XlsxWorksheetViewWriter.HasPersistableViewState))
        {
            packageStream.Position = 0;
            XlsxWorksheetViewWriter.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => !string.IsNullOrWhiteSpace(sheet.CodeName)))
        {
            packageStream.Position = 0;
            XlsxWorksheetCodeNameWriter.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.GetUsedCells().Any(pair => pair.Value.IgnoreFormulaError)))
        {
            packageStream.Position = 0;
            XlsxWorksheetDiagnosticsMapper.SaveIgnoredErrors(packageStream, workbook);
        }

        if (workbook.WatchedCells.Count > 0)
        {
            packageStream.Position = 0;
            XlsxWorksheetDiagnosticsMapper.SaveCellWatches(packageStream, workbook);
        }

        if (workbook.Scenarios.Count > 0)
        {
            packageStream.Position = 0;
            XlsxWorksheetScenarioMapper.Save(packageStream, workbook);
        }

        if (workbook.CustomViews.Count > 0)
        {
            packageStream.Position = 0;
            XlsxCustomViewMapper.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.CustomProperties.Count > 0))
        {
            packageStream.Position = 0;
            XlsxWorksheetCustomPropertyMapper.Save(packageStream, workbook);
        }

        packageStream.Position = 0;
        XlsxWorkbookThemeWriter.Save(packageStream, workbook.Theme);

        if (XlsxWorksheetChartWriter.HasSupportedCharts(workbook, IsSupportedXlsxChart))
        {
            packageStream.Position = 0;
            XlsxWorksheetChartWriter.Save(packageStream, workbook, IsSupportedXlsxChart, ToChartXml);
        }

        if (XlsxWorksheetDrawingObjectWriter.HasSupportedObjects(workbook))
        {
            packageStream.Position = 0;
            XlsxWorksheetDrawingObjectWriter.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.StructuredTables.Count > 0))
        {
            packageStream.Position = 0;
            XlsxStructuredTableWriter.Save(packageStream, workbook);
        }

        if (workbook.PivotTableStyles.Count > 0)
        {
            packageStream.Position = 0;
            XlsxSlicerTimelineWriter.SavePivotTableStyles(packageStream, workbook);
        }

        IReadOnlyDictionary<int, int> numberFormatIdMap = new Dictionary<int, int>();
        if (workbook.NumberFormatCatalog.Count > 0 ||
            workbook.Sheets.SelectMany(sheet => sheet.PivotTables)
                .SelectMany(pivot => pivot.DataFields)
                .Any(field => field.NumberFormatId is >= 164 && !string.IsNullOrWhiteSpace(field.NumberFormatCode)))
        {
            packageStream.Position = 0;
            numberFormatIdMap = XlsxNumberFormatCatalogWriter.Save(packageStream, workbook);
        }

        if (!SourcePackages.TryGetValue(workbook, out _) &&
            workbook.PivotCaches.Count > 0 &&
            workbook.Sheets.Any(sheet => sheet.PivotTables.Count > 0))
        {
            packageStream.Position = 0;
            XlsxPivotTableWriter.Save(packageStream, workbook, numberFormatIdMap);
        }

        if (!SourcePackages.TryGetValue(workbook, out _) &&
            (workbook.Slicers.Count > 0 || workbook.Timelines.Count > 0))
        {
            packageStream.Position = 0;
            XlsxSlicerTimelineWriter.SaveSlicerTimelines(packageStream, workbook);
        }

        packageStream.Position = 0;
        PreserveSourcePackageParts(workbook, packageStream);

        if (numberFormatIdMap.Any(pair => pair.Key != pair.Value))
        {
            packageStream.Position = 0;
            XlsxNumberFormatCatalogWriter.RemapPivotTableNumberFormats(packageStream, numberFormatIdMap);
        }

        packageStream.Position = 0;
        packageStream.CopyTo(stream);
    }

    private static void PreserveSourcePackageParts(Workbook workbook, MemoryStream generatedPackage)
    {
        if (!SourcePackages.TryGetValue(workbook, out var sourcePackage))
            return;

        using var sourceStream = new MemoryStream(sourcePackage.Bytes, writable: false);
        using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false);
        using var generatedArchive = new ZipArchive(generatedPackage, ZipArchiveMode.Update, leaveOpen: true);
        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, generatedArchive);

        XlsxPackageMetadataMerger.MergeContentTypes(sourceArchive, generatedArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, generatedArchive, generatedEntriesBeforeMerge);
        XlsxDocumentPropertiesPreserver.Preserve(sourceArchive, generatedArchive);
        XlsxWorkbookMetadataPreserver.Preserve(sourceArchive, generatedArchive, workbook);
        XlsxStylesheetMetadataPreserver.Preserve(sourceArchive, generatedArchive);
        XlsxPivotXmlReferencePreserver.Preserve(sourceArchive, generatedArchive);
        XlsxStructuredTableReferencePreserver.Preserve(sourceArchive, generatedArchive);
        XlsxExternalLinkReferencePreserver.Preserve(sourceArchive, generatedArchive);
        XlsxUnsupportedSheetReferencePreserver.Preserve(sourceArchive, generatedArchive);
        XlsxWorksheetDrawingPartMerger.Merge(sourceArchive, generatedArchive);
        XlsxWorksheetDrawingReferencePreserver.Preserve(sourceArchive, generatedArchive);
        XlsxWorksheetPrinterSettingsReferencePreserver.Preserve(sourceArchive, generatedArchive);
        PreserveWorksheetMetadataBlocks(sourceArchive, generatedArchive, workbook);
        XlsxLegacyCommentPreserver.Preserve(sourceArchive, generatedArchive, workbook);
        XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, generatedArchive);
        XlsxUnsupportedConditionalFormattingPreserver.Preserve(sourceArchive, generatedArchive);
    }

    private static string EnsureRelationshipForPackagePart(
        XDocument relsXml,
        XNamespace packageRelNs,
        string sourcePart,
        string targetPart,
        string relationshipType)
        => XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            relsXml,
            packageRelNs,
            sourcePart,
            targetPart,
            relationshipType);

    private static void PreserveWorksheetMetadataBlocks(ZipArchive sourceArchive, ZipArchive targetArchive, Workbook workbook)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XName[] retainedChildNames =
        [
            workbookNs + "customSheetViews",
            workbookNs + "scenarios",
            workbookNs + "ignoredErrors",
            workbookNs + "cellWatches",
            workbookNs + "sheetCalcPr",
            workbookNs + "phoneticPr",
            workbookNs + "sortState",
            workbookNs + "dataConsolidate",
            workbookNs + "legacyDrawing",
            workbookNs + "legacyDrawingHF",
            workbookNs + "picture",
            workbookNs + "customProperties",
            workbookNs + "smartTags",
            workbookNs + "singleXmlCells",
            workbookNs + "autoFilter",
            workbookNs + "protectedRanges",
            workbookNs + "rowBreaks",
            workbookNs + "colBreaks",
            workbookNs + "queryTableParts",
            workbookNs + "webPublishItems",
            workbookNs + "oleObjects",
            workbookNs + "controls"
        ];

        var sourceWorkbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var sourceWorkbookRelsEntry = sourceArchive.GetEntry("xl/_rels/workbook.xml.rels");
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookRelsEntry = targetArchive.GetEntry("xl/_rels/workbook.xml.rels");
        if (sourceWorkbookEntry is null || sourceWorkbookRelsEntry is null ||
            targetWorkbookEntry is null || targetWorkbookRelsEntry is null)
        {
            return;
        }

        var sourceWorkbookXml = LoadXml(sourceWorkbookEntry);
        var targetWorkbookXml = LoadXml(targetWorkbookEntry);
        var sourceWorkbookRels = LoadRelationshipTargets(
            sourceArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var targetWorkbookRels = LoadRelationshipTargets(
            targetArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);

        var sourceSheets = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(sourceWorkbookXml, sourceWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);
        var targetSheets = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(targetWorkbookXml, targetWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        foreach (var (sheetName, sourceWorksheetPath) in sourceSheets)
        {
            if (!targetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sourceWorksheetEntry = sourceArchive.GetEntry(sourceWorksheetPath);
            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (sourceWorksheetEntry is null || targetWorksheetEntry is null)
                continue;

            var sourceWorksheetXml = LoadXml(sourceWorksheetEntry);
            var sourceBlocks = retainedChildNames
                .Select(name => sourceWorksheetXml.Root?.Element(name))
                .Where(element => element is not null)
                .Cast<XElement>()
                .ToList();
            var sourceSheetProperties = sourceWorksheetXml.Root?.Element(workbookNs + "sheetPr");
            var sourceSheetFormatProperties = sourceWorksheetXml.Root?.Element(workbookNs + "sheetFormatPr");
            var sourcePrintOptions = sourceWorksheetXml.Root?.Element(workbookNs + "printOptions");
            var sourcePageMargins = sourceWorksheetXml.Root?.Element(workbookNs + "pageMargins");
            var sourcePageSetup = sourceWorksheetXml.Root?.Element(workbookNs + "pageSetup");
            var sourceColumns = sourceWorksheetXml.Root?.Element(workbookNs + "cols");
            var sourceSheetData = sourceWorksheetXml.Root?.Element(workbookNs + "sheetData");
            var sourceSheetProtection = sourceWorksheetXml.Root?.Element(workbookNs + "sheetProtection");
            var sourceSheetViews = sourceWorksheetXml.Root?.Element(workbookNs + "sheetViews");
            var sourceHyperlinks = sourceWorksheetXml.Root?.Element(workbookNs + "hyperlinks");
            var sourceExtensionList = sourceWorksheetXml.Root?.Element(workbookNs + "extLst");
            if (sourceBlocks.Count == 0 &&
                sourceSheetProperties is null &&
                sourceSheetFormatProperties is null &&
                sourcePrintOptions is null &&
                sourcePageMargins is null &&
                sourcePageSetup is null &&
                sourceColumns is null &&
                sourceSheetData is null &&
                sourceSheetProtection is null &&
                sourceSheetViews is null &&
                sourceHyperlinks is null &&
                sourceExtensionList is null)
            {
                continue;
            }

            var targetWorksheetXml = LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            var changed = false;
            if (MergeWorksheetSheetProperties(sourceSheetProperties, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetSheetFormatProperties(sourceSheetFormatProperties, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetNativeOnlyElementAttributes(
                    sourcePrintOptions,
                    targetRoot,
                    workbookNs + "printOptions",
                    ModeledPrintOptionsAttributes))
                changed = true;
            if (MergeWorksheetNativeOnlyElementAttributes(
                    sourcePageMargins,
                    targetRoot,
                    workbookNs + "pageMargins",
                    ModeledPageMarginsAttributes))
                changed = true;
            if (MergeWorksheetNativeOnlyElementAttributes(
                    sourcePageSetup,
                    targetRoot,
                    workbookNs + "pageSetup",
                    ModeledPageSetupAttributes))
                changed = true;
            if (MergeWorksheetColumnAttributes(sourceColumns, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetRowAttributes(sourceSheetData, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetCellAttributes(sourceSheetData, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetSheetProtection(sourceSheetProtection, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetSheetViews(sourceSheetViews, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetHyperlinkMetadata(sourceHyperlinks, targetRoot, workbookNs, relNs))
                changed = true;
            foreach (var sourceBlock in sourceBlocks)
            {
                if (sourceBlock.Name == workbookNs + "protectedRanges")
                {
                    if (MergeWorksheetProtectedRanges(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxAllowEditRangeMapper.GetModeledReferences(workbook, sheetName)))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "sheetCalcPr")
                {
                    if (MergeWorksheetCalculationProperties(sourceBlock, targetRoot, workbookNs))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "phoneticPr")
                {
                    if (MergeWorksheetPhoneticProperties(sourceBlock, targetRoot, workbookNs))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "customSheetViews")
                {
                    if (MergeWorksheetCustomSheetViews(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxCustomViewMapper.GetModeledIds(workbook)))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "customProperties")
                {
                    if (MergeWorksheetCustomProperties(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxWorksheetCustomPropertyMapper.GetModeledNames(workbook, sheetName)))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "rowBreaks")
                {
                    if (MergeWorksheetBreaks(
                            sourceBlock,
                            targetRoot,
                            workbookNs,
                            GetModeledWorksheetBreakIds(workbook, sheetName, rowBreaks: true),
                            CellAddress.MaxRow))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "colBreaks")
                {
                    if (MergeWorksheetBreaks(
                            sourceBlock,
                            targetRoot,
                            workbookNs,
                            GetModeledWorksheetBreakIds(workbook, sheetName, rowBreaks: false),
                            CellAddress.MaxCol))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "ignoredErrors" &&
                    XlsxWorksheetDiagnosticsMapper.MergeIgnoredErrors(sourceBlock, targetRoot, workbookNs))
                {
                    changed = true;
                    continue;
                }
                if (sourceBlock.Name == workbookNs + "cellWatches" &&
                    XlsxWorksheetDiagnosticsMapper.MergeCellWatches(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxWorksheetDiagnosticsMapper.GetModeledCellWatchReferences(workbook, sheetName)))
                {
                    changed = true;
                    continue;
                }

                if (sourceBlock.Name == workbookNs + "scenarios" &&
                    MergeWorksheetScenarios(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxWorksheetScenarioMapper.GetModeledNamesForSheet(workbook, sheetName)))
                {
                    changed = true;
                }
                if (sourceBlock.Name == workbookNs + "scenarios")
                    continue;

                if (targetRoot.Element(sourceBlock.Name) is not null)
                    continue;

                targetRoot.Add(new XElement(sourceBlock));
                changed = true;
            }

            if (XlsxNativeXmlMerger.MergeExtensionList(sourceExtensionList, targetRoot, workbookNs))
                changed = true;

            if (changed)
                ReplacePackageXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }

    private static bool MergeWorksheetHyperlinkMetadata(
        XElement? sourceHyperlinks,
        XElement targetRoot,
        XNamespace workbookNs,
        XNamespace relNs)
    {
        if (sourceHyperlinks is null)
            return false;

        var targetHyperlinks = targetRoot.Element(workbookNs + "hyperlinks");
        if (targetHyperlinks is null)
            return false;

        var targetByReference = targetHyperlinks
            .Elements(workbookNs + "hyperlink")
            .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("ref")?.Value))
            .ToDictionary(
                element => element.Attribute("ref")!.Value,
                StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var sourceHyperlink in sourceHyperlinks.Elements(workbookNs + "hyperlink"))
        {
            var reference = sourceHyperlink.Attribute("ref")?.Value;
            if (string.IsNullOrWhiteSpace(reference) ||
                !targetByReference.TryGetValue(reference, out var targetHyperlink))
            {
                continue;
            }

            foreach (var attribute in sourceHyperlink.Attributes())
            {
                if (attribute.Name.LocalName == "ref" ||
                    attribute.Name == relNs + "id" ||
                    targetHyperlink.Attribute(attribute.Name) is not null)
                {
                    continue;
                }

                targetHyperlink.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }
        }

        return changed;
    }

    private static bool MergeWorksheetColumnAttributes(XElement? sourceColumns, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceColumns is null)
            return false;

        var targetColumns = targetRoot.Element(workbookNs + "cols");
        if (targetColumns is null)
            return false;

        var targetColumnsByRange = targetColumns
            .Elements(workbookNs + "col")
            .Where(column => !string.IsNullOrWhiteSpace(column.Attribute("min")?.Value) &&
                             !string.IsNullOrWhiteSpace(column.Attribute("max")?.Value))
            .ToDictionary(ColumnRangeKey, StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var sourceColumn in sourceColumns.Elements(workbookNs + "col"))
        {
            var key = ColumnRangeKey(sourceColumn);
            if (string.IsNullOrWhiteSpace(key) ||
                !targetColumnsByRange.TryGetValue(key, out var targetColumn))
            {
                continue;
            }

            foreach (var attribute in sourceColumn.Attributes())
            {
                if (targetColumn.Attribute(attribute.Name) is not null)
                    continue;

                targetColumn.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }
        }

        return changed;

        static string ColumnRangeKey(XElement column)
        {
            var min = column.Attribute("min")?.Value;
            var max = column.Attribute("max")?.Value;
            return string.IsNullOrWhiteSpace(min) || string.IsNullOrWhiteSpace(max)
                ? string.Empty
                : $"{min}:{max}";
        }
    }

    private static bool MergeWorksheetRowAttributes(XElement? sourceSheetData, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetData is null)
            return false;

        var targetSheetData = targetRoot.Element(workbookNs + "sheetData");
        if (targetSheetData is null)
            return false;

        var targetRowsByNumber = targetSheetData
            .Elements(workbookNs + "row")
            .Where(row => !string.IsNullOrWhiteSpace(row.Attribute("r")?.Value))
            .ToDictionary(
                row => row.Attribute("r")!.Value,
                StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var sourceRow in sourceSheetData.Elements(workbookNs + "row"))
        {
            var rowNumber = sourceRow.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(rowNumber) ||
                !targetRowsByNumber.TryGetValue(rowNumber, out var targetRow))
            {
                continue;
            }

            foreach (var attribute in sourceRow.Attributes())
            {
                if (targetRow.Attribute(attribute.Name) is not null)
                    continue;

                targetRow.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }

            if (XlsxNativeXmlMerger.MergeExtensionList(sourceRow.Element(workbookNs + "extLst"), targetRow, workbookNs))
                changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetCellAttributes(XElement? sourceSheetData, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetData is null)
            return false;

        var targetSheetData = targetRoot.Element(workbookNs + "sheetData");
        if (targetSheetData is null)
            return false;

        var targetCellsByAddress = targetSheetData
            .Descendants(workbookNs + "c")
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Attribute("r")?.Value))
            .ToDictionary(
                cell => cell.Attribute("r")!.Value,
                StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var sourceCell in sourceSheetData.Descendants(workbookNs + "c"))
        {
            var address = sourceCell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(address) ||
                !targetCellsByAddress.TryGetValue(address, out var targetCell))
            {
                continue;
            }

            foreach (var attribute in sourceCell.Attributes())
            {
                if (targetCell.Attribute(attribute.Name) is not null)
                    continue;

                targetCell.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }

            if (XlsxNativeXmlMerger.MergeExtensionList(sourceCell.Element(workbookNs + "extLst"), targetCell, workbookNs))
                changed = true;
        }

        return changed;
    }

    private static void InsertWorksheetMetadataElementInOrder(
        XElement worksheetRoot,
        XNamespace workbookNs,
        XElement metadataElement)
    {
        string[] laterWorksheetElements = metadataElement.Name.LocalName switch
        {
            "sheetCalcPr" =>
            [
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            "protectedRanges" =>
            [
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            "scenarios" =>
            [
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            "customSheetViews" =>
            [
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            "phoneticPr" =>
            [
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            "customProperties" =>
            [
                "cellWatches",
                "ignoredErrors",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            "cellWatches" =>
            [
                "ignoredErrors",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            "queryTableParts" =>
            [
                "extLst"
            ],
            _ =>
            [
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ]
        };

        var insertionPoint = worksheetRoot.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == workbookNs &&
                laterWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
        if (insertionPoint is null)
            worksheetRoot.Add(metadataElement);
        else
            insertionPoint.AddBeforeSelf(metadataElement);
    }

    private static bool MergeWorksheetNativeOnlyElementAttributes(
        XElement? sourceElement,
        XElement targetRoot,
        XName elementName,
        HashSet<string> modeledAttributeNames)
    {
        if (sourceElement is null)
            return false;

        var retainedAttributes = sourceElement
            .Attributes()
            .Where(attribute => IsNativeOnlyWorksheetAttribute(attribute, modeledAttributeNames))
            .Select(attribute => new XAttribute(attribute))
            .ToList();
        if (retainedAttributes.Count == 0)
            return false;

        var targetElement = targetRoot.Element(elementName);
        if (targetElement is null)
        {
            targetRoot.Add(new XElement(elementName, retainedAttributes));
            return true;
        }

        var changed = false;
        foreach (var attribute in retainedAttributes)
        {
            if (targetElement.Attribute(attribute.Name) is not null)
                continue;

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool IsNativeOnlyWorksheetAttribute(XAttribute attribute, HashSet<string> modeledAttributeNames)
    {
        if (attribute.IsNamespaceDeclaration)
            return false;

        if (attribute.Name.NamespaceName.Length == 0 &&
            modeledAttributeNames.Contains(attribute.Name.LocalName))
        {
            return false;
        }

        return attribute.Name != XName.Get(
            "id",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
    }

    private static HashSet<uint> GetModeledWorksheetBreakIds(Workbook workbook, string sheetName, bool rowBreaks)
    {
        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return [];

        var maxBreakId = rowBreaks ? CellAddress.MaxRow : CellAddress.MaxCol;
        return (rowBreaks ? sheet.RowPageBreaks : sheet.ColumnPageBreaks)
            .Where(id => IsSupportedWorksheetBreakId(id, maxBreakId))
            .ToHashSet();
    }

    private static bool MergeWorksheetBreaks(
        XElement sourceBreaks,
        XElement targetRoot,
        XNamespace workbookNs,
        HashSet<uint> modeledBreakIds,
        uint maxBreakId)
    {
        var targetBreaks = targetRoot.Element(sourceBreaks.Name);
        if (targetBreaks is null)
        {
            var retainedBreaks = sourceBreaks
                .Elements(workbookNs + "brk")
                .Where(sourceBreak =>
                    !TryGetSupportedWorksheetBreakId(sourceBreak, maxBreakId, out var sourceId) ||
                    modeledBreakIds.Contains(sourceId))
                .Select(sourceBreak => new XElement(sourceBreak))
                .ToList();
            if (retainedBreaks.Count == 0)
                return false;

            targetRoot.Add(new XElement(sourceBreaks.Name, sourceBreaks.Attributes(), retainedBreaks));
            return true;
        }

        var changed = false;
        foreach (var attribute in sourceBreaks.Attributes())
        {
            if (targetBreaks.Attribute(attribute.Name) is not null)
                continue;

            targetBreaks.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var targetBreaksBySupportedId = targetBreaks
            .Elements(workbookNs + "brk")
            .Select(element => new
            {
                Element = element,
                Parsed = TryGetSupportedWorksheetBreakId(element, maxBreakId, out var id),
                Id = id
            })
            .Where(entry => entry.Parsed)
            .GroupBy(entry => entry.Id)
            .ToDictionary(
                group => group.Key,
                group => group.First().Element);
        var targetBreaksByRawId = targetBreaks
            .Elements(workbookNs + "brk")
            .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("id")?.Value))
            .GroupBy(element => element.Attribute("id")!.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var sourceBreak in sourceBreaks.Elements(workbookNs + "brk"))
        {
            var id = sourceBreak.Attribute("id")?.Value;
            if (TryGetSupportedWorksheetBreakId(sourceBreak, maxBreakId, out var sourceId))
            {
                if (!modeledBreakIds.Contains(sourceId))
                    continue;

                if (targetBreaksBySupportedId.TryGetValue(sourceId, out var targetBreak))
                {
                    changed |= MergeMissingAttributes(sourceBreak, targetBreak);
                    continue;
                }

                targetBreaks.Add(new XElement(sourceBreak));
                var addedBreak = targetBreaks.Elements(workbookNs + "brk").Last();
                targetBreaksBySupportedId[sourceId] = addedBreak;
                if (!string.IsNullOrWhiteSpace(id))
                    targetBreaksByRawId[id] = addedBreak;
                changed = true;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(id) &&
                targetBreaksByRawId.ContainsKey(id))
            {
                continue;
            }

            targetBreaks.Add(new XElement(sourceBreak));
            if (!string.IsNullOrWhiteSpace(id))
                targetBreaksByRawId[id] = targetBreaks.Elements(workbookNs + "brk").Last();
            changed = true;
        }

        return changed;
    }

    private static bool TryGetSupportedWorksheetBreakId(XElement breakElement, uint maxBreakId, out uint id)
    {
        id = 0;
        var rawId = breakElement.Attribute("id")?.Value;
        if (string.IsNullOrWhiteSpace(rawId) ||
            !uint.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out id))
        {
            return false;
        }

        return IsSupportedWorksheetBreakId(id, maxBreakId);
    }

    private static bool IsSupportedWorksheetBreakId(uint id, uint maxBreakId)
    {
        return id >= 2 && id <= maxBreakId;
    }

    private static bool MergeWorksheetCalculationProperties(
        XElement sourceSheetCalcPr,
        XElement targetRoot,
        XNamespace workbookNs)
    {
        var targetSheetCalcPr = targetRoot.Element(workbookNs + "sheetCalcPr");
        if (targetSheetCalcPr is null)
        {
            var retained = new XElement(sourceSheetCalcPr);
            retained.Attribute("fullCalcOnLoad")?.Remove();
            if (!retained.HasAttributes && !retained.HasElements)
                return false;

            InsertWorksheetMetadataElementInOrder(targetRoot, workbookNs, retained);
            return true;
        }

        var changed = MergeMissingAttributes(sourceSheetCalcPr, targetSheetCalcPr, ["fullCalcOnLoad"]);
        foreach (var sourceChild in sourceSheetCalcPr.Elements())
        {
            var targetChild = targetSheetCalcPr.Elements(sourceChild.Name)
                .FirstOrDefault(child => ElementIdentityKey(child) == ElementIdentityKey(sourceChild));
            if (targetChild is not null)
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetSheetCalcPr.Add(new XElement(sourceChild));
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetPhoneticProperties(
        XElement sourcePhoneticPr,
        XElement targetRoot,
        XNamespace workbookNs)
    {
        var modeledAttributes = new[] { "fontId", "type", "alignment" };
        var targetPhoneticPr = targetRoot.Element(workbookNs + "phoneticPr");
        if (targetPhoneticPr is null)
        {
            var retained = new XElement(sourcePhoneticPr);
            foreach (var attributeName in modeledAttributes)
                retained.Attribute(attributeName)?.Remove();
            if (!retained.HasAttributes && !retained.HasElements)
                return false;

            InsertWorksheetMetadataElementInOrder(targetRoot, workbookNs, retained);
            return true;
        }

        var changed = MergeMissingAttributes(sourcePhoneticPr, targetPhoneticPr, modeledAttributes);
        foreach (var sourceChild in sourcePhoneticPr.Elements())
        {
            var targetChild = targetPhoneticPr.Elements(sourceChild.Name)
                .FirstOrDefault(child => ElementIdentityKey(child) == ElementIdentityKey(sourceChild));
            if (targetChild is not null)
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetPhoneticPr.Add(new XElement(sourceChild));
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetCustomProperties(
        XElement sourceCustomProperties,
        XElement targetRoot,
        XNamespace workbookNs,
        IReadOnlySet<string> modeledPropertyNames)
    {
        var targetCustomProperties = targetRoot.Element(workbookNs + "customProperties");
        if (targetCustomProperties is null)
        {
            var retainedProperties = sourceCustomProperties
                .Elements(workbookNs + "customPr")
                .Where(property => !IsSupportedWorksheetCustomProperty(property))
                .Select(property => new XElement(property))
                .ToList();
            if (retainedProperties.Count == 0)
                return false;

            InsertWorksheetMetadataElementInOrder(
                targetRoot,
                workbookNs,
                new XElement(sourceCustomProperties.Name, sourceCustomProperties.Attributes(), retainedProperties));
            return true;
        }

        var changed = MergeMissingAttributes(sourceCustomProperties, targetCustomProperties, []);
        var targetPropertiesByName = targetCustomProperties
            .Elements(workbookNs + "customPr")
            .Select(property => new
            {
                Name = property.Attribute("name")?.Value,
                Element = property
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Element, StringComparer.OrdinalIgnoreCase);

        foreach (var sourceProperty in sourceCustomProperties.Elements(workbookNs + "customPr"))
        {
            var name = sourceProperty.Attribute("name")?.Value;
            if (!string.IsNullOrWhiteSpace(name) && targetPropertiesByName.TryGetValue(name, out var targetProperty))
            {
                changed |= MergeMissingAttributes(sourceProperty, targetProperty, ["name", "id"]);
                foreach (var sourceChild in sourceProperty.Elements())
                {
                    var targetChild = targetProperty.Elements(sourceChild.Name)
                        .FirstOrDefault(child => ElementIdentityKey(child) == ElementIdentityKey(sourceChild));
                    if (targetChild is not null)
                    {
                        if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                            changed = true;
                        continue;
                    }

                    targetProperty.Add(new XElement(sourceChild));
                    changed = true;
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(name) && modeledPropertyNames.Contains(name))
                continue;

            if (IsSupportedWorksheetCustomProperty(sourceProperty))
                continue;

            targetCustomProperties.Add(new XElement(sourceProperty));
            if (!string.IsNullOrWhiteSpace(name))
                targetPropertiesByName[name] = targetCustomProperties.Elements(workbookNs + "customPr").Last();
            changed = true;
        }

        return changed;
    }

    private static bool IsSupportedWorksheetCustomProperty(XElement customProperty)
    {
        return !string.IsNullOrWhiteSpace(customProperty.Attribute("name")?.Value) &&
               int.TryParse(
                   customProperty.Attribute("id")?.Value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out var id) &&
               id > 0;
    }

    private static bool MergeWorksheetCustomSheetViews(
        XElement sourceCustomSheetViews,
        XElement targetRoot,
        XNamespace workbookNs,
        IReadOnlySet<string> modeledCustomViewIds)
    {
        var targetCustomSheetViews = targetRoot.Element(workbookNs + "customSheetViews");
        if (targetCustomSheetViews is null)
        {
            var retainedViews = sourceCustomSheetViews
                .Elements(workbookNs + "customSheetView")
                .Where(view => !modeledCustomViewIds.Contains(XlsxCustomViewMapper.NormalizeId(view.Attribute("guid")?.Value) ?? string.Empty))
                .Select(view => new XElement(view))
                .ToList();
            if (retainedViews.Count == 0)
                return false;

            InsertWorksheetMetadataElementInOrder(
                targetRoot,
                workbookNs,
                new XElement(sourceCustomSheetViews.Name, sourceCustomSheetViews.Attributes(), retainedViews));
            return true;
        }

        var changed = MergeMissingAttributes(sourceCustomSheetViews, targetCustomSheetViews, []);
        var targetViewsById = targetCustomSheetViews
            .Elements(workbookNs + "customSheetView")
            .Select(view => new
            {
                Id = XlsxCustomViewMapper.NormalizeId(view.Attribute("guid")?.Value),
                View = view
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().View, StringComparer.OrdinalIgnoreCase);

        foreach (var sourceView in sourceCustomSheetViews.Elements(workbookNs + "customSheetView"))
        {
            var id = XlsxCustomViewMapper.NormalizeId(sourceView.Attribute("guid")?.Value);
            if (!string.IsNullOrWhiteSpace(id) && targetViewsById.TryGetValue(id, out var targetView))
            {
                changed |= MergeModeledCustomSheetViewMetadata(sourceView, targetView, workbookNs);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(id) && modeledCustomViewIds.Contains(id))
                continue;

            targetCustomSheetViews.Add(new XElement(sourceView));
            if (!string.IsNullOrWhiteSpace(id))
                targetViewsById[id] = targetCustomSheetViews.Elements(workbookNs + "customSheetView").Last();
            changed = true;
        }

        return changed;
    }

    private static bool MergeModeledCustomSheetViewMetadata(
        XElement sourceView,
        XElement targetView,
        XNamespace workbookNs)
    {
        var changed = MergeMissingAttributes(
            sourceView,
            targetView,
            ["guid", "view", "showGridLines", "showRowCol", "showRuler", "scale", "showFormulas", "state"]);

        var sourcePane = sourceView.Element(workbookNs + "pane");
        var targetPane = targetView.Element(workbookNs + "pane");
        if (sourcePane is not null && targetPane is not null)
        {
            changed |= MergeMissingAttributes(sourcePane, targetPane, ["xSplit", "ySplit", "state"]);
            foreach (var sourceChild in sourcePane.Elements())
            {
                var targetChild = targetPane.Elements(sourceChild.Name)
                    .FirstOrDefault(child => ElementIdentityKey(child) == ElementIdentityKey(sourceChild));
                if (targetChild is not null)
                {
                    if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                        changed = true;
                    continue;
                }

                targetPane.Add(new XElement(sourceChild));
                changed = true;
            }
        }

        foreach (var sourceChild in sourceView.Elements().Where(child => child.Name != workbookNs + "pane"))
        {
            var targetChild = targetView.Elements(sourceChild.Name)
                .FirstOrDefault(child => ElementIdentityKey(child) == ElementIdentityKey(sourceChild));
            if (targetChild is not null)
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetView.Add(new XElement(sourceChild));
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetScenarios(
        XElement sourceScenarios,
        XElement targetRoot,
        XNamespace workbookNs,
        HashSet<string> modeledScenarioNames)
    {
        var targetScenarios = targetRoot.Element(workbookNs + "scenarios");
        var changed = false;
        if (targetScenarios is not null)
        {
            changed |= MergeMissingAttributes(sourceScenarios, targetScenarios, ["current", "show"]);
        }

        foreach (var sourceScenario in sourceScenarios.Elements(workbookNs + "scenario"))
        {
            var name = sourceScenario.Attribute("name")?.Value;
            var supported = IsSupportedWorksheetScenario(sourceScenario, workbookNs);
            if (supported)
            {
                if (string.IsNullOrWhiteSpace(name) || !modeledScenarioNames.Contains(name))
                    continue;

                if (targetScenarios is null)
                {
                    targetScenarios = new XElement(
                        workbookNs + "scenarios",
                        sourceScenarios.Attributes()
                            .Where(attribute => !IsScenarioListIndexAttribute(attribute))
                            .Select(attribute => new XAttribute(attribute)));
                    InsertWorksheetMetadataElementInOrder(targetRoot, workbookNs, targetScenarios);
                    changed = true;
                }

                var targetScenario = targetScenarios
                    .Elements(workbookNs + "scenario")
                    .FirstOrDefault(element => string.Equals(
                        element.Attribute("name")?.Value,
                        name,
                        StringComparison.OrdinalIgnoreCase));
                if (targetScenario is not null)
                {
                    changed |= MergeScenarioMetadata(sourceScenario, targetScenario, workbookNs);
                }

                continue;
            }

            if (targetScenarios is null)
            {
                targetScenarios = new XElement(
                    workbookNs + "scenarios",
                    sourceScenarios.Attributes()
                        .Where(attribute => !IsScenarioListIndexAttribute(attribute))
                        .Select(attribute => new XAttribute(attribute)));
                InsertWorksheetMetadataElementInOrder(targetRoot, workbookNs, targetScenarios);
                changed = true;
            }

            if (HasEquivalentScenario(targetScenarios, sourceScenario))
                continue;

            targetScenarios.Add(new XElement(sourceScenario));
            changed = true;
        }

        return changed;
    }

    private static bool IsSupportedWorksheetScenario(XElement scenario, XNamespace workbookNs)
    {
        if (string.IsNullOrWhiteSpace(scenario.Attribute("name")?.Value))
            return false;

        var inputCells = scenario.Elements(workbookNs + "inputCells").ToList();
        if (inputCells.Count == 0)
            return false;

        return inputCells.All(inputCell =>
            !string.IsNullOrWhiteSpace(inputCell.Attribute("r")?.Value) &&
            inputCell.Attribute("val") is not null &&
            CellAddress.TryParse(inputCell.Attribute("r")!.Value, SheetId.New(), out _));
    }

    private static bool MergeScenarioMetadata(XElement sourceScenario, XElement targetScenario, XNamespace workbookNs)
    {
        var changed = MergeMissingAttributes(sourceScenario, targetScenario, ["name", "count"]);

        foreach (var sourceChild in sourceScenario.Elements().Where(child => child.Name != workbookNs + "inputCells"))
        {
            var targetChild = targetScenario.Elements(sourceChild.Name)
                .FirstOrDefault(child => ElementIdentityKey(child) == ElementIdentityKey(sourceChild));
            if (targetChild is not null)
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetScenario.Add(new XElement(sourceChild));
            changed = true;
        }

        var targetInputCellsByReference = targetScenario
            .Elements(workbookNs + "inputCells")
            .Where(inputCell => !string.IsNullOrWhiteSpace(inputCell.Attribute("r")?.Value))
            .GroupBy(inputCell => inputCell.Attribute("r")!.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        foreach (var sourceInputCell in sourceScenario.Elements(workbookNs + "inputCells"))
        {
            var reference = sourceInputCell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(reference) ||
                !targetInputCellsByReference.TryGetValue(reference, out var targetInputCell))
            {
                continue;
            }

            if (MergeMissingAttributes(sourceInputCell, targetInputCell, ["r", "val"]))
                changed = true;
            foreach (var sourceChild in sourceInputCell.Elements())
            {
                var targetChild = targetInputCell.Elements(sourceChild.Name)
                    .FirstOrDefault(child => ElementIdentityKey(child) == ElementIdentityKey(sourceChild));
                if (targetChild is not null)
                {
                    if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                        changed = true;
                    continue;
                }

                targetInputCell.Add(new XElement(sourceChild));
                changed = true;
            }
        }

        return changed;
    }

    private static bool IsScenarioListIndexAttribute(XAttribute attribute)
    {
        return !attribute.IsNamespaceDeclaration &&
               string.IsNullOrEmpty(attribute.Name.NamespaceName) &&
               (string.Equals(attribute.Name.LocalName, "current", StringComparison.Ordinal) ||
                string.Equals(attribute.Name.LocalName, "show", StringComparison.Ordinal));
    }

    private static bool HasEquivalentScenario(XElement targetScenarios, XElement sourceScenario)
    {
        var sourceRaw = sourceScenario.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        return targetScenarios
            .Elements(sourceScenario.Name)
            .Any(targetScenario => string.Equals(
                targetScenario.ToString(System.Xml.Linq.SaveOptions.DisableFormatting),
                sourceRaw,
                StringComparison.Ordinal));
    }

    private static bool MergeMissingAttributes(
        XElement sourceElement,
        XElement targetElement,
        IReadOnlyCollection<string> excludedLocalNames)
    {
        var changed = false;
        foreach (var attribute in sourceElement.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                excludedLocalNames.Contains(attribute.Name.LocalName, StringComparer.Ordinal) ||
                targetElement.Attribute(attribute.Name) is not null)
            {
                continue;
            }

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool MergeMissingAttributes(XElement sourceElement, XElement targetElement)
    {
        var changed = false;
        foreach (var attribute in sourceElement.Attributes())
        {
            if (targetElement.Attribute(attribute.Name) is not null)
                continue;

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetSheetFormatProperties(XElement? sourceSheetFormatProperties, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetFormatProperties is null)
            return false;

        var targetSheetFormatProperties = targetRoot.Element(workbookNs + "sheetFormatPr");
        if (targetSheetFormatProperties is null)
        {
            targetRoot.AddFirst(new XElement(sourceSheetFormatProperties));
            return true;
        }

        string[] nativeOnlyAttributes =
        [
            "baseColWidth",
            "zeroHeight",
            "thickTop",
            "thickBottom",
            "outlineLevelRow",
            "outlineLevelCol"
        ];
        var nativeOnlyAttributeNames = nativeOnlyAttributes
            .Select(name => XName.Get(name))
            .ToHashSet();

        var changed = false;
        foreach (var attribute in sourceSheetFormatProperties.Attributes())
        {
            if (targetSheetFormatProperties.Attribute(attribute.Name) is not null &&
                !nativeOnlyAttributeNames.Contains(attribute.Name))
            {
                continue;
            }

            if (targetSheetFormatProperties.Attribute(attribute.Name)?.Value == attribute.Value)
                continue;

            targetSheetFormatProperties.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetSheetViews(XElement? sourceSheetViews, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetViews is null)
            return false;

        var sourceViews = sourceSheetViews.Elements(workbookNs + "sheetView").ToList();
        if (sourceViews.Count == 0)
            return false;

        var targetSheetViews = targetRoot.Element(workbookNs + "sheetViews");
        if (targetSheetViews is null)
        {
            targetRoot.AddFirst(new XElement(sourceSheetViews));
            return true;
        }

        var existingViewIds = targetSheetViews
            .Elements(workbookNs + "sheetView")
            .Select(element => element.Attribute("workbookViewId")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var sourceView in sourceViews)
        {
            var viewId = sourceView.Attribute("workbookViewId")?.Value;
            var targetView = !string.IsNullOrWhiteSpace(viewId)
                ? targetSheetViews
                    .Elements(workbookNs + "sheetView")
                    .FirstOrDefault(element => string.Equals(
                        element.Attribute("workbookViewId")?.Value,
                        viewId,
                        StringComparison.OrdinalIgnoreCase))
                : null;
            if (targetView is not null)
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceView, targetView))
                    changed = true;
                continue;
            }

            targetSheetViews.Add(new XElement(sourceView));
            if (!string.IsNullOrWhiteSpace(viewId))
                existingViewIds.Add(viewId);
            changed = true;
        }

        return changed;
    }

    private static string ElementIdentityKey(XElement element)
    {
        var address = element.Attribute("pane")?.Value
            ?? element.Attribute("sqref")?.Value
            ?? element.Attribute("ref")?.Value
            ?? element.Attribute("r")?.Value
            ?? element.Attribute("activeCell")?.Value
            ?? element.Attribute("name")?.Value
            ?? element.Attribute("id")?.Value
            ?? element.Attribute("uid")?.Value
            ?? element.Attribute("uri")?.Value
            ?? string.Empty;
        return $"{element.Name}\u001f{address}";
    }

    private static bool MergeWorksheetProtectedRanges(
        XElement sourceProtectedRanges,
        XElement targetRoot,
        XNamespace workbookNs,
        IReadOnlySet<string> modeledSqrefs)
    {
        var targetProtectedRanges = targetRoot.Element(workbookNs + "protectedRanges");

        var changed = false;
        var targetBySqref = targetProtectedRanges is null
            ? new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase)
            : targetProtectedRanges
                .Elements(workbookNs + "protectedRange")
                .Select(element => (Element: element, Key: CanonicalSupportedProtectedRangeSqref(element)))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .GroupBy(pair => pair.Key!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Element,
                    StringComparer.OrdinalIgnoreCase);

        foreach (var sourceRange in sourceProtectedRanges.Elements(workbookNs + "protectedRange"))
        {
            var sourceSqref = CanonicalSupportedProtectedRangeSqref(sourceRange);
            if (!string.IsNullOrWhiteSpace(sourceSqref))
            {
                if (!modeledSqrefs.Contains(sourceSqref) ||
                    !targetBySqref.TryGetValue(sourceSqref, out var targetRange))
                {
                    continue;
                }

                if (MergeProtectedRangeMetadata(sourceRange, targetRange))
                    changed = true;
                continue;
            }

            if (targetProtectedRanges is null)
            {
                targetProtectedRanges = new XElement(workbookNs + "protectedRanges");
                targetRoot.Add(targetProtectedRanges);
                changed = true;
            }

            if (!HasEquivalentProtectedRange(targetProtectedRanges, sourceRange, workbookNs))
            {
                targetProtectedRanges.Add(new XElement(sourceRange));
                changed = true;
            }
        }

        return changed;
    }

    private static string? CanonicalSupportedProtectedRangeSqref(XElement protectedRange)
    {
        var sqref = protectedRange.Attribute("sqref")?.Value;
        if (string.IsNullOrWhiteSpace(sqref))
            return null;

        var tokens = sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length != 1)
            return null;

        return TryParseSqrefToken(tokens[0], SheetId.New(), out var range)
            ? range.ToString()
            : null;
    }

    private static bool MergeProtectedRangeMetadata(XElement sourceRange, XElement targetRange)
    {
        var changed = false;
        foreach (var sourceAttribute in sourceRange.Attributes())
        {
            if (sourceAttribute.Name == "sqref")
                continue;

            if (targetRange.Attribute(sourceAttribute.Name)?.Value == sourceAttribute.Value)
                continue;

            targetRange.SetAttributeValue(sourceAttribute.Name, sourceAttribute.Value);
            changed = true;
        }

        var existingChildNames = targetRange
            .Elements()
            .Select(element => element.Name)
            .ToHashSet();
        foreach (var sourceChild in sourceRange.Elements())
        {
            if (existingChildNames.Contains(sourceChild.Name))
                continue;

            targetRange.Add(new XElement(sourceChild));
            existingChildNames.Add(sourceChild.Name);
            changed = true;
        }

        return changed;
    }

    private static bool HasEquivalentProtectedRange(
        XElement targetProtectedRanges,
        XElement sourceRange,
        XNamespace workbookNs)
    {
        var sourceSqref = sourceRange.Attribute("sqref")?.Value;
        var sourceName = sourceRange.Attribute("name")?.Value;
        return targetProtectedRanges
            .Elements(workbookNs + "protectedRange")
            .Any(targetRange =>
                (!string.IsNullOrWhiteSpace(sourceSqref) &&
                 string.Equals(targetRange.Attribute("sqref")?.Value, sourceSqref, StringComparison.OrdinalIgnoreCase)) ||
                (string.IsNullOrWhiteSpace(sourceSqref) &&
                 !string.IsNullOrWhiteSpace(sourceName) &&
                 string.Equals(targetRange.Attribute("name")?.Value, sourceName, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool MergeWorksheetSheetProtection(XElement? sourceSheetProtection, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetProtection is null)
            return false;

        var targetSheetProtection = targetRoot.Element(workbookNs + "sheetProtection");
        if (targetSheetProtection is null)
        {
            targetRoot.Add(new XElement(sourceSheetProtection));
            return true;
        }

        var changed = false;
        foreach (var attribute in sourceSheetProtection.Attributes())
        {
            if (targetSheetProtection.Attribute(attribute.Name) is not null)
                continue;

            targetSheetProtection.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var existingChildNames = targetSheetProtection
            .Elements()
            .Select(element => element.Name)
            .ToHashSet();
        foreach (var sourceChild in sourceSheetProtection.Elements())
        {
            if (existingChildNames.Contains(sourceChild.Name))
                continue;

            targetSheetProtection.Add(new XElement(sourceChild));
            existingChildNames.Add(sourceChild.Name);
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetSheetProperties(XElement? sourceSheetProperties, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetProperties is null)
            return false;

        var targetSheetProperties = targetRoot.Element(workbookNs + "sheetPr");
        if (targetSheetProperties is null)
        {
            targetRoot.AddFirst(new XElement(sourceSheetProperties));
            return true;
        }

        var changed = false;
        foreach (var attribute in sourceSheetProperties.Attributes())
        {
            if (targetSheetProperties.Attribute(attribute.Name) is not null)
                continue;

            targetSheetProperties.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var existingChildNames = targetSheetProperties
            .Elements()
            .Select(element => element.Name)
            .ToHashSet();
        foreach (var sourceChild in sourceSheetProperties.Elements())
        {
            if (existingChildNames.Contains(sourceChild.Name))
                continue;

            targetSheetProperties.Add(new XElement(sourceChild));
            existingChildNames.Add(sourceChild.Name);
            changed = true;
        }

        return changed;
    }

    private static string QuoteSheetName(string sheetName) =>
        sheetName.Any(ch => char.IsWhiteSpace(ch) || ch == '\'')
            ? $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'"
            : sheetName;

    private static XDocument ToChartXml(ChartModel chart, Sheet sheet)
    {
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var plotCharts = ToPlotChartXml(chart, sheet, chartNs, drawingNs).ToList();

        return new XDocument(
                new XElement(chartNs + "chartSpace",
                    new XAttribute(XNamespace.Xmlns + "c", chartNs),
                    new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                    chart.ExternalData?.RelationshipId is null ? null : new XAttribute(XNamespace.Xmlns + "r", relNs),
                    chart.Uses1904DateSystem ? new XElement(chartNs + "date1904", new XAttribute("val", "1")) : null,
                    string.IsNullOrWhiteSpace(chart.Language) ? null : new XElement(chartNs + "lang", new XAttribute("val", chart.Language)),
                    chart.ChartStyleId is { } styleId ? new XElement(chartNs + "style", new XAttribute("val", styleId.ToString(CultureInfo.InvariantCulture))) : null,
                    ToChartColorMapOverrideXml(chart, chartNs, drawingNs),
                    chart.RoundedCorners ? new XElement(chartNs + "roundedCorners", new XAttribute("val", "1")) : null,
                    ToChartProtectionXml(chart, chartNs),
                    ToChartExternalDataXml(chart, chartNs, relNs),
                    ToChartPrintSettingsXml(chart, chartNs),
                    ToChartAreaShapeProperties(chart, chartNs, drawingNs),
                ToPivotSourceXml(chart, sheet, chartNs),
                new XElement(chartNs + "chart",
                    string.IsNullOrWhiteSpace(chart.Title)
                        ? null
                        : ToChartTitleXml(chart, chartNs, drawingNs),
                    chart.AutoTitleDeleted ? new XElement(chartNs + "autoTitleDeleted", new XAttribute("val", "1")) : null,
                    ToPivotFormatsXml(chart, chartNs),
                    new XElement(chartNs + "plotArea",
                        ToManualLayoutXml(chart.PlotAreaLayout, chartNs),
                        plotCharts,
                        ShouldWriteChartAxes(chart.Type)
                            ? ToChartAxesXml(chart, chartNs, drawingNs)
                            : null,
                        ToChartDataTableXml(chart, chartNs),
                        ToPlotAreaShapeProperties(chart, chartNs, drawingNs)),
                    ToLegendXml(chart, chartNs, drawingNs),
                    chart.ShowDataInHiddenRowsAndColumns ? new XElement(chartNs + "plotVisOnly", new XAttribute("val", "0")) : null,
                    ToBlankDisplayXml(chart, chartNs),
                    chart.ShowDataLabelsOverMaximum ? new XElement(chartNs + "showDLblsOverMax", new XAttribute("val", "1")) : null)));
    }

    private static XElement? ToPivotFormatsXml(ChartModel chart, XNamespace chartNs)
    {
        if (string.IsNullOrWhiteSpace(chart.PivotFormatsXml))
            return null;

        try
        {
            var element = XElement.Parse(chart.PivotFormatsXml);
            return element.Name == chartNs + "pivotFmts"
                ? element
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static XElement? ToChartDataTableXml(ChartModel chart, XNamespace chartNs)
    {
        if (chart.DataTable is not { } dataTable)
            return null;

        return new XElement(chartNs + "dTable",
            ToChartBooleanValueXml(chartNs, "showHorzBorder", dataTable.ShowHorizontalBorder),
            ToChartBooleanValueXml(chartNs, "showVertBorder", dataTable.ShowVerticalBorder),
            ToChartBooleanValueXml(chartNs, "showOutline", dataTable.ShowOutline),
            ToChartBooleanValueXml(chartNs, "showKeys", dataTable.ShowLegendKeys));
    }

    private static XElement? ToBlankDisplayXml(ChartModel chart, XNamespace chartNs) =>
        chart.BlankDisplayMode == ChartBlankDisplayMode.Gap
            ? null
            : new XElement(chartNs + "dispBlanksAs",
                new XAttribute("val", chart.BlankDisplayMode == ChartBlankDisplayMode.Span ? "span" : "zero"));

    private static XElement? ToChartExternalDataXml(ChartModel chart, XNamespace chartNs, XNamespace relNs)
    {
        if (chart.ExternalData is not { } externalData)
            return null;

        if (string.IsNullOrWhiteSpace(externalData.RelationshipId) && externalData.AutoUpdate is null)
            return null;

        return new XElement(chartNs + "externalData",
            string.IsNullOrWhiteSpace(externalData.RelationshipId)
                ? null
                : new XAttribute(relNs + "id", externalData.RelationshipId),
            externalData.AutoUpdate is { } autoUpdate
                ? new XElement(chartNs + "autoUpdate", new XAttribute("val", autoUpdate ? "1" : "0"))
                : null);
    }

    private static XElement? ToChartColorMapOverrideXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.ColorMapOverride is not { } colorMapOverride)
            return null;

        if (colorMapOverride.UseMasterColorMapping)
            return new XElement(chartNs + "clrMapOvr", new XElement(drawingNs + "masterClrMapping"));

        if (colorMapOverride.OverrideMappings.Count == 0)
            return null;

        return new XElement(chartNs + "clrMapOvr",
            new XElement(drawingNs + "overrideClrMapping",
                colorMapOverride.OverrideMappings
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new XAttribute(pair.Key, pair.Value))));
    }

    private static XElement? ToChartProtectionXml(ChartModel chart, XNamespace chartNs)
    {
        if (chart.Protection is not { } protection)
            return null;

        var element = new XElement(chartNs + "protection");
        AddOptionalBoolAttribute(element, "chartObject", protection.ChartObject);
        AddOptionalBoolAttribute(element, "data", protection.Data);
        AddOptionalBoolAttribute(element, "formatting", protection.Formatting);
        AddOptionalBoolAttribute(element, "selection", protection.Selection);
        AddOptionalBoolAttribute(element, "userInterface", protection.UserInterface);
        return element.HasAttributes ? element : null;
    }

    private static XElement? ToChartPrintSettingsXml(ChartModel chart, XNamespace chartNs)
    {
        if (chart.PrintSettings is not { } printSettings)
            return null;

        var element = new XElement(chartNs + "printSettings",
            ToChartPageMarginsXml(printSettings.PageMargins, chartNs),
            ToChartPageSetupXml(printSettings.PageSetup, chartNs));
        return element.HasElements ? element : null;
    }

    private static XElement? ToChartPageMarginsXml(ChartPageMarginsModel? margins, XNamespace chartNs)
    {
        if (margins is null)
            return null;

        var element = new XElement(chartNs + "pageMargins");
        AddOptionalDoubleAttribute(element, "l", margins.Left);
        AddOptionalDoubleAttribute(element, "r", margins.Right);
        AddOptionalDoubleAttribute(element, "t", margins.Top);
        AddOptionalDoubleAttribute(element, "b", margins.Bottom);
        AddOptionalDoubleAttribute(element, "header", margins.Header);
        AddOptionalDoubleAttribute(element, "footer", margins.Footer);
        return element.HasAttributes ? element : null;
    }

    private static XElement? ToChartPageSetupXml(ChartPageSetupModel? pageSetup, XNamespace chartNs)
    {
        if (pageSetup is null)
            return null;

        var element = new XElement(chartNs + "pageSetup");
        if (!string.IsNullOrWhiteSpace(pageSetup.PaperSize))
            element.SetAttributeValue("paperSize", pageSetup.PaperSize);
        if (!string.IsNullOrWhiteSpace(pageSetup.Orientation))
            element.SetAttributeValue("orientation", pageSetup.Orientation);
        if (pageSetup.Copies is { } copies)
            element.SetAttributeValue("copies", copies.ToString(CultureInfo.InvariantCulture));
        AddOptionalBoolAttribute(element, "blackAndWhite", pageSetup.BlackAndWhite);
        AddOptionalBoolAttribute(element, "draft", pageSetup.Draft);
        return element.HasAttributes ? element : null;
    }

    private static void AddOptionalBoolAttribute(XElement element, string name, bool? value)
    {
        if (value is { } boolValue)
            element.SetAttributeValue(name, boolValue ? "1" : "0");
    }

    private static void AddOptionalDoubleAttribute(XElement element, string name, double? value)
    {
        if (value is { } doubleValue)
            element.SetAttributeValue(name, doubleValue.ToString("G15", CultureInfo.InvariantCulture));
    }

    private static XElement? ToPivotSourceXml(ChartModel chart, Sheet sheet, XNamespace chartNs)
    {
        if (!chart.IsPivotChart || string.IsNullOrWhiteSpace(chart.PivotTableName))
            return null;

        var sourceSheetName = string.IsNullOrWhiteSpace(chart.PivotSourceSheetName)
            ? sheet.Name
            : chart.PivotSourceSheetName;
        return new XElement(chartNs + "pivotSource",
            new XElement(chartNs + "name", $"{QuoteSheetName(sourceSheetName)}!{chart.PivotTableName}"),
            new XElement(chartNs + "fmtId", new XAttribute("val", "0")));
    }

    private static bool ShouldWriteChartAxes(ChartType chartType) =>
        chartType is not ChartType.Pie and not ChartType.Doughnut;

    private static IEnumerable<XElement> ToPlotChartXml(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var dataLabelWritten = false;
        foreach (var (plotChart, usesSecondaryAxis) in CreatePlotCharts(chart, sheet, chartNs, drawingNs))
        {
            AddPlotChartCommonElements(plotChart, chart, chartNs, drawingNs, usesSecondaryAxis, includeDataLabels: !dataLabelWritten);
            dataLabelWritten = true;
            yield return plotChart;
        }
    }

    private static IEnumerable<(XElement PlotChart, bool UsesSecondaryAxis)> CreatePlotCharts(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
        var secondaryIndexes = GetSecondaryAxisSeriesIndexes(chart, seriesCount);
        var comboLineIndexes = GetComboLineSeriesIndexes(chart, seriesCount);
        if (secondaryIndexes.Count > 0 && chart.Type == ChartType.Scatter)
        {
            var primaryScatter = Enumerable.Range(0, seriesCount)
                .Where(index => !secondaryIndexes.Contains(index))
                .ToHashSet();
            if (primaryScatter.Count > 0)
                yield return (CreateScatterPlotChart(chart, sheet, chartNs, drawingNs, primaryScatter.Contains), false);
            yield return (CreateScatterPlotChart(chart, sheet, chartNs, drawingNs, secondaryIndexes.Contains), true);
            yield break;
        }

        if (secondaryIndexes.Count > 0 && chart.Type == ChartType.Line)
        {
            var primaryLine = Enumerable.Range(0, seriesCount)
                .Where(index => !secondaryIndexes.Contains(index))
                .ToHashSet();
            if (primaryLine.Count > 0)
                yield return (CreateLinePlotChart(chart, sheet, chartNs, drawingNs, primaryLine.Contains), false);
            yield return (CreateLinePlotChart(chart, sheet, chartNs, drawingNs, secondaryIndexes.Contains), true);
            yield break;
        }

        if ((secondaryIndexes.Count > 0 || comboLineIndexes.Count > 0) &&
            chart.Type is ChartType.Column or ChartType.StackedColumn or ChartType.PercentStackedColumn or ChartType.Area)
        {
            var primaryBase = Enumerable.Range(0, seriesCount)
                .Where(index => !secondaryIndexes.Contains(index) && !comboLineIndexes.Contains(index))
                .ToHashSet();
            var secondaryBase = secondaryIndexes
                .Where(index => !comboLineIndexes.Contains(index))
                .ToHashSet();
            var primaryLine = comboLineIndexes
                .Where(index => !secondaryIndexes.Contains(index))
                .ToHashSet();
            var secondaryLine = comboLineIndexes
                .Where(secondaryIndexes.Contains)
                .ToHashSet();

            if (primaryBase.Count > 0)
                yield return (CreateNativePlotChart(chart, sheet, chartNs, drawingNs, primaryBase.Contains), false);
            if (secondaryBase.Count > 0)
                yield return (CreateNativePlotChart(chart, sheet, chartNs, drawingNs, secondaryBase.Contains), true);
            if (primaryLine.Count > 0)
                yield return (CreateLinePlotChart(chart, sheet, chartNs, drawingNs, primaryLine.Contains), false);
            if (secondaryLine.Count > 0)
                yield return (CreateLinePlotChart(chart, sheet, chartNs, drawingNs, secondaryLine.Contains), true);

            yield break;
        }

        yield return (CreateNativePlotChart(chart, sheet, chartNs, drawingNs, _ => true), false);
    }

    private static XElement CreateNativePlotChart(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool> includeSeries) =>
        chart.Type switch
        {
            ChartType.Line => CreateLinePlotChart(chart, sheet, chartNs, drawingNs, includeSeries),
            ChartType.Scatter => CreateScatterPlotChart(chart, sheet, chartNs, drawingNs, includeSeries),
            ChartType.Radar => new XElement(chartNs + "radarChart",
                new XElement(chartNs + "radarStyle", new XAttribute("val", "marker")),
                BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries, forceLineShapeProperties: true)),
            ChartType.Stock => new XElement(chartNs + "stockChart",
                BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries, forceLineShapeProperties: true),
                ToChartGuideLineXml(chart, chartNs)),
            ChartType.Area => new XElement(chartNs + "areaChart",
                new XElement(chartNs + "grouping", new XAttribute("val", "standard")),
                BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries)),
            ChartType.Bubble => new XElement(chartNs + "bubbleChart",
                BuildBubbleChartSeries(chart, sheet, chartNs, drawingNs)),
            ChartType.Pie => new XElement(chartNs + "pieChart",
                ToFirstSliceAngleXml(chart, chartNs),
                BuildPieFamilyChartSeries(chart, sheet, chartNs, drawingNs)),
            ChartType.Doughnut => new XElement(chartNs + "doughnutChart",
                ToFirstSliceAngleXml(chart, chartNs),
                BuildPieFamilyChartSeries(chart, sheet, chartNs, drawingNs),
                new XElement(chartNs + "holeSize",
                    new XAttribute("val", Math.Clamp((int)Math.Round(chart.DoughnutHoleSize * 100), 10, 90)))),
            _ => WithBarChartSpacing(new XElement(chartNs + "barChart",
                new XElement(chartNs + "barDir", new XAttribute("val", ToXlsxBarDirection(chart.Type))),
                new XElement(chartNs + "grouping", new XAttribute("val", ToXlsxBarGrouping(chart.Type))),
                ToChartBooleanValueXml(chartNs, "varyColors", chart.VaryColorsByPoint),
                BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries)), chart, chartNs)
        };

    private static XElement WithBarChartSpacing(XElement barChart, ChartModel chart, XNamespace chartNs)
    {
        if (chart.BarOverlap is { } overlap)
            barChart.Add(new XElement(chartNs + "overlap", new XAttribute("val", Math.Clamp(overlap, -100, 100))));
        if (chart.BarGapWidth is { } gapWidth)
            barChart.Add(new XElement(chartNs + "gapWidth", new XAttribute("val", Math.Clamp(gapWidth, 0, 500))));
        return barChart;
    }

    private static XElement? ToChartBooleanValueXml(XNamespace chartNs, string elementName, bool? value) =>
        value.HasValue
            ? new XElement(chartNs + elementName, new XAttribute("val", value.Value ? "1" : "0"))
            : null;

    private static XElement CreateLinePlotChart(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool> includeSeries) =>
        new(chartNs + "lineChart",
            BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries, forceLineShapeProperties: true),
            ToChartGuideLineXml(chart, chartNs));

    private static XElement CreateScatterPlotChart(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool> includeSeries) =>
        new(chartNs + "scatterChart",
            new XElement(chartNs + "scatterStyle", new XAttribute("val", "lineMarker")),
            BuildScatterChartSeries(chart, sheet, chartNs, drawingNs, includeSeries));

    private static IEnumerable<XElement> ToChartGuideLineXml(ChartModel chart, XNamespace chartNs)
    {
        if (chart.ShowDropLines)
            yield return new XElement(chartNs + "dropLines");
        if (chart.ShowHighLowLines)
            yield return new XElement(chartNs + "hiLowLines");
        if (chart.ShowUpDownBars)
            yield return new XElement(chartNs + "upDownBars");
    }

    private static void AddPlotChartCommonElements(
        XElement plotChart,
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs,
        bool usesSecondaryAxis,
        bool includeDataLabels)
    {
        if (includeDataLabels && ToDataLabelsXml(chart, chartNs, drawingNs) is { } dataLabels)
            plotChart.Add(dataLabels);

        if (!ShouldWriteChartAxes(chart.Type))
            return;

        plotChart.Add(
            new XElement(chartNs + "axId", new XAttribute("val", CategoryAxisId)),
            new XElement(chartNs + "axId", new XAttribute("val", usesSecondaryAxis ? SecondaryValueAxisId : ValueAxisId)));
    }

    private static IEnumerable<XElement> ToChartAxesXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.Type is ChartType.Scatter or ChartType.Bubble)
        {
            yield return ToValueAxisXml(
                chart.XAxisTitle,
                CategoryAxisId,
                ValueAxisId,
                "b",
                chart.XAxisMinimum,
                chart.XAxisMaximum,
                chart.XAxisMajorUnit,
                chart.XAxisMinorUnit,
                chart.XAxisLogScale,
                chart.XAxisNumberFormat,
                chart.ShowXAxisMajorGridlines,
                chart.ShowXAxisMinorGridlines,
                chart.XAxisMajorGridlineColor,
                chart.XAxisMinorGridlineColor,
                chart.XAxisGridlineThickness,
                chart.XAxisMajorTickStyle,
                chart.XAxisMinorTickStyle,
                chart.XAxisLineColor,
                chart.XAxisLineThickness,
                chart.ShowXAxisLabels,
                chart.AxisTitleTextColor,
                chart.AxisTitleFontSize,
                chartNs,
                drawingNs);
            yield return ToValueAxisXml(
                chart.YAxisTitle,
                ValueAxisId,
                CategoryAxisId,
                "l",
                chart.YAxisMinimum,
                chart.YAxisMaximum,
                chart.YAxisMajorUnit,
                chart.YAxisMinorUnit,
                chart.YAxisLogScale,
                chart.YAxisNumberFormat,
                chart.ShowYAxisMajorGridlines,
                chart.ShowYAxisMinorGridlines,
                chart.YAxisMajorGridlineColor,
                chart.YAxisMinorGridlineColor,
                chart.YAxisGridlineThickness,
                chart.YAxisMajorTickStyle,
                chart.YAxisMinorTickStyle,
                chart.YAxisLineColor,
                chart.YAxisLineThickness,
                chart.ShowYAxisLabels,
                chart.AxisTitleTextColor,
                chart.AxisTitleFontSize,
                chartNs,
                drawingNs);
            var scatterSecondaryIndexes = GetSecondaryAxisSeriesIndexes(chart, ChartTypeSupport.GetDataSeriesCount(chart));
            if (chart.Type == ChartType.Scatter && scatterSecondaryIndexes.Count > 0)
            {
                yield return ToValueAxisXml(
                    null,
                    SecondaryValueAxisId,
                    CategoryAxisId,
                    "r",
                    chart.YAxisMinimum,
                    chart.YAxisMaximum,
                    chart.YAxisMajorUnit,
                    chart.YAxisMinorUnit,
                    chart.YAxisLogScale,
                    chart.YAxisNumberFormat,
                    false,
                    false,
                    null,
                    null,
                    chart.YAxisGridlineThickness,
                    chart.YAxisMajorTickStyle,
                    chart.YAxisMinorTickStyle,
                    chart.YAxisLineColor,
                    chart.YAxisLineThickness,
                    chart.ShowYAxisLabels,
                    chart.AxisTitleTextColor,
                    chart.AxisTitleFontSize,
                    chartNs,
                    drawingNs);
            }
            yield break;
        }

        yield return ToCategoryAxisXml(chart, chartNs, drawingNs);
        yield return ToValueAxisXml(
            chart.YAxisTitle,
            ValueAxisId,
            CategoryAxisId,
            "l",
            chart.YAxisMinimum,
            chart.YAxisMaximum,
            chart.YAxisMajorUnit,
            chart.YAxisMinorUnit,
            chart.YAxisLogScale,
            chart.YAxisNumberFormat,
            chart.ShowYAxisMajorGridlines,
            chart.ShowYAxisMinorGridlines,
            chart.YAxisMajorGridlineColor,
            chart.YAxisMinorGridlineColor,
            chart.YAxisGridlineThickness,
            chart.YAxisMajorTickStyle,
            chart.YAxisMinorTickStyle,
            chart.YAxisLineColor,
            chart.YAxisLineThickness,
            chart.ShowYAxisLabels,
            chart.AxisTitleTextColor,
            chart.AxisTitleFontSize,
            chartNs,
            drawingNs);

        var secondaryIndexes = GetSecondaryAxisSeriesIndexes(chart, ChartTypeSupport.GetDataSeriesCount(chart));
        if (secondaryIndexes.Count > 0)
        {
            yield return ToValueAxisXml(
                null,
                SecondaryValueAxisId,
                CategoryAxisId,
                "r",
                chart.YAxisMinimum,
                chart.YAxisMaximum,
                chart.YAxisMajorUnit,
                chart.YAxisMinorUnit,
                chart.YAxisLogScale,
                chart.YAxisNumberFormat,
                false,
                false,
                null,
                null,
                chart.YAxisGridlineThickness,
                chart.YAxisMajorTickStyle,
                chart.YAxisMinorTickStyle,
                chart.YAxisLineColor,
                chart.YAxisLineThickness,
                chart.ShowYAxisLabels,
                chart.AxisTitleTextColor,
                chart.AxisTitleFontSize,
                chartNs,
                drawingNs);
        }
    }

    private static XElement ToCategoryAxisXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs) =>
        new(chartNs + "catAx",
            new XElement(chartNs + "axId", new XAttribute("val", CategoryAxisId)),
            new XElement(chartNs + "scaling",
                new XElement(chartNs + "orientation", new XAttribute("val", "minMax"))),
            new XElement(chartNs + "delete", new XAttribute("val", "0")),
            new XElement(chartNs + "axPos", new XAttribute("val", "b")),
            ToAxisTitleXml(chart.XAxisTitle, chart.AxisTitleTextColor, chart.AxisTitleFontSize, chartNs, drawingNs),
            ToAxisGridlinesXml("majorGridlines", chart.ShowXAxisMajorGridlines, chart.XAxisMajorGridlineColor, chart.XAxisGridlineThickness, chartNs, drawingNs),
            ToAxisGridlinesXml("minorGridlines", chart.ShowXAxisMinorGridlines, chart.XAxisMinorGridlineColor, chart.XAxisGridlineThickness, chartNs, drawingNs),
            new XElement(chartNs + "majorTickMark", new XAttribute("val", ToXlsxTickMark(chart.XAxisMajorTickStyle))),
            new XElement(chartNs + "minorTickMark", new XAttribute("val", ToXlsxTickMark(chart.XAxisMinorTickStyle))),
            new XElement(chartNs + "tickLblPos", new XAttribute("val", ToXlsxTickLabelPosition(chart.ShowXAxisLabels))),
            ToAxisLineShapeProperties(chart.XAxisLineColor, chart.XAxisLineThickness, chartNs, drawingNs),
            new XElement(chartNs + "crossAx", new XAttribute("val", ValueAxisId)),
            new XElement(chartNs + "crosses", new XAttribute("val", "autoZero")));

    private static XElement ToValueAxisXml(
        string? title,
        int axisId,
        int crossAxisId,
        string axisPosition,
        double? minimum,
        double? maximum,
        double? majorUnit,
        double? minorUnit,
        bool logScale,
        ChartDataLabelNumberFormat numberFormat,
        bool showMajorGridlines,
        bool showMinorGridlines,
        CellColor? majorGridlineColor,
        CellColor? minorGridlineColor,
        double gridlineThickness,
        ChartAxisTickStyle majorTickStyle,
        ChartAxisTickStyle minorTickStyle,
        CellColor? lineColor,
        double lineThickness,
        bool showLabels,
        CellColor? axisTitleTextColor,
        double axisTitleFontSize,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        new(chartNs + "valAx",
            new XElement(chartNs + "axId", new XAttribute("val", axisId)),
            new XElement(chartNs + "scaling",
                logScale ? new XElement(chartNs + "logBase", new XAttribute("val", "10")) : null,
                new XElement(chartNs + "orientation", new XAttribute("val", "minMax")),
                ToAxisBoundXml("max", maximum, chartNs),
                ToAxisBoundXml("min", minimum, chartNs)),
            new XElement(chartNs + "delete", new XAttribute("val", "0")),
            new XElement(chartNs + "axPos", new XAttribute("val", axisPosition)),
            ToAxisTitleXml(title, axisTitleTextColor, axisTitleFontSize, chartNs, drawingNs),
            new XElement(chartNs + "numFmt",
                new XAttribute("formatCode", ToXlsxNumberFormatCode(numberFormat)),
                new XAttribute("sourceLinked", numberFormat == ChartDataLabelNumberFormat.General ? "1" : "0")),
            ToAxisGridlinesXml("majorGridlines", showMajorGridlines, majorGridlineColor, gridlineThickness, chartNs, drawingNs),
            ToAxisGridlinesXml("minorGridlines", showMinorGridlines, minorGridlineColor, gridlineThickness, chartNs, drawingNs),
            ToAxisUnitXml("majorUnit", majorUnit, chartNs),
            ToAxisUnitXml("minorUnit", minorUnit, chartNs),
            new XElement(chartNs + "majorTickMark", new XAttribute("val", ToXlsxTickMark(majorTickStyle))),
            new XElement(chartNs + "minorTickMark", new XAttribute("val", ToXlsxTickMark(minorTickStyle))),
            new XElement(chartNs + "tickLblPos", new XAttribute("val", ToXlsxTickLabelPosition(showLabels))),
            ToAxisLineShapeProperties(lineColor, lineThickness, chartNs, drawingNs),
            new XElement(chartNs + "crossAx", new XAttribute("val", crossAxisId)),
            new XElement(chartNs + "crosses", new XAttribute("val", "autoZero")));

    private static XElement? ToAxisGridlinesXml(
        string elementName,
        bool visible,
        CellColor? color,
        double thickness,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (!visible)
            return null;

        return new XElement(chartNs + elementName,
            ToShapeProperties(
                chartNs,
                drawingNs,
                fillThemeColor: null,
                fillColor: null,
                borderThemeColor: null,
                borderColor: color,
                borderThickness: Math.Clamp(thickness, 0.25, 10)));
    }

    private static XElement? ToAxisLineShapeProperties(
        CellColor? lineColor,
        double lineThickness,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        ToShapeProperties(
            chartNs,
            drawingNs,
            fillThemeColor: null,
            fillColor: null,
            borderThemeColor: null,
            borderColor: lineColor,
            borderThickness: Math.Clamp(lineThickness, 0.5, 10));

    private static string ToXlsxTickMark(ChartAxisTickStyle tickStyle) =>
        tickStyle switch
        {
            ChartAxisTickStyle.None => "none",
            ChartAxisTickStyle.Inside => "in",
            ChartAxisTickStyle.Cross => "cross",
            _ => "out"
        };

    private static string ToXlsxTickLabelPosition(bool showLabels) =>
        showLabels ? "nextTo" : "none";

    private static XElement? ToAxisBoundXml(string elementName, double? value, XNamespace chartNs) =>
        value is { } numeric && double.IsFinite(numeric)
            ? new XElement(chartNs + elementName, new XAttribute("val", numeric.ToString(CultureInfo.InvariantCulture)))
            : null;

    private static XElement? ToAxisUnitXml(string elementName, double? value, XNamespace chartNs) =>
        value is { } numeric && double.IsFinite(numeric)
            ? new XElement(chartNs + elementName, new XAttribute("val", Math.Max(numeric, double.Epsilon).ToString(CultureInfo.InvariantCulture)))
            : null;

    private static string ToXlsxNumberFormatCode(ChartDataLabelNumberFormat format) =>
        format switch
        {
            ChartDataLabelNumberFormat.Number => "0.00",
            ChartDataLabelNumberFormat.Currency => "$#,##0.00",
            ChartDataLabelNumberFormat.Percent => "0%",
            _ => "General"
        };

    private static XElement ToChartTitleXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs) =>
        new(chartNs + "title",
            new XElement(chartNs + "tx",
                new XElement(chartNs + "rich",
                    new XElement(drawingNs + "p",
                        new XElement(drawingNs + "r",
                            ToTextRunProperties(null, chart.ChartTitleTextColor, chart.ChartTitleFontSize, drawingNs),
                            new XElement(drawingNs + "t", chart.Title))))));

    private static XElement? ToAxisTitleXml(
        string? title,
        CellColor? textColor,
        double fontSize,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        string.IsNullOrWhiteSpace(title)
            ? null
            : new XElement(chartNs + "title",
                new XElement(chartNs + "tx",
                    new XElement(chartNs + "rich",
                        new XElement(drawingNs + "p",
                            new XElement(drawingNs + "r",
                                ToTextRunProperties(null, textColor, fontSize, drawingNs),
                                new XElement(drawingNs + "t", title))))));

    private static XElement? ToTextRunProperties(
        WorkbookThemeColorReference? textThemeColor,
        CellColor? textColor,
        double fontSize,
        XNamespace drawingNs)
    {
        var size = Math.Clamp((int)Math.Round(fontSize * 100), 600, 7200);
        return new XElement(drawingNs + "rPr",
            new XAttribute("sz", size),
            ToTextRunPropertiesContent(textThemeColor, textColor, fontSize, drawingNs));
    }

    private static IEnumerable<object> ToTextRunPropertiesContent(
        WorkbookThemeColorReference? textThemeColor,
        CellColor? textColor,
        double fontSize,
        XNamespace drawingNs)
    {
        var fill = ToSolidFill(textThemeColor, textColor, drawingNs);
        if (fill is not null)
        {
            yield return fill;
        }
    }

    private static XElement? ToChartAreaShapeProperties(
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        ToShapeProperties(
            chartNs,
            drawingNs,
            chart.ChartAreaFillThemeColor,
            chart.ChartAreaFillColor,
            borderThemeColor: null,
            borderColor: null,
            borderThickness: null);

    private static XElement? ToPlotAreaShapeProperties(
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        ToShapeProperties(
            chartNs,
            drawingNs,
            chart.PlotAreaFillThemeColor,
            chart.PlotAreaFillColor,
            chart.PlotAreaBorderThemeColor,
            chart.PlotAreaBorderColor,
            chart.PlotAreaBorderThickness);

    private static XElement? ToShapeProperties(
        XNamespace chartNs,
        XNamespace drawingNs,
        WorkbookThemeColorReference? fillThemeColor,
        CellColor? fillColor,
        WorkbookThemeColorReference? borderThemeColor,
        CellColor? borderColor,
        double? borderThickness)
    {
        var fill = ToSolidFill(fillThemeColor, fillColor, drawingNs);
        var lineFill = ToSolidFill(borderThemeColor, borderColor, drawingNs);
        var line = lineFill is null && borderThickness is null
            ? null
            : new XElement(drawingNs + "ln",
                borderThickness is null
                    ? null
                    : new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(borderThickness.Value, 0, 10) * 12700))),
                lineFill);

        return fill is null && line is null
            ? null
            : new XElement(chartNs + "spPr", fill, line);
    }

    private static XElement? ToSolidFill(
        WorkbookThemeColorReference? themeColor,
        CellColor? color,
        XNamespace drawingNs)
    {
        XElement? colorElement = null;
        if (themeColor is { } theme)
        {
            colorElement = new XElement(drawingNs + "schemeClr",
                new XAttribute("val", ToDrawingSchemeColor(theme.Slot)));
            ApplyTint(colorElement, theme.Tint, drawingNs);
        }
        else if (color is { } concrete)
        {
            colorElement = new XElement(drawingNs + "srgbClr",
                new XAttribute("val", FormatThemeColor(concrete)));
        }

        return colorElement is null
            ? null
            : new XElement(drawingNs + "solidFill", colorElement);
    }

    private static string FormatThemeColor(CellColor color) =>
        $"{color.R:X2}{color.G:X2}{color.B:X2}";

    private static void ApplyTint(XElement colorElement, double tint, XNamespace drawingNs)
    {
        if (tint > 0)
        {
            colorElement.Add(
                new XElement(drawingNs + "lumMod", new XAttribute("val", Math.Clamp((int)Math.Round((1 - tint) * 100000), 0, 100000))),
                new XElement(drawingNs + "lumOff", new XAttribute("val", Math.Clamp((int)Math.Round(tint * 100000), 0, 100000))));
        }
        else if (tint < 0)
        {
            colorElement.Add(new XElement(drawingNs + "lumMod",
                new XAttribute("val", Math.Clamp((int)Math.Round((1 + tint) * 100000), 0, 100000))));
        }
    }

    private static XElement? ToLegendXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (!chart.ShowLegend || chart.LegendPosition == ChartLegendPosition.None)
            return null;

        return new XElement(chartNs + "legend",
            new XElement(chartNs + "legendPos",
                new XAttribute("val", ToXlsxLegendPosition(chart.LegendPosition))),
            ToManualLayoutXml(chart.LegendLayout, chartNs),
            new XElement(chartNs + "overlay",
                new XAttribute("val", chart.LegendOverlay ? "1" : "0")),
            ToShapeProperties(
                chartNs,
                drawingNs,
                chart.LegendFillThemeColor,
                chart.LegendFillColor,
                chart.LegendBorderThemeColor,
                chart.LegendBorderColor,
                chart.LegendBorderThickness),
            ToLegendTextProperties(chart, chartNs, drawingNs));
    }

    private static XElement? ToManualLayoutXml(ChartManualLayoutModel? layout, XNamespace chartNs)
    {
        if (layout is null ||
            string.IsNullOrWhiteSpace(layout.LayoutTarget) &&
            string.IsNullOrWhiteSpace(layout.XMode) &&
            string.IsNullOrWhiteSpace(layout.YMode) &&
            string.IsNullOrWhiteSpace(layout.WidthMode) &&
            string.IsNullOrWhiteSpace(layout.HeightMode) &&
            layout.X is null &&
            layout.Y is null &&
            layout.Width is null &&
            layout.Height is null)
        {
            return null;
        }

        return new XElement(chartNs + "layout",
            new XElement(chartNs + "manualLayout",
                string.IsNullOrWhiteSpace(layout.LayoutTarget) ? null : new XElement(chartNs + "layoutTarget", new XAttribute("val", layout.LayoutTarget)),
                string.IsNullOrWhiteSpace(layout.XMode) ? null : new XElement(chartNs + "xMode", new XAttribute("val", layout.XMode)),
                string.IsNullOrWhiteSpace(layout.YMode) ? null : new XElement(chartNs + "yMode", new XAttribute("val", layout.YMode)),
                string.IsNullOrWhiteSpace(layout.WidthMode) ? null : new XElement(chartNs + "wMode", new XAttribute("val", layout.WidthMode)),
                string.IsNullOrWhiteSpace(layout.HeightMode) ? null : new XElement(chartNs + "hMode", new XAttribute("val", layout.HeightMode)),
                layout.X is { } x ? new XElement(chartNs + "x", new XAttribute("val", ToChartLayoutDecimal(x))) : null,
                layout.Y is { } y ? new XElement(chartNs + "y", new XAttribute("val", ToChartLayoutDecimal(y))) : null,
                layout.Width is { } width ? new XElement(chartNs + "w", new XAttribute("val", ToChartLayoutDecimal(width))) : null,
                layout.Height is { } height ? new XElement(chartNs + "h", new XAttribute("val", ToChartLayoutDecimal(height))) : null));
    }

    private static string ToChartLayoutDecimal(double value) =>
        value.ToString("0.###############", CultureInfo.InvariantCulture);

    private static XElement? ToLegendTextProperties(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.LegendTextColor is null && chart.LegendTextThemeColor is null && chart.LegendFontSize == 12)
            return null;

        return new XElement(chartNs + "txPr",
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        new XAttribute("sz", Math.Clamp((int)Math.Round(chart.LegendFontSize * 100), 600, 7200)),
                        ToTextRunPropertiesContent(chart.LegendTextThemeColor, chart.LegendTextColor, chart.LegendFontSize, drawingNs)))));
    }

    private static XElement? ToDataLabelsXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (!chart.ShowDataLabels)
            return null;

        return new XElement(chartNs + "dLbls",
            new XElement(chartNs + "dLblPos", new XAttribute("val", ToXlsxDataLabelPosition(chart.DataLabelPosition))),
            new XElement(chartNs + "numFmt",
                new XAttribute("formatCode", ToXlsxNumberFormatCode(chart.DataLabelNumberFormat)),
                new XAttribute("sourceLinked", chart.DataLabelNumberFormat == ChartDataLabelNumberFormat.General ? "1" : "0")),
            ToShapeProperties(
                chartNs,
                drawingNs,
                chart.DataLabelFillThemeColor,
                chart.DataLabelFillColor,
                chart.DataLabelBorderThemeColor,
                chart.DataLabelBorderColor,
                chart.DataLabelBorderThickness),
            ToDataLabelTextProperties(chart, chartNs, drawingNs),
            new XElement(chartNs + "showLegendKey", new XAttribute("val", "0")),
            new XElement(chartNs + "showVal", new XAttribute("val", "1")),
            new XElement(chartNs + "showCatName", new XAttribute("val", chart.ShowDataLabelCategoryName ? "1" : "0")),
            new XElement(chartNs + "showSerName", new XAttribute("val", chart.ShowDataLabelSeriesName ? "1" : "0")),
            new XElement(chartNs + "showPercent", new XAttribute("val", chart.ShowDataLabelPercentage && ChartTypeSupport.SupportsPercentageDataLabels(chart.Type) ? "1" : "0")),
            new XElement(chartNs + "showBubbleSize", new XAttribute("val", "0")),
            new XElement(chartNs + "separator",
                chart.DataLabelSeparator == ChartDataLabelSeparator.NewLine
                    ? new XAttribute(XNamespace.Xml + "space", "preserve")
                    : null,
                ToXlsxDataLabelSeparator(chart.DataLabelSeparator)),
            new XElement(chartNs + "showLeaderLines", new XAttribute("val", chart.ShowDataLabelCallouts ? "1" : "0")));
    }

    private static XElement? ToDataLabelTextProperties(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.DataLabelTextColor is null && chart.DataLabelTextThemeColor is null && chart.DataLabelFontSize == 11 && chart.DataLabelAngle == 0)
            return null;

        var textFill = ToSolidFill(chart.DataLabelTextThemeColor, chart.DataLabelTextColor, drawingNs);
        return new XElement(chartNs + "txPr",
            ToTextBodyProperties(chart.DataLabelAngle, drawingNs),
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        new XAttribute("sz", Math.Clamp((int)Math.Round(chart.DataLabelFontSize * 100), 600, 7200)),
                        textFill))));
    }

    private static XElement? ToTextBodyProperties(double angle, XNamespace drawingNs) =>
        angle == 0
            ? null
            : new XElement(drawingNs + "bodyPr",
                new XAttribute("rot", Math.Clamp((int)Math.Round(angle * 60000), -5400000, 5400000)));

    private static string ToXlsxDataLabelPosition(ChartDataLabelPosition position) =>
        position switch
        {
            ChartDataLabelPosition.Center => "ctr",
            ChartDataLabelPosition.InsideEnd => "inEnd",
            ChartDataLabelPosition.OutsideEnd => "outEnd",
            _ => "bestFit"
        };

    private static string ToXlsxDataLabelSeparator(ChartDataLabelSeparator separator) =>
        separator switch
        {
            ChartDataLabelSeparator.Semicolon => "; ",
            ChartDataLabelSeparator.NewLine => "\n",
            ChartDataLabelSeparator.Space => " ",
            _ => ", "
        };

    private static string ToXlsxLegendPosition(ChartLegendPosition position) =>
        position switch
        {
            ChartLegendPosition.Left => "l",
            ChartLegendPosition.Top => "t",
            ChartLegendPosition.Bottom => "b",
            _ => "r"
        };

    private static bool IsSupportedXlsxChart(ChartModel chart) =>
        ChartTypeSupport.GetDataSeriesCount(chart) > 0 &&
        ChartTypeSupport.GetDataPointCount(chart) > 0 &&
        (!Enum.IsDefined(chart.Type) ||
            chart.Type is ChartType.Column
                or ChartType.StackedColumn
                or ChartType.PercentStackedColumn
                or ChartType.Bar
                or ChartType.StackedBar
                or ChartType.PercentStackedBar
                or ChartType.Line
                or ChartType.Scatter
                or ChartType.Area
                or ChartType.Bubble
                or ChartType.Pie
                or ChartType.Doughnut
                or ChartType.Radar
                or ChartType.Stock);

    private static string ToXlsxBarDirection(ChartType chartType) =>
        chartType is ChartType.Bar or ChartType.StackedBar or ChartType.PercentStackedBar
            ? "bar"
            : "col";

    private static string ToXlsxBarGrouping(ChartType chartType) =>
        chartType switch
        {
            ChartType.StackedColumn or ChartType.StackedBar => "stacked",
            ChartType.PercentStackedColumn or ChartType.PercentStackedBar => "percentStacked",
            _ => "clustered"
        };

    private static IEnumerable<XElement> BuildChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool>? includeSeries = null,
        bool forceLineShapeProperties = false)
    {
        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var seriesStartCol = chart.FirstColIsCategories ? chart.DataRange.Start.Col + 1 : chart.DataRange.Start.Col;
        var categoryRange = chart.FirstColIsCategories
            ? FormatSheetRange(sheet.Name, dataStartRow, chart.DataRange.Start.Col, chart.DataRange.End.Row, chart.DataRange.Start.Col)
            : null;

        var seriesIndex = 0;
        for (var col = seriesStartCol; col <= chart.DataRange.End.Col; col++)
        {
            if (includeSeries is not null && !includeSeries(seriesIndex))
            {
                seriesIndex++;
                continue;
            }

            var valueRange = FormatSheetRange(sheet.Name, dataStartRow, col, chart.DataRange.End.Row, col);
            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                ToSeriesTitleXml(chart, sheet, col, chartNs),
                chart.Type == ChartType.Line || forceLineShapeProperties
                    ? ToSeriesLineShapeProperties(chart, seriesIndex, chartNs, drawingNs)
                    : ToSeriesShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                chart.Type == ChartType.Line || forceLineShapeProperties
                    ? ToSeriesMarkerXml(chart, seriesIndex, chartNs, drawingNs)
                    : null,
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToTrendlineXml(chart, seriesIndex, chartNs, drawingNs),
                ToErrorBarsXml(chart, seriesIndex, chartNs),
                ToCategoryRangeXml(categoryRange, chartNs),
                new XElement(chartNs + "val",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", valueRange))));
            seriesIndex++;
        }
    }

    private static XElement? ToCategoryRangeXml(string? categoryRange, XNamespace chartNs) =>
        string.IsNullOrWhiteSpace(categoryRange)
            ? null
            : new XElement(chartNs + "cat",
                new XElement(chartNs + "strRef",
                    new XElement(chartNs + "f", categoryRange)));

    private static IEnumerable<XElement> BuildScatterChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool>? includeSeries = null)
    {
        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var xValueCol = chart.DataRange.Start.Col;
        var seriesStartCol = chart.DataRange.Start.Col + 1;
        var xValueRange = FormatSheetRange(sheet.Name, dataStartRow, xValueCol, chart.DataRange.End.Row, xValueCol);

        var seriesIndex = 0;
        for (var col = seriesStartCol; col <= chart.DataRange.End.Col; col++)
        {
            if (includeSeries is not null && !includeSeries(seriesIndex))
            {
                seriesIndex++;
                continue;
            }

            var yValueRange = FormatSheetRange(sheet.Name, dataStartRow, col, chart.DataRange.End.Row, col);
            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                ToSeriesTitleXml(chart, sheet, col, chartNs),
                ToSeriesLineShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                ToSeriesMarkerXml(chart, seriesIndex, chartNs, drawingNs),
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToTrendlineXml(chart, seriesIndex, chartNs, drawingNs),
                ToErrorBarsXml(chart, seriesIndex, chartNs),
                new XElement(chartNs + "xVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", xValueRange))),
                new XElement(chartNs + "yVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", yValueRange))));
            seriesIndex++;
        }
    }

    private static HashSet<int> GetSecondaryAxisSeriesIndexes(ChartModel chart, int seriesCount)
    {
        if (!chart.ShowSecondaryAxis || !ChartTypeSupport.SupportsSecondaryAxis(chart.Type) || seriesCount < 2)
            return [];

        if (chart.SecondaryAxisSeriesIndexes.Count == 0)
            return Enumerable.Range(1, seriesCount - 1).ToHashSet();

        return chart.SecondaryAxisSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .ToHashSet();
    }

    private static HashSet<int> GetComboLineSeriesIndexes(ChartModel chart, int seriesCount)
    {
        if (!chart.UseComboLineForSecondarySeries || !ChartTypeSupport.SupportsComboLineOverlay(chart) || seriesCount < 2)
            return [];

        return chart.ComboLineSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .ToHashSet();
    }

    private static IEnumerable<XElement> BuildBubbleChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (chart.DataRange.End.Col - chart.DataRange.Start.Col < 2)
            yield break;

        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var xValueCol = chart.DataRange.Start.Col;
        var xValueRange = FormatSheetRange(sheet.Name, dataStartRow, xValueCol, chart.DataRange.End.Row, xValueCol);

        var seriesIndex = 0;
        for (var yValueCol = chart.DataRange.Start.Col + 1; yValueCol < chart.DataRange.End.Col; yValueCol += 2)
        {
            var sizeCol = yValueCol + 1;
            var yValueRange = FormatSheetRange(sheet.Name, dataStartRow, yValueCol, chart.DataRange.End.Row, yValueCol);
            var sizeRange = FormatSheetRange(sheet.Name, dataStartRow, sizeCol, chart.DataRange.End.Row, sizeCol);

            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                ToSeriesTitleXml(chart, sheet, yValueCol, chartNs),
                ToSeriesShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToTrendlineXml(chart, seriesIndex, chartNs, drawingNs),
                ToErrorBarsXml(chart, seriesIndex, chartNs),
                new XElement(chartNs + "xVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", xValueRange))),
                new XElement(chartNs + "yVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", yValueRange))),
                new XElement(chartNs + "bubbleSize",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", sizeRange))));
            seriesIndex++;
        }
    }

    private static IEnumerable<XElement> BuildPieFamilyChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (chart.FirstColIsCategories && chart.DataRange.End.Col <= chart.DataRange.Start.Col)
            yield break;

        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var firstValueCol = chart.FirstColIsCategories ? chart.DataRange.Start.Col + 1 : chart.DataRange.Start.Col;
        var categoryRange = chart.FirstColIsCategories
            ? FormatSheetRange(sheet.Name, dataStartRow, chart.DataRange.Start.Col, chart.DataRange.End.Row, chart.DataRange.Start.Col)
            : null;

        var seriesIndex = 0;
        for (var valueCol = firstValueCol; valueCol <= chart.DataRange.End.Col; valueCol++)
        {
            var valueRange = FormatSheetRange(sheet.Name, dataStartRow, valueCol, chart.DataRange.End.Row, valueCol);
            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                ToSeriesTitleXml(chart, sheet, valueCol, chartNs),
                ToSeriesShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                seriesIndex == 0 ? ToExplodedSliceXml(chart, chartNs) : null,
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToCategoryRangeXml(categoryRange, chartNs),
                new XElement(chartNs + "val",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", valueRange))));
            seriesIndex++;
        }
    }

    private static XElement? ToSeriesTitleXml(
        ChartModel chart,
        Sheet sheet,
        uint seriesColumn,
        XNamespace chartNs)
    {
        if (!chart.FirstRowIsHeader)
            return null;

        var titleRange = FormatSheetRange(sheet.Name, chart.DataRange.Start.Row, seriesColumn, chart.DataRange.Start.Row, seriesColumn);
        return new XElement(chartNs + "tx",
            new XElement(chartNs + "strRef",
                new XElement(chartNs + "f", titleRange)));
    }

    private static XElement? ToFirstSliceAngleXml(ChartModel chart, XNamespace chartNs)
    {
        var normalized = chart.FirstSliceAngle % 360;
        if (normalized < 0)
            normalized += 360;

        return normalized == 0
            ? null
            : new XElement(chartNs + "firstSliceAng",
                new XAttribute("val", Math.Clamp((int)Math.Round(normalized), 0, 360)));
    }

    private static XElement? ToExplodedSliceXml(ChartModel chart, XNamespace chartNs)
    {
        var pointCount = ChartTypeSupport.GetDataPointCount(chart);
        if (chart.ExplodedSliceIndex < 0 || chart.ExplodedSliceIndex >= pointCount || chart.ExplodedSliceDistance <= 0)
            return null;

        return new XElement(chartNs + "dPt",
            new XElement(chartNs + "idx", new XAttribute("val", chart.ExplodedSliceIndex)),
            new XElement(chartNs + "explosion",
                new XAttribute("val", Math.Clamp((int)Math.Round(chart.ExplodedSliceDistance * 100), 0, 50))));
    }

    private static XElement? ToSeriesLineShapeProperties(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var format = GetSeriesFormat(chart, seriesIndex);
        if (format is null)
            return null;

        var fill = ToSolidFill(format.StrokeThemeColor, format.StrokeColor, drawingNs);
        var hasLineFormatting = fill is not null ||
            format.StrokeThickness is not null ||
            format.DashStyle is not null;

        return !hasLineFormatting
            ? null
            : new XElement(chartNs + "spPr",
                new XElement(drawingNs + "ln",
                    format.StrokeThickness is { } strokeThickness
                        ? new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(strokeThickness, 0.5, 10) * 12700)))
                        : null,
                    fill,
                    format.DashStyle is { } dashStyle
                        ? ToPresetDash(dashStyle, drawingNs)
                        : null));
    }

    private static XElement? ToSeriesMarkerXml(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (!ChartTypeSupport.SupportsSeriesMarkers(chart.Type))
            return null;

        var format = GetSeriesFormat(chart, seriesIndex);
        if (format is null)
            return null;

        var fill = ToSolidFill(format.FillThemeColor, format.FillColor, drawingNs);
        if (format.MarkerStyle is null && format.MarkerSize is null && fill is null)
            return null;

        return new XElement(chartNs + "marker",
            format.MarkerStyle is { } markerStyle
                ? new XElement(chartNs + "symbol", new XAttribute("val", ToXlsxMarkerStyle(markerStyle)))
                : null,
            format.MarkerSize is { } markerSize
                ? new XElement(chartNs + "size", new XAttribute("val", Math.Clamp((int)Math.Round(markerSize), 1, 30)))
                : null,
            fill is not null
                ? new XElement(chartNs + "spPr", fill)
                : null);
    }

    private static XElement? ToSeriesShapeProperties(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var format = GetSeriesFormat(chart, seriesIndex);
        if (format is null)
            return null;

        var fill = ToSolidFill(format.FillThemeColor, format.FillColor, drawingNs);
        var lineFill = ToSolidFill(format.StrokeThemeColor, format.StrokeColor, drawingNs);
        var hasLineFormatting = lineFill is not null ||
            format.StrokeThickness is not null ||
            format.DashStyle is not null;

        return fill is null && !hasLineFormatting
            ? null
            : new XElement(chartNs + "spPr",
                fill,
                hasLineFormatting
                    ? new XElement(drawingNs + "ln",
                        format.StrokeThickness is { } strokeThickness
                            ? new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(strokeThickness, 0.5, 10) * 12700)))
                            : null,
                        lineFill,
                        format.DashStyle is { } dashStyle
                            ? ToPresetDash(dashStyle, drawingNs)
                            : null)
                    : null);
    }

    private static ChartSeriesFormat? GetSeriesFormat(ChartModel chart, int seriesIndex)
    {
        var format = chart.SeriesFormats.LastOrDefault(item => item.SeriesIndex == seriesIndex);
        return format is null
            ? null
            : format with
            {
                DashStyle = ValidNullableEnumOrNull(format.DashStyle),
                MarkerStyle = ValidNullableEnumOrNull(format.MarkerStyle)
            };
    }

    private static TEnum? ValidNullableEnumOrNull<TEnum>(TEnum? value)
        where TEnum : struct, Enum =>
        value is { } enumValue && Enum.IsDefined(enumValue) ? enumValue : null;

    private static XElement? ToPointDataLabelsXml(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var pointCount = ChartTypeSupport.GetDataPointCount(chart);
        var labels = chart.PointDataLabelFormats
            .Where(format => format.SeriesIndex == seriesIndex && format.PointIndex >= 0 && format.PointIndex < pointCount)
            .GroupBy(format => format.PointIndex)
            .Select(group => group.Last())
            .Where(HasPointDataLabelFormatting)
            .OrderBy(format => format.PointIndex)
            .Select(format => ToPointDataLabelXml(format, chartNs, drawingNs))
            .ToArray();

        return labels.Length == 0
            ? null
            : new XElement(chartNs + "dLbls", labels);
    }

    private static bool HasPointDataLabelFormatting(ChartPointDataLabelFormat format) =>
        format.FillColor is not null
        || format.BorderColor is not null
        || format.BorderThickness is not null
        || format.TextColor is not null
        || format.FontSize is not null
        || format.FillThemeColor is not null
        || format.BorderThemeColor is not null
        || format.TextThemeColor is not null;

    private static XElement ToPointDataLabelXml(
        ChartPointDataLabelFormat format,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        new(chartNs + "dLbl",
            new XElement(chartNs + "idx", new XAttribute("val", format.PointIndex)),
            ToShapeProperties(
                chartNs,
                drawingNs,
                format.FillThemeColor,
                format.FillColor,
                format.BorderThemeColor,
                format.BorderColor,
                format.BorderThickness),
            ToPointDataLabelTextProperties(format, chartNs, drawingNs));

    private static XElement? ToPointDataLabelTextProperties(
        ChartPointDataLabelFormat format,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var textFill = ToSolidFill(format.TextThemeColor, format.TextColor, drawingNs);
        if (textFill is null && format.FontSize is null)
            return null;

        return new XElement(chartNs + "txPr",
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        format.FontSize is { } fontSize
                            ? new XAttribute("sz", Math.Clamp((int)Math.Round(fontSize * 100), 600, 7200))
                            : null,
                        textFill))));
    }

    private static XElement? ToTrendlineXml(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (!chart.ShowLinearTrendline || seriesIndex != 0 || !ChartTypeSupport.SupportsTrendlines(chart.Type))
            return null;

        return new XElement(chartNs + "trendline",
            new XElement(chartNs + "trendlineType",
                new XAttribute("val", ToXlsxTrendlineType(chart.TrendlineType))),
            chart.TrendlineType == ChartTrendlineType.Polynomial
                ? new XElement(chartNs + "order", new XAttribute("val", Math.Clamp(chart.TrendlineOrder, 2, 6)))
                : null,
            chart.TrendlineType == ChartTrendlineType.MovingAverage
                ? new XElement(chartNs + "period", new XAttribute("val", Math.Max(2, chart.TrendlinePeriod)))
                : null,
            ToTrendlineShapeProperties(chart, chartNs, drawingNs),
            new XElement(chartNs + "dispEq", new XAttribute("val", chart.ShowTrendlineEquation ? "1" : "0")),
            new XElement(chartNs + "dispRSqr", new XAttribute("val", chart.ShowTrendlineRSquared ? "1" : "0")));
    }

    private static XElement? ToTrendlineShapeProperties(
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var fill = ToSolidFill(chart.TrendlineThemeColor, chart.TrendlineColor, drawingNs);
        if (fill is null && chart.TrendlineThickness == 1.5 && chart.TrendlineDashStyle == ChartLineDashStyle.Solid)
            return null;

        return new XElement(chartNs + "spPr",
            new XElement(drawingNs + "ln",
                new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(chart.TrendlineThickness, 0.5, 10) * 12700))),
                fill,
                ToPresetDash(chart.TrendlineDashStyle, drawingNs)));
    }

    private static XElement? ToPresetDash(ChartLineDashStyle dashStyle, XNamespace drawingNs) =>
        dashStyle == ChartLineDashStyle.Solid
            ? null
            : new XElement(drawingNs + "prstDash",
                new XAttribute("val", dashStyle == ChartLineDashStyle.Dot ? "dot" : "dash"));

    private static string ToXlsxMarkerStyle(ChartMarkerStyle markerStyle) =>
        markerStyle switch
        {
            ChartMarkerStyle.None => "none",
            ChartMarkerStyle.Square => "square",
            ChartMarkerStyle.Diamond => "diamond",
            ChartMarkerStyle.Triangle => "triangle",
            _ => "circle"
        };

    private static string ToXlsxTrendlineType(ChartTrendlineType type) =>
        type switch
        {
            ChartTrendlineType.Exponential => "exp",
            ChartTrendlineType.Logarithmic => "log",
            ChartTrendlineType.Power => "power",
            ChartTrendlineType.MovingAverage => "movingAvg",
            ChartTrendlineType.Polynomial => "poly",
            _ => "linear"
        };

    private static XElement? ToErrorBarsXml(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs)
    {
        if (!chart.ShowErrorBars || seriesIndex != 0 || !SupportsErrorBars(chart.Type))
            return null;

        return new XElement(chartNs + "errBars",
            new XElement(chartNs + "errBarType", new XAttribute("val", ToXlsxErrorBarDirection(chart.ErrorBarDirection))),
            new XElement(chartNs + "errValType", new XAttribute("val", ToXlsxErrorBarKind(chart.ErrorBarKind))),
            chart.ErrorBarEndCaps ? null : new XElement(chartNs + "noEndCap", new XAttribute("val", "1")),
            chart.ErrorBarKind is ChartErrorBarKind.Percentage or ChartErrorBarKind.FixedValue
                ? new XElement(chartNs + "val", new XAttribute("val", Math.Clamp(chart.ErrorBarValue, 0, 1000).ToString(CultureInfo.InvariantCulture)))
                : null);
    }

    private static bool SupportsErrorBars(ChartType chartType) =>
        chartType is ChartType.Column or ChartType.StackedColumn or ChartType.PercentStackedColumn or
            ChartType.Bar or ChartType.StackedBar or ChartType.PercentStackedBar or
            ChartType.Line or ChartType.Scatter or ChartType.Area;

    private static string ToXlsxErrorBarKind(ChartErrorBarKind kind) =>
        kind switch
        {
            ChartErrorBarKind.Percentage => "percentage",
            ChartErrorBarKind.FixedValue => "fixedVal",
            _ => "stdErr"
        };

    private static string ToXlsxErrorBarDirection(ChartErrorBarDirection direction) =>
        direction switch
        {
            ChartErrorBarDirection.Plus => "plus",
            ChartErrorBarDirection.Minus => "minus",
            _ => "both"
        };

    private static string ToDrawingSchemeColor(WorkbookThemeColorSlot slot) =>
        slot switch
        {
            WorkbookThemeColorSlot.Dark1 => "dk1",
            WorkbookThemeColorSlot.Light1 => "lt1",
            WorkbookThemeColorSlot.Dark2 => "dk2",
            WorkbookThemeColorSlot.Light2 => "lt2",
            WorkbookThemeColorSlot.Accent1 => "accent1",
            WorkbookThemeColorSlot.Accent2 => "accent2",
            WorkbookThemeColorSlot.Accent3 => "accent3",
            WorkbookThemeColorSlot.Accent4 => "accent4",
            WorkbookThemeColorSlot.Accent5 => "accent5",
            WorkbookThemeColorSlot.Accent6 => "accent6",
            WorkbookThemeColorSlot.Hyperlink => "hlink",
            WorkbookThemeColorSlot.FollowedHyperlink => "folHlink",
            _ => "accent1"
        };

    private static string FormatSheetRange(string sheetName, uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var quotedSheet = $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'";
        var start = $"${CellAddress.NumberToColumnName(startCol)}${startRow}";
        var end = $"${CellAddress.NumberToColumnName(endCol)}${endRow}";
        return start == end
            ? $"{quotedSheet}!{start}"
            : $"{quotedSheet}!{start}:{end}";
    }

    private static void LoadMergedRegions(IXLWorksheet xlSheet, Sheet sheet)
    {
        foreach (var xlMerge in xlSheet.MergedRanges)
        {
            var sheetId = sheet.Id;
            var start = new CellAddress(sheetId,
                (uint)xlMerge.RangeAddress.FirstAddress.RowNumber,
                (uint)xlMerge.RangeAddress.FirstAddress.ColumnNumber);
            var end = new CellAddress(sheetId,
                (uint)xlMerge.RangeAddress.LastAddress.RowNumber,
                (uint)xlMerge.RangeAddress.LastAddress.ColumnNumber);
            sheet.AddMergedRegion(new GridRange(start, end));
        }
    }

    private sealed record XlsxSourcePackage(byte[] Bytes);



}

