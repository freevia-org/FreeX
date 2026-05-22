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
                                 NormalizeInternalHyperlinkAddress(hyperlink.InternalAddress) ??
                                 string.Empty;
                    if (string.IsNullOrEmpty(target)) continue;

                    var addr = new CellAddress(sheet.Id, (uint)cell.Address.RowNumber, (uint)cell.Address.ColumnNumber);
                    sheet.Hyperlinks[addr] = target;
                    sheet.HyperlinkMetadata[addr] = new HyperlinkMetadata(
                        GetHyperlinkTargetKind(hyperlink, target),
                        hyperlink.Tooltip ?? "",
                        NormalizeInternalHyperlinkAddress(hyperlink.InternalAddress) ?? "");
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
                    sheet.HyperlinkMetadata.TryGetValue(address, out var metadata);
                    xlSheet.Cell((int)address.Row, (int)address.Col)
                        .SetHyperlink(CreateXlsxHyperlink(target, metadata));
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

        if (XlsxWorksheetChartWriter.HasSupportedCharts(workbook, XlsxChartXmlWriter.IsSupportedXlsxChart))
        {
            packageStream.Position = 0;
            XlsxWorksheetChartWriter.Save(packageStream, workbook, XlsxChartXmlWriter.IsSupportedXlsxChart, XlsxChartXmlWriter.ToChartXml);
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
        XlsxWorksheetMetadataPreserver.Preserve(sourceArchive, generatedArchive, workbook);
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

    private static HyperlinkTargetKind GetHyperlinkTargetKind(XLHyperlink hyperlink, string target)
    {
        if (!string.IsNullOrWhiteSpace(hyperlink.InternalAddress))
            return HyperlinkTargetKind.PlaceInThisDocument;

        return target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? HyperlinkTargetKind.EmailAddress
            : HyperlinkTargetKind.ExistingFileOrWebPage;
    }

    private static string? NormalizeInternalHyperlinkAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return address;

        var bangIndex = address.IndexOf('!');
        if (bangIndex > 2 && address[0] == '\'' && address[bangIndex - 1] == '\'')
            return address[1..(bangIndex - 1)] + address[bangIndex..];

        return address;
    }

    private static XLHyperlink CreateXlsxHyperlink(string target, HyperlinkMetadata? metadata)
    {
        metadata ??= new HyperlinkMetadata();
        var linkTarget = metadata.LinkType == HyperlinkTargetKind.PlaceInThisDocument &&
                         !string.IsNullOrWhiteSpace(metadata.Bookmark)
            ? metadata.Bookmark
            : target;
        var hyperlink = new XLHyperlink(linkTarget);

        if (metadata.LinkType == HyperlinkTargetKind.PlaceInThisDocument)
        {
            hyperlink.IsExternal = false;
            hyperlink.InternalAddress = linkTarget;
        }
        else
        {
            hyperlink.IsExternal = true;
            if (Uri.TryCreate(linkTarget, UriKind.Absolute, out var uri))
                hyperlink.ExternalAddress = uri;
        }

        if (!string.IsNullOrWhiteSpace(metadata.ScreenTip))
            hyperlink.Tooltip = metadata.ScreenTip;

        return hyperlink;
    }

    private sealed record XlsxSourcePackage(byte[] Bytes);



}

