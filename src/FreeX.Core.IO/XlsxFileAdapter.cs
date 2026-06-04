using System.Globalization;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Xml.Linq;
using ClosedXML.Excel;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// XLSX file adapter using ClosedXML.
/// Supports standard .xlsx workbook files.
/// </summary>
public sealed partial class XlsxFileAdapter : IFileAdapter
{
    private const int ClosedXmlStyleOnlyStripCellThreshold = 16_384;
    private static readonly ConditionalWeakTable<Workbook, XlsxSourcePackage> SourcePackages = new();
    // ClosedXML keeps the immutable style key on internal cell types. Use a reflected delegate
    // so repeated styles are mapped once without materializing an XLStyle for every used cell.
    private static readonly Func<IXLCell, object?>? XlCellStyleValueAccessor = CreateXlCellStyleValueAccessor();
    public string Extension => ".xlsx";
    public string FormatName => "XLSX Workbook";
    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
        new(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false),
        new(".xltx", "XLTX Template", CanOpen: true, CanSave: false, OpensAsTemplate: true),
        new(".xltm", "XLTM Macro-Enabled Template", CanOpen: true, CanSave: false, OpensAsTemplate: true)
    ];

    /// <summary>
    /// Loads a workbook from the given stream and returns it together with any non-fatal
    /// warnings collected during loading (e.g. features that failed to parse).
    /// The workbook is always returned; warnings indicate partial data loss.
    /// </summary>
    public XlsxLoadResult LoadWithWarnings(Stream stream)
        => LoadWithWarnings(stream, inspectFeatures: false);

    public XlsxLoadResult LoadWithWarnings(Stream stream, bool inspectFeatures)
    {
        var warnings = new List<string>();
        var workbook = LoadCore(stream, warnings, inspectFeatures, out var featureReport);
        return new XlsxLoadResult(workbook, warnings.AsReadOnly(), featureReport);
    }

    /// <inheritdoc/>
    public Workbook Load(Stream stream) => LoadWithWarnings(stream).Workbook;

    private Workbook LoadCore(
        Stream stream,
        List<string> warnings,
        bool inspectFeatures,
        out XlsxFeatureReport? featureReport)
    {
        featureReport = null;
        var loadPackage = CreateLoadPackageStream(stream);
        using var packageStream = loadPackage.PackageStream;
        var packageParts = XlsxLoadPackageParts.Empty;
        var workbookTheme = WorkbookTheme.Office;
        var workbookMetadata = XlsxWorkbookMetadataSnapshot.Default;
        XDocument? stylesXml = null;
        var numberFormatCatalog = new Dictionary<int, string>();
        var pivotMetadata = XlsxPivotTableReader.PivotPackageMetadata.Empty;
        var slicerTimelineMetadata = SlicerTimelinePackageMetadata.Empty;
        IReadOnlyList<ExternalLinkModel> externalLinkMetadata = [];
        var structuredTableMetadata = StructuredTablePackageMetadata.Empty;
        try
        {
            using var packageArchive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
            // Reject zip-bomb / oversized packages before any decompression-heavy reads.
            WorkbookOpenSizeGuard.EnsureArchiveWithinLimits(packageArchive);
            if (inspectFeatures)
                featureReport = XlsxFeatureInspector.Inspect(packageArchive);

            packageParts = XlsxLoadPackageParts.Inspect(packageArchive);

            workbookTheme = packageParts.HasTheme
                ? XlsxWorkbookThemeReader.Load(packageArchive)
                : WorkbookTheme.Office;
            workbookMetadata = packageParts.HasWorkbook
                ? XlsxWorkbookMetadataReader.LoadWorkbookMetadata(packageArchive)
                : XlsxWorkbookMetadataSnapshot.Default;
            stylesXml = packageParts.HasStyles
                ? XlsxStylesheetReader.Load(packageArchive)
                : null;
            numberFormatCatalog = XlsxWorkbookMetadataReader.LoadNumberFormatCatalog(stylesXml);
            if (packageParts.HasPivotPackageParts)
                pivotMetadata = XlsxPivotTableReader.Load(packageArchive, numberFormatCatalog);
            if (packageParts.HasSlicerTimelinePackageParts)
                slicerTimelineMetadata = XlsxSlicerTimelineMetadataReader.Load(packageArchive);
            if (packageParts.HasExternalLinks)
                externalLinkMetadata = XlsxExternalLinkMetadataReader.Load(packageArchive);
            if (packageParts.HasStructuredTables)
                structuredTableMetadata = XlsxStructuredTableMetadataReader.Load(packageArchive);
        }
        catch (InvalidDataException)
        {
            // Not a valid zip archive; let the ClosedXML loader produce the format error.
        }

        var indexedColors = XlsxIndexedColorPaletteMapper.Load(stylesXml);
        var pivotTableStyleMetadata = XlsxPivotTableStyleMetadataReader.Load(stylesXml);
        var structuredTableStyleMetadata = XlsxStructuredTableStyleMetadataReader.Load(stylesXml);
        var xlsxCustomViews = workbookMetadata.CustomViews;

        packageStream.Position = 0;
        var sheetXmlLayoutWarningCount = warnings.Count;
        var sheetXmlLayout = LoadSheetXmlLayout(packageStream, stylesXml, workbookTheme, indexedColors, warnings);
        var sheetXmlLayoutHadWarnings = warnings.Count != sheetXmlLayoutWarningCount;
        var shouldStripStyleOnlyCells = ShouldStripClosedXmlStyleOnlyCells(
            sheetXmlLayout,
            sheetXmlLayoutHadWarnings);
        packageStream.Position = 0;
        var closedXmlLoad = OpenClosedXmlWorkbookWithSanitizationFallback(packageStream, shouldStripStyleOnlyCells);
        using var closedXmlPackageStream = closedXmlLoad.PackageStream;
        using var xlWorkbook = closedXmlLoad.Workbook;
        var worksheetsWithPreservableSourceMetadata = GetWorksheetsWithPreservableSourceMetadata(
            sheetXmlLayout,
            sheetXmlLayoutHadWarnings,
            xlWorkbook.Worksheets.Count);
        var hasUnsupportedConditionalFormatting = GetHasUnsupportedConditionalFormatting(
            sheetXmlLayout,
            sheetXmlLayoutHadWarnings,
            xlWorkbook.Worksheets.Count);
        var workbook = new Workbook("Untitled");
        workbook.Theme = workbookTheme;
        workbook.Uses1904DateSystem = workbookMetadata.Uses1904DateSystem;
        workbook.Properties = workbookMetadata.WorkbookProperties;
        var workbookViewProperties = workbookMetadata.WorkbookViewProperties;
        workbook.ShowSheetTabs = workbookViewProperties.ShowSheetTabs;
        workbook.SheetTabRatio = workbookViewProperties.SheetTabRatio is { } tabRatio ? Math.Clamp(tabRatio, 0, 1000) : null;
        workbook.FirstVisibleSheetIndex = workbookViewProperties.FirstVisibleSheetIndex is { } firstSheet
            ? Math.Clamp(firstSheet, 0, Math.Max(0, xlWorkbook.Worksheets.Count - 1))
            : null;
        workbook.ActiveSheetIndex = workbookViewProperties.ActiveSheetIndex is { } activeTab
            ? Math.Clamp(activeTab, 0, Math.Max(0, xlWorkbook.Worksheets.Count - 1))
            : null;
        workbook.FileSharing = workbookMetadata.FileSharing;
        workbook.FileRecoveryProperties.AddRange(workbookMetadata.FileRecoveryProperties);
        workbook.FileVersion = workbookMetadata.FileVersion;
        workbook.FunctionGroups = workbookMetadata.FunctionGroups;
        workbook.SmartTags = workbookMetadata.SmartTags;
        workbook.AdditionalViews = workbookMetadata.AdditionalViews;
        workbook.IsStructureProtected = workbookMetadata.Protection.IsStructureProtected;
        workbook.StructureProtectionPassword = workbookMetadata.Protection.PasswordHash;
        workbook.ProtectionMetadata = workbookMetadata.ProtectionMetadata;
        workbook.CalculationMode = xlWorkbook.CalculateMode == XLCalculateMode.Manual
            ? WorkbookCalculationMode.Manual
            : WorkbookCalculationMode.Automatic;
        var calculationProperties = workbookMetadata.CalculationProperties;
        if (calculationProperties.Mode is { } calculationMode)
            workbook.CalculationMode = calculationMode;
        workbook.FullCalculationOnLoad = calculationProperties.FullCalculationOnLoad;
        workbook.ForceFullCalculation = calculationProperties.ForceFullCalculation;
        workbook.IterativeCalculation = calculationProperties.IterativeCalculation;
        workbook.MaxCalculationIterations = calculationProperties.MaxIterations;
        workbook.MaxCalculationChange = calculationProperties.MaxChange;
        foreach (var (numberFormatId, formatCode) in numberFormatCatalog)
            workbook.NumberFormatCatalog[numberFormatId] = formatCode;
        foreach (var (index, color) in indexedColors.Colors)
            workbook.IndexedColors.SetColor(index, color);
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
        foreach (var structuredTableStyle in structuredTableStyleMetadata)
            workbook.StructuredTableStyles.Add(structuredTableStyle);

        var loadedScenarioNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var customViewStatesById = new Dictionary<string, List<WorksheetCustomViewState>>(StringComparer.OrdinalIgnoreCase);
        var explicitStyleOnlyStyleIdsByXlsxStyleIndex = new Dictionary<int, StyleId?>();
        var styleIdsByXlsxStyleValue = new Dictionary<object, StyleId?>();
        foreach (var xlSheet in xlWorkbook.Worksheets)
        {
            var sheet = workbook.AddSheet(xlSheet.Name);
            sheetXmlLayout.TryGetValue(xlSheet.Name, out var xmlLayout);
            if (xmlLayout is { PopulatedCellCount: > 0 } layoutWithCells)
                sheet.EnsureCellCapacity(layoutWithCells.PopulatedCellCount);

            sheet.IsVeryHidden = xlSheet.Visibility == XLWorksheetVisibility.VeryHidden;
            sheet.IsHidden = xlSheet.Visibility != XLWorksheetVisibility.Visible;
            if (xlSheet.TabColor.HasValue)
            {
                sheet.TabColor = XlsxClosedXmlCellMapper.MapColor(xlSheet.TabColor, workbook.Theme);
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
                    var cached = XlsxClosedXmlCellMapper.MapFormulaValue(xlCell);
                    if (cached is not BlankValue)
                        cell.Value = cached;
                    else if (xmlLayout?.CachedFormulaErrors.TryGetValue(((uint)xlCell.Address.RowNumber, (uint)xlCell.Address.ColumnNumber), out var cachedFormulaError) == true)
                        cell.Value = cachedFormulaError;
                }
                else
                {
                    cell = Cell.FromValue(XlsxClosedXmlCellMapper.MapValue(xlCell));
                }

                if (GetRegisteredStyleId(xlCell, workbook, workbook.Theme, styleIdsByXlsxStyleValue) is { } styleId)
                    cell.StyleId = styleId;

                if (cell.Value is BlankValue && !cell.HasFormula)
                {
                    if (cell.StyleId != StyleId.Default)
                        sheet.SetStyleOnly(addr.Row, addr.Col, cell.StyleId);

                    continue;
                }

                sheet.SetCell(addr, cell);
            }

            foreach (var (row, col, styleIndex) in xmlLayout?.ExplicitStyleOnlyCells ?? [])
            {
                if (sheet.GetCell(row, col) is not null)
                    continue;

                if (!explicitStyleOnlyStyleIdsByXlsxStyleIndex.TryGetValue(styleIndex, out var styleId))
                {
                    var xlCell = xlSheet.Cell((int)row, (int)col);
                    var style = XlsxClosedXmlCellMapper.MapStyle(xlCell.Style, workbook.Theme);
                    styleId = style.Equals(CellStyle.Default)
                        ? null
                        : workbook.RegisterStyle(style);
                    explicitStyleOnlyStyleIdsByXlsxStyleIndex[styleIndex] = styleId;
                }

                if (styleId is { } nonDefaultStyleId)
                    sheet.SetStyleOnly(row, col, nonDefaultStyleId);
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
                catch (Exception ex)
                {
                    warnings.Add($"[hyperlinks] Sheet '{xlSheet.Name}': {ex.Message}");
                }
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
                    XlsxStructuredTableModelMapper.MaterializeStyle(workbook, sheet, table);
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
            catch (Exception ex) { warnings.Add($"[print-area] Sheet '{xlSheet.Name}': {ex.Message}"); }

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
            sheet.PrintQualityVerticalDpi = xlSheet.PageSetup.VerticalDpi > 0
                ? xlSheet.PageSetup.VerticalDpi
                : null;
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
            catch (Exception ex) { warnings.Add($"[conditional-format] Sheet '{xlSheet.Name}': {ex.Message}"); }

            // Load data validation rules (best-effort)
            try { XlsxDataValidationClosedXmlMapper.Load(xlSheet, sheet); }
            catch (Exception ex) { warnings.Add($"[data-validation] Sheet '{xlSheet.Name}': {ex.Message}"); }
            if (xmlLayout is not null)
                XlsxDataValidationNativeMetadataMapper.Apply(sheet, xmlLayout.DataValidationNativeMetadata);

            // Load merged regions (best-effort)
            try { LoadMergedRegions(xlSheet, sheet); }
            catch (Exception ex) { warnings.Add($"[merged-regions] Sheet '{xlSheet.Name}': {ex.Message}"); }
        }

        ResolvePivotChartCacheBindings(workbook);

        // Load named ranges (best-effort; skip any we cannot map)
        try { XlsxNamedRangeMapper.Load(xlWorkbook, workbook); }
        catch (Exception ex) { warnings.Add($"[named-ranges]: {ex.Message}"); }

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

        SourcePackages.Remove(workbook);
        SourcePackages.Add(workbook, XlsxSourcePackage.Capture(
            packageStream,
            workbook,
            loadPackage.CanReuseBufferForSnapshot,
            worksheetsWithPreservableSourceMetadata,
            hasUnsupportedConditionalFormatting));
        return workbook;
    }

    private static bool ShouldStripClosedXmlStyleOnlyCells(
        IReadOnlyDictionary<string, SheetXmlLayout> sheetXmlLayout,
        bool sheetXmlLayoutHadWarnings)
    {
        if (sheetXmlLayoutHadWarnings || sheetXmlLayout.Count == 0)
            return true;

        var explicitStyleOnlyCellCount = 0;
        foreach (var layout in sheetXmlLayout.Values)
        {
            if (layout.HasStyleOnlyCells && layout.ExplicitStyleOnlyCells.Count == 0)
                return true;

            explicitStyleOnlyCellCount += layout.ExplicitStyleOnlyCells.Count;
            if (explicitStyleOnlyCellCount > ClosedXmlStyleOnlyStripCellThreshold)
                return true;
        }

        return false;
    }

    private static IReadOnlySet<string>? GetWorksheetsWithPreservableSourceMetadata(
        IReadOnlyDictionary<string, SheetXmlLayout> sheetXmlLayout,
        bool sheetXmlLayoutHadWarnings,
        int expectedSheetCount)
    {
        if (sheetXmlLayoutHadWarnings || sheetXmlLayout.Count != expectedSheetCount)
            return null;

        var worksheetsWithMetadata = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sheetName, layout) in sheetXmlLayout)
            if (layout.HasPreservableSourceWorksheetMetadata)
                worksheetsWithMetadata.Add(sheetName);

        return worksheetsWithMetadata;
    }

    private static bool? GetHasUnsupportedConditionalFormatting(
        IReadOnlyDictionary<string, SheetXmlLayout> sheetXmlLayout,
        bool sheetXmlLayoutHadWarnings,
        int expectedSheetCount)
    {
        if (sheetXmlLayoutHadWarnings || sheetXmlLayout.Count != expectedSheetCount)
            return null;

        foreach (var layout in sheetXmlLayout.Values)
            if (layout.HasUnsupportedConditionalFormatting)
                return true;

        return false;
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

    private static StyleId? GetRegisteredStyleId(
        IXLCell xlCell,
        Workbook workbook,
        WorkbookTheme theme,
        Dictionary<object, StyleId?> styleIdsByStyleValue)
    {
        var styleValue = XlCellStyleValueAccessor is not null
            ? XlCellStyleValueAccessor(xlCell)
            : null;
        if (styleValue is not null && styleIdsByStyleValue.TryGetValue(styleValue, out var cachedStyleId))
            return cachedStyleId;

        var style = XlsxClosedXmlCellMapper.MapStyle(xlCell.Style, theme);
        StyleId? styleId = style.Equals(CellStyle.Default)
            ? null
            : workbook.RegisterStyle(style);
        if (styleValue is not null)
            styleIdsByStyleValue[styleValue] = styleId;
        return styleId;
    }

    private static Func<IXLCell, object?>? CreateXlCellStyleValueAccessor()
    {
        var xlCellType = typeof(XLWorkbook).Assembly.GetType("ClosedXML.Excel.XLCell");
        var property = xlCellType?.GetProperty("StyleValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property is null)
            return null;

        var cell = Expression.Parameter(typeof(IXLCell), "cell");
        var styleValue = Expression.Property(Expression.Convert(cell, xlCellType!), property);
        return Expression.Lambda<Func<IXLCell, object?>>(
            Expression.Convert(styleValue, typeof(object)),
            cell).Compile();
    }

    private readonly struct XlsxLoadPackageParts
    {
        private XlsxLoadPackageParts(
            bool hasWorkbook,
            bool hasStyles,
            bool hasTheme,
            bool hasPivotPackageParts,
            bool hasSlicerTimelinePackageParts,
            bool hasExternalLinks,
            bool hasStructuredTables)
        {
            HasWorkbook = hasWorkbook;
            HasStyles = hasStyles;
            HasTheme = hasTheme;
            HasPivotPackageParts = hasPivotPackageParts;
            HasSlicerTimelinePackageParts = hasSlicerTimelinePackageParts;
            HasExternalLinks = hasExternalLinks;
            HasStructuredTables = hasStructuredTables;
        }

        public static XlsxLoadPackageParts Empty => default;
        public bool HasWorkbook { get; }
        public bool HasStyles { get; }
        public bool HasTheme { get; }
        public bool HasPivotPackageParts { get; }
        public bool HasSlicerTimelinePackageParts { get; }
        public bool HasExternalLinks { get; }
        public bool HasStructuredTables { get; }

        public static XlsxLoadPackageParts Inspect(ZipArchive archive)
        {
            var hasWorkbook = false;
            var hasStyles = false;
            var hasTheme = false;
            var hasPivotPackageParts = false;
            var hasSlicerTimelinePackageParts = false;
            var hasExternalLinks = false;
            var hasStructuredTables = false;

            foreach (var entry in archive.Entries)
            {
                var path = entry.FullName;
                hasWorkbook |= EntryPathEquals(path, "xl/workbook.xml");
                hasStyles |= EntryPathEquals(path, "xl/styles.xml");
                hasTheme |= EntryPathEquals(path, "xl/theme/theme1.xml");
                hasPivotPackageParts |=
                    EntryPathStartsWith(path, "xl/pivotCache/") ||
                    EntryPathStartsWith(path, "xl/pivotTables/");
                hasSlicerTimelinePackageParts |=
                    EntryPathStartsWith(path, "xl/slicerCaches/") ||
                    EntryPathStartsWith(path, "xl/slicers/") ||
                    EntryPathStartsWith(path, "xl/timelineCaches/") ||
                    EntryPathStartsWith(path, "xl/timelines/");
                hasExternalLinks |= EntryPathStartsWith(path, "xl/externalLinks/");
                hasStructuredTables |= EntryPathStartsWith(path, "xl/tables/");

                if (hasWorkbook &&
                    hasStyles &&
                    hasTheme &&
                    hasPivotPackageParts &&
                    hasSlicerTimelinePackageParts &&
                    hasExternalLinks &&
                    hasStructuredTables)
                {
                    break;
                }
            }

            return new XlsxLoadPackageParts(
                hasWorkbook,
                hasStyles,
                hasTheme,
                hasPivotPackageParts,
                hasSlicerTimelinePackageParts,
                hasExternalLinks,
                hasStructuredTables);
        }

        private static bool EntryPathEquals(string path, string expectedPath) =>
            string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase) ||
            (path.Contains('\\') &&
             string.Equals(path.Replace('\\', '/'), expectedPath, StringComparison.OrdinalIgnoreCase));

        private static bool EntryPathStartsWith(string path, string expectedPrefix) =>
            path.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) ||
            (path.Contains('\\') &&
             path.Replace('\\', '/').StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static LoadPackageStream CreateLoadPackageStream(Stream stream) =>
        CreateLoadPackageStream(stream, WorkbookOpenSizeGuard.DefaultMaxFileBytes);

    private static LoadPackageStream CreateLoadPackageStream(Stream stream, long maxFileBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 1);

        if (stream is MemoryStream memoryStream &&
            memoryStream.CanSeek &&
            memoryStream.TryGetBuffer(out var sourceBuffer))
        {
            var memoryRemainingLength = memoryStream.Length - memoryStream.Position;
            if (memoryRemainingLength is >= 0 and <= int.MaxValue)
            {
                WorkbookOpenSizeGuard.EnsureFileWithinLimit(memoryRemainingLength, maxFileBytes);
                var memoryPackageStream = new MemoryStream(
                    sourceBuffer.Array!,
                    sourceBuffer.Offset + (int)memoryStream.Position,
                    (int)memoryRemainingLength,
                    writable: false,
                    publiclyVisible: true);
                memoryStream.Position = memoryStream.Length;
                memoryPackageStream.Position = memoryPackageStream.Length;
                return new LoadPackageStream(memoryPackageStream, CanReuseBufferForSnapshot: false);
            }
        }

        var remainingLength = stream.CanSeek
            ? Math.Max(0, stream.Length - stream.Position)
            : 0;
        if (stream.CanSeek)
            WorkbookOpenSizeGuard.EnsureFileWithinLimit(remainingLength, maxFileBytes);

        var packageStream = remainingLength is > 0 and <= int.MaxValue
            ? new MemoryStream((int)remainingLength)
            : new MemoryStream();
        CopyToMemoryStreamWithLimit(stream, packageStream, maxFileBytes);
        return new LoadPackageStream(packageStream, CanReuseBufferForSnapshot: true);
    }

    private static void CopyToMemoryStreamWithLimit(Stream source, MemoryStream destination, long maxFileBytes)
    {
        var buffer = new byte[81920];
        while (true)
        {
            var remainingAllowance = maxFileBytes - destination.Length;
            var maxRead = remainingAllowance >= buffer.Length
                ? buffer.Length
                : (int)Math.Max(1, remainingAllowance + 1);
            var read = source.Read(buffer, 0, maxRead);
            if (read == 0)
                return;

            if (read > remainingAllowance)
            {
                throw new WorkbookTooLargeException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The file exceeds the {FormatFileSize(maxFileBytes)} open limit."));
            }

            destination.Write(buffer, 0, read);
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {units[unit]}");
    }

    private readonly record struct LoadPackageStream(
        MemoryStream PackageStream,
        bool CanReuseBufferForSnapshot);

    private static (MemoryStream PackageStream, XLWorkbook Workbook) OpenClosedXmlWorkbookWithSanitizationFallback(
        MemoryStream packageStream,
        bool stripStyleOnlyCells)
    {
        var closedXmlPackageStream = CreateClosedXmlParsePackage(
            packageStream,
            stripStyleOnlyCells,
            removeUnsupportedConditionalFormatting: false);
        try
        {
            return (closedXmlPackageStream, new XLWorkbook(closedXmlPackageStream));
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(closedXmlPackageStream, packageStream))
                closedXmlPackageStream.Dispose();

            if (IsClosedXmlConditionalFormattingLoadFailure(ex))
                return OpenClosedXmlWorkbookWithConditionalFormattingStripped(packageStream, stripStyleOnlyCells);

            packageStream.Position = 0;
            var fallbackPackageStream = CreateClosedXmlParsePackage(
                packageStream,
                stripStyleOnlyCells,
                removeUnsupportedConditionalFormatting: true,
                removeAllConditionalFormatting: false);
            try
            {
                return (fallbackPackageStream, new XLWorkbook(fallbackPackageStream));
            }
            catch
            {
                if (!ReferenceEquals(fallbackPackageStream, packageStream))
                    fallbackPackageStream.Dispose();

                return OpenClosedXmlWorkbookWithConditionalFormattingStripped(packageStream, stripStyleOnlyCells);
            }
        }
    }

    private static (MemoryStream PackageStream, XLWorkbook Workbook) OpenClosedXmlWorkbookWithConditionalFormattingStripped(
        MemoryStream packageStream,
        bool stripStyleOnlyCells)
    {
        packageStream.Position = 0;
        var conditionalFormattingStrippedPackageStream = CreateClosedXmlParsePackage(
            packageStream,
            stripStyleOnlyCells,
            removeUnsupportedConditionalFormatting: true,
            removeAllConditionalFormatting: true);
        try
        {
            return (conditionalFormattingStrippedPackageStream, new XLWorkbook(conditionalFormattingStrippedPackageStream));
        }
        catch
        {
            if (!ReferenceEquals(conditionalFormattingStrippedPackageStream, packageStream))
                conditionalFormattingStrippedPackageStream.Dispose();
            throw;
        }
    }

    private static bool IsClosedXmlConditionalFormattingLoadFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.StackTrace?.Contains("LoadConditionalFormatting", StringComparison.Ordinal) == true)
                return true;
        }

        return false;
    }

    private static MemoryStream CreateClosedXmlParsePackage(
        MemoryStream packageStream,
        bool stripStyleOnlyCells,
        bool removeUnsupportedConditionalFormatting,
        bool removeAllConditionalFormatting = false)
    {
        var styleOptimizedPackage = stripStyleOnlyCells
            ? XlsxClosedXmlStyleOnlyCellStripper.Create(packageStream)
            : packageStream;
        try
        {
            var sanitizedPackage = XlsxClosedXmlLoadPackageSanitizer.Create(
                styleOptimizedPackage,
                removeUnsupportedConditionalFormatting,
                removeAllConditionalFormatting);
            if (!ReferenceEquals(sanitizedPackage, styleOptimizedPackage) &&
                !ReferenceEquals(styleOptimizedPackage, packageStream))
            {
                styleOptimizedPackage.Dispose();
            }

            return sanitizedPackage;
        }
        catch
        {
            if (!ReferenceEquals(styleOptimizedPackage, packageStream))
                styleOptimizedPackage.Dispose();
            throw;
        }
    }

}
