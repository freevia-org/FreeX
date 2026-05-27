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
        renderSparklines.IndexOf("Sparklines is not { Count: > 0 }", StringComparison.Ordinal)
            .Should().BeLessThan(renderSparklines.IndexOf("ToDictionary", StringComparison.Ordinal));
        source.Should().Contain("private static readonly SolidColorBrush SparklinePositiveBrush");
        source.Should().Contain("private static readonly Pen SparklineLinePen");
        renderSparklines.Should().NotContain("new SolidColorBrush");
        renderSparklines.Should().NotContain("new Pen");
    }

    [Fact]
    public void RenderCells_ReusesCellColorBrushesWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("var brushCache = new Dictionary<CellColor, SolidColorBrush>();");
        renderCells.Should().Contain("BrushForCellColor(bg.FillColor.Value, brushCache)");
        renderCells.Should().Contain("BrushForCellColor(fc, brushCache)");
        renderCells.Should().NotContain("new SolidColorBrush");
    }

    [Fact]
    public void RenderCells_ReusesBorderPensWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("var borderPenCache = new Dictionary<CellBorder, Pen>();");
        renderCells.Should().Contain("brushCache, borderPenCache");
    }

    [Fact]
    public void RenderCells_ReusesTypefacesWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("var typefaceCache = new Dictionary<CellTypefaceKey, Typeface>();");
        renderCells.Should().Contain("CreateCellTypeface(style, typefaceCache)");
    }

    [Fact]
    public void RenderCells_ReusesDoubleUnderlinePensWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("var underlinePenCache = new Dictionary<Brush, Pen>();");
        renderCells.Should().Contain("UnderlinePenForTextBrush(textBrush, underlinePenCache)");
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
        calculateLayouts.Should().Contain("foreach (var cell in cells)");
        calculateLayouts.Should().Contain("occupied.Add((cell.Row, cell.Col))");
        calculateLayouts.Should().NotContain(".ToDictionary(");
        calculateLayouts.Should().NotContain(".Where(");
        calculateLayouts.Should().NotContain(".Select(");
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
