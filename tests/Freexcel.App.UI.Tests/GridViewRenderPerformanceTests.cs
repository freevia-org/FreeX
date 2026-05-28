using System;
using System.IO;
using System.Reflection;
using Freexcel.App.UI;
using Freexcel.Core.Model;
using FluentAssertions;
using System.Windows;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewRenderPerformanceTests
{
    [Fact]
    public void RenderCells_UsesMetricDictionariesForExplicitBorderCells()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.cs"));
        var borderPass = source[
            source.IndexOf("// Pass 2: explicit cell borders", StringComparison.Ordinal)..
            source.IndexOf("// Pass 2b: comment/note indicators", StringComparison.Ordinal)];

        borderPass.Should().Contain("rowLookupAll.TryGetValue(cell.Row");
        borderPass.Should().Contain("colLookupAll.TryGetValue(cell.Col");
        borderPass.Should().NotContain("Viewport.RowMetrics.FirstOrDefault");
        borderPass.Should().NotContain("Viewport.ColMetrics.FirstOrDefault");
    }

    [Fact]
    public void RenderCells_BuildsResizeLookupsWithoutLinqPipelines()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.cs"));
        var setup = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("// Pass 1: backgrounds", StringComparison.Ordinal)];

        setup.Should().Contain("BuildRenderCellStyleLookup(Viewport!.Cells)");
        setup.Should().Contain("BuildRenderRowMetricLookup(Viewport.RowMetrics)");
        setup.Should().Contain("BuildRenderColumnMetricLookup(Viewport.ColMetrics)");
        setup.Should().NotContain(".Where(");
        setup.Should().NotContain(".ToDictionary(");

        source.Should().Contain("lookup.Add((cell.Row, cell.Col), style)");
        source.Should().Contain("lookup.Add(row.Row, row)");
        source.Should().Contain("lookup.Add(column.Col, column)");
    }

    [Fact]
    public void RenderCells_ReusesPixelsPerDipAcrossFormattedTextCalls()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        renderCells.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip).Width");
        renderCells.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip);");
    }

    [Fact]
    public void RenderHeaders_ReusesPixelsPerDipAcrossFormattedTextCalls()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.Headers.cs"));
        var renderHeaders = source[
            source.IndexOf("private void RenderHeaders(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("internal static string FormatColumnHeader", StringComparison.Ordinal)];

        renderHeaders.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        renderHeaders.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip);");
    }

    [Fact]
    public void RenderHeaders_CachesA1ColumnLabelsAcrossRenderPasses()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.Headers.cs"));
        var formatColumnHeader = source[
            source.IndexOf("internal static string FormatColumnHeader", StringComparison.Ordinal)..];

        source.Should().Contain("private static readonly ConcurrentDictionary<uint, string> ColumnHeaderCache = new();");
        formatColumnHeader.Should().Contain("ColumnHeaderCache.GetOrAdd(column");
        formatColumnHeader.Should().Contain("CellAddress.NumberToColumnName(col)");
    }

    [Fact]
    public void RenderHeaders_AvoidsPerHeaderLinqSelectionScans()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.Headers.cs"));
        var renderHeaders = source[
            source.IndexOf("private void RenderHeaders(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("internal static string FormatColumnHeader", StringComparison.Ordinal)];
        var renderFreezeDivider = source[
            source.IndexOf("private void RenderFreezeDivider(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void RenderHeaders(DrawingContext dc)", StringComparison.Ordinal)];

        renderHeaders.Should().Contain("IsColumnHeaderSelected(col.Col, selectedRanges, selRange)");
        renderHeaders.Should().Contain("IsRowHeaderSelected(row.Row, selectedRanges, selRange)");
        renderHeaders.Should().Contain("foreach (var range in selectedRanges)");
        renderHeaders.Should().NotContain(".Any(");
        renderFreezeDivider.Should().Contain("FindRowMetric(Viewport.RowMetrics, fp.Rows)");
        renderFreezeDivider.Should().Contain("FindColMetric(Viewport.ColMetrics, fp.Cols)");
        renderFreezeDivider.Should().NotContain("FirstOrDefault");
    }

    [Fact]
    public void FormatColumnHeader_UsesA1NamesOrR1C1Numbers()
    {
        var formatColumnHeader = typeof(GridView).GetMethod(
            "FormatColumnHeader",
            BindingFlags.NonPublic | BindingFlags.Static);

        formatColumnHeader.Should().NotBeNull();
        formatColumnHeader!.Invoke(null, [27u, false]).Should().Be("AA");
        formatColumnHeader.Invoke(null, [27u, true]).Should().Be("27");
    }

    [Fact]
    public void RenderSparklines_AvoidsEmptyRenderAllocations()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Overlays.Sparklines.cs"));
        var renderSparklines = source[
            source.IndexOf("private void RenderSparklines(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static SolidColorBrush FrozenBrush", StringComparison.Ordinal)];

        renderSparklines.Should().Contain("Sparklines is not { Count: > 0 }");
        renderSparklines.Should().Contain("SparklineValues is not { Count: > 0 }");
        renderSparklines.Should().Contain("BuildSparklineRowMetricLookup(Viewport.RowMetrics)");
        renderSparklines.Should().Contain("BuildSparklineColumnMetricLookup(Viewport.ColMetrics)");
        source.Should().Contain("private static readonly SolidColorBrush SparklinePositiveBrush");
        source.Should().Contain("private static readonly Pen SparklineLinePen");
        source.Should().Contain("lookup.Add(row.Row, row)");
        source.Should().Contain("lookup.Add(column.Col, column)");
        renderSparklines.Should().NotContain(".ToDictionary(");
        renderSparklines.Should().NotContain(".Select(");
        renderSparklines.Should().NotContain("new SolidColorBrush");
        renderSparklines.Should().NotContain("new Pen");
    }

    [Fact]
    public void OnRender_SkipsHeavyVisualLayersDuringLiveResize()
    {
        var properties = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Properties.cs"));
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.RenderDispatch.cs"));
        var onRender = source[
            source.IndexOf("protected override void OnRender", StringComparison.Ordinal)..];

        properties.Should().Contain("public static readonly DependencyProperty IsLiveResizingProperty");
        properties.Should().Contain("FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender)");
        onRender.Should().Contain("var isLiveResizing = IsLiveResizing;");
        onRender.Should().Contain("if (!isLiveResizing)");
        onRender.Should().Contain("RenderLiveResizeContinuation(dc);");
        onRender.Should().Contain("RenderCells(dc);");
        onRender.Should().Contain("RenderSelection(dc);");

        onRender.IndexOf("RenderCells(dc);", StringComparison.Ordinal)
            .Should().BeLessThan(onRender.IndexOf("RenderWorksheetViewOverlay(dc);", StringComparison.Ordinal));
        onRender.IndexOf("RenderSelection(dc);", StringComparison.Ordinal)
            .Should().BeLessThan(onRender.IndexOf("RenderFormulaTraceArrows(dc);", StringComparison.Ordinal));
        onRender.IndexOf("if (ObjectDisplayMode == GridObjectDisplayMode.Placeholders)", StringComparison.Ordinal)
            .Should().BeGreaterThan(onRender.LastIndexOf("if (!isLiveResizing)", StringComparison.Ordinal));
    }

    [Fact]
    public void LiveResizeContinuation_PaintsExpandedGridWithoutViewportRefresh()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.cs"));
        var continuation = rendering[
            rendering.IndexOf("private void RenderLiveResizeContinuation", StringComparison.Ordinal)..
            rendering.IndexOf("private void RenderSplitPaneCells", StringComparison.Ordinal)];

        continuation.Should().Contain("ActualWidth > gridRight");
        continuation.Should().Contain("ActualHeight > gridBottom");
        continuation.Should().Contain("RenderLiveResizeColumnContinuation");
        continuation.Should().Contain("RenderLiveResizeRowContinuation");
        continuation.Should().Contain("DrawLiveResizeHorizontalGridLines");
        continuation.Should().Contain("DrawLiveResizeVerticalGridLines");
        continuation.Should().Contain("dc.DrawRectangle(Brushes.White, null");
        continuation.Should().NotContain("UpdateViewport");
        continuation.Should().NotContain("Viewport =");
    }

    [Fact]
    public void RenderCaches_AreClassLevelFieldsNotLocalAllocations()
    {
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.cs"));

        gridViewSource.Should().Contain("private readonly Dictionary<CellColor, SolidColorBrush> _brushCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<CellBorder, Pen> _borderPenCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<CellTypefaceKey, Typeface> _typefaceCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<Brush, Pen> _underlinePenCache = new();");
    }

    [Fact]
    public void RenderCells_ClearsCachesAtStartOfEachPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("// Pass 1: backgrounds", StringComparison.Ordinal)];

        renderCells.Should().Contain("_brushCache.Clear();");
        renderCells.Should().Contain("_borderPenCache.Clear();");
        renderCells.Should().Contain("_typefaceCache.Clear();");
        renderCells.Should().Contain("_underlinePenCache.Clear();");
        renderCells.Should().NotContain("new Dictionary<CellColor, SolidColorBrush>");
        renderCells.Should().NotContain("new Dictionary<CellBorder, Pen>");
        renderCells.Should().NotContain("new Dictionary<CellTypefaceKey, Typeface>");
        renderCells.Should().NotContain("new Dictionary<Brush, Pen>");
    }

    [Fact]
    public void RenderCells_ReusesCellColorBrushesWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("BrushForCellColor(bg.FillColor.Value, _brushCache)");
        renderCells.Should().Contain("BrushForCellColor(fc, _brushCache)");
        renderCells.Should().NotContain("new SolidColorBrush");
    }

    [Fact]
    public void RenderCells_ReusesBorderPensWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("_brushCache, _borderPenCache");
    }

    [Fact]
    public void RenderCells_ReusesTypefacesWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("CreateCellTypeface(style, _typefaceCache)");
    }

    [Fact]
    public void RenderCells_ReusesDoubleUnderlinePensWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("UnderlinePenForTextBrush(textBrush, _underlinePenCache)");
        source.Should().Contain("private static Pen UnderlinePenForTextBrush");
        source.Should().Contain("pen.Freeze();");
        renderCells.Should().NotContain("new Pen(textBrush");
    }

    [Fact]
    public void ConditionalIconGlyphRenderer_ReusesFrozenBrushesAndPens()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "ConditionalIconGlyphRenderer.cs"));

        source.Should().Contain("private static readonly SolidColorBrush IconDarkRedBrush");
        source.Should().Contain("private static readonly Pen OutlinePen");
        source.Should().Contain("private static readonly Pen WhiteThinPen");
        source.Should().Contain("brush.Freeze();");
        source.Should().Contain("pen.Freeze();");
        source.Should().NotContain("new BrushConverter");
        source.Should().NotContain("new Pen(Brushes.White");
        source.Should().NotContain("var outline = new Pen");
    }

    [Fact]
    public void CalculateSplitDividerLayout_AvoidsLinqMetricScans()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.SplitPanes.cs"));
        var calculateLayout = source[
            source.IndexOf("public static SplitDividerLayout CalculateSplitDividerLayout", StringComparison.Ordinal)..
            source.IndexOf("public static SplitPaneScrollbarChrome CalculateSplitPaneScrollbarChrome", StringComparison.Ordinal)];

        calculateLayout.Should().Contain("FindRowMetric(viewport.RowMetrics, splitRow)");
        calculateLayout.Should().Contain("FindColMetric(viewport.ColMetrics, splitColumn)");
        calculateLayout.Should().NotContain("FirstOrDefault");
    }

    [Fact]
    public void ResizeDragInput_ReusesMetricScanHelpersWithoutLinqIterators()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Input.cs"));
        var resizeMove = source[
            source.IndexOf("if (_resizeTarget == ResizeTarget.Column)", StringComparison.Ordinal)..
            source.IndexOf("public static GridAutoScrollRequest CalculateAutofillEdgeScrollIntent", StringComparison.Ordinal)];
        var resizeStart = source[
            source.IndexOf("if (target != ResizeTarget.None)", StringComparison.Ordinal)..
            source.IndexOf("protected override void OnMouseRightButtonDown", StringComparison.Ordinal)];

        resizeMove.Should().Contain("FindColMetric(Viewport!.ColMetrics, _resizeIndex)");
        resizeMove.Should().Contain("FindRowMetric(Viewport!.RowMetrics, _resizeIndex)");
        resizeMove.Should().NotContain("FirstOrDefault");
        resizeStart.Should().Contain("FindColMetric(Viewport!.ColMetrics, index)");
        resizeStart.Should().Contain("FindRowMetric(Viewport!.RowMetrics, index)");
        resizeStart.Should().NotContain(".First(");
    }

    [Fact]
    public void PivotChartFieldButtonHitTest_ScansChartsBackToFrontWithoutLinqIterators()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.HitTesting.cs"));
        var hitTest = source[
            source.IndexOf("public static (ChartModel Chart, string FieldButton)? HitTestPivotChartFieldButton", StringComparison.Ordinal)..];

        hitTest.Should().Contain("for (var i = charts.Count - 1; i >= 0; i--)");
        hitTest.Should().NotContain(".Where(");
        hitTest.Should().NotContain(".Reverse(");
    }

    [Fact]
    public void ChartRenderer_BuildsChartCellLookupWithoutLinqFiltering()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "ChartRenderer.cs"));
        var buildLookup = source[
            source.IndexOf("private static Dictionary<(uint Row, uint Col), DisplayCell> BuildChartCellLookup", StringComparison.Ordinal)..
            source.IndexOf("private static LineSeries CreateLineSeries", StringComparison.Ordinal)];

        buildLookup.Should().Contain("foreach (var cell in viewport.ChartDataCells)");
        buildLookup.Should().Contain("if (cell.SheetId != sheetId)");
        buildLookup.Should().NotContain(".Where(");
        buildLookup.Should().NotContain(".Select(");
    }

    [Fact]
    public void RenderManualPageBreaks_ScansVisibleMetricsOnce()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Overlays.cs"));
        var renderManualPageBreaks = source[
            source.IndexOf("private void RenderManualPageBreaks", StringComparison.Ordinal)..
            source.IndexOf("public enum FormulaTraceArrowLayoutKind", StringComparison.Ordinal)];

        renderManualPageBreaks.Should().Contain("AsPageBreakLookup(rowPageBreaks)");
        renderManualPageBreaks.Should().Contain("AsPageBreakLookup(columnPageBreaks)");
        renderManualPageBreaks.Should().Contain("pageBreaks as IReadOnlySet<uint> ?? new HashSet<uint>(pageBreaks)");
        renderManualPageBreaks.Should().Contain("foreach (var metric in Viewport.RowMetrics)");
        renderManualPageBreaks.Should().Contain("foreach (var metric in Viewport.ColMetrics)");
        renderManualPageBreaks.Should().NotContain("FirstOrDefault");
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_BoundsTallMergeWorkToVisibleCells()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(500_000, 18, 0)],
            [new ColMetric(10, 64, 0)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64)],
                [
                    Cell(1, 1, "anchor"),
                    Cell(500_000, 1, "covered"),
                    Cell(1, 10, "visible")
                ]));
        var mergedRegions = new[]
        {
            new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, CellAddress.MaxRow, 2))
        };

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var layouts = SplitPaneCellLayoutPlanner.CalculateLayouts(viewport, mergedRegions);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var rowHeaderWidth = GridView.CalculateRowHeaderWidth(viewport);
        allocatedBytes.Should().BeLessThan(1_000_000);
        layouts.Select(layout => (layout.Cell.Row, layout.Cell.Col, layout.Rect))
            .Should().Equal(
                (1u, 1u, new Rect(rowHeaderWidth, GridView.ColHeaderHeight, 144, 40)),
                (1u, 10u, new Rect(rowHeaderWidth + 144, GridView.ColHeaderHeight, 64, 18)));
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_BuildsMetricLookupsAndOccupiedCellsWithoutLinqPipelines()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "SplitPaneCellLayoutPlanner.cs"));
        var calculateLayouts = source[
            source.IndexOf("public static IReadOnlyList<SplitPaneCellLayout> CalculateLayouts", StringComparison.Ordinal)..
            source.IndexOf("private static bool CanOverflowSplitPaneText", StringComparison.Ordinal)];

        calculateLayouts.Should().Contain("BuildRowLookup(topRows)");
        calculateLayouts.Should().Contain("BuildRowLookup(bottomLeftRows)");
        calculateLayouts.Should().Contain("BuildColumnLookup(leftColumns)");
        calculateLayouts.Should().Contain("BuildColumnLookup(topRightColumns)");
        calculateLayouts.Should().Contain("ResolveSplitPaneRegion(isTopPane, isLeftPane)");
        calculateLayouts.Should().Contain("foreach (var cell in cells)");
        calculateLayouts.Should().Contain("occupied.Add((cell.Row, cell.Col))");
        calculateLayouts.Should().NotContain(".ToDictionary(");
        calculateLayouts.Should().NotContain(".Where(");
        calculateLayouts.Should().NotContain(".Select(");
    }

    [Fact]
    public void RenderSplitPaneCells_UsesPrecomputedLayoutRegionForClipping()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.cs"));
        var splitPanes = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.SplitPanes.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private GridRange? FindMerge", StringComparison.Ordinal)];
        var setup = renderSplitPaneCells[..renderSplitPaneCells.IndexOf("foreach (var layout in CalculateSplitPaneCellLayouts", StringComparison.Ordinal)];
        var loop = renderSplitPaneCells[
            renderSplitPaneCells.IndexOf("foreach (var layout in CalculateSplitPaneCellLayouts", StringComparison.Ordinal)..];

        setup.Should().Contain("var topLeftClip = FrozenClipGeometry(clips.TopLeft)");
        setup.Should().Contain("var bottomRightClip = FrozenClipGeometry(clips.BottomRight)");
        loop.Should().Contain("GetSplitPaneClipGeometryForRegion(");
        loop.Should().Contain("layout.Region");
        loop.Should().NotContain("new RectangleGeometry(clipRect)");
        loop.Should().NotContain("GetSplitPaneClipRectForCell");
        rendering.Should().Contain("geometry.Freeze();");
        splitPanes.Should().Contain("public sealed record SplitPaneCellLayout(DisplayCell Cell, Rect Rect, Rect TextClipRect, SplitPaneRegion Region)");
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }

    private static DisplayCell Cell(uint row, uint col, string text, CellStyle? style = null) =>
        new(row, col, new TextValue(text), text, null, StyleId.Default, null, style);
}
