using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.UI;
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
    private const int XlScreen = 1;
    private const int XlPicture = -4147;

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

        if (options.ExportExcelPngs)
        {
            Console.WriteLine("\n[2/4] Exporting Excel PNGs...");
            ExportExcelReferencePngs(xlsxPath, workbook, excelInputDir, options);
        }

        // ------------------------------------------------------------------
        // 3. Render each visible sheet to PNG using GridView
        // ------------------------------------------------------------------
        Console.WriteLine(options.ExportExcelPngs
            ? "\n[3/4] Rendering sheets via GridView..."
            : "\n[2/3] Rendering sheets via GridView...");

        var sheetResults = new List<SheetResult>();
        int sheetIndex = 1;

        if (options.PivotRangesOnly)
        {
            foreach (var (sheet, pivot, range) in EnumeratePivotVisualRanges(workbook))
            {
                var safeName = SanitizeFileName($"{sheet.Name}_{pivot.Name}");
                var outFileName = $"freex_{sheetIndex:D2}_{safeName}.png";
                var outPath = Path.Combine(freexOutputDir, outFileName);

                Console.Write($"  [{sheetIndex:D2}] {sheet.Name}!{range} ({pivot.Name}) ... ");

                var result = new SheetResult
                {
                    NN = sheetIndex,
                    SheetName = $"{sheet.Name} - {pivot.Name}",
                    FreeXPngPath = outPath,
                    FreeXPngFileName = outFileName,
                };

                try
                {
                    RenderSheetToGridViewPng(workbook, sheet, viewportService, outPath, range);
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
            };

            try
            {
                RenderSheetToGridViewPng(workbook, sheet, viewportService, outPath, captureRange: null);
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
            reportPath = RunDiffMode(workbook, sheetResults, freexOutputDir, excelInputDir, options.ThresholdPercent);
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
            ? sheetResults.Count(r => r.DiffPercent > options.ThresholdPercent)
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
        GridRange? captureRange)
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
        var rowHeaderWidth = captureRange is null ? GridView.RowHeaderWidth : 0.0;
        var colHeaderHeight = captureRange is null ? GridView.ColHeaderHeight : 0.0;

        // The viewport is capped so huge sheets don't explode. The cap must be at least as large as
        // the drawing-content cap; otherwise an object that sits just past MaxViewportWidth/Height
        // (e.g. a chart below a tall table) would be clipped again here even though the region above
        // already accounted for it. ExpandRegionForDrawingObjects bounds totalColWidth/totalRowHeight
        // to MaxDrawingContentWidth/Height, so these effective caps stay bounded.
        double maxViewW = Math.Max(MaxViewportWidth,  MaxDrawingContentWidth  + rowHeaderWidth  + 20);
        double maxViewH = Math.Max(MaxViewportHeight, MaxDrawingContentHeight + colHeaderHeight + 20);

        double viewW = Math.Min(maxViewW, totalColWidth  + rowHeaderWidth  + 20);
        double viewH = Math.Min(maxViewH, totalRowHeight + colHeaderHeight + 20);

        // Ensure minimum size
        viewW = Math.Max(viewW, 200);
        viewH = Math.Max(viewH, 100);

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
            MergedRegions   = sheet.MergedRegions,
            WorksheetBackground = null,
            ObjectDisplayMode = GridObjectDisplayMode.All,
            ShowGridLines   = sheet.ShowGridlines,
            ShowHeaders     = captureRange is null && sheet.ShowHeadings,
            ZoomFactor      = 1.0,
            WorksheetViewMode = sheet.ViewMode,
        };

        // Surface the sheet's AutoFilter range (worksheet-level OR carried inside a structured table)
        // so the GridView draws the filter-arrow dropdown buttons on the header row, matching Excel.
        grid.AutoFilterRange =
            FreeX.App.Host.AutoFilterDropdownPlanner.TryGetAutoFilterRange(sheet, out var autoFilterRange)
                ? autoFilterRange
                : null;

        // Step 4: Off-screen layout pass
        grid.Measure(new Size(viewW, viewH));
        grid.Arrange(new Rect(0, 0, viewW, viewH));
        grid.UpdateLayout();

        // Step 5: Rasterize to RenderTargetBitmap
        int pixelW = Math.Max(1, (int)Math.Ceiling(viewW * RenderScale));
        int pixelH = Math.Max(1, (int)Math.Ceiling(viewH * RenderScale));

        var rtb = new RenderTargetBitmap(pixelW, pixelH, RenderDpi, RenderDpi, PixelFormats.Pbgra32);

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
            ctx.DrawRectangle(vb, null, new Rect(0, 0, viewW * RenderScale, viewH * RenderScale));
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

    private static IEnumerable<(Sheet Sheet, PivotTableModel Pivot, GridRange Range)> EnumeratePivotVisualRanges(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.IsHidden || sheet.IsVeryHidden)
                continue;

            foreach (var pivot in sheet.PivotTables)
                yield return (sheet, pivot, pivot.LastRenderedRange ?? pivot.TargetRange);
        }
    }

    private static void ExportExcelReferencePngs(
        string workbookPath,
        Workbook workbook,
        string outputDirectory,
        GridImageCompareOptions options)
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
            app.Visible = false;
            app.DisplayAlerts = false;
            workbooks = app.Workbooks;
            workbookObject = ((dynamic)workbooks).Open(Path.GetFullPath(workbookPath), 0);

            var ordinal = 1;
            var ranges = options.PivotRangesOnly
                ? EnumeratePivotVisualRanges(workbook).ToArray()
                : workbook.Sheets
                    .Where(sheet => !sheet.IsHidden && !sheet.IsVeryHidden)
                    .Select(sheet => (Sheet: sheet, Pivot: (PivotTableModel?)null, Range: sheet.GetUsedRange()))
                    .Where(item => item.Range is not null)
                    .Select(item => (item.Sheet, Pivot: new PivotTableModel { Name = item.Sheet.Name }, Range: item.Range!.Value))
                    .ToArray();

            foreach (var (sheet, pivot, range) in ranges)
            {
                var safeName = SanitizeFileName($"{sheet.Name}_{pivot.Name}");
                var outPath = Path.Combine(outputDirectory, $"excel_{ordinal:D2}_{safeName}.png");
                ExportExcelRangeToPng(workbookObject, sheet.Name, range, outPath);
                Console.WriteLine($"  [{ordinal:D2}] {sheet.Name}!{range} -> {Path.GetFileName(outPath)}");
                ordinal++;
            }

            ((dynamic)workbookObject).Close(false);
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

    private static void ExportExcelRangeToPng(object workbook, string sheetName, GridRange range, string outPath)
    {
        object? worksheet = null;
        object? excelRange = null;
        object? chartObjects = null;
        object? chartObject = null;
        object? chart = null;
        try
        {
            worksheet = ((dynamic)workbook).Worksheets[sheetName];
            excelRange = ((dynamic)worksheet).Range(range.ToString());
            ((dynamic)excelRange).CopyPicture(XlScreen, XlPicture);
            chartObjects = ((dynamic)worksheet).ChartObjects();
            chartObject = ((dynamic)chartObjects).Add(0, 0, Math.Max(120, (double)((dynamic)excelRange).Width), Math.Max(80, (double)((dynamic)excelRange).Height));
            chart = ((dynamic)chartObject).Chart;
            ((dynamic)chart).Paste();
            ((dynamic)chart).Export(outPath, "PNG", false);
            ((dynamic)chartObject).Delete();
        }
        finally
        {
            ReleaseComObject(chart);
            ReleaseComObject(chartObject);
            ReleaseComObject(chartObjects);
            ReleaseComObject(excelRange);
            ReleaseComObject(worksheet);
        }
    }

    // -----------------------------------------------------------------------
    // Diff mode: compare FreeX PNGs against Excel PNGs
    // -----------------------------------------------------------------------
    private static string RunDiffMode(
        Workbook workbook,
        IReadOnlyList<SheetResult> results,
        string freexOutputDir,
        string excelInputDir,
        double thresholdPercent)
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
        sb.AppendLine($"Threshold: {thresholdPercent:F2}%");
        sb.AppendLine();
        sb.AppendLine("=== RANKED BY DIFF% (worst first) ===");
        sb.AppendLine($"{"NN",-4}  {"Diff%",7}  {"Status",-8}  Sheet");
        sb.AppendLine(new string('-', 80));

        foreach (var r in rows.OrderByDescending(r => r.DiffPercent))
        {
            var diffStr = r.DiffPercent >= 0 ? $"{r.DiffPercent:F1}%" : "  N/A";
            var status = r.DiffPercent > thresholdPercent ? "FAIL" : r.Status;
            sb.AppendLine($"{r.NN.ToString("D2"),-4}  {diffStr,7}  {status,-8}  {r.SheetName}");
            if (r.Error != null)
                sb.AppendLine($"       NOTE: {r.Error}");
        }

        var reportPath = Path.Combine(freexOutputDir, "REPORT.txt");
        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
        return reportPath;
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

            ctx.DrawText(MakeText("Excel (ground truth)",          11, Brushes.DarkSlateGray, FontWeights.Normal), new Point(xLeft,  yImg + ThumbH + 4));
            ctx.DrawText(MakeText($"FreeX GridView  diff={row.DiffPercent:F1}%", 11, Brushes.DarkSlateGray, FontWeights.Normal), new Point(xRight, yImg + ThumbH + 4));

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
}

internal sealed record GridImageCompareOptions(
    string? WorkbookPath,
    string? OutputDirectory,
    bool ExportExcelPngs,
    bool PivotRangesOnly,
    double ThresholdPercent)
{
    public static GridImageCompareOptions Parse(string[] args)
    {
        string? workbookPath = null;
        string? outputDirectory = null;
        var exportExcelPngs = false;
        var pivotRangesOnly = false;
        var thresholdPercent = 12.0;

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
                case "--threshold":
                    if (index + 1 < args.Length &&
                        double.TryParse(args[++index], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                        thresholdPercent = parsed;
                    break;
                default:
                    if (!args[index].StartsWith("-", StringComparison.Ordinal))
                        workbookPath ??= args[index];
                    break;
            }
        }

        return new GridImageCompareOptions(workbookPath, outputDirectory, exportExcelPngs, pivotRangesOnly, thresholdPercent);
    }
}
