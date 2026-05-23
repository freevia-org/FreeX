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
public sealed partial class XlsxFileAdapter : IFileAdapter
{
    private static readonly ConditionalWeakTable<Workbook, XlsxSourcePackage> SourcePackages = new();
    public string Extension => ".xlsx";
    public string FormatName => "Excel Workbook";
    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new(".xlsx", "Excel Workbook", CanOpen: true, CanSave: true),
        new(".xlsm", "Excel Macro-Enabled Workbook", CanOpen: true, CanSave: false),
        new(".xltx", "Excel Template", CanOpen: true, CanSave: false, OpensAsTemplate: true),
        new(".xltm", "Excel Macro-Enabled Template", CanOpen: true, CanSave: false, OpensAsTemplate: true)
    ];

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
            sheetXmlLayout.TryGetValue(xlSheet.Name, out var xmlLayout);
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
                    else if (xmlLayout?.CachedFormulaErrors.TryGetValue(((uint)xlCell.Address.RowNumber, (uint)xlCell.Address.ColumnNumber), out var cachedFormulaError) == true)
                        cell.Value = cachedFormulaError;
                }
                else
                {
                    cell = Cell.FromValue(XlsxClosedXmlCellMapper.MapValue(xlCell));
                }

                var style = XlsxClosedXmlCellMapper.MapStyle(xlCell.Style, workbook.Theme);
                if (!style.Equals(CellStyle.Default))
                    cell.StyleId = workbook.RegisterStyle(style);

                if (cell.Value is BlankValue && !cell.HasFormula)
                {
                    if (cell.StyleId != StyleId.Default)
                        sheet.SetStyleOnly(addr.Row, addr.Col, cell.StyleId);

                    continue;
                }

                sheet.SetCell(addr, cell);
            }

            foreach (var (row, col) in xmlLayout?.ExplicitStyleOnlyCells ?? [])
            {
                if (sheet.GetCell(row, col) is not null)
                    continue;

                var xlCell = xlSheet.Cell((int)row, (int)col);
                var style = XlsxClosedXmlCellMapper.MapStyle(xlCell.Style, workbook.Theme);
                if (!style.Equals(CellStyle.Default))
                    sheet.SetStyleOnly(row, col, workbook.RegisterStyle(style));
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

            if (xmlLayout is { } layout)
                ApplySheetXmlLayout(workbook, sheet, layout, loadedScenarioNames, customViewStatesById);
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

            if (xmlLayout?.PaneState is "frozen" or "frozenSplit")
            {
                sheet.FrozenRows = xmlLayout.PaneRowSplit ?? 0;
                sheet.FrozenCols = xmlLayout.PaneColumnSplit ?? 0;
            }
            else
            {
                var splitRow = xlSheet.SheetView.SplitRow > 0
                    ? (uint)xlSheet.SheetView.SplitRow
                    : xmlLayout?.PaneRowSplit;
                var splitColumn = xlSheet.SheetView.SplitColumn > 0
                    ? (uint)xlSheet.SheetView.SplitColumn
                    : xmlLayout?.PaneColumnSplit;
                if (splitRow > 0)
                    sheet.SplitRow = splitRow;
                if (splitColumn > 0)
                    sheet.SplitColumn = splitColumn;
            }
            sheet.ViewTopRow = xmlLayout?.ViewTopRow;
            sheet.ViewLeftCol = xmlLayout?.ViewLeftCol;
            sheet.ActiveRow = xmlLayout?.ActiveRow;
            sheet.ActiveCol = xmlLayout?.ActiveCol;

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
            if (xmlLayout is not null)
                XlsxDataValidationNativeMetadataMapper.Apply(sheet, xmlLayout.DataValidationNativeMetadata);

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
                workbook.CustomViews.Add(new WorkbookCustomView(
                    customView.Name,
                    states,
                    customView.Id,
                    customView.IncludePrintSettings,
                    customView.IncludeHiddenRowsColumnsAndFilterSettings));
        }

        return workbook;
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

}
