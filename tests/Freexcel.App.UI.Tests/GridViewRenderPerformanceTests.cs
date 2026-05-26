using System;
using System.IO;
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
