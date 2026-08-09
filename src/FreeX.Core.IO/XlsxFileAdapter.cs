using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Xml.Linq;
using ClosedXML.Excel;
using FreeX.Core.Model;
using NPOI.POIFS.FileSystem;

namespace FreeX.Core.IO;

/// <summary>
/// Raised when an .xlsx file cannot be opened because it is an OLE/CFB-wrapped
/// "Encrypt with Password" package (an EncryptedPackage stream inside an OLE compound file)
/// rather than a plain OOXML zip. Distinguishes this common, user-actionable case from a
/// generic corrupt-file error so the UI can tell the user the real reason.
/// </summary>
public sealed class WorkbookPasswordProtectedException : Exception
{
    public WorkbookPasswordProtectedException(string message) : base(message)
    {
    }
}

/// <summary>
/// XLSX file adapter using ClosedXML.
/// Supports standard .xlsx workbook files.
/// </summary>
public sealed partial class XlsxFileAdapter : IFileAdapter, IWarningCollectingFileAdapter
{
    private const int ClosedXmlStyleOnlyStripCellThreshold = 16_384;
    private static readonly ConditionalWeakTable<Workbook, XlsxSourcePackage> SourcePackages = new();
    // ClosedXML keeps the immutable style key on internal cell types. Use a reflected delegate
    // so repeated styles are mapped once without materializing an XLStyle for every used cell.
    private static readonly Func<IXLCell, object?>? XlCellStyleValueAccessor = CreateXlCellStyleValueAccessor();
    public string Extension => ".xlsx";
    public string FormatName => "XLSX Workbook";
    internal XlsxSaveDiagnostics LastSaveDiagnostics { get; private set; } = XlsxSaveDiagnostics.NotRun;
    internal XlsxLoadDiagnostics LastLoadDiagnostics { get; private set; } = XlsxLoadDiagnostics.NotRun;

    public static void DetachSourcePackage(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        SourcePackages.Remove(workbook);
    }

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

    // ClosedXML's XLWorkbook construction and population touch process-global static state and are
    // NOT safe to run on multiple threads at once.  Every ClosedXML-backed load AND full-save is
    // serialized through this single process-wide gate so a background startup prewarm — or a second
    // window opening/saving a file — can never race a concurrent load and corrupt ClosedXML's
    // internals.  That race manifested as intermittent crashes on the first file opens after launch.
    // Opens/saves are infrequent and user-initiated, so serializing them is a non-issue versus the
    // crash it prevents.
    internal static readonly object ClosedXmlGate = new();

    public XlsxLoadResult LoadWithWarnings(Stream stream, bool inspectFeatures)
    {
        var warnings = new List<string>();
        Workbook workbook;
        XlsxFeatureReport? featureReport;
        lock (ClosedXmlGate)
        {
            workbook = LoadCore(stream, warnings, inspectFeatures, out featureReport);
        }

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
        LastLoadDiagnostics = XlsxLoadDiagnostics.NotRun;
        var totalAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var totalStopwatch = Stopwatch.StartNew();
        var (loadPackage, packageCopyDiagnostics) = MeasureLoadPhase(() => CreateLoadPackageStream(stream));
        using var packageStream = loadPackage.PackageStream;
        ThrowIfPasswordEncrypted(packageStream);
        var packageParts = XlsxLoadPackageParts.Empty;
        var workbookTheme = WorkbookTheme.Office;
        var workbookMetadata = XlsxWorkbookMetadataSnapshot.Default;
        XlsxFeatureReport? inspectedFeatureReport = null;
        XDocument? stylesXml = null;
        var numberFormatCatalog = new Dictionary<int, string>();
        var pivotMetadata = XlsxPivotTableReader.PivotPackageMetadata.Empty;
        var slicerTimelineMetadata = SlicerTimelinePackageMetadata.Empty;
        IReadOnlyList<ExternalLinkModel> externalLinkMetadata = [];
        var structuredTableMetadata = StructuredTablePackageMetadata.Empty;
        IReadOnlyList<XlsxChartsheet> chartsheets = [];
        // Raw pageSetup@useFirstPageNumber per worksheet part path (keyed like SheetXmlLayout.WorksheetPath).
        // ClosedXML's IXLPageSetup.FirstPageNumber reflects only the raw firstPageNumber attribute value and
        // drops the useFirstPageNumber checkbox flag entirely, so it must be read directly from the source
        // package XML to tell an enabled custom first-page-number from a disabled one with a stale numeric
        // value left in the file (see the FirstPageNumber assignment below).
        Dictionary<string, bool>? firstPageNumberEnabledByWorksheetPath = null;
        // Raw hyperlink "location" attribute per worksheet part path + cell ref, but ONLY for
        // hyperlink elements that ALSO carry an r:id (external relationship). R55-io-hyperlink-
        // round-trip-5-2: ClosedXML's XLHyperlink.InternalAddress getter -- the only source the load
        // loop below otherwise reads Bookmark from -- comes back null/empty for this external+location
        // combination (Excel's "Existing File > Bookmark..." picker), so the sub-address is recovered
        // directly from the source XML instead.
        Dictionary<string, Dictionary<string, string>>? externalHyperlinkLocationsByWorksheetPath = null;
        // R106-io-hyperlink-range-shift: whole-column ("C:C"), whole-row ("3:3"), and oversized
        // bounded-range hyperlink refs are stripped from the ClosedXML-input copy before ClosedXML
        // loads it (XlsxWorksheetHyperlinkNormalizer.StripRangeHyperlinkRefs), so ClosedXML's own
        // xlSheet.Hyperlinks collection below never contains them at all. Read them directly from
        // this PRISTINE, unmodified package archive instead so Sheet.RangeHyperlinks can track their
        // live GridRange and shift it on row/column insert/delete, mirroring
        // ReadWorksheetExternalHyperlinkLocations just above.
        Dictionary<string, Dictionary<string, GridRange>>? rangeHyperlinksByWorksheetPath = null;
        var packageMetadataDiagnostics = MeasureLoadPhase(() =>
        {
            try
            {
                using var packageArchive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
                // Reject zip-bomb / oversized packages before any decompression-heavy reads.
                WorkbookOpenSizeGuard.EnsureArchiveWithinLimits(packageArchive);
                if (inspectFeatures)
                    inspectedFeatureReport = XlsxFeatureInspector.Inspect(packageArchive);

                packageParts = XlsxLoadPackageParts.Inspect(packageArchive);
                chartsheets = XlsxChartsheetReader.Read(packageArchive);
                firstPageNumberEnabledByWorksheetPath = ReadWorksheetFirstPageNumberEnabledFlags(packageArchive);
                externalHyperlinkLocationsByWorksheetPath = ReadWorksheetExternalHyperlinkLocations(packageArchive);
                rangeHyperlinksByWorksheetPath = ReadWorksheetRangeHyperlinks(packageArchive);

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
            }
            catch (InvalidDataException)
            {
                // Not a valid zip archive; let the ClosedXML loader produce the format error.
            }
        });
        featureReport = inspectedFeatureReport;

        var (styleMetadata, styleMetadataDiagnostics) = MeasureLoadPhase(() =>
        {
            var loadedIndexedColors = XlsxIndexedColorPaletteMapper.Load(stylesXml);
            return (
                IndexedColors: loadedIndexedColors,
                CellBorderStyles: XlsxCellBorderStyleReader.Read(stylesXml, workbookTheme, loadedIndexedColors),
                CellGradientFills: XlsxCellGradientFillReader.Read(stylesXml, workbookTheme, loadedIndexedColors),
                PivotTableStyles: XlsxPivotTableStyleMetadataReader.Load(stylesXml),
                StructuredTableStyles: XlsxStructuredTableStyleMetadataReader.Load(stylesXml, workbookTheme, loadedIndexedColors),
                CustomViews: workbookMetadata.CustomViews);
        });
        var indexedColors = styleMetadata.IndexedColors;
        var cellBorderStyles = styleMetadata.CellBorderStyles;
        var cellGradientFills = styleMetadata.CellGradientFills;
        var pivotTableStyleMetadata = styleMetadata.PivotTableStyles;
        var structuredTableStyleMetadata = styleMetadata.StructuredTableStyles;
        var xlsxCustomViews = styleMetadata.CustomViews;

        packageStream.Position = 0;
        Dictionary<string, SheetXmlLayout> sheetXmlLayout = [];
        var sheetXmlLayoutHadWarnings = false;
        var loadedStructuredTableMetadataFromSheetLayout = false;
        IReadOnlySet<string>? styleOnlyWorksheetPathsToStrip = null;
        var sanitizationHints = default(XlsxClosedXmlLoadSanitizationHints);
        var sheetXmlLayoutDiagnostics = MeasureLoadPhase(() =>
        {
            var sheetXmlLayoutWarningCount = warnings.Count;
            sheetXmlLayout = LoadSheetXmlLayout(
                packageStream,
                stylesXml,
                workbookTheme,
                indexedColors,
                packageParts.HasStructuredTables,
                out var sheetLayoutStructuredTableMetadata,
                warnings);
            sheetXmlLayoutHadWarnings = warnings.Count != sheetXmlLayoutWarningCount;
            if (packageParts.HasStructuredTables &&
                !sheetXmlLayoutHadWarnings &&
                sheetXmlLayout.Count > 0)
            {
                structuredTableMetadata = sheetLayoutStructuredTableMetadata;
                loadedStructuredTableMetadataFromSheetLayout = true;
            }

            styleOnlyWorksheetPathsToStrip = GetClosedXmlStyleOnlyWorksheetPathsToStrip(
                sheetXmlLayout,
                sheetXmlLayoutHadWarnings);
            sanitizationHints = CreateClosedXmlLoadSanitizationHints(
                packageParts,
                sheetXmlLayout,
                sheetXmlLayoutHadWarnings);
        });
        if (packageParts.HasStructuredTables && !loadedStructuredTableMetadataFromSheetLayout)
        {
            packageStream.Position = 0;
            packageMetadataDiagnostics = AddLoadPhaseDiagnostics(
                packageMetadataDiagnostics,
                MeasureLoadPhase(() =>
                {
                    try
                    {
                        using var packageArchive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
                        structuredTableMetadata = XlsxStructuredTableMetadataReader.Load(packageArchive);
                    }
                    catch (InvalidDataException)
                    {
                        // Not a valid zip archive; let the ClosedXML loader produce the format error.
                    }
                }));
        }
        packageStream.Position = 0;
        var closedXmlLoad = OpenClosedXmlWorkbookWithSanitizationFallback(
            packageStream,
            styleOnlyWorksheetPathsToStrip,
            sanitizationHints);
        var closedXmlLoadDiagnostics = closedXmlLoad.Diagnostics;
        using var closedXmlPackageStream = closedXmlLoad.PackageStream;
        using var xlWorkbook = closedXmlLoad.Workbook;
        var materializationAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var materializationStopwatch = Stopwatch.StartNew();
        var worksheetsWithPreservableSourceMetadata = GetWorksheetsWithPreservableSourceMetadata(
            sheetXmlLayout,
            sheetXmlLayoutHadWarnings,
            xlWorkbook.Worksheets.Count);
        var hasUnsupportedConditionalFormatting = GetHasUnsupportedConditionalFormatting(
            sheetXmlLayout,
            sheetXmlLayoutHadWarnings,
            xlWorkbook.Worksheets.Count);
        var workbook = new Workbook("Untitled", XlsxClosedXmlCellMapper.MapStyle(xlWorkbook.Style, workbookTheme, indexedColors));
        workbook.Theme = workbookTheme;
        workbook.HasVbaProjectPackage = packageParts.HasVbaProjectPackage;
        workbook.Uses1904DateSystem = workbookMetadata.Uses1904DateSystem;
        workbook.Properties = workbookMetadata.WorkbookProperties;
        var workbookViewProperties = workbookMetadata.WorkbookViewProperties;
        workbook.ShowSheetTabs = workbookViewProperties.ShowSheetTabs;
        workbook.SheetTabRatio = workbookViewProperties.SheetTabRatio is { } tabRatio ? Math.Clamp(tabRatio, 0, 1000) : null;
        // NOTE: workbookView/@firstSheet and @activeTab are indices into the workbook's FULL <sheets>
        // order (worksheets AND chartsheets interspersed), matching XlsxChartsheet.WorkbookSheetIndex
        // below. They must not be clamped/assigned against `xlWorkbook.Worksheets.Count` (ClosedXML's
        // worksheet-only count) here -- that count excludes chartsheets, which are only spliced into
        // `workbook.Sheets` later by InsertChartsheets. Assigning here would both under-count the
        // clamp bound and, once InsertChartsheets shifts later sheets to the right, leave the index
        // pointing at whatever chartsheet(s) got inserted before it instead of the originally active
        // sheet. The actual assignment (clamped against the final, post-splice sheet count) happens
        // after InsertChartsheets runs, below.
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
        workbook.FullPrecision = calculationProperties.FullPrecision;
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
        var styleIdsByNativeBorderStyleIndex = new Dictionary<int, StyleId?>();
        var styleIdsByNativeGradientStyleIndex = new Dictionary<int, StyleId?>();
        var styleIdsByXlsxStyleValue = new Dictionary<object, StyleId?>();
        // Single shared dictionary instance reused across all sheets (cleared between sheets) to avoid
        // per-sheet allocation churn. The ordering invariant of ExplicitPopulatedCellStyles (XLSX
        // spec requires cells in ascending row/col order) would allow binary search, but we keep the
        // dictionary to handle malformed files where cells arrive out of order without silent mis-styling.
        Dictionary<(uint Row, uint Col), int>? sharedPopulatedCellStyleIndexes = null;
        // Pre-register all sheets so that the sheetNameResolver built inside ApplySheetXmlLayout
        // contains every sheet's SheetId — even sheets that haven't been fully loaded yet.
        // Without this pre-pass, charts on early sheets (e.g. "10 Charts") that reference later
        // sheets (e.g. "4. Dynamic Histogram") would not find the referenced SheetId and would
        // fall back to the chart host's own SheetId, breaking cross-sheet DataRange resolution.
        foreach (var xlSheet in xlWorkbook.Worksheets)
            workbook.AddSheet(xlSheet.Name);
        foreach (var xlSheet in xlWorkbook.Worksheets)
        {
            var sheet = workbook.GetSheet(xlSheet.Name)!;
            sheetXmlLayout.TryGetValue(xlSheet.Name, out var xmlLayout);
            Dictionary<(uint Row, uint Col), int>? populatedCellStyleIndexes = null;
            if (cellBorderStyles.HasVisibleBorders || cellGradientFills.HasAny)
            {
                sharedPopulatedCellStyleIndexes = BuildCellStyleIndexLookup(
                    xmlLayout?.ExplicitPopulatedCellStyles,
                    sharedPopulatedCellStyleIndexes);
                populatedCellStyleIndexes = sharedPopulatedCellStyleIndexes;
            }
            if (xmlLayout is { PopulatedCellCount: > 0 } layoutWithCells)
                sheet.EnsureCellCapacity(layoutWithCells.PopulatedCellCount);
            if (xmlLayout is { ExplicitStyleOnlyCells.Count: > 0 } layoutWithStyleOnlyCells)
                sheet.EnsureStyleOnlyCapacity(layoutWithStyleOnlyCells.ExplicitStyleOnlyCells.Count);

            sheet.IsVeryHidden = xlSheet.Visibility == XLWorksheetVisibility.VeryHidden;
            sheet.IsHidden = xlSheet.Visibility != XLWorksheetVisibility.Visible;
            if (xlSheet.TabColor.HasValue)
            {
                // Capture both the baked RGB (for renderers that don't yet re-resolve live) and the
                // theme-color reference (slot+tint), mirroring font/fill/border colors — see
                // R123-tab-theme-color-1: without the reference, a theme-relative <tabColor theme="…"/>
                // was permanently baked to RGB at load and silently downgraded to a literal <tabColor
                // rgb="…"/> on save, losing the theme link. Order matters: assigning TabColor first
                // clears any stale TabThemeColor (see Sheet.TabColor), so the theme reference must be
                // set AFTER the baked color here.
                sheet.TabColor = XlsxClosedXmlCellMapper.MapColor(xlSheet.TabColor, workbook.Theme, indexedColors);
                sheet.TabThemeColor = XlsxClosedXmlCellMapper.MapThemeColorReference(xlSheet.TabColor);
            }

            // Track declared array formula ref ranges (anchor address + bounding box).
            // Excel 365 stores the cached output of a dynamic/CSE array formula as plain <v> cells
            // in the spill range (no formula). FreeX loads these as "provisional spill cells":
            //   • They are stored in _cells so the viewport displays them on open (no recalc).
            //   • They are tagged in Sheet._provisionalSpillCells so IsSpillBlocked allows the
            //     owning anchor to overwrite them when the anchor formula re-spills on recalc.
            //   • SetSpillRange removes provisional entries from _cells before writing to _spillValues.
            List<(uint R0, uint C0, uint R1, uint C1, CellAddress AnchorAddr)>? arraySpillRanges = null;

            foreach (var xlCell in xlSheet.CellsUsed())
            {
                var addr = new CellAddress(sheet.Id, (uint)xlCell.Address.RowNumber, (uint)xlCell.Address.ColumnNumber);

                // Determine whether this cell is a provisional spill cell (non-anchor in array range).
                CellAddress? provisionalAnchor = null;
                // Declared <f t="array" ref="..."> extent for this cell if it is the anchor of a legacy
                // CSE array formula (set below, consumed when constructing the anchor's Cell further down).
                uint legacyArrayRows = 0, legacyArrayCols = 0;

                if (xlCell.HasArrayFormula && xlCell.FormulaReference is { } arrayRef)
                {
                    // Legacy multi-cell array formula (CSE / Ctrl+Shift+Enter): Excel stores one formula
                    // on the top-left anchor with a declared <f t="array" ref="..."> range and propagates
                    // it to every covered cell. Load only the anchor as the formula cell — covered cells
                    // must not become independent formula cells (they would mutually block each other's
                    // spill and collapse the range to #SPILL!). The non-anchor cells carry cached <v>
                    // values that we load as provisional spill cells for display.
                    var isAnchor = arrayRef.FirstAddress.RowNumber == xlCell.Address.RowNumber &&
                                   arrayRef.FirstAddress.ColumnNumber == xlCell.Address.ColumnNumber;

                    if (isAnchor)
                    {
                        // Register the declared ref range so we can identify non-anchor cells as
                        // provisional spill cells when they appear later in the iteration. A genuinely
                        // multi-cell ref also marks this anchor as a fixed-extent legacy CSE array
                        // formula (see Cell.LegacyArrayRows) so it never free-spills like a modern
                        // dynamic-array formula. A 1x1 ref is deliberately NOT treated as fixed-extent
                        // here: FreeX's own full save (XlsxFileAdapter.Save.cs) writes a currently-1x1
                        // dynamic-array/blocked-spill formula as a single-cell "t=array ref=anchor" too
                        // (purely to keep HasArrayFormula true across a round-trip so it can re-spill
                        // again later) -- that representation is indistinguishable from a genuine
                        // single-cell legacy CSE formula, and treating every 1x1 array-formula reload
                        // as permanently fixed-extent would stop those FreeX-authored dynamic arrays
                        // from ever re-spilling after an edit (R17_save_io_Tests, FreeXR13S11Tests).
                        if (arrayRef.LastAddress.RowNumber > arrayRef.FirstAddress.RowNumber ||
                            arrayRef.LastAddress.ColumnNumber > arrayRef.FirstAddress.ColumnNumber)
                        {
                            legacyArrayRows = (uint)(arrayRef.LastAddress.RowNumber - arrayRef.FirstAddress.RowNumber + 1);
                            legacyArrayCols = (uint)(arrayRef.LastAddress.ColumnNumber - arrayRef.FirstAddress.ColumnNumber + 1);

                            arraySpillRanges ??= [];
                            arraySpillRanges.Add((
                                (uint)arrayRef.FirstAddress.RowNumber,
                                (uint)arrayRef.FirstAddress.ColumnNumber,
                                (uint)arrayRef.LastAddress.RowNumber,
                                (uint)arrayRef.LastAddress.ColumnNumber,
                                addr));
                        }
                        // Anchor falls through to normal formula-cell loading below.
                    }
                    else
                    {
                        // Non-anchor: find its owning anchor and mark as provisional.
                        if (arraySpillRanges is not null)
                        {
                            var row = (uint)xlCell.Address.RowNumber;
                            var col = (uint)xlCell.Address.ColumnNumber;
                            foreach (var (r0, c0, r1, c1, anchorAddr) in arraySpillRanges)
                            {
                                if (row >= r0 && row <= r1 && col >= c0 && col <= c1)
                                {
                                    provisionalAnchor = anchorAddr;
                                    break;
                                }
                            }
                        }
                        if (provisionalAnchor is null)
                            continue; // orphaned non-anchor — skip as before
                        // Fall through to provisional value loading below.
                    }
                }
                else if (!xlCell.HasFormula && arraySpillRanges is not null)
                {
                    // Excel 365 stores dynamic-array spill cells as plain value cells (no formula,
                    // no HasArrayFormula flag). Detect if this cell falls inside a known array
                    // formula ref range (but is not the anchor) and mark it as provisional.
                    var row = (uint)xlCell.Address.RowNumber;
                    var col = (uint)xlCell.Address.ColumnNumber;
                    foreach (var (r0, c0, r1, c1, anchorAddr) in arraySpillRanges)
                    {
                        if (row >= r0 && row <= r1 && col >= c0 && col <= c1 && !(row == r0 && col == c0))
                        {
                            provisionalAnchor = anchorAddr;
                            break;
                        }
                    }
                    // Fall through to provisional value loading below if provisionalAnchor is set;
                    // otherwise falls through to normal value-cell loading.
                }
                else if (xlCell.HasFormula && !xlCell.HasArrayFormula &&
                         string.IsNullOrEmpty(xlCell.FormulaA1) &&
                         arraySpillRanges is not null)
                {
                    // Excel 365 also stores dynamic-array spill cells with a <f ca="1"/> marker —
                    // an empty formula element with ca="1" (which ClosedXML surfaces as HasFormula=true,
                    // FormulaA1="").  These are NOT independent formula cells; they are spill
                    // continuation cells that carry the anchor's cached result as <v>.
                    // Detect them by checking whether they fall inside a declared array ref range.
                    var row = (uint)xlCell.Address.RowNumber;
                    var col = (uint)xlCell.Address.ColumnNumber;
                    foreach (var (r0, c0, r1, c1, anchorAddr) in arraySpillRanges)
                    {
                        if (row >= r0 && row <= r1 && col >= c0 && col <= c1 && !(row == r0 && col == c0))
                        {
                            provisionalAnchor = anchorAddr;
                            break;
                        }
                    }
                    // Fall through to provisional value loading below if provisionalAnchor is set;
                    // otherwise falls through to the empty-formula path (treated as a formula cell
                    // that will produce #VALUE! on recalc — unexpected but not a regression).
                }

                Cell cell;
                if (xlCell.HasFormula && provisionalAnchor is null)
                {
                    cell = Cell.FromFormula(XlsxClosedXmlCellMapper.NormalizeFormulaText(xlCell.FormulaA1));
                    // A plain (non-array) formula uses Excel's legacy implicit intersection; an array
                    // formula (CSE or dynamic) spills. Cell.FromFormula defaults to Dynamic.
                    if (!xlCell.HasArrayFormula)
                    {
                        cell.ArrayMode = FormulaArrayMode.Implicit;
                    }
                    else if (legacyArrayRows > 0)
                    {
                        // This is the anchor of a legacy CSE array formula (<f t="array" ref="...">):
                        // confine its result to the originally declared ref extent on every recalc
                        // instead of letting it free-spill like a modern dynamic-array formula (see
                        // Cell.LegacyArrayRows / RecalcEngine).
                        cell.LegacyArrayRows = legacyArrayRows;
                        cell.LegacyArrayCols = legacyArrayCols;
                    }
                    // Preserve the cached formula result so callers see the last-calculated value
                    // without needing to recalculate immediately.
                    var cached = XlsxClosedXmlCellMapper.MapFormulaValue(xlCell, workbook.Uses1904DateSystem);
                    if (cached is not BlankValue)
                        cell.Value = cached;
                    else if (xmlLayout?.CachedFormulaErrors.TryGetValue(((uint)xlCell.Address.RowNumber, (uint)xlCell.Address.ColumnNumber), out var cachedFormulaError) == true)
                        cell.Value = cachedFormulaError;
                }
                else
                {
                    // Plain value cell (or provisional spill cell that ClosedXML exposes as HasArrayFormula
                    // — those have a cached <v> value accessible via MapFormulaValue).
                    ScalarValue v;
                    if (xlCell.HasFormula && !xlCell.HasArrayFormula && string.IsNullOrEmpty(xlCell.FormulaA1))
                    {
                        // Excel 365's <f ca="1"/> spill-continuation marker (empty formula element,
                        // ca="1"): there is no formula text to evaluate, so asking ClosedXML for
                        // xlCell.Value here would force it to parse the empty formula string and throw
                        // (ExpressionParseException: "Unexpected token EofSymbolId"). Read the cached
                        // <v> directly instead — this also correctly yields BlankValue when the marker
                        // carries no cached value at all (e.g. a blank last spill member).
                        v = XlsxClosedXmlCellMapper.MapValue(xlCell.CachedValue, workbook.Uses1904DateSystem);
                    }
                    else
                    {
                        v = xlCell.HasFormula
                            ? XlsxClosedXmlCellMapper.MapFormulaValue(xlCell, workbook.Uses1904DateSystem)
                            : XlsxClosedXmlCellMapper.MapValue(xlCell, workbook.Uses1904DateSystem);
                    }
                    cell = Cell.FromValue(v);
                }

                int? xlsxStyleIndex = populatedCellStyleIndexes is not null &&
                    populatedCellStyleIndexes.TryGetValue((addr.Row, addr.Col), out var parsedStyleIndex)
                        ? parsedStyleIndex
                        : null;
                if (GetRegisteredStyleId(
                        xlCell,
                        workbook,
                        workbook.Theme,
                        indexedColors,
                        styleIdsByXlsxStyleValue,
                        cellBorderStyles,
                        cellGradientFills,
                        xlsxStyleIndex,
                        styleIdsByNativeBorderStyleIndex,
                        styleIdsByNativeGradientStyleIndex) is { } styleId)
                {
                    cell.StyleId = styleId;
                }

                cell.QuotePrefix = XlsxClosedXmlCellMapper.MapQuotePrefix(xlCell);

                if (cell.Value is BlankValue && !cell.HasFormula)
                {
                    // A blank member cell still belonging to a declared CSE/dynamic array ref range
                    // (e.g. C1:C3 where C3's cached <v> is blank or absent) must still be registered
                    // as a provisional spill member so Sheet.TryGetArrayExtent recovers the file's
                    // full declared extent at save time — otherwise the extent silently shrinks to
                    // the bounding box of only the members that happened to carry a cached value,
                    // and the array's declared range is lost on round-trip (see XlsxFileAdapter.Save.cs).
                    if (provisionalAnchor is { } blankAnchor)
                    {
                        sheet.SetProvisionalSpillCell(blankAnchor, addr.Row, addr.Col, cell);
                        continue;
                    }

                    if (cell.StyleId != StyleId.Default)
                        sheet.SetStyleOnly(addr.Row, addr.Col, cell.StyleId);

                    continue;
                }

                if (provisionalAnchor is { } anchor)
                    sheet.SetProvisionalSpillCell(anchor, addr.Row, addr.Col, cell);
                else
                    sheet.SetCell(addr, cell);
            }

            // ClosedXML's CellsUsed() silently skips SharedString cells whose SST entry is an
            // empty string (t="s" with <v> pointing to SST[n]="").  Those cells are data cells
            // that carry a formula-empty-string value ("") and must be loaded as TextValue("").
            // We detect them via the raw XML layout (SharedStringValueCells) and access them
            // directly from ClosedXML here so they are not silently treated as BlankValue.
            foreach (var (row, col) in xmlLayout?.SharedStringValueCells ?? [])
            {
                if (sheet.GetCell(row, col) is not null)
                    continue; // already loaded by CellsUsed()

                var xlCell = xlSheet.Cell((int)row, (int)col);
                // Note: do NOT use xlCell.IsEmpty() here — ClosedXML considers SharedString-""
                // cells as "empty" (the displayed value is ""), but they are NOT blank; they are
                // text cells that carry an explicit empty-string value from the SST.
                if (xlCell.DataType != XLDataType.Text)
                    continue; // not a text cell — skip (e.g. blank cell unexpectedly in the list)

                var text = xlCell.Value.GetText();
                if (text.Length > 0)
                    continue; // non-empty text — CellsUsed() should have caught it; skip here

                // Empty-string SharedString cell: store as TextValue("") so SORT/FILTER treat it
                // as a text value (sorts after numbers, before true blanks), matching Excel behavior.
                var addr = new CellAddress(sheet.Id, row, col);
                var styleId = GetRegisteredStyleId(
                    xlCell,
                    workbook,
                    workbook.Theme,
                    indexedColors,
                    styleIdsByXlsxStyleValue,
                    cellBorderStyles,
                    cellGradientFills,
                    populatedCellStyleIndexes is not null &&
                        populatedCellStyleIndexes.TryGetValue((row, col), out var ssStyleIndex)
                        ? ssStyleIndex
                        : null,
                    styleIdsByNativeBorderStyleIndex,
                    styleIdsByNativeGradientStyleIndex);
                var valueCell = Cell.FromValue(new TextValue(""));
                if (styleId is { } ssStyleId)
                    valueCell.StyleId = ssStyleId;
                sheet.SetCell(addr, valueCell);
            }

            List<StyleOnlyRun>? explicitStyleOnlyRuns = null;
            foreach (var (row, col, styleIndex) in xmlLayout?.ExplicitStyleOnlyCells ?? [])
            {
                if (sheet.GetCell(row, col) is not null)
                    continue;

                if (!explicitStyleOnlyStyleIdsByXlsxStyleIndex.TryGetValue(styleIndex, out var styleId))
                {
                    var xlCell = xlSheet.Cell((int)row, (int)col);
                    var style = MapStyleWithNativeFills(xlCell.Style, workbook.Theme, indexedColors, cellBorderStyles, cellGradientFills, styleIndex);
                    styleId = style.Equals(CellStyle.Default)
                        ? null
                        : workbook.RegisterStyle(style);
                    explicitStyleOnlyStyleIdsByXlsxStyleIndex[styleIndex] = styleId;
                }

                if (styleId is { } nonDefaultStyleId)
                    AddStyleOnlyRun(ref explicitStyleOnlyRuns, row, col, nonDefaultStyleId);
            }

            if (explicitStyleOnlyRuns is { Count: > 0 })
                sheet.SetStyleOnlyRuns(explicitStyleOnlyRuns);

            foreach (var hyperlink in xlSheet.Hyperlinks)
            {
                try
                {
                    var cell = hyperlink.Cell;
                    if (cell is null) continue;

                    var addr = new CellAddress(sheet.Id, (uint)cell.Address.RowNumber, (uint)cell.Address.ColumnNumber);
                    var bookmark = NormalizeInternalHyperlinkAddress(hyperlink.InternalAddress, xlSheet.Name);

                    // R55-io-hyperlink-round-trip-5-2: ClosedXML's InternalAddress getter is empty for
                    // an external hyperlink that ALSO carries a "location" sub-address (Excel's
                    // "Existing File > Bookmark..." feature); recover it from the raw source XML.
                    if (string.IsNullOrWhiteSpace(bookmark) &&
                        hyperlink.ExternalAddress is not null &&
                        xmlLayout?.WorksheetPath is { Length: > 0 } hyperlinkWorksheetPath &&
                        externalHyperlinkLocationsByWorksheetPath is not null &&
                        externalHyperlinkLocationsByWorksheetPath.TryGetValue(hyperlinkWorksheetPath, out var locationsByRef) &&
                        locationsByRef.TryGetValue(addr.ToA1(), out var rawLocation))
                    {
                        bookmark = rawLocation;
                    }

                    var target = hyperlink.ExternalAddress?.ToString() ?? bookmark ?? string.Empty;
                    if (string.IsNullOrEmpty(target)) continue;

                    sheet.Hyperlinks[addr] = target;
                    sheet.HyperlinkMetadata[addr] = new HyperlinkMetadata(
                        GetHyperlinkTargetKind(hyperlink, target),
                        hyperlink.Tooltip ?? "",
                        bookmark ?? "");
                }
                catch (Exception ex)
                {
                    warnings.Add($"[hyperlinks] Sheet '{xlSheet.Name}': {ex.Message}");
                }
            }

            // R106-io-hyperlink-range-shift: populate Sheet.RangeHyperlinks with the whole-column/
            // row and oversized-bounded-range refs xlSheet.Hyperlinks above could never see (they
            // were stripped before ClosedXML loaded its copy). Keyed by worksheet part path, read
            // from the pristine source archive up front (rangeHyperlinksByWorksheetPath).
            if (xmlLayout?.WorksheetPath is { Length: > 0 } rangeHyperlinkWorksheetPath &&
                rangeHyperlinksByWorksheetPath is not null &&
                rangeHyperlinksByWorksheetPath.TryGetValue(rangeHyperlinkWorksheetPath, out var rangesByRef))
            {
                foreach (var (originalRef, range) in rangesByRef)
                    sheet.RangeHyperlinks[originalRef] = range;
            }

            if (xmlLayout is { } layout)
            {
                ApplySheetXmlLayout(workbook, sheet, layout, loadedScenarioNames, customViewStatesById);
                sheet.ShowZeros = layout.ShowZeros;
            }
            // Both mappers below parse a range reference straight out of the file
            // (the pivot's <location ref> and the table's <table ref>) through the throwing
            // GridRange.Parse/ParseCellOrRange. A malformed ref — empty, "#REF!", or truncated by a
            // sloppy third-party exporter — therefore aborted the whole workbook load rather than
            // dropping just that one pivot/table. Degrade per feature like every sibling above.
            if (pivotMetadata.PivotTablesBySheetName.TryGetValue(xlSheet.Name, out var pivotTables))
            {
                foreach (var pivotTable in pivotTables)
                {
                    try { sheet.PivotTables.Add(pivotTable.ToPivotTableModel(workbook, sheet.Id)); }
                    catch (Exception ex) { warnings.Add($"[pivot-table] Sheet '{xlSheet.Name}': {ex.Message}"); }
                }
            }
            if (structuredTableMetadata.TablesBySheetName.TryGetValue(xlSheet.Name, out var structuredTables))
            {
                foreach (var structuredTable in structuredTables)
                {
                    try
                    {
                        var table = XlsxStructuredTableModelMapper.ToModel(structuredTable, sheet.Id);
                        sheet.StructuredTables.Add(table);
                        XlsxStructuredTableModelMapper.MaterializeFilters(sheet, table);
                        XlsxStructuredTableModelMapper.MaterializeStyle(workbook, sheet, table);
                    }
                    catch (Exception ex) { warnings.Add($"[structured-table] Sheet '{xlSheet.Name}': {ex.Message}"); }
                }
            }

            if (xmlLayout?.PaneState is "frozen" or "frozenSplit")
            {
                sheet.FrozenRows = xmlLayout.PaneRowSplit ?? 0;
                sheet.FrozenCols = xmlLayout.PaneColumnSplit ?? 0;
            }
            else
            {
                // A real (non-frozen) window split (state="split") stores xSplit/ySplit as
                // twentieths-of-a-point pixel positions per the OOXML spec, not row/column
                // counts -- unlike the frozen-pane case above. ClosedXML's own SheetView
                // SplitRow/SplitColumn are only populated for its freeze-pane API, so they
                // are safe to use as literal row/column indices here; the raw xmlLayout
                // PaneRowSplit/PaneColumnSplit value must never be used as a fallback for a
                // real split, since it is a pixel position, not an index.
                var splitRow = xlSheet.SheetView.SplitRow > 0
                    ? (uint?)xlSheet.SheetView.SplitRow
                    : null;
                var splitColumn = xlSheet.SheetView.SplitColumn > 0
                    ? (uint?)xlSheet.SheetView.SplitColumn
                    : null;

                // ClosedXML never populates SplitRow/SplitColumn for state="split" (only for its
                // own freeze-pane API), so the above is always null here. A split divider always
                // sits exactly on a row/column boundary -- that's how Excel positions it -- so the
                // persisted xSplit/ySplit twips position can be inverted back to the row/column
                // index it was computed from, mirroring XlsxWorksheetViewWriter's
                // SplitRowToTwips/SplitColumnToTwips in reverse. Only trust the reconstruction when
                // EVERY split axis present on the pane resolves to an exact boundary match: if one
                // axis doesn't (e.g. the row heights/column widths on reload no longer match what
                // produced the original pixel position), the whole reconstruction is unreliable and
                // both axes are left null rather than guessed at.
                if ((splitRow is null || splitColumn is null) && xmlLayout?.PaneState is "split")
                {
                    var invertedRow = xmlLayout.PaneRowSplit is { } ySplitTwips
                        ? InvertSplitTwipsToIndex(
                            ySplitTwips,
                            row => sheet.RowHeights.TryGetValue(row, out var height) ? height : sheet.DefaultRowHeight,
                            CellAddress.MaxRow)
                        : null;
                    var invertedColumn = xmlLayout.PaneColumnSplit is { } xSplitTwips
                        ? InvertSplitTwipsToIndex(
                            xSplitTwips,
                            col => SplitCharacterWidthToPixels(sheet.ColumnWidths.TryGetValue(col, out var width) ? width : sheet.DefaultColumnWidth),
                            CellAddress.MaxCol)
                        : null;

                    var rowAxisOk = xmlLayout.PaneRowSplit is null || invertedRow is not null;
                    var columnAxisOk = xmlLayout.PaneColumnSplit is null || invertedColumn is not null;
                    if (rowAxisOk && columnAxisOk)
                    {
                        splitRow ??= invertedRow;
                        splitColumn ??= invertedColumn;
                    }
                }

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
            // Preserve the raw OOXML paper-size code so non-Letter/A4/Legal sizes round-trip.
            var rawPaperCode = (int)xlSheet.PageSetup.PaperSize;
            sheet.PaperSizeCode = rawPaperCode > 0 ? rawPaperCode : PaperSizeCodes.DefaultCode;
            sheet.PaperSize = PaperSizeCodes.TryGetEnum(sheet.PaperSizeCode, out var mappedPaperSize)
                ? mappedPaperSize
                : WorksheetPaperSize.A4;
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
            // ClosedXML reads the raw firstPageNumber attribute unconditionally, ignoring whether the
            // "First page number" checkbox (useFirstPageNumber) was actually on -- Excel commonly leaves
            // a stale firstPageNumber value in the XML after the box is unchecked. Only trust a nonzero
            // FirstPageNumber here when the source XML positively confirms useFirstPageNumber was truthy;
            // if we can't determine that (no raw metadata available), fall back to ClosedXML's value
            // rather than risk dropping a genuinely-enabled custom first page number.
            bool? firstPageNumberExplicitlyEnabled = xmlLayout?.WorksheetPath is { Length: > 0 } firstPageNumberWorksheetPath &&
                firstPageNumberEnabledByWorksheetPath is not null &&
                firstPageNumberEnabledByWorksheetPath.TryGetValue(firstPageNumberWorksheetPath, out var firstPageNumberEnabled)
                ? firstPageNumberEnabled
                : null;
            sheet.FirstPageNumber = xlSheet.PageSetup.FirstPageNumber == 0 || firstPageNumberExplicitlyEnabled == false
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

            // Load CellIs conditional format rules (best-effort; skip anything we can't map).
            // Priorities come from xmlLayout.ClassicConditionalFormatPriorities (real file priorities,
            // in document order) rather than a private counter, so they share one priority sequence
            // with the advanced (ColorScale/DataBar/IconSet/long-tail) rules already added above via
            // ApplySheetXmlLayout, preserving the original file's relative evaluation order.
            // ClassicConditionalFormatContainerAttributes (same document order) restores each classic
            // rule's preserved <conditionalFormatting> container attributes (e.g. pivot="1") that
            // ClosedXML's own object model has no API surface to read (R75-io-cf-classic-4-2).
            try
            {
                XlsxConditionalFormatClosedXmlMapper.Load(
                    xlSheet, sheet, workbook.Theme, XlsxClosedXmlCellMapper.MapStyle,
                    xmlLayout?.ClassicConditionalFormatPriorities,
                    xmlLayout?.ClassicConditionalFormatContainerAttributes);
            }
            catch (Exception ex) { warnings.Add($"[conditional-format] Sheet '{xlSheet.Name}': {ex.Message}"); }

            // Load data validation rules (best-effort)
            try { XlsxDataValidationClosedXmlMapper.Load(xlSheet, sheet, warnings); }
            catch (Exception ex) { warnings.Add($"[data-validation] Sheet '{xlSheet.Name}': {ex.Message}"); }
            if (xmlLayout is not null)
            {
                XlsxDataValidationNativeMetadataMapper.Apply(sheet, xmlLayout.DataValidationNativeMetadata);
                XlsxX14DataValidationReader.Apply(sheet, xmlLayout.X14DataValidations);
            }

            if (xmlLayout is null)
            {
                // Fall back to ClosedXML only when trusted worksheet XML layout was unavailable.
                try { LoadMergedRegions(xlSheet, sheet); }
                catch (Exception ex) { warnings.Add($"[merged-regions] Sheet '{xlSheet.Name}': {ex.Message}"); }
            }
        }

        InsertChartsheets(workbook, chartsheets, warnings);

        // Assign firstSheet/activeTab now that chartsheets have been spliced into `workbook.Sheets` at
        // their original interspersed positions, so both the clamp bound and the resulting index refer
        // to the workbook's final, full tab order (see the note where these were previously read, above).
        workbook.FirstVisibleSheetIndex = workbookViewProperties.FirstVisibleSheetIndex is { } firstSheet
            ? Math.Clamp(firstSheet, 0, Math.Max(0, workbook.Sheets.Count - 1))
            : null;
        workbook.ActiveSheetIndex = workbookViewProperties.ActiveSheetIndex is { } activeTab
            ? Math.Clamp(activeTab, 0, Math.Max(0, workbook.Sheets.Count - 1))
            : null;

        // Load per-run rich-text into sheet.RichTextRuns (best-effort, separate XML pass).
        try
        {
            packageStream.Position = 0;
            XlsxRichRunLoader.Load(packageStream, workbook, workbookTheme, indexedColors);
        }
        catch (Exception ex)
        {
            warnings.Add($"[rich-text-runs]: {ex.Message}");
        }

        ResolvePivotChartCacheBindings(workbook);

        // Load named ranges (best-effort; skip any we cannot map)
        try { XlsxNamedRangeMapper.Load(xlWorkbook, workbook, warnings); }
        catch (Exception ex) { warnings.Add($"[named-ranges]: {ex.Message}"); }
        try
        {
            packageStream.Position = 0;
            XlsxNamedRangeMapper.LoadWorkbookDefinedNameFormulasFromPackage(packageStream, workbook, warnings);
        }
        catch (Exception ex) { warnings.Add($"[named-ranges-xml]: {ex.Message}"); }

        foreach (var customView in xlsxCustomViews)
        {
            if (customViewStatesById.TryGetValue(customView.Id, out var states) && states.Count > 0)
                workbook.CustomViews.Add(new WorkbookCustomView(
                    customView.Name,
                    states,
                    customView.Id,
                    customView.IncludePrintSettings,
                    customView.IncludeHiddenRowsColumnsAndFilterSettings,
                    customView.ActiveSheetIndex is >= 0 && customView.ActiveSheetIndex < workbook.Sheets.Count
                        ? customView.ActiveSheetIndex
                        : null));
        }

        SourcePackages.Remove(workbook);
        materializationStopwatch.Stop();
        var materializationDiagnostics = new XlsxLoadPhaseDiagnostics(
            materializationStopwatch.Elapsed.TotalMilliseconds,
            GC.GetTotalAllocatedBytes(precise: true) - materializationAllocatedBefore);

        // Strip Excel-emitted invalid pageSetup DPI (horizontalDpi/verticalDpi = 0) from the source-package
        // snapshot bytes BEFORE capture so every save path (verbatim copy, patch-save, full save) round-trips
        // a schema-valid worksheet. The pre-scan keeps the common case (no invalid DPI) buffer-reuse-eligible.
        var dpiSanitized = XlsxWorksheetPageSetupDpiSanitizer.Sanitize(packageStream);
        var canReuseBufferForSnapshot = loadPackage.CanReuseBufferForSnapshot && !dpiSanitized;
        packageStream.Position = 0;
        var (sourcePackage, sourceSnapshotDiagnostics) = MeasureLoadPhase(() => XlsxSourcePackage.Capture(
            packageStream,
            workbook,
            canReuseBufferForSnapshot,
            worksheetsWithPreservableSourceMetadata,
            hasUnsupportedConditionalFormatting,
            sheetXmlLayout,
            sourceHasWorkbookCustomViews: xlsxCustomViews.Count > 0,
            sourceNeedsPackageGraphNormalization: XlsxDocumentPropertiesPreserver.NeedsPackageGraphNormalization(packageStream)));
        SourcePackages.Add(workbook, sourcePackage);
        totalStopwatch.Stop();
        LastLoadDiagnostics = new XlsxLoadDiagnostics(
            totalStopwatch.Elapsed.TotalMilliseconds,
            GC.GetTotalAllocatedBytes(precise: true) - totalAllocatedBefore,
            packageCopyDiagnostics,
            packageMetadataDiagnostics,
            styleMetadataDiagnostics,
            sheetXmlLayoutDiagnostics,
            closedXmlLoadDiagnostics.Total,
            closedXmlLoadDiagnostics.PackagePreparation,
            closedXmlLoadDiagnostics.WorkbookOpen,
            materializationDiagnostics,
            sourceSnapshotDiagnostics);
        return workbook;
    }

    // Twentieths-of-a-point per pixel (96 DPI), matching XlsxWorksheetViewWriter's own
    // TwipsPerPixel constant used to produce a state="split" pane's xSplit/ySplit values.
    private const double SplitTwipsPerPixel = 15.0;

    /// <summary>
    /// Inverts a state="split" pane's xSplit/ySplit twips position back into the row/column index
    /// it was computed from, by walking forward accumulating <paramref name="sizeForIndex"/> (row
    /// height or column width in pixels) exactly as XlsxWorksheetViewWriter's
    /// SplitRowToTwips/SplitColumnToTwips does going the other way. Returns the first index whose
    /// cumulative extent rounds to <paramref name="targetTwips"/>, or null if no boundary matches
    /// (the position is mid-row/column, or lies beyond <paramref name="maxIndex"/>).
    /// </summary>
    private static uint? InvertSplitTwipsToIndex(uint targetTwips, Func<uint, double> sizeForIndex, uint maxIndex)
    {
        var cumulativePixels = 0.0;
        for (var index = 1u; index <= maxIndex; index++)
        {
            var cumulativeTwips = (uint)Math.Max(0, Math.Round(cumulativePixels * SplitTwipsPerPixel, MidpointRounding.AwayFromZero));
            if (cumulativeTwips == targetTwips)
                return index;
            if (cumulativeTwips > targetTwips)
                return null;

            cumulativePixels += sizeForIndex(index);
        }

        return null;
    }

    // Mirrors XlsxWorksheetViewWriter's own CharacterWidthToPixels (duplicated here rather than
    // sharing a helper across an internal split in the same project's view-write/view-read halves).
    private static double SplitCharacterWidthToPixels(double width)
    {
        if (!double.IsFinite(width) || width <= 0)
            return 0;

        return width < 1
            ? Math.Round(width * 12.0, MidpointRounding.AwayFromZero)
            : Math.Round(width * 7.0 + 5.0, MidpointRounding.AwayFromZero);
    }

    private static XlsxLoadPhaseDiagnostics MeasureLoadPhase(Action action)
    {
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        return new XlsxLoadPhaseDiagnostics(
            stopwatch.Elapsed.TotalMilliseconds,
            GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore);
    }

    private static (T Result, XlsxLoadPhaseDiagnostics Diagnostics) MeasureLoadPhase<T>(Func<T> action)
    {
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        var result = action();
        stopwatch.Stop();
        return (
            result,
            new XlsxLoadPhaseDiagnostics(
                stopwatch.Elapsed.TotalMilliseconds,
                GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore));
    }

    private static void AddStyleOnlyRun(
        ref List<StyleOnlyRun>? runs,
        uint row,
        uint col,
        StyleId styleId)
    {
        runs ??= [];
        if (runs.Count > 0)
        {
            var last = runs[^1];
            if (last.Row == row &&
                last.EndCol != uint.MaxValue &&
                last.EndCol + 1 == col &&
                last.StyleId == styleId)
            {
                runs[^1] = last with { EndCol = col };
                return;
            }
        }

        runs.Add(new StyleOnlyRun(row, col, col, styleId));
    }

    private static Dictionary<(uint Row, uint Col), int>? BuildCellStyleIndexLookup(
        IReadOnlyList<(uint Row, uint Col, int StyleIndex)>? styles,
        Dictionary<(uint Row, uint Col), int>? reuseInstance = null)
    {
        if (styles is not { Count: > 0 })
        {
            reuseInstance?.Clear();
            return null;
        }

        Dictionary<(uint Row, uint Col), int> lookup;
        if (reuseInstance is not null)
        {
            reuseInstance.Clear();
            reuseInstance.EnsureCapacity(styles.Count);
            lookup = reuseInstance;
        }
        else
        {
            lookup = new Dictionary<(uint Row, uint Col), int>(styles.Count);
        }

        foreach (var (row, col, styleIndex) in styles)
            lookup[(row, col)] = styleIndex;

        return lookup;
    }

    private static IReadOnlySet<string>? GetClosedXmlStyleOnlyWorksheetPathsToStrip(
        IReadOnlyDictionary<string, SheetXmlLayout> sheetXmlLayout,
        bool sheetXmlLayoutHadWarnings)
    {
        if (sheetXmlLayoutHadWarnings || sheetXmlLayout.Count == 0)
            return null;

        var explicitStyleOnlyCellCount = 0;
        HashSet<string>? worksheetPathsToStrip = null;
        foreach (var layout in sheetXmlLayout.Values)
        {
            if (layout.HasStyleOnlyCells && layout.ExplicitStyleOnlyCells.Count == 0)
                return null;

            explicitStyleOnlyCellCount += layout.ExplicitStyleOnlyCells.Count;
            if (layout.HasDuplicateStyleOnlyCellStyleIndexes)
            {
                worksheetPathsToStrip ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                worksheetPathsToStrip.Add(layout.WorksheetPath);
            }
        }

        if (explicitStyleOnlyCellCount <= ClosedXmlStyleOnlyStripCellThreshold)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return worksheetPathsToStrip ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static XlsxClosedXmlLoadSanitizationHints CreateClosedXmlLoadSanitizationHints(
        XlsxLoadPackageParts packageParts,
        IReadOnlyDictionary<string, SheetXmlLayout> sheetXmlLayout,
        bool sheetXmlLayoutHadWarnings)
    {
        bool? hasConditionalFormattingBlocks = null;
        bool? hasClosedXmlUnsupportedConditionalFormatting = null;
        bool? hasWorksheetDynamicFilters = null;
        bool? hasWorksheetRelationshipMarkerSchemaIssues = null;
        bool? hasWorksheetPageLayoutSchemaIssues = null;
        bool? hasWorksheetPageBreakSchemaIssues = null;
        bool? hasWorksheetAutoFilterSchemaIssues = null;
        bool? hasWorksheetSheetViewSchemaIssues = null;
        bool? hasWorksheetNativeMetadataSchemaIssues = null;
        IReadOnlySet<string>? mergeCellWorksheetPathsToStrip = null;
        if (!sheetXmlLayoutHadWarnings && sheetXmlLayout.Count > 0)
        {
            hasConditionalFormattingBlocks = false;
            hasClosedXmlUnsupportedConditionalFormatting = false;
            hasWorksheetDynamicFilters = false;
            hasWorksheetRelationshipMarkerSchemaIssues = false;
            hasWorksheetPageLayoutSchemaIssues = false;
            hasWorksheetPageBreakSchemaIssues = false;
            hasWorksheetAutoFilterSchemaIssues = false;
            hasWorksheetSheetViewSchemaIssues = false;
            hasWorksheetNativeMetadataSchemaIssues = false;
            var mergeCellWorksheetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var layout in sheetXmlLayout.Values)
            {
                hasConditionalFormattingBlocks |= layout.HasConditionalFormattingBlocks;
                hasClosedXmlUnsupportedConditionalFormatting |= layout.HasClosedXmlUnsupportedConditionalFormatting;
                hasWorksheetDynamicFilters |= layout.HasWorksheetDynamicFilters;
                hasWorksheetRelationshipMarkerSchemaIssues |= layout.HasWorksheetRelationshipMarkerSchemaIssues;
                hasWorksheetPageLayoutSchemaIssues |= layout.HasWorksheetPageLayoutSchemaIssues;
                hasWorksheetPageBreakSchemaIssues |= layout.HasWorksheetPageBreakSchemaIssues;
                hasWorksheetAutoFilterSchemaIssues |= layout.HasWorksheetAutoFilterSchemaIssues;
                hasWorksheetSheetViewSchemaIssues |= layout.HasWorksheetSheetViewSchemaIssues;
                hasWorksheetNativeMetadataSchemaIssues |= layout.HasWorksheetNativeMetadataSchemaIssues;
                if (layout.MergedRegions.Count > 0)
                    mergeCellWorksheetPaths.Add(layout.WorksheetPath);
            }

            mergeCellWorksheetPathsToStrip = mergeCellWorksheetPaths;
        }

        return new XlsxClosedXmlLoadSanitizationHints(
            packageParts.HasInspected ? packageParts.HasPivotPackageParts : null,
            packageParts.HasInspected ? packageParts.HasChartExChartParts : null,
            !sheetXmlLayoutHadWarnings && packageParts.HasInspected ? packageParts.HasDrawingPackageParts : null,
            hasConditionalFormattingBlocks,
            hasClosedXmlUnsupportedConditionalFormatting,
            hasWorksheetDynamicFilters,
            null,                                                                              // HasWorksheetGridXmlSchemaIssues: left null; check depends on sheetData cells
            hasWorksheetPageLayoutSchemaIssues,
            hasWorksheetPageBreakSchemaIssues,
            hasWorksheetAutoFilterSchemaIssues,
            null,
            null,
            null,
            null,
            null,
            hasWorksheetSheetViewSchemaIssues,
            null,
            null,
            null,
            null,
            null,
            packageParts.HasInspected ? packageParts.HasWorkbookWebPublishingSchemaIssues : null,
            packageParts.HasInspected ? packageParts.HasWorkbookSmartTagSchemaIssues : null,
            packageParts.HasInspected ? packageParts.HasWorkbookNativeMetadataSchemaIssues : null,
            hasWorksheetRelationshipMarkerSchemaIssues,
            hasWorksheetNativeMetadataSchemaIssues,
            mergeCellWorksheetPathsToStrip,
            packageParts.HasInspected ? packageParts.HasCalculationChainPackagePart : null);
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

    // ClosedXML only surfaces worksheets, so chartsheets (full-page chart-only sheets) are inserted
    // here from the raw package. Each chartsheet is modeled as a Sheet with Kind = Chartsheet that
    // carries its single full-page chart, placed at its original position in the workbook's sheet
    // tab order.
    private static void InsertChartsheets(
        Workbook workbook,
        IReadOnlyList<XlsxChartsheet> chartsheets,
        List<string> warnings)
    {
        if (chartsheets.Count == 0)
            return;

        // Resolver covers every already-loaded worksheet so chart series that reference data on
        // another sheet (e.g. "Sheet1!$A$2:$A$5") resolve to the correct SheetId.
        var sheetNameResolver = workbook.Sheets
            .ToDictionary(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var chartsheet in chartsheets)
        {
            try
            {
                var index = Math.Clamp(chartsheet.WorkbookSheetIndex, 0, workbook.Sheets.Count);
                var sheet = workbook.InsertSheet(index, chartsheet.Name);
                sheet.Kind = SheetKind.Chartsheet;
                sheet.IsHidden = chartsheet.IsHidden;
                sheet.IsVeryHidden = chartsheet.IsVeryHidden;

                if (chartsheet.ChartPart is { } chartPart &&
                    XlsxChartPartReader.TryReadSupportedChart(
                        chartPart.Xml, sheet.Id, fallbackDataRange: null, sheetNameResolver, out var chart))
                {
                    chart.Name = chartPart.Name;
                    chart.AltTextTitle = chartPart.Title;
                    chart.AltTextDescription = chartPart.AltText;
                    // R98-io-chart-hyperlink-model-field: see the matching comment in
                    // XlsxFileAdapter.LoadSheetXmlLayoutApplication.cs -- a chartsheet's single chart
                    // needs its object-level hyperlink on the model too.
                    chart.Hyperlink = chartPart.Hyperlink;
                    XlsxDrawingAnchorApplier.ApplyToChart(chart, chartPart.Anchor, sheet);
                    ApplyChartExternalDataRelationshipMetadata(chart, chartPart);
                    ApplyChartUserShapesRelationshipMetadata(chart, chartPart);
                    sheet.Charts.Add(chart);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"[chartsheet] Sheet '{chartsheet.Name}': {ex.Message}");
            }
        }
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
        WorkbookIndexedColorPalette indexedColors,
        Dictionary<object, StyleId?> styleIdsByStyleValue,
        XlsxCellBorderStyleTable cellBorderStyles,
        XlsxCellGradientFillTable cellGradientFills,
        int? xlsxStyleIndex,
        Dictionary<int, StyleId?> styleIdsByNativeBorderStyleIndex,
        Dictionary<int, StyleId?> styleIdsByNativeGradientStyleIndex)
    {
        if (xlsxStyleIndex is { } styleIndex)
        {
            bool hasBorder   = cellBorderStyles.TryGetVisibleBorders(styleIndex, out _);
            bool hasGradient = cellGradientFills.TryGet(styleIndex, out _);
            if (hasBorder || hasGradient)
            {
                // Borders and gradients use separate caches so we pick the innermost hit.
                // When both are present on the same xf, the gradient cache wins (it contains border too).
                if (hasGradient)
                {
                    if (styleIdsByNativeGradientStyleIndex.TryGetValue(styleIndex, out var cachedGradId))
                        return cachedGradId;
                    var gradStyle = MapStyleWithNativeFills(xlCell.Style, theme, indexedColors, cellBorderStyles, cellGradientFills, styleIndex);
                    StyleId? gradStyleId = gradStyle.Equals(CellStyle.Default) ? null : workbook.RegisterStyle(gradStyle);
                    styleIdsByNativeGradientStyleIndex[styleIndex] = gradStyleId;
                    return gradStyleId;
                }

                if (styleIdsByNativeBorderStyleIndex.TryGetValue(styleIndex, out var cachedNativeStyleId))
                    return cachedNativeStyleId;
                var nativeStyle = MapStyleWithNativeFills(xlCell.Style, theme, indexedColors, cellBorderStyles, cellGradientFills, styleIndex);
                StyleId? nativeStyleId = nativeStyle.Equals(CellStyle.Default) ? null : workbook.RegisterStyle(nativeStyle);
                styleIdsByNativeBorderStyleIndex[styleIndex] = nativeStyleId;
                return nativeStyleId;
            }
        }

        var styleValue = XlCellStyleValueAccessor is not null
            ? XlCellStyleValueAccessor(xlCell)
            : null;
        if (styleValue is not null && styleIdsByStyleValue.TryGetValue(styleValue, out var cachedStyleId))
            return cachedStyleId;

        var style = XlsxClosedXmlCellMapper.MapStyle(xlCell.Style, theme, indexedColors);
        StyleId? styleId = style.Equals(CellStyle.Default)
            ? null
            : workbook.RegisterStyle(style);
        if (styleValue is not null)
            styleIdsByStyleValue[styleValue] = styleId;
        return styleId;
    }

    private static CellStyle MapStyleWithNativeFills(
        IXLStyle xlStyle,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors,
        XlsxCellBorderStyleTable cellBorderStyles,
        XlsxCellGradientFillTable cellGradientFills,
        int? xlsxStyleIndex)
    {
        var style = XlsxClosedXmlCellMapper.MapStyle(xlStyle, theme, indexedColors);
        if (xlsxStyleIndex is { } styleIndex)
        {
            if (cellBorderStyles.TryGetVisibleBorders(styleIndex, out var nativeBorders))
                nativeBorders.ApplyTo(style);
            if (cellGradientFills.TryGet(styleIndex, out var gradient))
            {
                style.GradientFill = gradient;
                // A gradient fill cell has no solid FillColor from ClosedXML — clear any spurious
                // default solid fill that ClosedXML assigned (it typically assigns a NoFill xf as solid).
                style.FillColor = null;
                style.FillPatternStyle = CellFillPatternStyle.None;
            }
        }

        return style;
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

    // Compiled delegate that calls XLStylizedBase.SetStyle(XLStyleValue, propagate: false) on a
    // cell without going through the property-setter machinery.  Used by the per-save ClosedXML
    // style cache to replay a fully-built XLStyleValue in one call instead of ~15 individual
    // setter calls per styled cell.
    private static readonly Action<IXLCell, object>? XlCellSetStyleValueAction = CreateXlCellSetStyleValueAction();

    // Internal probe for CI: if a ClosedXML package bump renames/removes the SetStyle method, this
    // returns false and the test XlsxFileAdapterClosedXmlReflectionTests.ClosedXmlSetStyleDelegate_ResolvesSuccessfully
    // fails loudly instead of silently degrading to the slow per-property path.
    internal static bool ClosedXmlSetStyleDelegateResolved => XlCellSetStyleValueAction is not null;

    private static Action<IXLCell, object>? CreateXlCellSetStyleValueAction()
    {
        // XLStylizedBase.SetStyle(XLStyleValue value, bool propagate) is the single-call path
        // that applies a complete immutable style key to a cell.
        var assembly = typeof(XLWorkbook).Assembly;
        var xlCellType = assembly.GetType("ClosedXML.Excel.XLCell");
        var xlStyleValueType = assembly.GetType("ClosedXML.Excel.XLStyleValue");
        if (xlCellType is null || xlStyleValueType is null)
            return null;

        var setStyleMethod = xlCellType.GetMethod(
            "SetStyle",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [xlStyleValueType, typeof(bool)],
            modifiers: null);
        if (setStyleMethod is null)
        {
            // Also try base type XLStylizedBase
            var baseType = xlCellType.BaseType;
            while (baseType is not null && setStyleMethod is null)
            {
                setStyleMethod = baseType.GetMethod(
                    "SetStyle",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: [xlStyleValueType, typeof(bool)],
                    modifiers: null);
                baseType = baseType.BaseType;
            }
        }

        if (setStyleMethod is null)
            return null;

        // Compile: (IXLCell cell, object boxedStyleValue) =>
        //     ((XLCell)cell).SetStyle((XLStyleValue)boxedStyleValue, false)
        var cellParam = Expression.Parameter(typeof(IXLCell), "cell");
        var valueParam = Expression.Parameter(typeof(object), "boxedStyleValue");
        var callExpr = Expression.Call(
            Expression.Convert(cellParam, xlCellType),
            setStyleMethod,
            Expression.Convert(valueParam, xlStyleValueType),
            Expression.Constant(false));
        return Expression.Lambda<Action<IXLCell, object>>(callExpr, cellParam, valueParam).Compile();
    }

    private readonly struct XlsxLoadPackageParts
    {
        private XlsxLoadPackageParts(
            bool hasInspected,
            bool hasWorkbook,
            bool hasStyles,
            bool hasTheme,
            bool hasPivotPackageParts,
            bool? hasChartExChartParts,
            bool hasDrawingPackageParts,
            bool hasSlicerTimelinePackageParts,
            bool hasExternalLinks,
            bool hasStructuredTables,
            bool hasVbaProjectPackage,
            bool hasCalculationChainPackagePart,
            bool? hasWorkbookWebPublishingSchemaIssues,
            bool? hasWorkbookSmartTagSchemaIssues,
            bool? hasWorkbookNativeMetadataSchemaIssues)
        {
            HasInspected = hasInspected;
            HasWorkbook = hasWorkbook;
            HasStyles = hasStyles;
            HasTheme = hasTheme;
            HasPivotPackageParts = hasPivotPackageParts;
            HasChartExChartParts = hasChartExChartParts;
            HasDrawingPackageParts = hasDrawingPackageParts;
            HasSlicerTimelinePackageParts = hasSlicerTimelinePackageParts;
            HasExternalLinks = hasExternalLinks;
            HasStructuredTables = hasStructuredTables;
            HasVbaProjectPackage = hasVbaProjectPackage;
            HasCalculationChainPackagePart = hasCalculationChainPackagePart;
            HasWorkbookWebPublishingSchemaIssues = hasWorkbookWebPublishingSchemaIssues;
            HasWorkbookSmartTagSchemaIssues = hasWorkbookSmartTagSchemaIssues;
            HasWorkbookNativeMetadataSchemaIssues = hasWorkbookNativeMetadataSchemaIssues;
        }

        public static XlsxLoadPackageParts Empty => default;
        public bool HasInspected { get; }
        public bool HasWorkbook { get; }
        public bool HasStyles { get; }
        public bool HasTheme { get; }
        public bool HasPivotPackageParts { get; }
        public bool? HasChartExChartParts { get; }
        public bool HasDrawingPackageParts { get; }
        public bool HasSlicerTimelinePackageParts { get; }
        public bool HasExternalLinks { get; }
        public bool HasStructuredTables { get; }
        public bool HasVbaProjectPackage { get; }
        public bool HasCalculationChainPackagePart { get; }
        public bool? HasWorkbookWebPublishingSchemaIssues { get; }
        public bool? HasWorkbookSmartTagSchemaIssues { get; }
        public bool? HasWorkbookNativeMetadataSchemaIssues { get; }

        public static XlsxLoadPackageParts Inspect(ZipArchive archive)
        {
            var hasWorkbook = false;
            var hasStyles = false;
            var hasTheme = false;
            var hasPivotPackageParts = false;
            var hasDrawingPackageParts = false;
            var hasSlicerTimelinePackageParts = false;
            var hasExternalLinks = false;
            var hasStructuredTables = false;
            var hasVbaProjectPackage = false;

            foreach (var entry in archive.Entries)
            {
                var path = entry.FullName;
                hasWorkbook |= EntryPathEquals(path, "xl/workbook.xml");
                hasStyles |= EntryPathEquals(path, "xl/styles.xml");
                hasTheme |= EntryPathEquals(path, "xl/theme/theme1.xml");
                hasPivotPackageParts |=
                    EntryPathStartsWith(path, "xl/pivotCache/") ||
                    EntryPathStartsWith(path, "xl/pivotTables/");
                hasDrawingPackageParts |=
                    EntryPathStartsWith(path, "xl/drawings/drawing") ||
                    EntryPathStartsWith(path, "xl/drawings/_rels/drawing") ||
                    EntryPathStartsWith(path, "xl/charts/");
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
                    hasDrawingPackageParts &&
                    hasSlicerTimelinePackageParts &&
                    hasExternalLinks &&
                    hasStructuredTables)
                {
                    break;
                }
            }
            hasVbaProjectPackage = archive.GetEntry("xl/vbaProject.bin") is not null;

            // Parse workbook.xml once and share the XDocument across all three schema-issue inspectors.
            XDocument? sharedWorkbookXml = null;
            bool? sharedWorkbookXmlMissing = null;
            bool? sharedWorkbookXmlCorrupt = null;
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
            {
                sharedWorkbookXmlMissing = true;
            }
            else
            {
                try
                {
                    sharedWorkbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
                }
                catch
                {
                    sharedWorkbookXmlCorrupt = true;
                }
            }

            return new XlsxLoadPackageParts(
                hasInspected: true,
                hasWorkbook,
                hasStyles,
                hasTheme,
                hasPivotPackageParts,
                InspectChartExChartParts(archive),
                hasDrawingPackageParts,
                hasSlicerTimelinePackageParts,
                hasExternalLinks,
                hasStructuredTables,
                hasVbaProjectPackage,
                archive.GetEntry("xl/calcChain.xml") is not null,
                InspectWorkbookWebPublishingSchemaIssues(sharedWorkbookXml, sharedWorkbookXmlMissing, sharedWorkbookXmlCorrupt),
                InspectWorkbookSmartTagSchemaIssues(sharedWorkbookXml, sharedWorkbookXmlMissing, sharedWorkbookXmlCorrupt),
                InspectWorkbookNativeMetadataSchemaIssues(sharedWorkbookXml, sharedWorkbookXmlMissing, sharedWorkbookXmlCorrupt));
        }

        private static bool? InspectWorkbookWebPublishingSchemaIssues(
            XDocument? workbookXml,
            bool? isMissing,
            bool? isCorrupt)
        {
            if (isMissing == true)
                return false;
            if (isCorrupt == true)
                return null;

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var root = workbookXml?.Root;
            return root is not null &&
                   (XlsxWorkbookWebPublishingNormalizer.NormalizeWorkbookRoot(root, workbookNs) |
                    XlsxWorkbookWebPublishObjectsNormalizer.NormalizeWorkbookRoot(root, workbookNs));
        }

        private static bool? InspectWorkbookSmartTagSchemaIssues(
            XDocument? workbookXml,
            bool? isMissing,
            bool? isCorrupt)
        {
            if (isMissing == true)
                return false;
            if (isCorrupt == true)
                return null;

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var root = workbookXml?.Root;
            if (root is null)
                return false;

            var changed = false;
            if (root.Element(workbookNs + "smartTagPr") is { } smartTagPr)
                changed |= XlsxWorkbookSmartTagNormalizer.NormalizeSmartTagPropertiesElement(smartTagPr);
            if (root.Element(workbookNs + "smartTagTypes") is { } smartTagTypes)
            {
                changed |= XlsxWorkbookSmartTagNormalizer.NormalizeSmartTagTypesElement(smartTagTypes);
                changed |= XlsxWorkbookSmartTagNormalizer.ShouldRemoveSmartTagTypesElement(smartTagTypes);
            }

            return changed;
        }

        private static bool? InspectWorkbookNativeMetadataSchemaIssues(
            XDocument? workbookXml,
            bool? isMissing,
            bool? isCorrupt)
        {
            if (isMissing == true)
                return false;
            if (isCorrupt == true)
                return null;

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var root = workbookXml?.Root;
            if (root is null)
                return false;

            var changed = false;
            if (root.Element(workbookNs + "workbookPr") is { } workbookPr)
                changed |= XlsxWorkbookPropertiesNormalizer.NormalizeElement(workbookPr);
            foreach (var customWorkbookViews in root.Elements(workbookNs + "customWorkbookViews").ToList())
            {
                changed |= XlsxWorkbookCustomViewNormalizer.NormalizeCustomWorkbookViewsElement(customWorkbookViews);
                changed |= XlsxWorkbookCustomViewNormalizer.ShouldRemoveCustomWorkbookViewsElement(customWorkbookViews);
            }
            changed |= XlsxWorkbookExternalReferencesNormalizer.NormalizeWorkbookRoot(root, workbookNs);
            foreach (var definedNames in root.Elements(workbookNs + "definedNames").ToList())
            {
                changed |= XlsxWorkbookDefinedNameNormalizer.NormalizeDefinedNamesElement(definedNames);
                changed |= XlsxWorkbookDefinedNameNormalizer.ShouldRemoveDefinedNamesElement(definedNames);
            }
            changed |= XlsxWorkbookOleSizeNormalizer.NormalizeWorkbookRoot(root, workbookNs);
            changed |= XlsxWorkbookPivotCachesNormalizer.NormalizeWorkbookRoot(root, workbookNs);
            changed |= XlsxWorkbookExtensionListNormalizer.NormalizeWorkbookRoot(root, workbookNs);
            if (root.Element(workbookNs + "fileVersion") is { } fileVersion)
                changed |= XlsxWorkbookFileVersionNormalizer.NormalizeElement(fileVersion);
            if (root.Element(workbookNs + "functionGroups") is { } functionGroups)
                changed |= XlsxWorkbookFunctionGroupsNormalizer.NormalizeElement(functionGroups);

            return changed;
        }

        private static bool? InspectChartExChartParts(ZipArchive archive)
        {
            const string chartExContentType = "application/vnd.ms-office.chartex+xml";
            XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
            if (contentTypesEntry is null)
                return null;

            try
            {
                var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
                return contentTypesXml.Root?
                    .Elements(contentTypesNs + "Override")
                    .Any(element => string.Equals(
                        element.Attribute("ContentType")?.Value,
                        chartExContentType,
                        StringComparison.OrdinalIgnoreCase)) == true;
            }
            catch
            {
                return null;
            }
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
                        $"The file exceeds the {WorkbookOpenSizeGuard.FormatBytes(maxFileBytes)} open limit."));
            }

            destination.Write(buffer, 0, read);
        }
    }

    // OLE/CFB compound-file signature ("Encrypt with Password" wraps the real OOXML zip in an
    // EncryptedPackage stream inside an OLE compound file). ZipArchive can't open this at all, so
    // without an explicit check here the user only ever sees a low-level zip/ClosedXML format
    // exception, never the actual reason ("this workbook is password protected").
    private static readonly byte[] CompoundFileBinarySignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static readonly XNamespace FirstPageNumberWorksheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // Reads, per worksheet part path, whether the source XML's <pageSetup useFirstPageNumber="..."/>
    // attribute is truthy. ClosedXML's IXLPageSetup surface has no property for this flag (it only
    // exposes the raw FirstPageNumber value), so it has to be recovered directly from the package XML.
    // Best-effort: any entry that fails to parse or has no pageSetup element is simply omitted, and
    // callers treat a missing entry as "unknown" rather than "disabled".
    private static Dictionary<string, bool> ReadWorksheetFirstPageNumberEnabledFlags(ZipArchive archive)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry))
        {
            XDocument xml;
            try
            {
                xml = XlsxPackageXmlEditor.LoadXml(entry);
            }
            catch (Exception)
            {
                continue;
            }

            if (xml.Root?.Element(FirstPageNumberWorksheetNamespace + "pageSetup") is not { } pageSetup)
                continue;

            result[XlsxPackagePath.NormalizeEntryPath(entry)] =
                XlsxWorksheetXmlValueParser.IsTruthy(pageSetup.Attribute("useFirstPageNumber")?.Value);
        }

        return result;
    }

    private static readonly XNamespace HyperlinkRelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // R55-io-hyperlink-round-trip-5-2: reads, per worksheet part path, the raw "location" attribute
    // of every <hyperlink> element that ALSO carries an r:id (an external relationship) -- Excel's
    // "Existing File > Bookmark..." feature writes both together (r:id + location="Sheet2!A5") to
    // jump to a specific sheet/cell inside the linked external workbook. ClosedXML's
    // XLHyperlink.InternalAddress getter only ever populates for a purely INTERNAL hyperlink, so it
    // comes back null/empty for this external+location combination and the load loop below would
    // otherwise silently drop the sub-address. Best-effort: any entry that fails to parse, or has no
    // hyperlinks element, is simply omitted.
    private static Dictionary<string, Dictionary<string, string>> ReadWorksheetExternalHyperlinkLocations(
        ZipArchive archive)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry))
        {
            XDocument xml;
            try
            {
                xml = XlsxPackageXmlEditor.LoadXml(entry);
            }
            catch (Exception)
            {
                continue;
            }

            if (xml.Root is not { } root)
                continue;

            var ns = root.Name.Namespace;
            var hyperlinksElement = root.Element(ns + "hyperlinks");
            if (hyperlinksElement is null)
                continue;

            Dictionary<string, string>? byRef = null;
            foreach (var hyperlinkElement in hyperlinksElement.Elements(ns + "hyperlink"))
            {
                var reference = hyperlinkElement.Attribute("ref")?.Value;
                var location = hyperlinkElement.Attribute("location")?.Value;
                var relationshipId = hyperlinkElement.Attribute(HyperlinkRelationshipNamespace + "id")?.Value;
                if (string.IsNullOrEmpty(reference) ||
                    string.IsNullOrWhiteSpace(location) ||
                    string.IsNullOrEmpty(relationshipId))
                {
                    continue;
                }

                byRef ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                // R64-io-hyperlink-6-2: a hyperlink's "ref" may span multiple cells (e.g. "A1:B1"), but
                // the per-cell recovery lookup below keys by a single cell's A1 address. Expand the
                // range so every cell it covers recovers the same shared bookmark/location.
                foreach (var cellKey in ExpandHyperlinkReferenceToCellKeys(reference))
                {
                    byRef[cellKey] = location;
                }
            }

            if (byRef is not null)
                result[XlsxPackagePath.NormalizeEntryPath(entry)] = byRef;
        }

        return result;
    }

    // R106-io-hyperlink-range-shift: reads, per worksheet part path, every hyperlink "ref" that is
    // a whole-column ("C:C"), whole-row ("3:3"), or oversized bounded range (over
    // MaxExpandableHyperlinkRangeCellCount cells below) -- the exact same refs
    // XlsxWorksheetHyperlinkNormalizer.StripRangeHyperlinkRefs removes from the ClosedXML-input copy
    // before load, so ClosedXML's own xlSheet.Hyperlinks collection (read in the main load loop
    // above) never contains them at all. Keyed by the ORIGINAL ref string -- the identity
    // Sheet.RangeHyperlinks uses to track a live, shift-adjusted GridRange for each one, and the
    // same identity XlsxWorksheetMetadataPreserver.MergeWorksheetHyperlinkMetadata re-correlates
    // against at save time. Best-effort: any worksheet entry that fails to parse is simply omitted.
    private static Dictionary<string, Dictionary<string, GridRange>> ReadWorksheetRangeHyperlinks(
        ZipArchive archive)
    {
        var result = new Dictionary<string, Dictionary<string, GridRange>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry))
        {
            XDocument xml;
            try
            {
                xml = XlsxPackageXmlEditor.LoadXml(entry);
            }
            catch (Exception)
            {
                continue;
            }

            if (xml.Root is not { } root)
                continue;

            var ns = root.Name.Namespace;
            var hyperlinksElement = root.Element(ns + "hyperlinks");
            if (hyperlinksElement is null)
                continue;

            Dictionary<string, GridRange>? byRef = null;
            foreach (var hyperlinkElement in hyperlinksElement.Elements(ns + "hyperlink"))
            {
                var reference = hyperlinkElement.Attribute("ref")?.Value;
                if (string.IsNullOrWhiteSpace(reference))
                    continue;

                if (TryParseRangeHyperlinkGridRange(reference, out var range))
                    (byRef ??= new Dictionary<string, GridRange>(StringComparer.Ordinal))[reference] = range;
            }

            if (byRef is not null)
                result[XlsxPackagePath.NormalizeEntryPath(entry)] = byRef;
        }

        return result;
    }

    /// <summary>
    /// Mirrors <see cref="XlsxWorksheetHyperlinkNormalizer"/>'s strip criteria exactly (whole-
    /// column/row, or a bounded range above <see cref="MaxExpandableHyperlinkRangeCellCount"/>).
    /// Returns the equivalent <see cref="GridRange"/> -- a whole-column ref spans the full row
    /// extent, a whole-row ref the full column extent -- so RowColumnShiftHelpers.ShiftRange*Up/Down
    /// (which already special-case a whole-column/row GridRange as a no-op on the perpendicular
    /// axis) apply unchanged. A plain bounded ref within the cap returns <see langword="false"/>: it
    /// is representable via the ordinary CellAddress-keyed Sheet.Hyperlinks/HyperlinkMetadata path
    /// and must not also enter Sheet.RangeHyperlinks.
    /// </summary>
    private static bool TryParseRangeHyperlinkGridRange(string reference, out GridRange range)
    {
        range = default;
        var trimmed = reference.Trim();
        if (trimmed.Length == 0 || trimmed.Contains(' ', StringComparison.Ordinal))
            return false;

        var parts = trimmed.Split(':');
        if (parts.Length != 2)
            return false;

        var sheet = SheetId.New();
        if (TryParseWholeColumnOrRowRangeRef(parts[0], parts[1], sheet, out range))
            return true;

        if (!CellAddress.TryParse(parts[0], sheet, out var start) ||
            !CellAddress.TryParse(parts[1], sheet, out var end))
        {
            return false;
        }

        var bounded = new GridRange(start, end);
        if (bounded.CellCount <= MaxExpandableHyperlinkRangeCellCount)
            return false;

        range = bounded;
        return true;
    }

    private static bool TryParseWholeColumnOrRowRangeRef(string left, string right, SheetId sheet, out GridRange range)
    {
        range = default;
        if (left.Length == 0 || right.Length == 0)
            return false;

        if (left.All(char.IsAsciiLetter) && right.All(char.IsAsciiLetter))
        {
            // Whole-column: "C:C" / "B:D" -- both sides are bare column letters. Anchor each to
            // row 1 purely to reuse CellAddress.TryParse's column-letter parsing.
            if (!CellAddress.TryParse(left + "1", sheet, out var startCol) ||
                !CellAddress.TryParse(right + "1", sheet, out var endCol))
            {
                return false;
            }

            range = new GridRange(
                new CellAddress(sheet, 1, startCol.Col),
                new CellAddress(sheet, CellAddress.MaxRow, endCol.Col));
            return true;
        }

        if (left.All(char.IsAsciiDigit) && right.All(char.IsAsciiDigit))
        {
            // Whole-row: "3:3" / "2:5" -- both sides are bare row numbers.
            if (!uint.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var startRow) ||
                !uint.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var endRow) ||
                startRow == 0 || endRow == 0)
            {
                return false;
            }

            range = new GridRange(
                new CellAddress(sheet, startRow, 1),
                new CellAddress(sheet, endRow, CellAddress.MaxCol));
            return true;
        }

        return false;
    }

    // R98-io-hyperlink-oversized-ref: mirrors XlsxWorksheetHyperlinkNormalizer's
    // MaxBoundedHyperlinkRangeCellCount cap. A bounded (non whole-column/row) "ref" such as
    // "A1:XFD1048576" parses fine via CellAddress.TryParse and would otherwise drive the
    // expansion loop below through ~17 billion iterations -- a crafted worksheet XML of only a
    // few hundred bytes can trigger this OOM/hang. Any range above this cap is treated the same
    // way the normalizer treats it (dropped) instead of materialized cell-by-cell.
    private const long MaxExpandableHyperlinkRangeCellCount = 100_000;

    /// <summary>
    /// Expands a hyperlink "ref" attribute -- either a single cell ("A1") or a rectangular range
    /// ("A1:B1") -- into its individual A1-notation cell keys. Falls back to yielding the raw
    /// reference unchanged if it cannot be parsed as either form. Bounded ranges larger than
    /// <see cref="MaxExpandableHyperlinkRangeCellCount"/> yield nothing (see remarks above).
    /// </summary>
    private static IEnumerable<string> ExpandHyperlinkReferenceToCellKeys(string reference)
    {
        var colonIndex = reference.IndexOf(':');
        if (colonIndex < 0)
        {
            yield return reference;
            yield break;
        }

        if (!CellAddress.TryParse(reference[..colonIndex], default, out var start) ||
            !CellAddress.TryParse(reference[(colonIndex + 1)..], default, out var end))
        {
            yield return reference;
            yield break;
        }

        var minRow = Math.Min(start.Row, end.Row);
        var maxRow = Math.Max(start.Row, end.Row);
        var minCol = Math.Min(start.Col, end.Col);
        var maxCol = Math.Max(start.Col, end.Col);

        var cellCount = (long)(maxRow - minRow + 1) * (maxCol - minCol + 1);
        if (cellCount > MaxExpandableHyperlinkRangeCellCount)
        {
            yield break;
        }

        for (var row = minRow; row <= maxRow; row++)
        {
            for (var col = minCol; col <= maxCol; col++)
            {
                yield return new CellAddress(default, row, col).ToA1();
            }
        }
    }

    private static void ThrowIfPasswordEncrypted(MemoryStream packageStream)
    {
        if (!packageStream.TryGetBuffer(out var buffer) || buffer.Count < CompoundFileBinarySignature.Length)
            return;

        for (var i = 0; i < CompoundFileBinarySignature.Length; i++)
        {
            if (buffer.Array![buffer.Offset + i] != CompoundFileBinarySignature[i])
                return;
        }

        // The 8-byte OLE/CFB header is shared by EVERY compound-file document -- a real
        // "Encrypt with Password" OOXML wrapper, but also a genuinely unencrypted legacy
        // .xls/.xlt/.xlb workbook (or any other compound-file document) that merely ended up with a
        // .xlsx extension (a mail gateway/export tool normalizing extensions, or a user renaming the
        // file). Disambiguate by inspecting the compound file's own directory: MS-OFFCRYPTO's
        // "Encrypt with Password" wrapper is the only shape that carries BOTH an "EncryptedPackage"
        // stream (the encrypted OOXML zip payload) and an "EncryptionInfo" stream (the key/algorithm
        // metadata) at the root. Anything else that merely shares the header -- including a real,
        // unencrypted legacy binary workbook that FreeX's own LegacyXlsFileAdapter could open fine --
        // must not be misreported as password-protected (see R86-services-file-format-detect-5-2).
        //
        // If the buffer isn't even a well-formed CFB structure (e.g. just the signature padded with
        // zeros, as in a corrupt/truncated download), POIFSFileSystem itself throws while parsing the
        // header -- fall back to the original conservative behavior in that case, since a signature
        // match with no parseable structure at all still looks the most like a mishandled encrypted
        // package.
        try
        {
            using var cfbStream = new MemoryStream(buffer.Array!, buffer.Offset, buffer.Count, writable: false);
            var poifs = new POIFSFileSystem(cfbStream);
            try
            {
                var root = poifs.Root;
                var hasEncryptedPackage = false;
                var hasEncryptionInfo = false;
                foreach (var entry in root)
                {
                    if (string.Equals(entry.Name, "EncryptedPackage", StringComparison.Ordinal))
                        hasEncryptedPackage = true;
                    else if (string.Equals(entry.Name, "EncryptionInfo", StringComparison.Ordinal))
                        hasEncryptionInfo = true;
                }

                if (!hasEncryptedPackage || !hasEncryptionInfo)
                {
                    throw new WorkbookInvalidException(
                        "The workbook could not be read because the file is not a valid .xlsx package (it appears to be a legacy .xls/.xlt binary workbook or another OLE compound-file document saved with a .xlsx extension).");
                }
            }
            finally
            {
                poifs.Close();
            }
        }
        catch (WorkbookInvalidException)
        {
            throw;
        }
        catch (Exception)
        {
            // Not a parseable CFB structure at all -- fall through to the conservative
            // password-protected report below, matching the previous behavior.
        }

        throw new WorkbookPasswordProtectedException(
            "This workbook is password-protected. Remove the password protection in Excel (File > Info > Protect Workbook > Encrypt with Password, then clear the password) and try again.");
    }

    private readonly record struct LoadPackageStream(
        MemoryStream PackageStream,
        bool CanReuseBufferForSnapshot);

    private static ClosedXmlLoadResult OpenClosedXmlWorkbookWithSanitizationFallback(
        MemoryStream packageStream,
        IReadOnlySet<string>? styleOnlyWorksheetPathsToStrip,
        XlsxClosedXmlLoadSanitizationHints sanitizationHints)
    {
        var totalAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var totalStopwatch = Stopwatch.StartNew();
        var packagePreparationDiagnostics = XlsxLoadPhaseDiagnostics.NotRun;
        var workbookOpenDiagnostics = XlsxLoadPhaseDiagnostics.NotRun;

        var closedXmlPackageStream = MeasurePackagePreparation(() => CreateClosedXmlParsePackage(
            packageStream,
            styleOnlyWorksheetPathsToStrip,
            sanitizationHints,
            removeUnsupportedConditionalFormatting: false));
        try
        {
            return Complete(closedXmlPackageStream, MeasureWorkbookOpen(() => new XLWorkbook(closedXmlPackageStream)));
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(closedXmlPackageStream, packageStream))
                closedXmlPackageStream.Dispose();

            if (IsClosedXmlConditionalFormattingLoadFailure(ex))
                return OpenConditionalFormattingStripped();

            if (IsClosedXmlRelationshipLookupFailure(ex))
                return OpenPivotStripped();

            if (IsClosedXmlSharedFormulaReconstructionFailure(ex))
                return OpenOrphanedSharedFormulaSlavesStripped();

            packageStream.Position = 0;
            var fallbackPackageStream = MeasurePackagePreparation(() => CreateClosedXmlParsePackage(
                packageStream,
                styleOnlyWorksheetPathsToStrip,
                sanitizationHints,
                removeUnsupportedConditionalFormatting: true,
                removeAllConditionalFormatting: false));
            try
            {
                return Complete(fallbackPackageStream, MeasureWorkbookOpen(() => new XLWorkbook(fallbackPackageStream)));
            }
            catch
            {
                if (!ReferenceEquals(fallbackPackageStream, packageStream))
                    fallbackPackageStream.Dispose();

                return OpenConditionalFormattingStripped();
            }
        }

        ClosedXmlLoadResult OpenPivotStripped()
        {
            packageStream.Position = 0;
            var pivotStrippedHints = sanitizationHints with { HasPivotPackageMetadata = true };
            var pivotStrippedPackageStream = MeasurePackagePreparation(() => CreateClosedXmlParsePackage(
                packageStream,
                styleOnlyWorksheetPathsToStrip,
                pivotStrippedHints,
                removeUnsupportedConditionalFormatting: false));
            try
            {
                return Complete(
                    pivotStrippedPackageStream,
                    MeasureWorkbookOpen(() => new XLWorkbook(pivotStrippedPackageStream)));
            }
            catch
            {
                if (!ReferenceEquals(pivotStrippedPackageStream, packageStream))
                    pivotStrippedPackageStream.Dispose();
                throw;
            }
        }

        ClosedXmlLoadResult OpenOrphanedSharedFormulaSlavesStripped()
        {
            packageStream.Position = 0;
            var orphanStrippedPackageStream = MeasurePackagePreparation(() =>
            {
                var basePackageStream = CreateClosedXmlParsePackage(
                    packageStream,
                    styleOnlyWorksheetPathsToStrip,
                    sanitizationHints,
                    removeUnsupportedConditionalFormatting: false);
                StripOrphanedSharedFormulaSlaves(basePackageStream);
                return basePackageStream;
            });
            try
            {
                return Complete(
                    orphanStrippedPackageStream,
                    MeasureWorkbookOpen(() => new XLWorkbook(orphanStrippedPackageStream)));
            }
            catch
            {
                if (!ReferenceEquals(orphanStrippedPackageStream, packageStream))
                    orphanStrippedPackageStream.Dispose();
                throw;
            }
        }

        ClosedXmlLoadResult OpenConditionalFormattingStripped()
        {
            packageStream.Position = 0;
            var conditionalFormattingStrippedPackageStream = MeasurePackagePreparation(() => CreateClosedXmlParsePackage(
                packageStream,
                styleOnlyWorksheetPathsToStrip,
                sanitizationHints,
                removeUnsupportedConditionalFormatting: true,
                removeAllConditionalFormatting: true));
            try
            {
                return Complete(
                    conditionalFormattingStrippedPackageStream,
                    MeasureWorkbookOpen(() => new XLWorkbook(conditionalFormattingStrippedPackageStream)));
            }
            catch
            {
                if (!ReferenceEquals(conditionalFormattingStrippedPackageStream, packageStream))
                    conditionalFormattingStrippedPackageStream.Dispose();
                throw;
            }
        }

        T MeasurePackagePreparation<T>(Func<T> action)
        {
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                return action();
            }
            finally
            {
                stopwatch.Stop();
                packagePreparationDiagnostics = AddLoadPhaseDiagnostics(
                    packagePreparationDiagnostics,
                    new XlsxLoadPhaseDiagnostics(
                        stopwatch.Elapsed.TotalMilliseconds,
                        GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore));
            }
        }

        T MeasureWorkbookOpen<T>(Func<T> action)
        {
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                return action();
            }
            finally
            {
                stopwatch.Stop();
                workbookOpenDiagnostics = AddLoadPhaseDiagnostics(
                    workbookOpenDiagnostics,
                    new XlsxLoadPhaseDiagnostics(
                        stopwatch.Elapsed.TotalMilliseconds,
                        GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore));
            }
        }

        ClosedXmlLoadResult Complete(MemoryStream closedXmlPackageStream, XLWorkbook workbook)
        {
            totalStopwatch.Stop();
            return new ClosedXmlLoadResult(
                closedXmlPackageStream,
                workbook,
                new XlsxClosedXmlLoadDiagnostics(
                    new XlsxLoadPhaseDiagnostics(
                        totalStopwatch.Elapsed.TotalMilliseconds,
                        GC.GetTotalAllocatedBytes(precise: true) - totalAllocatedBefore),
                    packagePreparationDiagnostics,
                    workbookOpenDiagnostics));
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

    // ClosedXML uses .First() when resolving part relationships; files authored by LibreOffice
    // (and other non-Excel producers) sometimes emit table or pivot-cache relationships in a
    // layout ClosedXML doesn't match, causing InvalidOperationException with the LINQ sentinel
    // message.  Strip pivot metadata and retry so the rest of the workbook loads cleanly.
    private static bool IsClosedXmlRelationshipLookupFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidOperationException &&
                current.Message.Contains("Sequence contains no matching element", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // A shared-formula slave cell (<c><f t="shared" si="N"/><v>...</v></c>) has no formula text
    // of its own -- it relies on a master cell elsewhere on the same sheet (<f t="shared"
    // ref="..." si="N">...formula text...</f>) to reconstruct its actual formula. If that master
    // is missing (deleted by a buggy writer, dropped by a partial/failed save, or lost in a
    // manual merge -- exactly the corruption shape FreeXR13S10Tests.cs guards against on FreeX's
    // OWN save path) ClosedXML has nothing to reconstruct the slave's R1C1 formula from and
    // throws deep inside its parser (ClosedXML.Parser.ModContext..ctor via FormulaConverter.ToR1C1)
    // rather than degrading gracefully. Real Excel opens such a file and keeps the orphaned
    // slave's cached value. Recognize the failure signature so the caller can retry with the
    // dangling shared-formula references stripped instead of failing the whole workbook load.
    private static bool IsClosedXmlSharedFormulaReconstructionFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is ArgumentException &&
                current.StackTrace?.Contains("ModContext", StringComparison.Ordinal) == true)
                return true;
        }

        return false;
    }

    private static readonly XNamespace SharedFormulaWorksheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // Removes <f t="shared" si="N"/> slave references whose si has no master (<f t="shared"
    // ref="..." si="N">formula text</f>) anywhere on the same worksheet, leaving the cell's
    // existing <v> (and <c> type attribute) untouched -- turning the orphaned formula cell into a
    // plain cached value, matching Excel's own degrade-on-open behavior for this corruption shape.
    // Per-worksheet scoping matters: shared-formula si indices are only unique within one
    // worksheet, so a sheet's masters must not be used to "rescue" another sheet's orphaned si.
    private static void StripOrphanedSharedFormulaSlaves(MemoryStream packageStream)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        foreach (var entry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(entry);
            if (worksheetXml.Root is not { } root)
                continue;

            var sharedFormulas = root
                .Descendants(SharedFormulaWorksheetNamespace + "f")
                .Where(f => string.Equals(f.Attribute("t")?.Value, "shared", StringComparison.Ordinal) &&
                    f.Attribute("si")?.Value is { Length: > 0 })
                .ToList();
            if (sharedFormulas.Count == 0)
                continue;

            var masterSharedFormulaIds = sharedFormulas
                .Where(f => !string.IsNullOrEmpty(f.Value))
                .Select(f => f.Attribute("si")!.Value)
                .ToHashSet(StringComparer.Ordinal);

            var orphanedSlaves = sharedFormulas
                .Where(f => string.IsNullOrEmpty(f.Value) &&
                    !masterSharedFormulaIds.Contains(f.Attribute("si")!.Value))
                .ToList();
            if (orphanedSlaves.Count == 0)
                continue;

            orphanedSlaves.Remove();
            XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static MemoryStream CreateClosedXmlParsePackage(
        MemoryStream packageStream,
        IReadOnlySet<string>? styleOnlyWorksheetPathsToStrip,
        XlsxClosedXmlLoadSanitizationHints sanitizationHints,
        bool removeUnsupportedConditionalFormatting,
        bool removeAllConditionalFormatting = false)
    {
        return XlsxClosedXmlLoadPackageSanitizer.Create(
            packageStream,
            styleOnlyWorksheetPathsToStrip,
            removeUnsupportedConditionalFormatting,
            removeAllConditionalFormatting,
            sanitizationHints);
    }

    private static XlsxLoadPhaseDiagnostics AddLoadPhaseDiagnostics(
        XlsxLoadPhaseDiagnostics left,
        XlsxLoadPhaseDiagnostics right) =>
        new(
            left.ElapsedMilliseconds + right.ElapsedMilliseconds,
            left.AllocatedBytes + right.AllocatedBytes);

    private readonly record struct ClosedXmlLoadResult(
        MemoryStream PackageStream,
        XLWorkbook Workbook,
        XlsxClosedXmlLoadDiagnostics Diagnostics);

    private readonly record struct XlsxClosedXmlLoadDiagnostics(
        XlsxLoadPhaseDiagnostics Total,
        XlsxLoadPhaseDiagnostics PackagePreparation,
        XlsxLoadPhaseDiagnostics WorkbookOpen);

}
