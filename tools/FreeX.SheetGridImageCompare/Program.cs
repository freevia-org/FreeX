using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.Presentation.Filtering;
using FreeX.App.UI;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;

/// <summary>
/// FreeX Sheet Grid Image Compare — renders each worksheet of an .xlsx to a PNG using
/// FreeX's REAL on-screen GridView control (off-screen, headless), so cell fills,
/// conditional-format fills/data-bars, and table-style banding are all included.
///
/// Discovery tool only; no product-code changes.
/// </summary>
internal static class Program
{
    private const string DefaultWorkbookPath = @"E:\Users\quinn\Downloads\ExcelExamples1.xlsx";

    // Render at 1.5× of 96 dpi (144 dpi) for higher-resolution PNGs
    private const double RenderScale = 1.5;
    private const double RenderDpi = 96.0 * RenderScale;

    // Maximum viewport size to avoid exploding on huge sheets
    private const double MaxViewportWidth  = 3000.0;
    private const double MaxViewportHeight = 2000.0;

    // When expanding the captured region to also include anchored drawing objects
    // (charts/pictures/shapes/text boxes) that sit below or beside the data, a stray
    // far-flung object must not blow the image up absurdly. Cap the drawing-driven
    // content extent (in DIPs, relative to the A1 grid origin) to these maximums and
    // log when the expansion is clamped. These are deliberately larger than
    // MaxViewportWidth/Height (which still caps the final rendered viewport) so that
    // normally-placed on-page objects are reliably captured.
    private const double MaxDrawingContentWidth  = 8000.0;
    private const double MaxDrawingContentHeight = 8000.0;
    private const uint MaxPivotVisualInferenceRows = 120;
    private const uint MaxPivotVisualInferenceCols = 40;
    private const int XlScreen = 1;
    private const int XlPicture = -4147;
    private const int XlBitmap = 2;

    [STAThread]
    public static int Main(string[] args)
    {
        // Pin culture so number/date formatting is predictable
        var culture = CultureInfo.GetCultureInfo("en-US");
        System.Threading.Thread.CurrentThread.CurrentCulture = culture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        var options = GridImageCompareOptions.Parse(args);
        var xlsxPath = options.WorkbookPath ?? DefaultWorkbookPath;

        // Derive output dir from workbook file name (matches SheetImageCompare convention)
        var baseName = Path.GetFileNameWithoutExtension(xlsxPath).ToLowerInvariant()
            .Replace(" ", "").Replace("-", "").Replace("_", "");
        var freexOutputDir = options.OutputDirectory is null
            ? Path.Combine(Path.GetTempPath(), $"{baseName}-gridview")
            : Path.Combine(Path.GetFullPath(options.OutputDirectory), "freex");
        var excelInputDir = options.OutputDirectory is null
            ? Path.Combine(Path.GetTempPath(), $"{baseName}-excel")
            : Path.Combine(Path.GetFullPath(options.OutputDirectory), "excel");
        Directory.CreateDirectory(freexOutputDir);
        if (options.ExportExcelPngs)
            Directory.CreateDirectory(excelInputDir);

        Console.WriteLine("=== FreeX Sheet Grid Image Compare (GridView renderer) ===");
        Console.WriteLine($"Workbook    : {xlsxPath}");
        Console.WriteLine($"FreeX output: {freexOutputDir}");
        Console.WriteLine($"Excel input : {excelInputDir} (optional — for diff mode)");
        Console.WriteLine();

        // ------------------------------------------------------------------
        // 1. Load workbook
        // ------------------------------------------------------------------
        Console.WriteLine("[1/3] Loading workbook...");
        Workbook workbook;
        try
        {
            using var stream = File.OpenRead(xlsxPath);
            var result = new XlsxFileAdapter().LoadWithWarnings(stream, inspectFeatures: false);
            workbook = result.Workbook;
            if (result.Warnings.Count > 0)
                Console.WriteLine($"  Load warnings ({result.Warnings.Count}): {string.Join("; ", result.Warnings.Take(5))}");

            // Mirror the real app's open pipeline: recalc all formulas so volatile/date-driven cells
            // (e.g. =D3-TODAY()) and the conditional-format rules that read them reflect *today*, not the
            // stale cached values from when the file was last saved.  Without this the GridView would
            // render cached values and CF would highlight the wrong rows vs Excel (which recalcs on open).
            new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(workbook);

            // Mirror WorkbookOpenService: Excel applies pivot/table styles dynamically and does not
            // bake them into per-cell styles, so materialize them onto the loaded cells so the GridView
            // render shows the header fills + row banding exactly as the real app does on open.
            FreeX.Core.Commands.PivotTableRefreshService.ApplyLoadedPivotStyles(workbook);
            FreeX.Core.Commands.StructuredTableStyleService.ApplyLoadedTableStyles(workbook);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: Could not load workbook: {ex.Message}");
            return 1;
        }
        Console.WriteLine($"  Sheets: {workbook.Sheets.Count}");

        // ------------------------------------------------------------------
        // 2. Build viewport service (parameterless ctor)
        // ------------------------------------------------------------------
        var viewportService = new ViewportService();
        IReadOnlyList<PivotVisualCase>? pivotVisualCases = options.PivotRangesOnly
            ? EnumeratePivotVisualRanges(workbook).ToArray()
            : null;
        IReadOnlyList<SheetVisualCase>? pivotSheetVisualCases = options.PivotSheetRanges
            ? EnumeratePivotSheetVisualRanges(workbook).ToArray()
            : null;
        IReadOnlyList<SheetVisualCase>? tableSheetVisualCases = options.TableSheetRanges
            ? EnumerateTableSheetVisualRanges(workbook).ToArray()
            : null;

        if (options.ExportExcelPngs)
        {
            Console.WriteLine("\n[2/4] Exporting Excel PNGs...");
            (pivotVisualCases, pivotSheetVisualCases, tableSheetVisualCases) = ExportExcelReferencePngs(
                xlsxPath,
                workbook,
                excelInputDir,
                options,
                pivotVisualCases,
                pivotSheetVisualCases,
                tableSheetVisualCases);
        }

        var excelReferenceDimensions = Directory.Exists(excelInputDir)
            ? LoadExcelReferenceDimensions(excelInputDir)
            : new Dictionary<int, PngDimensions>();

        // ------------------------------------------------------------------
        // 3. Render each visible sheet to PNG using GridView
        // ------------------------------------------------------------------
        Console.WriteLine(options.ExportExcelPngs
            ? "\n[3/4] Rendering sheets via GridView..."
            : "\n[2/3] Rendering sheets via GridView...");

        // --capture-range: when supplied, override the viewport for every rendered sheet with
        // the specified cell range (no row/col headers), mirroring Excel's CopyPicture-of-range.
        // The range is re-parsed for each sheet using that sheet's SheetId.
        GridRange? globalCaptureRange = null;
        if (!string.IsNullOrWhiteSpace(options.CaptureRangeRaw))
        {
            // Best-effort parse: use the first visible sheet's id for the initial parse.
            var firstSheet = workbook.Sheets.FirstOrDefault(s => !s.IsHidden && !s.IsVeryHidden);
            if (firstSheet is not null)
            {
                try
                {
                    globalCaptureRange = GridRange.ParseCellOrRange(
                        options.CaptureRangeRaw.Replace("$", "", StringComparison.Ordinal).Trim(),
                        firstSheet.Id);
                    Console.WriteLine($"  Capture range : {options.CaptureRangeRaw} → {globalCaptureRange}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  WARNING: could not parse --capture-range '{options.CaptureRangeRaw}': {ex.Message}");
                }
            }
        }

        var sheetResults = new List<SheetResult>();
        int sheetIndex = 1;

        if (options.PivotRangesOnly)
        {
            foreach (var item in pivotVisualCases ?? EnumeratePivotVisualRanges(workbook).ToArray())
            {
                var (sheet, pivot, range, rangeSource) = item;
                var safeName = SanitizeFileName($"{sheet.Name}_{pivot.Name}");
                var outFileName = $"freex_{sheetIndex:D2}_{safeName}.png";
                var outPath = Path.Combine(freexOutputDir, outFileName);

                Console.Write($"  [{sheetIndex:D2}] {sheet.Name}!{range} ({pivot.Name}; {rangeSource}) ... ");

                var result = new SheetResult
                {
                    NN = sheetIndex,
                    SheetName = $"{sheet.Name} - {pivot.Name} [{rangeSource}]",
                    FreeXPngPath = outPath,
                    FreeXPngFileName = outFileName,
                    PivotDropdownSummary = DescribePivotDropdownTargets(workbook, sheet),
                };

                try
                {
                    var targetDimensions = excelReferenceDimensions.TryGetValue(sheetIndex, out var dimensions)
                        ? dimensions
                        : (PngDimensions?)null;
                    RenderSheetToGridViewPng(workbook, sheet, viewportService, outPath, range, targetDimensions);
                    result.Rendered = true;
                    Console.WriteLine($"-> {outFileName}");
                }
                catch (Exception ex)
                {
                    result.Error = $"{ex.GetType().Name}: {ex.Message}";
                    Console.WriteLine($"ERROR: {result.Error}");
                }

                sheetResults.Add(result);
                sheetIndex++;
            }
        }
        else if (options.PivotSheetRanges)
        {
            foreach (var item in pivotSheetVisualCases ?? EnumeratePivotSheetVisualRanges(workbook).ToArray())
            {
                var safeName = SanitizeFileName(item.Name);
                var outFileName = $"freex_{sheetIndex:D2}_{safeName}.png";
                var outPath = Path.Combine(freexOutputDir, outFileName);

                Console.Write($"  [{sheetIndex:D2}] {item.Sheet.Name}!{item.Range} ({item.RangeSource}) ... ");

                var result = new SheetResult
                {
                    NN = sheetIndex,
                    SheetName = $"{item.Sheet.Name} [{item.RangeSource}]",
                    FreeXPngPath = outPath,
                    FreeXPngFileName = outFileName,
                    PivotDropdownSummary = DescribePivotDropdownTargets(workbook, item.Sheet),
                };

                try
                {
                    var targetDimensions = excelReferenceDimensions.TryGetValue(sheetIndex, out var dimensions)
                        ? dimensions
                        : (PngDimensions?)null;
                    RenderSheetToGridViewPng(workbook, item.Sheet, viewportService, outPath, item.Range, targetDimensions);
                    result.Rendered = true;
                    Console.WriteLine($"-> {outFileName}");
                }
                catch (Exception ex)
                {
                    result.Error = $"{ex.GetType().Name}: {ex.Message}";
                    Console.WriteLine($"ERROR: {result.Error}");
                }

                sheetResults.Add(result);
                sheetIndex++;
            }
        }
        else if (options.TableSheetRanges)
        {
            foreach (var item in tableSheetVisualCases ?? EnumerateTableSheetVisualRanges(workbook).ToArray())
            {
                var safeName = SanitizeFileName(item.Name);
                var outFileName = $"freex_{sheetIndex:D2}_{safeName}.png";
                var outPath = Path.Combine(freexOutputDir, outFileName);

                Console.Write($"  [{sheetIndex:D2}] {item.Sheet.Name}!{item.Range} ({item.RangeSource}) ... ");

                var result = new SheetResult
                {
                    NN = sheetIndex,
                    SheetName = $"{item.Sheet.Name} [{item.RangeSource}]",
                    FreeXPngPath = outPath,
                    FreeXPngFileName = outFileName,
                    PivotDropdownSummary = null,
                };

                try
                {
                    var targetDimensions = excelReferenceDimensions.TryGetValue(sheetIndex, out var dimensions)
                        ? dimensions
                        : (PngDimensions?)null;
                    RenderSheetToGridViewPng(workbook, item.Sheet, viewportService, outPath, item.Range, targetDimensions);
                    result.Rendered = true;
                    Console.WriteLine($"-> {outFileName}");
                }
                catch (Exception ex)
                {
                    result.Error = $"{ex.GetType().Name}: {ex.Message}";
                    Console.WriteLine($"ERROR: {result.Error}");
                }

                sheetResults.Add(result);
                sheetIndex++;
            }
        }
        else foreach (var sheet in workbook.Sheets)
        {
            if (sheet.IsHidden || sheet.IsVeryHidden)
            {
                Console.WriteLine($"  [{sheetIndex:D2}] {sheet.Name} — SKIPPED (hidden)");
                sheetIndex++;
                continue;
            }

            var safeName = SanitizeFileName(sheet.Name);
            var outFileName = $"freex_{sheetIndex:D2}_{safeName}.png";
            var outPath = Path.Combine(freexOutputDir, outFileName);

            Console.Write($"  [{sheetIndex:D2}/{workbook.Sheets.Count}] {sheet.Name} ... ");

            var result = new SheetResult
            {
                NN = sheetIndex,
                SheetName = sheet.Name,
                FreeXPngPath = outPath,
                FreeXPngFileName = outFileName,
                PivotDropdownSummary = DescribePivotDropdownTargets(workbook, sheet),
            };

            try
            {
                var targetDimensions = excelReferenceDimensions.TryGetValue(sheetIndex, out var dimensions)
                    ? dimensions
                    : (PngDimensions?)null;

                // Re-anchor the capture range to this sheet's id if --capture-range was supplied.
                GridRange? captureRangeForSheet = null;
                if (globalCaptureRange.HasValue && !string.IsNullOrWhiteSpace(options.CaptureRangeRaw))
                {
                    try
                    {
                        captureRangeForSheet = GridRange.ParseCellOrRange(
                            options.CaptureRangeRaw.Replace("$", "", StringComparison.Ordinal).Trim(),
                            sheet.Id);
                    }
                    catch
                    {
                        captureRangeForSheet = null;
                    }
                }

                RenderSheetToGridViewPng(workbook, sheet, viewportService, outPath, captureRangeForSheet, targetDimensions);
                result.Rendered = true;
                Console.WriteLine($"-> {outFileName}");
            }
            catch (Exception ex)
            {
                result.Error = $"{ex.GetType().Name}: {ex.Message}";
                Console.WriteLine($"ERROR: {result.Error}");
            }

            sheetResults.Add(result);
            sheetIndex++;
        }

        // ------------------------------------------------------------------
        // 4. Optional diff mode or simple index
        // ------------------------------------------------------------------
        Console.WriteLine(options.ExportExcelPngs ? "\n[4/4] Building report..." : "\n[3/3] Building report...");

        bool hasDiffDir = Directory.Exists(excelInputDir) &&
            Directory.EnumerateFiles(excelInputDir, "excel_*.png").Any();

        string reportPath;
        if (hasDiffDir)
            reportPath = RunDiffMode(workbook, sheetResults, freexOutputDir, excelInputDir, options);
        else
            reportPath = WriteSimpleIndex(sheetResults, freexOutputDir, xlsxPath);

        int rendered = sheetResults.Count(r => r.Rendered);
        int skipped  = sheetResults.Count(r => r.Skipped);
        int errors   = sheetResults.Count(r => r.Error != null && !r.Skipped);

        Console.WriteLine();
        Console.WriteLine($"Rendered : {rendered}");
        Console.WriteLine($"Skipped  : {skipped} (hidden/empty)");
        Console.WriteLine($"Errors   : {errors}");
        Console.WriteLine($"Report   : {reportPath}");
        Console.WriteLine("\nDONE.");
        var diffFailures = hasDiffDir
            ? sheetResults.Count(r => r.ComparisonFailed || r.DiffPercent > options.ThresholdPercent)
            : 0;
        return errors > 0 || diffFailures > 0 ? 2 : 0;
    }

    // -----------------------------------------------------------------------
    // Core: render a single sheet to PNG via GridView
    // -----------------------------------------------------------------------
    private static void RenderSheetToGridViewPng(
        Workbook workbook,
        Sheet sheet,
        ViewportService viewportService,
        string outPath,
        GridRange? captureRange,
        PngDimensions? targetPixelDimensions)
    {
        // Step 1: Determine viewport dimensions from the sheet's used range.
        // Row heights are already in WPF DIPs; column widths need the pixel mapper.
        var usedRange = captureRange ?? sheet.GetUsedRange();
        uint topRow = captureRange?.Start.Row ?? 1u;
        uint leftCol = captureRange?.Start.Col ?? 1u;
        uint maxRow = usedRange?.End.Row ?? 40u;
        uint maxCol = usedRange?.End.Col ?? 10u;

        // Sum column widths (in pixels/DIPs) for the used range
        double totalColWidth = 0;
        for (uint c = leftCol; c <= maxCol; c++)
        {
            if (sheet.IsColEffectivelyHidden(c)) continue;
            var charWidth = sheet.ColumnWidths.GetValueOrDefault(c, sheet.DefaultColumnWidth);
            totalColWidth += ColumnWidthPixelMapper.ColumnWidthToPixels(charWidth);
        }

        // Sum row heights (already in DIPs) for the used range
        double totalRowHeight = 0;
        for (uint r = topRow; r <= maxRow; r++)
        {
            if (sheet.IsRowEffectivelyHidden(r)) continue;
            totalRowHeight += sheet.RowHeights.GetValueOrDefault(r, sheet.DefaultRowHeight);
        }

        // Expand the captured region to the UNION of (a) the cell used-range (computed above) and
        // (b) the bounding box of every anchored drawing object on the sheet (charts/pictures/
        // shapes/text boxes). Without this the capture stops at the data table and clips out charts/
        // pictures positioned BELOW or BESIDE it, so the harness can't validate them.
        if (captureRange is null)
            ExpandRegionForDrawingObjects(workbook, sheet, ref maxRow, ref maxCol, ref totalColWidth, ref totalRowHeight);

        // Estimate row-header width (uses GridView's static helper with a placeholder viewport)
        // We need an approximate lastVisibleRow for the header width calc.
        // When comparing against an Excel reference PNG (targetPixelDimensions is not null), Excel's
        // CopyPicture-of-range exports cell content only — no row/column header chrome — so FreeX must
        // also suppress headers to keep cell positions aligned in both images.
        var showHeaders = captureRange is null && sheet.ShowHeadings && targetPixelDimensions is null;
        var rowHeaderWidth = (captureRange is null && targetPixelDimensions is null) ? GridView.RowHeaderWidth : 0.0;
        var colHeaderHeight = (captureRange is null && targetPixelDimensions is null) ? GridView.ColHeaderHeight : 0.0;

        // The viewport is capped so huge sheets don't explode. The cap must be at least as large as
        // the drawing-content cap; otherwise an object that sits just past MaxViewportWidth/Height
        // (e.g. a chart below a tall table) would be clipped again here even though the region above
        // already accounted for it. ExpandRegionForDrawingObjects bounds totalColWidth/totalRowHeight
        // to MaxDrawingContentWidth/Height, so these effective caps stay bounded.
        // When matching Excel reference dimensions, omit safety padding so viewW/viewH exactly
        // equal the cell-content extents and the render scale matches Excel's CopyPicture scale.
        var safetyPadding = (captureRange is null && targetPixelDimensions is null) ? 20.0 : 0.0;
        double maxViewW = Math.Max(MaxViewportWidth,  MaxDrawingContentWidth  + rowHeaderWidth  + safetyPadding);
        double maxViewH = Math.Max(MaxViewportHeight, MaxDrawingContentHeight + colHeaderHeight + safetyPadding);

        double viewW = Math.Min(maxViewW, totalColWidth  + rowHeaderWidth  + safetyPadding);
        double viewH = Math.Min(maxViewH, totalRowHeight + colHeaderHeight + safetyPadding);

        // Ensure minimum size
        viewW = Math.Max(viewW, captureRange is null ? 200 : 1);
        viewH = Math.Max(viewH, captureRange is null ? 100 : 1);

        // Step 2: Build viewport — available area excludes headers
        var availableW = viewW - rowHeaderWidth;
        var availableH = viewH - colHeaderHeight;

        var request = new ViewportRequest(
            TopRow: topRow,
            LeftCol: leftCol,
            AvailableHeight: availableH,
            AvailableWidth: availableW,
            IncludeObjects: true,
            SplitPaneOffsets: null);

        var viewport = viewportService.GetViewport(workbook, sheet.Id, request);

        // Resolve list-control selected-item text (ListFillRange[SelectedIndex]) into SelectedText,
        // mirroring the real app's open/viewport pipeline so drop-downs render their selection.
        if (sheet.FormControls.Count > 0)
            FreeX.Core.Commands.FormControlListResolver.PopulateSelectedText(sheet, workbook);

        // Surface native slicers/timelines anchored on this sheet, mirroring MainWindow.Viewport: resolve
        // each slicer's available items (table-column distinct values / pivot cache shared items) into
        // AvailableItems, then hand the visible set to the GridView so it draws the slicer boxes.
        var nativeVisualFilters = FreeX.App.Host.SlicerTimelinePlanner.GetNativeVisualFilters(workbook, sheet);
        if (nativeVisualFilters.Slicers.Count > 0)
            FreeX.Core.Commands.SlicerItemResolver.PopulateAvailableItems(workbook);

        // Step 3: Configure GridView
        var grid = new GridView
        {
            Viewport        = viewport,
            WorkbookTheme   = workbook.Theme,
            HiddenRows      = sheet.HiddenRows,
            HiddenColumns   = sheet.HiddenCols,
            Charts          = sheet.Charts,
            Pictures        = sheet.Pictures,
            DrawingShapes   = sheet.DrawingShapes,
            TextBoxes       = sheet.TextBoxes,
            FormControls    = sheet.FormControls,
            NativeSlicers   = nativeVisualFilters.Slicers,
            NativeTimelines = nativeVisualFilters.Timelines,
            Sparklines      = sheet.Sparklines,
            SparklineValues = sheet.Sparklines.Count > 0
                ? SparklineSeriesReader.BuildValues(sheet)
                : null,
            MergedRegions   = sheet.MergedRegions,
            WorksheetBackground = null,
            ObjectDisplayMode = GridObjectDisplayMode.All,
            ShowGridLines   = sheet.ShowGridlines,
            ShowHeaders     = showHeaders,
            ZoomFactor      = 1.0,
            WorksheetViewMode = sheet.ViewMode,
            // Wire per-run rich text so cells with character-level formatting render correctly.
            ActiveSheetId   = sheet.Id,
            SheetRichTextRuns = sheet.RichTextRuns.Count > 0 ? sheet.RichTextRuns : null,
        };

        // Surface the sheet's AutoFilter range (worksheet-level OR carried inside a structured table)
        // so the GridView draws the filter-arrow dropdown buttons on the header row, matching Excel.
        grid.AutoFilterRange =
            AutoFilterDropdownMenuPlanner.TryGetAutoFilterRange(sheet, out var autoFilterRange)
                ? autoFilterRange
                : null;
        grid.PivotHeaderDropdowns = FreeX.App.Host.PivotHeaderDropdownPlanner.BuildTargets(workbook, sheet)
            .Select(target => new PivotHeaderDropdownButton(target.HeaderCell, target.IsActive))
            .ToArray();
        grid.PivotRowLabelAdornments = FreeX.App.Host.PivotRowLabelAdornmentPlanner.BuildAdornments(workbook, sheet);

        // Step 4: Off-screen layout pass
        grid.Measure(new Size(viewW, viewH));
        grid.Arrange(new Rect(0, 0, viewW, viewH));
        grid.UpdateLayout();

        // Step 5: Rasterize to RenderTargetBitmap. When an Excel reference PNG exists,
        // render FreeX to the same pixel canvas so strict dimension checks validate the
        // compared range instead of the two tools' different capture DPI conventions.
        var hasTargetDimensions = targetPixelDimensions is not null;
        int pixelW = targetPixelDimensions?.Width ?? Math.Max(1, (int)Math.Ceiling(viewW * RenderScale));
        int pixelH = targetPixelDimensions?.Height ?? Math.Max(1, (int)Math.Ceiling(viewH * RenderScale));
        pixelW = Math.Max(1, pixelW);
        pixelH = Math.Max(1, pixelH);

        var scaleX = hasTargetDimensions ? pixelW / viewW : RenderScale;
        var scaleY = hasTargetDimensions ? pixelH / viewH : RenderScale;
        var rtb = hasTargetDimensions
            ? new RenderTargetBitmap(pixelW, pixelH, 96.0, 96.0, PixelFormats.Pbgra32)
            : new RenderTargetBitmap(pixelW, pixelH, RenderDpi, RenderDpi, PixelFormats.Pbgra32);

        // Apply DPI scale transform so logical DIPs map to the correct pixel count
        var dv = new System.Windows.Media.DrawingVisual();
        using (var ctx = dv.RenderOpen())
        {
            var vb = new VisualBrush(grid)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
            if (hasTargetDimensions)
            {
                ctx.PushTransform(new ScaleTransform(scaleX, scaleY));
                ctx.DrawRectangle(vb, null, new Rect(0, 0, viewW, viewH));
                ctx.Pop();
            }
            else
            {
                ctx.DrawRectangle(vb, null, new Rect(0, 0, viewW * RenderScale, viewH * RenderScale));
            }
        }
        rtb.Render(dv);

        rtb.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fileStream = File.Create(outPath);
        encoder.Save(fileStream);
    }

    // -----------------------------------------------------------------------
    // Drawing-object region union
    // -----------------------------------------------------------------------
    /// <summary>
    /// Grows the captured region (in cell coords <paramref name="maxRow"/>/<paramref name="maxCol"/>
    /// and in DIP content extents <paramref name="totalColWidth"/>/<paramref name="totalRowHeight"/>)
    /// to also cover every visible anchored drawing object on the sheet. The DIP extents are measured
    /// from the A1 grid origin and use the SAME column-width/row-height mapping the viewport uses
    /// (<see cref="ColumnWidthPixelMapper"/> for columns, raw DIP row heights for rows), so the unioned
    /// extents line up with the GridView's metrics.
    ///
    /// A per-object cap (<see cref="MaxDrawingContentWidth"/>/<see cref="MaxDrawingContentHeight"/>)
    /// prevents a stray far-flung object from blowing the image up absurdly; clamps are logged.
    /// </summary>
    private static void ExpandRegionForDrawingObjects(
        Workbook workbook,
        Sheet sheet,
        ref uint maxRow,
        ref uint maxCol,
        ref double totalColWidth,
        ref double totalRowHeight)
    {
        var nativeFilters = FreeX.App.Host.SlicerTimelinePlanner.GetNativeVisualFilters(workbook, sheet);
        bool hasObjects =
            sheet.Charts.Count > 0 ||
            sheet.Pictures.Count > 0 ||
            sheet.DrawingShapes.Count > 0 ||
            sheet.TextBoxes.Count > 0 ||
            nativeFilters.Slicers.Count > 0 ||
            nativeFilters.Timelines.Count > 0;
        if (!hasObjects)
            return;

        // Track the farthest pixel right/bottom edge (relative to the A1 grid origin) reached by any
        // object, and the farthest cell row/col it spans, so we can extend both the DIP content extents
        // (which size the render surface) and the cell range (which seeds the viewport metrics).
        double maxRightPx  = totalColWidth;
        double maxBottomPx = totalRowHeight;
        uint   reachRow    = maxRow;
        uint   reachCol    = maxCol;
        bool   clamped     = false;

        void IncludeRect(double rightPx, double bottomPx, uint lastRow, uint lastCol)
        {
            if (rightPx > MaxDrawingContentWidth)  { rightPx = MaxDrawingContentWidth;  clamped = true; }
            if (bottomPx > MaxDrawingContentHeight) { bottomPx = MaxDrawingContentHeight; clamped = true; }
            if (rightPx  > maxRightPx)  maxRightPx  = rightPx;
            if (bottomPx > maxBottomPx) maxBottomPx = bottomPx;
            if (lastRow  > reachRow)    reachRow    = lastRow;
            if (lastCol  > reachCol)    reachCol    = lastCol;
        }

        // Charts: Left/Top/Width/Height are absolute DIPs from the A1 grid origin (see CreateChartRect).
        foreach (var chart in sheet.Charts)
        {
            if (!chart.IsVisible) continue;
            var right  = chart.Left + Math.Max(0, chart.Width);
            var bottom = chart.Top  + Math.Max(0, chart.Height);
            IncludeRect(right, bottom, PixelTopToRow(sheet, bottom), PixelLeftToCol(sheet, right));
        }

        // Pictures / shapes / text boxes: anchored to a cell (1-based) with Width/Height in DIPs.
        // The object's top-left is the anchor cell's top-left (matches TryCreateAnchoredObjectRect),
        // so its right/bottom edge = anchor-cell offset + extent.
        foreach (var pic in sheet.Pictures)
            if (pic.IsVisible)
                IncludeAnchoredObject(sheet, pic.Anchor.Row, pic.Anchor.Col, pic.Width, pic.Height, IncludeRect);

        foreach (var shape in sheet.DrawingShapes)
            if (shape.IsVisible)
                IncludeAnchoredObject(sheet, shape.Anchor.Row, shape.Anchor.Col, shape.Width, shape.Height, IncludeRect);

        foreach (var box in sheet.TextBoxes)
            if (box.IsVisible)
                IncludeAnchoredObject(sheet, box.Anchor.Row, box.Anchor.Col, box.Width, box.Height, IncludeRect);

        // Native slicers/timelines anchor by a From/To cell range (0-based + EMU corner offsets) rather than
        // a single anchor cell + extent, and often sit BESIDE the data table (e.g. file 03's "Category"/"Who"
        // slicers at G1:I5). Include the To corner so the captured region covers them.
        foreach (var slicer in nativeFilters.Slicers)
            if (slicer.DrawingAnchor is { } anchor)
                IncludeDrawingAnchor(sheet, anchor, IncludeRect);

        foreach (var timeline in nativeFilters.Timelines)
            if (timeline.DrawingAnchor is { } anchor)
                IncludeDrawingAnchor(sheet, anchor, IncludeRect);

        totalColWidth  = Math.Min(MaxDrawingContentWidth,  Math.Max(totalColWidth,  maxRightPx));
        totalRowHeight = Math.Min(MaxDrawingContentHeight, Math.Max(totalRowHeight, maxBottomPx));
        maxRow = Math.Max(maxRow, reachRow);
        maxCol = Math.Max(maxCol, reachCol);

        if (clamped)
            Console.Write("[drawing-bounds clamped] ");
    }

    private static void IncludeDrawingAnchor(
        Sheet sheet,
        DrawingAnchorRange anchor,
        Action<double, double, uint, uint> include)
    {
        if (anchor.To.Column == uint.MaxValue || anchor.To.Row == uint.MaxValue)
            return;

        // To.Column/Row are 0-based; the pixel helpers are 1-based and measure the cell's LEFT/TOP edge.
        // The right/bottom edge of the To corner is that edge plus the EMU corner offset.
        var right  = ColumnLeftPixels(sheet, anchor.To.Column + 1) + EmusToDip(anchor.To.ColumnOffsetEmu);
        var bottom = RowTopPixels(sheet, anchor.To.Row + 1) + EmusToDip(anchor.To.RowOffsetEmu);
        include(right, bottom, PixelTopToRow(sheet, bottom), PixelLeftToCol(sheet, right));
    }

    private static double EmusToDip(long emus) => emus / 9525.0;

    private static void IncludeAnchoredObject(
        Sheet sheet,
        uint anchorRow,
        uint anchorCol,
        double width,
        double height,
        Action<double, double, uint, uint> include)
    {
        if (anchorRow == 0 || anchorCol == 0)
            return;
        var left = ColumnLeftPixels(sheet, anchorCol);
        var top  = RowTopPixels(sheet, anchorRow);
        var right  = left + Math.Max(0, width);
        var bottom = top  + Math.Max(0, height);
        include(right, bottom, PixelTopToRow(sheet, bottom), PixelLeftToCol(sheet, right));
    }

    /// <summary>DIP offset of the LEFT edge of <paramref name="col"/> (1-based) from the A1 origin.</summary>
    private static double ColumnLeftPixels(Sheet sheet, uint col)
    {
        double x = 0;
        for (uint c = 1; c < col; c++)
        {
            if (sheet.IsColEffectivelyHidden(c)) continue;
            x += ColumnWidthPixelMapper.ColumnWidthToPixels(
                sheet.ColumnWidths.GetValueOrDefault(c, sheet.DefaultColumnWidth));
        }
        return x;
    }

    /// <summary>DIP offset of the TOP edge of <paramref name="row"/> (1-based) from the A1 origin.</summary>
    private static double RowTopPixels(Sheet sheet, uint row)
    {
        double y = 0;
        for (uint r = 1; r < row; r++)
        {
            if (sheet.IsRowEffectivelyHidden(r)) continue;
            y += sheet.RowHeights.GetValueOrDefault(r, sheet.DefaultRowHeight);
        }
        return y;
    }

    /// <summary>First 1-based column whose right edge reaches <paramref name="xPixels"/> from the A1 origin.</summary>
    private static uint PixelLeftToCol(Sheet sheet, double xPixels)
    {
        if (xPixels <= 0)
            return 1;
        double x = 0;
        for (uint c = 1; c <= CellAddress.MaxCol; c++)
        {
            if (sheet.IsColEffectivelyHidden(c)) continue;
            x += ColumnWidthPixelMapper.ColumnWidthToPixels(
                sheet.ColumnWidths.GetValueOrDefault(c, sheet.DefaultColumnWidth));
            if (x >= xPixels)
                return c;
        }
        return CellAddress.MaxCol;
    }

    /// <summary>First 1-based row whose bottom edge reaches <paramref name="yPixels"/> from the A1 origin.</summary>
    private static uint PixelTopToRow(Sheet sheet, double yPixels)
    {
        if (yPixels <= 0)
            return 1;
        double y = 0;
        for (uint r = 1; r <= CellAddress.MaxRow; r++)
        {
            if (sheet.IsRowEffectivelyHidden(r)) continue;
            y += sheet.RowHeights.GetValueOrDefault(r, sheet.DefaultRowHeight);
            if (y >= yPixels)
                return r;
        }
        return CellAddress.MaxRow;
    }

    // -----------------------------------------------------------------------
    // Simple index (no Excel PNGs to compare against)
    // -----------------------------------------------------------------------
    private static string WriteSimpleIndex(
        IReadOnlyList<SheetResult> results,
        string outputDir,
        string xlsxPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FreeX Sheet Grid Image Render Index (GridView renderer)");
        sb.AppendLine($"Generated : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Workbook  : {xlsxPath}");
        sb.AppendLine();
        sb.AppendLine($"{"NN",-4}  {"Status",-10}  {"Sheet",-35}  PNG");
        sb.AppendLine(new string('-', 100));

        foreach (var r in results)
        {
            string status  = r.Rendered ? "OK" : r.Skipped ? "SKIPPED" : "ERROR";
            string pngName = r.Rendered ? r.FreeXPngFileName : r.Error ?? r.SkipReason ?? "";
            sb.AppendLine($"{r.NN.ToString("D2"),-4}  {status,-10}  {Trunc(r.SheetName, 35),-35}  {pngName}");
        }

        var reportPath = Path.Combine(outputDir, "REPORT.txt");
        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
        return reportPath;
    }

    private static IEnumerable<PivotVisualCase> EnumeratePivotVisualRanges(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.IsHidden || sheet.IsVeryHidden)
                continue;

            foreach (var pivot in sheet.PivotTables)
            {
                var range = pivot.LastRenderedRange ?? pivot.TargetRange;
                var resolved = ResolvePivotVisualRange(sheet, pivot, range, out var rangeSource);
                yield return new PivotVisualCase(sheet, pivot, resolved, rangeSource);
            }
        }
    }

    private static IEnumerable<SheetVisualCase> EnumeratePivotSheetVisualRanges(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.IsHidden || sheet.IsVeryHidden || sheet.PivotTables.Count == 0)
                continue;

            var range = ResolvePivotSheetVisualRange(workbook, sheet, out var rangeSource);
            var pivotNames = string.Join("_", sheet.PivotTables.Select(pivot => pivot.Name));
            yield return new SheetVisualCase(
                sheet,
                range,
                $"{sheet.Name}_{pivotNames}",
                rangeSource);
        }
    }

    /// <summary>
    /// Yields one <see cref="SheetVisualCase"/> per structured table in the workbook, where the
    /// captured range is exactly the table's own cell range (no row/col headers).  This mirrors
    /// how Excel's CopyPicture crops to the table region, giving header-free, aligned comparisons.
    /// </summary>
    private static IEnumerable<SheetVisualCase> EnumerateTableSheetVisualRanges(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.IsHidden || sheet.IsVeryHidden)
                continue;

            foreach (var table in sheet.StructuredTables)
            {
                var range = table.Range;
                if (range.Start.Sheet != sheet.Id ||
                    range.End.Row < range.Start.Row ||
                    range.End.Col < range.Start.Col)
                    continue;

                var safeName = SanitizeFileName($"{sheet.Name}_{table.Name}");
                yield return new SheetVisualCase(
                    sheet,
                    range,
                    safeName,
                    "TableRange");
            }
        }
    }

    private static GridRange ResolvePivotSheetVisualRange(Workbook workbook, Sheet sheet, out string rangeSource)
    {
        var usedRange = sheet.GetUsedRange();
        var start = usedRange?.Start ?? new CellAddress(sheet.Id, 1, 1);
        uint endRow = usedRange?.End.Row ?? 1;
        uint endCol = usedRange?.End.Col ?? 1;
        var includedNativeFilters = false;

        var nativeFilters = FreeX.App.Host.SlicerTimelinePlanner.GetNativeVisualFilters(workbook, sheet);
        foreach (var slicer in nativeFilters.Slicers)
            if (slicer.DrawingAnchor is { } anchor)
                IncludeDrawingAnchorCells(sheet, anchor, ref endRow, ref endCol, ref includedNativeFilters);

        foreach (var timeline in nativeFilters.Timelines)
            if (timeline.DrawingAnchor is { } anchor)
                IncludeDrawingAnchorCells(sheet, anchor, ref endRow, ref endCol, ref includedNativeFilters);

        rangeSource = includedNativeFilters
            ? "SheetUsedRangeWithNativeVisualFilters"
            : "SheetUsedRange";
        return new GridRange(start, new CellAddress(sheet.Id, Math.Max(start.Row, endRow), Math.Max(start.Col, endCol)));
    }

    private static void IncludeDrawingAnchorCells(
        Sheet sheet,
        DrawingAnchorRange anchor,
        ref uint endRow,
        ref uint endCol,
        ref bool included)
    {
        if (anchor.To.Column == uint.MaxValue || anchor.To.Row == uint.MaxValue)
            return;

        var right = ColumnLeftPixels(sheet, anchor.To.Column + 1) + EmusToDip(anchor.To.ColumnOffsetEmu);
        var bottom = RowTopPixels(sheet, anchor.To.Row + 1) + EmusToDip(anchor.To.RowOffsetEmu);
        endCol = Math.Max(endCol, PixelLeftToCol(sheet, right));
        endRow = Math.Max(endRow, PixelTopToRow(sheet, bottom));
        included = true;
    }

    private static GridRange ResolvePivotVisualRange(Sheet sheet, PivotTableModel pivot, GridRange range, out string rangeSource)
    {
        if (range.RowCount > 1 || range.ColCount > 1)
        {
            rangeSource = pivot.LastRenderedRange is not null ? "FreeXLastRenderedRange" : "FreeXTargetRange";
            return range;
        }

        var inferred = InferPivotVisualRangeFromCells(sheet, range.Start);
        if (inferred is not null)
        {
            rangeSource = "FreeXInferredCells";
            return inferred.Value;
        }

        rangeSource = pivot.LastRenderedRange is not null ? "FreeXLastRenderedRange" : "FreeXTargetRange";
        return range;
    }

    private static GridRange? InferPivotVisualRangeFromCells(Sheet sheet, CellAddress anchor)
    {
        var maxRow = Math.Min(CellAddress.MaxRow, anchor.Row + MaxPivotVisualInferenceRows - 1);
        var maxCol = Math.Min(CellAddress.MaxCol, anchor.Col + MaxPivotVisualInferenceCols - 1);

        var lastRow = FindLastOccupiedPivotRow(sheet, anchor.Row, anchor.Col, maxRow, maxCol);
        var lastCol = FindLastOccupiedPivotColumn(sheet, anchor.Row, anchor.Col, maxRow, maxCol);
        if (lastRow is null || lastCol is null)
            return null;

        var inferred = new GridRange(
            anchor,
            new CellAddress(anchor.Sheet, Math.Max(anchor.Row, lastRow.Value), Math.Max(anchor.Col, lastCol.Value)));
        return inferred.RowCount > 1 || inferred.ColCount > 1
            ? inferred
            : null;
    }

    private static uint? FindLastOccupiedPivotRow(Sheet sheet, uint startRow, uint startCol, uint maxRow, uint maxCol)
    {
        uint? last = null;
        var blankRun = 0;
        for (var row = startRow; row <= maxRow; row++)
        {
            var occupied = false;
            for (var col = startCol; col <= maxCol; col++)
            {
                if (HasRenderableCellContent(sheet, row, col))
                {
                    occupied = true;
                    break;
                }
            }

            if (occupied)
            {
                last = row;
                blankRun = 0;
            }
            else if (last is not null && ++blankRun >= 2)
            {
                break;
            }
        }

        return last;
    }

    private static uint? FindLastOccupiedPivotColumn(Sheet sheet, uint startRow, uint startCol, uint maxRow, uint maxCol)
    {
        uint? last = null;
        var blankRun = 0;
        for (var col = startCol; col <= maxCol; col++)
        {
            var occupied = false;
            for (var row = startRow; row <= maxRow; row++)
            {
                if (HasRenderableCellContent(sheet, row, col))
                {
                    occupied = true;
                    break;
                }
            }

            if (occupied)
            {
                last = col;
                blankRun = 0;
            }
            else if (last is not null && ++blankRun >= 2)
            {
                break;
            }
        }

        return last;
    }

    private static bool HasRenderableCellContent(Sheet sheet, uint row, uint col)
    {
        var cell = sheet.GetCell(row, col);
        return cell is not null &&
            (cell.HasFormula || cell.Value is not BlankValue);
    }

    private static (IReadOnlyList<PivotVisualCase>? PivotCases, IReadOnlyList<SheetVisualCase>? SheetCases, IReadOnlyList<SheetVisualCase>? TableCases) ExportExcelReferencePngs(
        string workbookPath,
        Workbook workbook,
        string outputDirectory,
        GridImageCompareOptions options,
        IReadOnlyList<PivotVisualCase>? pivotVisualCases,
        IReadOnlyList<SheetVisualCase>? pivotSheetVisualCases,
        IReadOnlyList<SheetVisualCase>? tableSheetVisualCases)
    {
        object? excel = null;
        object? workbooks = null;
        object? workbookObject = null;
        try
        {
            var excelType = Type.GetTypeFromProgID("Excel.Application")
                ?? throw new InvalidOperationException("Excel.Application COM registration not found.");
            excel = Activator.CreateInstance(excelType)
                ?? throw new InvalidOperationException("Excel.Application activation returned null.");
            dynamic app = excel;
            app.Visible = true;
            app.DisplayAlerts = false;
            // xlShowAllComments = -4144: pin all legacy note boxes open in the screenshot.
            if (options.ShowAllComments)
                app.DisplayCommentIndicator = -4144;
            workbooks = app.Workbooks;
            workbookObject = ((dynamic)workbooks).Open(Path.GetFullPath(workbookPath), 0);

            var ordinal = 1;
            if (options.PivotRangesOnly)
            {
                var resolvedCases = ResolveExcelPivotVisualRanges(
                    workbookObject,
                    pivotVisualCases ?? EnumeratePivotVisualRanges(workbook).ToArray()).ToArray();
                foreach (var item in resolvedCases)
                {
                    var safeName = SanitizeFileName($"{item.Sheet.Name}_{item.Pivot.Name}");
                    var outPath = Path.Combine(outputDirectory, $"excel_{ordinal:D2}_{safeName}.png");
                    ExportExcelRangeToPng(workbookObject, item.Sheet.Name, item.Range, outPath);
                    Console.WriteLine($"  [{ordinal:D2}] {item.Sheet.Name}!{item.Range} ({item.RangeSource}) -> {Path.GetFileName(outPath)}");
                    ordinal++;
                }

                ((dynamic)workbookObject).Close(false);
                return (resolvedCases, pivotSheetVisualCases, tableSheetVisualCases);
            }

            if (options.PivotSheetRanges)
            {
                var cases = pivotSheetVisualCases ?? EnumeratePivotSheetVisualRanges(workbook).ToArray();
                foreach (var item in cases)
                {
                    var safeName = SanitizeFileName(item.Name);
                    var outPath = Path.Combine(outputDirectory, $"excel_{ordinal:D2}_{safeName}.png");
                    ExportExcelRangeToPng(workbookObject, item.Sheet.Name, item.Range, outPath);
                    Console.WriteLine($"  [{ordinal:D2}] {item.Sheet.Name}!{item.Range} ({item.RangeSource}) -> {Path.GetFileName(outPath)}");
                    ordinal++;
                }

                ((dynamic)workbookObject).Close(false);
                return (pivotVisualCases, cases, tableSheetVisualCases);
            }

            if (options.TableSheetRanges)
            {
                var cases = tableSheetVisualCases ?? EnumerateTableSheetVisualRanges(workbook).ToArray();
                foreach (var item in cases)
                {
                    var safeName = SanitizeFileName(item.Name);
                    var outPath = Path.Combine(outputDirectory, $"excel_{ordinal:D2}_{safeName}.png");
                    ExportExcelRangeToPng(workbookObject, item.Sheet.Name, item.Range, outPath);
                    Console.WriteLine($"  [{ordinal:D2}] {item.Sheet.Name}!{item.Range} ({item.RangeSource}) -> {Path.GetFileName(outPath)}");
                    ordinal++;
                }

                ((dynamic)workbookObject).Close(false);
                return (pivotVisualCases, pivotSheetVisualCases, cases);
            }

            foreach (var sheet in workbook.Sheets.Where(sheet => !sheet.IsHidden && !sheet.IsVeryHidden))
            {
                // Honor --capture-range when exporting Excel reference PNGs so the crop matches
                // the FreeX render exactly. Re-parse per sheet to anchor to the right SheetId.
                GridRange? captureRangeForExcel = null;
                if (!string.IsNullOrWhiteSpace(options.CaptureRangeRaw))
                {
                    try
                    {
                        captureRangeForExcel = GridRange.ParseCellOrRange(
                            options.CaptureRangeRaw.Replace("$", "", StringComparison.Ordinal).Trim(),
                            sheet.Id);
                    }
                    catch { }
                }

                var range = captureRangeForExcel ?? sheet.GetUsedRange();
                if (range is null)
                    continue;

                var safeName = SanitizeFileName(sheet.Name);
                var outPath = Path.Combine(outputDirectory, $"excel_{ordinal:D2}_{safeName}.png");
                ExportExcelRangeToPng(workbookObject, sheet.Name, range.Value, outPath);
                Console.WriteLine($"  [{ordinal:D2}] {sheet.Name}!{range.Value} -> {Path.GetFileName(outPath)}");
                ordinal++;
            }

            ((dynamic)workbookObject).Close(false);
            return (pivotVisualCases, pivotSheetVisualCases, tableSheetVisualCases);
        }
        finally
        {
            try
            {
                if (workbookObject is not null)
                    ((dynamic)workbookObject).Close(false);
            }
            catch { }

            try
            {
                if (excel is not null)
                    ((dynamic)excel).Quit();
            }
            catch { }

            ReleaseComObject(workbookObject);
            ReleaseComObject(workbooks);
            ReleaseComObject(excel);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static IReadOnlyDictionary<int, PngDimensions> LoadExcelReferenceDimensions(string excelInputDir)
    {
        var dimensions = new SortedDictionary<int, PngDimensions>();
        foreach (var file in Directory.EnumerateFiles(excelInputDir, "excel_*.png"))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var parts = stem.Split('_');
            if (parts.Length >= 3 && int.TryParse(parts[1], out var ordinal))
                dimensions[ordinal] = GetPngDimensions(file);
        }

        if (dimensions.Count > 0)
            Console.WriteLine($"  Excel reference dimensions loaded: {dimensions.Count}");

        return dimensions;
    }

    private static IEnumerable<PivotVisualCase> ResolveExcelPivotVisualRanges(
        object workbook,
        IReadOnlyList<PivotVisualCase> cases)
    {
        foreach (var item in cases)
        {
            if (TryGetExcelPivotTableRange(workbook, item.Sheet.Name, item.Pivot.Name, item.Sheet.Id, out var excelRange))
            {
                if ((excelRange.RowCount > 1 || excelRange.ColCount > 1) ||
                    item.Range.RowCount == 1 && item.Range.ColCount == 1)
                {
                    yield return item with { Range = excelRange, RangeSource = "ExcelTableRange2" };
                }
                else
                {
                    yield return item with { RangeSource = $"{item.RangeSource}; ExcelTableRange2SingleCell" };
                }
            }
            else
            {
                yield return item;
            }
        }
    }

    private static bool TryGetExcelPivotTableRange(
        object workbook,
        string sheetName,
        string pivotName,
        SheetId sheetId,
        out GridRange range)
    {
        object? worksheet = null;
        object? pivotTables = null;
        object? pivotTable = null;
        object? tableRange = null;
        try
        {
            worksheet = ((dynamic)workbook).Worksheets[sheetName];
            pivotTables = ((dynamic)worksheet).PivotTables();
            pivotTable = ((dynamic)pivotTables).Item(pivotName);
            tableRange = ((dynamic)pivotTable).TableRange2;
            var address = Convert.ToString(((dynamic)tableRange).Address, CultureInfo.InvariantCulture);
            if (TryParseExcelRangeAddress(address, sheetId, out range))
                return true;

            ReleaseComObject(tableRange);
            tableRange = ((dynamic)pivotTable).TableRange1;
            address = Convert.ToString(((dynamic)tableRange).Address, CultureInfo.InvariantCulture);
            return TryParseExcelRangeAddress(address, sheetId, out range);
        }
        catch
        {
            range = default;
            return false;
        }
        finally
        {
            ReleaseComObject(tableRange);
            ReleaseComObject(pivotTable);
            ReleaseComObject(pivotTables);
            ReleaseComObject(worksheet);
        }
    }

    private static bool TryParseExcelRangeAddress(string? address, SheetId sheetId, out GridRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(address) || address.Contains(',', StringComparison.Ordinal))
            return false;

        var normalized = address.Replace("$", "", StringComparison.Ordinal).Trim();
        var bang = normalized.LastIndexOf('!');
        if (bang >= 0)
            normalized = normalized[(bang + 1)..];

        normalized = normalized.Trim('\'');
        try
        {
            range = GridRange.ParseCellOrRange(normalized, sheetId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ExportExcelRangeToPng(object workbook, string sheetName, GridRange range, string outPath)
    {
        Exception? lastFailure = null;
        foreach (var attempt in new[]
        {
            new ExcelRangePngExportAttempt(SelectRange: true, PictureFormat: XlPicture, Label: "selected picture"),
            new ExcelRangePngExportAttempt(SelectRange: true, PictureFormat: XlBitmap, Label: "selected bitmap"),
            new ExcelRangePngExportAttempt(SelectRange: false, PictureFormat: XlPicture, Label: "direct picture"),
        })
        {
            try
            {
                ExportExcelRangeToPngAttempt(workbook, sheetName, range, outPath, attempt);
                if (!IsLikelyBlankReferencePng(outPath))
                    return;

                lastFailure = new InvalidOperationException(
                    $"Excel range PNG export produced a blank-looking image using {attempt.Label}.");
            }
            catch (Exception ex)
            {
                lastFailure = ex;
            }
        }

        throw new InvalidOperationException(
            $"Excel range PNG export failed for {sheetName}!{range}.", lastFailure);
    }

    private static void ExportExcelRangeToPngAttempt(
        object workbook,
        string sheetName,
        GridRange range,
        string outPath,
        ExcelRangePngExportAttempt attempt)
    {
        object? worksheet = null;
        object? excelRange = null;
        object? activeWindow = null;
        object? chartObjects = null;
        object? chartObject = null;
        object? chart = null;
        try
        {
            worksheet = ((dynamic)workbook).Worksheets[sheetName];
            excelRange = ((dynamic)worksheet).Range(range.ToString());
            ((dynamic)worksheet).Activate();
            if (attempt.SelectRange)
            {
                ((dynamic)excelRange).Select();
                var app = ((dynamic)workbook).Application;
                activeWindow = ((dynamic)app).ActiveWindow;
                if (activeWindow is not null)
                {
                    ((dynamic)activeWindow).ScrollRow = Math.Max(1, (int)range.Start.Row);
                    ((dynamic)activeWindow).ScrollColumn = Math.Max(1, (int)range.Start.Col);
                }
            }

            ((dynamic)excelRange).CopyPicture(XlScreen, attempt.PictureFormat);
            System.Threading.Thread.Sleep(150);
            if (TrySaveClipboardImageToPng(outPath))
                return;

            chartObjects = ((dynamic)worksheet).ChartObjects();
            chartObject = ((dynamic)chartObjects).Add(0, 0, Math.Max(120, (double)((dynamic)excelRange).Width), Math.Max(80, (double)((dynamic)excelRange).Height));
            ((dynamic)chartObject).Activate();
            chart = ((dynamic)chartObject).Chart;
            try
            {
                ((dynamic)chart).ChartArea.Clear();
            }
            catch
            {
                // Some Excel versions reject Clear on an empty chart; paste can still proceed.
            }

            ((dynamic)chart).Paste();
            System.Threading.Thread.Sleep(150);
            ((dynamic)chart).Export(outPath, "PNG", false);
        }
        finally
        {
            try
            {
                if (chartObject is not null)
                    ((dynamic)chartObject).Delete();
            }
            catch
            {
                // Best-effort cleanup; export will retry with a fresh chart object.
            }

            ReleaseComObject(chart);
            ReleaseComObject(chartObject);
            ReleaseComObject(chartObjects);
            ReleaseComObject(activeWindow);
            ReleaseComObject(excelRange);
            ReleaseComObject(worksheet);
        }
    }

    private static bool TrySaveClipboardImageToPng(string outPath)
    {
        try
        {
            if (!Clipboard.ContainsImage())
                return TrySaveClipboardEnhancedMetafileToPng(outPath);

            var image = Clipboard.GetImage();
            if (image is not null)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = File.Create(outPath);
                encoder.Save(stream);
                return true;
            }

            return TrySaveClipboardEnhancedMetafileToPng(outPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySaveClipboardEnhancedMetafileToPng(string outPath)
    {
        var opened = false;
        try
        {
            opened = OpenClipboard(IntPtr.Zero);
            if (!opened)
                return false;

            var clipboardHandle = GetClipboardData(14); // CF_ENHMETAFILE
            if (clipboardHandle == IntPtr.Zero)
                return false;

            var ownedHandle = CopyEnhMetaFile(clipboardHandle, null);
            if (ownedHandle == IntPtr.Zero)
                return false;

            using var metafile = new System.Drawing.Imaging.Metafile(ownedHandle, deleteEmf: true);
            var width = Math.Max(1, metafile.Width);
            var height = Math.Max(1, metafile.Height);
            using var bitmap = new System.Drawing.Bitmap(width, height);
            bitmap.SetResolution(96, 96);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.White);
                graphics.DrawImage(metafile, 0, 0, width, height);
            }

            bitmap.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (opened)
                CloseClipboard();
        }
    }

    private static bool IsLikelyBlankReferencePng(string path)
    {
        if (!File.Exists(path))
            return true;

        var bitmap = LoadBitmap(path);
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        if (width <= 1 || height <= 1)
            return true;

        var pixels = GetBgra32Pixels(bitmap, width, height);
        var colors = new HashSet<int>();
        var opaquePixels = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset + 3] > 16)
                opaquePixels++;

            var argb =
                pixels[offset + 3] << 24 |
                pixels[offset + 2] << 16 |
                pixels[offset + 1] << 8 |
                pixels[offset];
            colors.Add(argb);
        }

        var opaqueRatio = (double)opaquePixels / (width * height);
        return colors.Count <= 24 || opaqueRatio < 0.15;
    }

    // -----------------------------------------------------------------------
    // Diff mode: compare FreeX PNGs against Excel PNGs
    // -----------------------------------------------------------------------
    private static string RunDiffMode(
        Workbook workbook,
        IReadOnlyList<SheetResult> results,
        string freexOutputDir,
        string excelInputDir,
        GridImageCompareOptions options)
    {
        Console.WriteLine($"  Diff mode: Excel PNGs found in {excelInputDir}");

        // Discover Excel PNGs: format excel_NN_<sheetToken>.png
        var excelPngs = new SortedDictionary<int, string>();
        foreach (var file in Directory.EnumerateFiles(excelInputDir, "excel_*.png"))
        {
            var stem  = Path.GetFileNameWithoutExtension(file);
            var parts = stem.Split('_');
            if (parts.Length >= 3 && int.TryParse(parts[1], out var nn))
                excelPngs[nn] = file;
        }
        Console.WriteLine($"  Excel PNGs discovered: {excelPngs.Count}");

        var rows = new List<DiffRow>();

        foreach (var r in results)
        {
            if (!r.Rendered)
            {
                rows.Add(new DiffRow
                {
                    NN        = r.NN,
                    SheetName = r.SheetName,
                    Status    = r.Skipped ? "SKIPPED" : "ERROR",
                    Error     = r.Skipped ? r.SkipReason : r.Error,
                    DiffPercent = -1,
                });
                continue;
            }

            var row = new DiffRow
            {
                NN         = r.NN,
                SheetName  = r.SheetName,
                FreeXPng   = r.FreeXPngPath,
                Status     = "OK",
            };

            if (excelPngs.TryGetValue(r.NN, out var excelPng) && File.Exists(excelPng))
            {
                row.ExcelPng = excelPng;
                try
                {
                    row.ExcelDimensions = GetPngDimensions(excelPng);
                    row.FreeXDimensions = GetPngDimensions(r.FreeXPngPath!);
                    row.DimensionMismatch = row.ExcelDimensions != row.FreeXDimensions;
                    if (row.DimensionMismatch)
                    {
                        row.Status = options.FailOnDimensionMismatch ? "DIM_FAIL" : "DIM_WARN";
                        row.Error = options.FailOnDimensionMismatch
                            ? $"Dimension mismatch: Excel {row.ExcelDimensions}, FreeX {row.FreeXDimensions}. Mean pixel diff uses 800x600 compatibility resize fallback only."
                            : $"Dimension mismatch warning: Excel {row.ExcelDimensions}, FreeX {row.FreeXDimensions}. Mean pixel diff uses 800x600 compatibility resize fallback only.";
                        if (options.FailOnDimensionMismatch)
                            r.ComparisonFailed = true;
                    }
                    else
                    {
                        var exactPixelMetrics = ComputeExactPixelDiff(excelPng, r.FreeXPngPath!, options.PixelTolerance);
                        row.ExactPixelMetrics = exactPixelMetrics;
                        if (options.StrictPixelThresholdPercent is { } strictPixelThreshold &&
                            exactPixelMetrics.ChangedPixelPercent > strictPixelThreshold)
                        {
                            row.Status = "PIX_FAIL";
                            row.Error = $"Strict pixel threshold exceeded: changed pixels {exactPixelMetrics.ChangedPixelPercent:F2}% > {strictPixelThreshold:F2}% with channel tolerance {options.PixelTolerance}.";
                            r.ComparisonFailed = true;
                        }
                    }

                    row.DiffPercent = ComputeMeanPixelDiff(excelPng, r.FreeXPngPath!);
                    r.DiffPercent = row.DiffPercent;
                }
                catch (Exception ex)
                {
                    row.Error       = $"Diff failed: {ex.Message}";
                    row.DiffPercent = 100.0;
                    r.DiffPercent = row.DiffPercent;
                }
            }
            else
            {
                row.DiffPercent = -1;
                row.Error = "No matching Excel PNG";
            }

            rows.Add(row);
        }

        // Write composites for worst-diff sheets
        const int WorstCount = 10;
        var validDiffs = rows
            .Where(r => r.DiffPercent >= 0 && r.ExcelPng != null)
            .OrderByDescending(r => r.DiffPercent)
            .Take(WorstCount)
            .ToList();

        foreach (var row in validDiffs)
        {
            var compositePath = Path.Combine(freexOutputDir, $"worst_{row.NN:D2}.png");
            try
            {
                WriteSideBySide(row.ExcelPng!, row.FreeXPng, compositePath, row);
                Console.WriteLine($"  worst_{row.NN:D2}.png  diff={row.DiffPercent:F1}%  {row.SheetName}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Composite failed NN={row.NN}: {ex.Message}");
            }
        }

        // Write REPORT.txt ranked by diff%
        var sb = new StringBuilder();
        sb.AppendLine("FreeX vs Excel Sheet Fidelity Report (GridView renderer)");
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Threshold: {options.ThresholdPercent:F2}%");
        sb.AppendLine(options.FailOnDimensionMismatch
            ? "Dimension gate: native Excel and FreeX PNG dimensions must match exactly."
            : "Dimension check: native Excel and FreeX PNG dimensions are reported; pass --fail-on-dimension-mismatch to make mismatches fail.");
        sb.AppendLine("Mean pixel diff: 800x600 compatibility resize fallback.");
        sb.AppendLine($"Exact same-size pixel metrics: alpha-composited over white; changed pixels use max channel delta > {options.PixelTolerance}.");
        sb.AppendLine(options.StrictPixelThresholdPercent is { } strictThreshold
            ? $"Strict pixel gate: changed pixels above tolerance must be <= {strictThreshold:F2}%."
            : "Strict pixel gate: not enabled; pass --strict-pixel-threshold <percent> to fail on exact changed-pixel percentage.");
        sb.AppendLine();
        sb.AppendLine("=== RANKED BY DIFF% (worst first) ===");
        sb.AppendLine($"{"NN",-4}  {"Diff%",7}  {"Status",-8}  {"PNG dimensions",-34}  Sheet");
        sb.AppendLine(new string('-', 118));

        foreach (var r in rows.OrderByDescending(r => r.DiffPercent))
        {
            var diffStr = r.DiffPercent >= 0 ? $"{r.DiffPercent:F1}%" : "  N/A";
            var status = EffectiveStatus(r, options);
            sb.AppendLine($"{r.NN.ToString("D2"),-4}  {diffStr,7}  {status,-8}  {FormatDimensions(r),-34}  {r.SheetName}");
            if (r.ExactPixelMetrics is { } metrics)
                sb.AppendLine($"       Exact pixels: mean={metrics.MeanDiffPercent:F3}% changed>{metrics.PixelTolerance}={metrics.ChangedPixelPercent:F2}% maxDelta={metrics.MaxChannelDelta}");
            if (r.Error != null)
                sb.AppendLine($"       NOTE: {r.Error}");

            var source = results.FirstOrDefault(result => result.NN == r.NN);
            if (!string.IsNullOrWhiteSpace(source?.PivotDropdownSummary))
                sb.AppendLine($"       Pivot dropdowns: {source.PivotDropdownSummary}");
        }

        var reportPath = Path.Combine(freexOutputDir, "REPORT.txt");
        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
        var metricsPath = WriteMetricsJson(rows, results, freexOutputDir, options);
        Console.WriteLine($"  Metrics JSON: {Path.GetFileName(metricsPath)}");
        return reportPath;
    }

    private static string WriteMetricsJson(
        IReadOnlyList<DiffRow> rows,
        IReadOnlyList<SheetResult> results,
        string freexOutputDir,
        GridImageCompareOptions options)
    {
        var resultByOrdinal = results.ToDictionary(r => r.NN);
        var rowsWithDiff = rows.Where(r => r.DiffPercent >= 0 && r.ExcelPng is not null).ToArray();
        var path = Path.Combine(freexOutputDir, "metrics.json");

        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("generatedAt", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));

        writer.WriteStartObject("options");
        writer.WriteNumber("thresholdPercent", options.ThresholdPercent);
        writer.WriteBoolean("failOnDimensionMismatch", options.FailOnDimensionMismatch);
        writer.WriteNumber("pixelTolerance", options.PixelTolerance);
        WriteNullableNumber(writer, "strictPixelThresholdPercent", options.StrictPixelThresholdPercent);
        writer.WriteEndObject();

        writer.WriteStartObject("summary");
        writer.WriteNumber("rows", rows.Count);
        writer.WriteNumber("comparedRows", rowsWithDiff.Length);
        writer.WriteNumber("failedRows", rows.Count(r => IsEffectiveFailure(r, options)));
        writer.WriteNumber("dimensionMismatches", rows.Count(r => r.DimensionMismatch));
        WriteNullableNumber(writer, "maxMeanDiffPercent", rowsWithDiff.Length == 0 ? null : rowsWithDiff.Max(r => r.DiffPercent));
        WriteNullableNumber(writer, "maxExactMeanDiffPercent", rowsWithDiff.Select(r => r.ExactPixelMetrics?.MeanDiffPercent).Where(v => v.HasValue).DefaultIfEmpty().Max());
        WriteNullableNumber(writer, "maxChangedPixelPercent", rowsWithDiff.Select(r => r.ExactPixelMetrics?.ChangedPixelPercent).Where(v => v.HasValue).DefaultIfEmpty().Max());
        writer.WriteEndObject();

        writer.WriteStartArray("rows");
        foreach (var row in rows.OrderBy(r => r.NN))
        {
            resultByOrdinal.TryGetValue(row.NN, out var source);

            writer.WriteStartObject();
            writer.WriteNumber("nn", row.NN);
            writer.WriteString("sheetName", row.SheetName);
            writer.WriteString("status", row.Status);
            writer.WriteString("effectiveStatus", EffectiveStatus(row, options));
            WriteNullableNumber(writer, "meanDiffPercent", row.DiffPercent >= 0 ? row.DiffPercent : null);
            writer.WriteString("excelPng", row.ExcelPng is null ? null : Path.GetFileName(row.ExcelPng));
            writer.WriteString("freeXPng", row.FreeXPng is null ? null : Path.GetFileName(row.FreeXPng));
            WriteDimensions(writer, "excelDimensions", row.ExcelDimensions);
            WriteDimensions(writer, "freeXDimensions", row.FreeXDimensions);
            writer.WriteBoolean("dimensionMismatch", row.DimensionMismatch);
            if (row.ExactPixelMetrics is { } metrics)
            {
                writer.WriteStartObject("exactPixelMetrics");
                writer.WriteNumber("meanDiffPercent", metrics.MeanDiffPercent);
                writer.WriteNumber("changedPixelPercent", metrics.ChangedPixelPercent);
                writer.WriteNumber("maxChannelDelta", metrics.MaxChannelDelta);
                writer.WriteNumber("pixelTolerance", metrics.PixelTolerance);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("exactPixelMetrics");
            }

            writer.WriteString("error", row.Error);
            writer.WriteString("pivotDropdowns", source?.PivotDropdownSummary);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
        return path;
    }

    // -----------------------------------------------------------------------
    // Image utilities (adapted from FreeX.SheetImageCompare/Program.cs)
    // -----------------------------------------------------------------------
    private static BitmapSource LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var source  = decoder.Frames[0];
        return source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
    }

    private static PngDimensions GetPngDimensions(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        return new PngDimensions(frame.PixelWidth, frame.PixelHeight);
    }

    private static double ComputeMeanPixelDiff(string excelPath, string freexPath)
    {
        const int W = 800, H = 600;

        var excelBmp = ResizeTo(LoadBitmap(excelPath), W, H);
        var freexBmp = File.Exists(freexPath)
            ? ResizeTo(LoadBitmap(freexPath), W, H)
            : CreateWhite(W, H);

        var excelPixels = GetBgra32Pixels(excelBmp, W, H);
        var freexPixels = GetBgra32Pixels(freexBmp, W, H);

        long totalDiff = 0;
        int  pixelCount = W * H;
        for (int i = 0; i < pixelCount; i++)
        {
            int    offset = i * 4;
            double ea     = excelPixels[offset + 3] / 255.0;
            double fa     = freexPixels[offset + 3] / 255.0;

            for (int c = 0; c < 3; c++)
            {
                double eVal = excelPixels[offset + c] * ea + 255 * (1 - ea);
                double fVal = freexPixels[offset + c] * fa + 255 * (1 - fa);
                totalDiff += (long)Math.Abs(eVal - fVal);
            }
        }

        double maxDiff = (double)pixelCount * 3 * 255;
        return totalDiff / maxDiff * 100.0;
    }

    private static PixelDiffMetrics ComputeExactPixelDiff(string excelPath, string freexPath, int pixelTolerance)
    {
        var excelBmp = LoadBitmap(excelPath);
        var freexBmp = File.Exists(freexPath)
            ? LoadBitmap(freexPath)
            : CreateWhite(excelBmp.PixelWidth, excelBmp.PixelHeight);
        if (excelBmp.PixelWidth != freexBmp.PixelWidth ||
            excelBmp.PixelHeight != freexBmp.PixelHeight)
        {
            throw new InvalidOperationException("Exact pixel diff requires matching PNG dimensions.");
        }

        var width = excelBmp.PixelWidth;
        var height = excelBmp.PixelHeight;
        var excelPixels = GetBgra32Pixels(excelBmp, width, height);
        var freexPixels = GetBgra32Pixels(freexBmp, width, height);

        long totalDiff = 0;
        var changedPixels = 0;
        var maxChannelDelta = 0;
        var pixelCount = width * height;
        for (var index = 0; index < pixelCount; index++)
        {
            var offset = index * 4;
            var pixelMaxDelta = 0;
            var excelAlpha = excelPixels[offset + 3] / 255.0;
            var freexAlpha = freexPixels[offset + 3] / 255.0;

            for (var channel = 0; channel < 3; channel++)
            {
                var excelValue = excelPixels[offset + channel] * excelAlpha + 255 * (1 - excelAlpha);
                var freexValue = freexPixels[offset + channel] * freexAlpha + 255 * (1 - freexAlpha);
                var delta = (int)Math.Round(Math.Abs(excelValue - freexValue));
                totalDiff += delta;
                pixelMaxDelta = Math.Max(pixelMaxDelta, delta);
                maxChannelDelta = Math.Max(maxChannelDelta, delta);
            }

            if (pixelMaxDelta > pixelTolerance)
                changedPixels++;
        }

        var maxDiff = (double)pixelCount * 3 * 255;
        return new PixelDiffMetrics(
            totalDiff / maxDiff * 100.0,
            (double)changedPixels / pixelCount * 100.0,
            maxChannelDelta,
            pixelTolerance);
    }

    private static BitmapSource ResizeTo(BitmapSource source, int w, int h)
    {
        var visual = new System.Windows.Media.DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            double scale = Math.Min((double)w / source.PixelWidth, (double)h / source.PixelHeight);
            double dw    = source.PixelWidth  * scale;
            double dh    = source.PixelHeight * scale;
            var    bounds = new Rect((w - dw) / 2, (h - dh) / 2, dw, dh);
            ctx.DrawImage(source, bounds);
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
    }

    private static BitmapSource CreateWhite(int w, int h)
    {
        var visual = new System.Windows.Media.DrawingVisual();
        using (var ctx = visual.RenderOpen())
            ctx.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
    }

    private static byte[] GetBgra32Pixels(BitmapSource bmp, int w, int h)
    {
        var pixels = new byte[w * h * 4];
        bmp.CopyPixels(pixels, w * 4, 0);
        return pixels;
    }

    private static void WriteSideBySide(string excelPath, string? freexPath, string outPath, DiffRow row)
    {
        const int ThumbW  = 700, ThumbH  = 500;
        const int Padding = 10;
        const int LabelH  = 30;
        int totalW = ThumbW * 2 + Padding * 3;
        int totalH = ThumbH + Padding * 2 + LabelH * 2;

        var excelBmp = File.Exists(excelPath)
            ? ResizeTo(LoadBitmap(excelPath), ThumbW, ThumbH)
            : CreateWhite(ThumbW, ThumbH);
        var freexBmp = freexPath != null && File.Exists(freexPath)
            ? ResizeTo(LoadBitmap(freexPath), ThumbW, ThumbH)
            : CreateWhite(ThumbW, ThumbH);

        var visual = new System.Windows.Media.DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(240, 240, 240)), null, new Rect(0, 0, totalW, totalH));

            var headerText = $"NN={row.NN:D2}  {row.SheetName}  diff={row.DiffPercent:F1}%";
            ctx.DrawText(MakeText(headerText, 13, Brushes.Black, FontWeights.SemiBold), new Point(Padding, 4));

            int yImg   = LabelH;
            int xLeft  = Padding;
            int xRight = Padding * 2 + ThumbW;

            ctx.DrawText(MakeText($"Excel (ground truth)  {row.ExcelDimensions}", 11, Brushes.DarkSlateGray, FontWeights.Normal), new Point(xLeft,  yImg + ThumbH + 4));
            ctx.DrawText(MakeText($"FreeX GridView  {row.FreeXDimensions}", 11, Brushes.DarkSlateGray, FontWeights.Normal), new Point(xRight, yImg + ThumbH + 4));
            ctx.DrawText(MakeText("Mean diff uses 800x600 compatibility resize fallback", 10, Brushes.DarkSlateGray, FontWeights.Normal), new Point(Padding, yImg + ThumbH + LabelH + 4));

            ctx.DrawImage(excelBmp, new Rect(xLeft,  yImg, ThumbW, ThumbH));
            ctx.DrawImage(freexBmp, new Rect(xRight, yImg, ThumbW, ThumbH));
        }

        var rtb = new RenderTargetBitmap(totalW, totalH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(outPath);
        encoder.Save(stream);
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is null)
            return;

        try
        {
            if (Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }
        catch
        {
            // Best-effort cleanup for visual comparison tooling.
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CopyEnhMetaFile(IntPtr hemfSrc, string? fileName);

    private static FormattedText MakeText(string text, double size, Brush brush, FontWeight weight) =>
        new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            1.0);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private static string SanitizeFileName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-')
                sb.Append(ch);
            else if (ch == ' ' || ch == '_')
                sb.Append('_');
            // drop other chars
        }
        return sb.Length > 0 ? sb.ToString() : "sheet";
    }

    private static string FormatDimensions(DiffRow row)
    {
        if (row.ExcelDimensions is null && row.FreeXDimensions is null)
            return "N/A";

        return $"Excel {row.ExcelDimensions?.ToString() ?? "N/A"}; FreeX {row.FreeXDimensions?.ToString() ?? "N/A"}";
    }

    private static string EffectiveStatus(DiffRow row, GridImageCompareOptions options)
    {
        if (row.DimensionMismatch)
            return options.FailOnDimensionMismatch ? "DIM_FAIL" : "DIM_WARN";

        return row.Status == "OK" && row.DiffPercent > options.ThresholdPercent
            ? "FAIL"
            : row.Status;
    }

    private static bool IsEffectiveFailure(DiffRow row, GridImageCompareOptions options) =>
        EffectiveStatus(row, options) is "FAIL" or "DIM_FAIL" or "PIX_FAIL" or "ERROR";

    private static void WriteNullableNumber(Utf8JsonWriter writer, string propertyName, double? value)
    {
        if (value.HasValue)
            writer.WriteNumber(propertyName, value.Value);
        else
            writer.WriteNull(propertyName);
    }

    private static void WriteDimensions(Utf8JsonWriter writer, string propertyName, PngDimensions? dimensions)
    {
        if (dimensions is not { } value)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteStartObject(propertyName);
        writer.WriteNumber("width", value.Width);
        writer.WriteNumber("height", value.Height);
        writer.WriteString("text", value.ToString());
        writer.WriteEndObject();
    }

    private static string DescribePivotDropdownTargets(Workbook workbook, Sheet sheet)
    {
        var targets = FreeX.App.Host.PivotHeaderDropdownPlanner.BuildTargets(workbook, sheet);
        if (targets.Count == 0)
            return "";

        return string.Join("; ", targets.Select(target =>
        {
            var active = target.IsActive ? "*" : "";
            return $"{target.Axis}:{ToA1(target.HeaderCell)}:{target.FieldCaption}{active}";
        }));
    }

    private static string ToA1(CellAddress address) =>
        $"{ColumnName(address.Col)}{address.Row}";

    private static string ColumnName(uint column)
    {
        var value = (int)column;
        if (value <= 0)
            return "?";

        var builder = new StringBuilder();
        while (value > 0)
        {
            value--;
            builder.Insert(0, (char)('A' + value % 26));
            value /= 26;
        }

        return builder.ToString();
    }

    private static string Trunc(string? s, int max)
    {
        s ??= "";
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}

// -----------------------------------------------------------------------
// Data structures
// -----------------------------------------------------------------------
internal sealed class SheetResult
{
    public int     NN              { get; set; }
    public string  SheetName       { get; set; } = "";
    public string? FreeXPngPath    { get; set; }
    public string  FreeXPngFileName { get; set; } = "";
    public bool    Rendered        { get; set; }
    public bool    Skipped         { get; set; }
    public string? SkipReason      { get; set; }
    public string? Error           { get; set; }
    public double  DiffPercent     { get; set; } = -1;
    public bool    ComparisonFailed { get; set; }
    public string? PivotDropdownSummary { get; set; }
}

internal sealed class DiffRow
{
    public int     NN          { get; set; }
    public string  SheetName   { get; set; } = "";
    public string? FreeXPng    { get; set; }
    public string? ExcelPng    { get; set; }
    public string  Status      { get; set; } = "";
    public string? Error       { get; set; }
    public double  DiffPercent { get; set; } = -1;
    public PngDimensions? ExcelDimensions { get; set; }
    public PngDimensions? FreeXDimensions { get; set; }
    public bool    DimensionMismatch { get; set; }
    public PixelDiffMetrics? ExactPixelMetrics { get; set; }
}

internal readonly record struct PngDimensions(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";
}

internal readonly record struct PixelDiffMetrics(
    double MeanDiffPercent,
    double ChangedPixelPercent,
    int MaxChannelDelta,
    int PixelTolerance);

internal sealed record GridImageCompareOptions(
    string? WorkbookPath,
    string? OutputDirectory,
    bool ExportExcelPngs,
    bool PivotRangesOnly,
    bool PivotSheetRanges,
    bool TableSheetRanges,
    double ThresholdPercent,
    bool FailOnDimensionMismatch,
    int PixelTolerance,
    double? StrictPixelThresholdPercent,
    bool ShowAllComments,
    string? CaptureRangeRaw)
{
    public static GridImageCompareOptions Parse(string[] args)
    {
        string? workbookPath = null;
        string? outputDirectory = null;
        var exportExcelPngs = false;
        var pivotRangesOnly = false;
        var pivotSheetRanges = false;
        var tableSheetRanges = false;
        var thresholdPercent = 12.0;
        var failOnDimensionMismatch = false;
        var pixelTolerance = 8;
        double? strictPixelThresholdPercent = null;
        var showAllComments = false;
        string? captureRangeRaw = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--out":
                    outputDirectory = index + 1 < args.Length ? args[++index] : null;
                    break;
                case "--export-excel-pngs":
                    exportExcelPngs = true;
                    break;
                case "--pivot-ranges":
                    pivotRangesOnly = true;
                    break;
                case "--pivot-sheet-ranges":
                    pivotSheetRanges = true;
                    break;
                case "--table-sheet-ranges":
                    tableSheetRanges = true;
                    break;
                case "--fail-on-dimension-mismatch":
                    failOnDimensionMismatch = true;
                    break;
                case "--threshold":
                    if (index + 1 < args.Length &&
                        double.TryParse(args[++index], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                        thresholdPercent = parsed;
                    break;
                case "--pixel-tolerance":
                    if (index + 1 < args.Length &&
                        int.TryParse(args[++index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTolerance))
                        pixelTolerance = Math.Clamp(parsedTolerance, 0, 255);
                    break;
                case "--strict-pixel-threshold":
                    if (index + 1 < args.Length &&
                        double.TryParse(args[++index], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedStrictThreshold))
                        strictPixelThresholdPercent = Math.Clamp(parsedStrictThreshold, 0.0, 100.0);
                    break;
                case "--show-all-comments":
                    showAllComments = true;
                    break;
                case "--capture-range":
                    captureRangeRaw = index + 1 < args.Length ? args[++index] : null;
                    break;
                default:
                    if (!args[index].StartsWith("-", StringComparison.Ordinal))
                        workbookPath ??= args[index];
                    break;
            }
        }

        if (pivotSheetRanges)
            pivotRangesOnly = false;
        if (tableSheetRanges)
        {
            pivotRangesOnly = false;
            pivotSheetRanges = false;
        }

        return new GridImageCompareOptions(workbookPath, outputDirectory, exportExcelPngs, pivotRangesOnly, pivotSheetRanges, tableSheetRanges, thresholdPercent, failOnDimensionMismatch, pixelTolerance, strictPixelThresholdPercent, showAllComments, captureRangeRaw);
    }
}

internal sealed record PivotVisualCase(
    Sheet Sheet,
    PivotTableModel Pivot,
    GridRange Range,
    string RangeSource);

internal sealed record SheetVisualCase(
    Sheet Sheet,
    GridRange Range,
    string Name,
    string RangeSource);

internal sealed record ExcelRangePngExportAttempt(
    bool SelectRange,
    int PictureFormat,
    string Label);
