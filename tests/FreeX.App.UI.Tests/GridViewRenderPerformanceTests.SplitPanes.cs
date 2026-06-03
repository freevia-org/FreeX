using System;
using System.IO;
using System.Reflection;
using FreeX.App.UI;
using FreeX.Core.Model;
using FluentAssertions;
using System.Windows;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewRenderPerformanceTests
{
    [Fact]
    public void RenderSplitPaneCells_ReusesDoubleUnderlinePensWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderSplitPaneCells = source[
            source.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("_underlinePenCache.Clear();");
        renderSplitPaneCells.Should().Contain("if (style?.DoubleUnderline == true)");
        renderSplitPaneCells.Should().Contain("UnderlinePenForTextBrush(textBrush, _underlinePenCache)");
        renderSplitPaneCells.Should().Contain("dc.DrawLine(underlinePen, new Point(textX, uY), new Point(textX + text.Width, uY));");
        renderSplitPaneCells.Should().NotContain("new Pen(textBrush");
    }

    [Fact]
    public void CalculateSplitDividerLayout_AvoidsLinqMetricScans()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.SplitPanes.cs"));
        var calculateLayout = source[
            source.IndexOf("public static SplitDividerLayout CalculateSplitDividerLayout", StringComparison.Ordinal)..
            source.IndexOf("public static SplitPaneScrollbarChrome CalculateSplitPaneScrollbarChrome", StringComparison.Ordinal)];

        calculateLayout.Should().Contain("FindRowMetric(viewport.RowMetrics, splitRow)");
        calculateLayout.Should().Contain("FindColMetric(viewport.ColMetrics, splitColumn)");
        calculateLayout.Should().Contain("SumRowHeights(pinnedRows)");
        calculateLayout.Should().Contain("SumColumnWidths(pinnedColumns)");
        calculateLayout.Should().NotContain("FirstOrDefault");
        calculateLayout.Should().NotContain(".Sum(");
        source.Should().NotContain(".Sum(");
    }

    [Fact]
    public void CalculateSplitDividerDragTarget_StopsSortedMetricScansAfterPointer()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.SplitPanes.cs"));
        var findSplitRow = source[
            source.IndexOf("private static uint? FindSplitRow", StringComparison.Ordinal)..
            source.IndexOf("private static uint? FindSplitColumn", StringComparison.Ordinal)];
        var findSplitColumn = source[
            source.IndexOf("private static uint? FindSplitColumn", StringComparison.Ordinal)..
            source.IndexOf("private static uint IncrementWithinLimit", StringComparison.Ordinal)];

        findSplitRow.Should().Contain("foreach (var row in mainRows)");
        findSplitRow.Should().Contain("if (y < top)");
        findSplitRow.Should().Contain("break;");
        findSplitRow.IndexOf("if (y < top)", StringComparison.Ordinal)
            .Should().BeLessThan(findSplitRow.IndexOf("if (y >= top && y <= top + row.Height)", StringComparison.Ordinal));

        findSplitColumn.Should().Contain("foreach (var column in mainColumns)");
        findSplitColumn.Should().Contain("if (x < left)");
        findSplitColumn.Should().Contain("break;");
        findSplitColumn.IndexOf("if (x < left)", StringComparison.Ordinal)
            .Should().BeLessThan(findSplitColumn.IndexOf("if (x >= left && x <= left + column.Width)", StringComparison.Ordinal));
    }

    [Fact]
    public void SplitPaneDividerHandles_ReuseFrozenStaticPen()
    {
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
        var splitPanesSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.SplitPanes.cs"));
        var renderHandles = splitPanesSource[
            splitPanesSource.IndexOf("private void RenderSplitDividerHandles", StringComparison.Ordinal)..
            splitPanesSource.IndexOf("private void RenderSplitPaneScrollbarChrome", StringComparison.Ordinal)];

        gridViewSource.Should().Contain("private static readonly Brush SplitDividerHandleBrush = MakeBrush(112, 112, 112);");
        gridViewSource.Should().Contain("private static readonly Pen SplitDividerHandlePen = MakePen(SplitDividerHandleBrush, 1);");
        renderHandles.Should().Contain("SplitDividerHandlePen");
        renderHandles.Should().NotContain("MakeBrush(");
        renderHandles.Should().NotContain("new Pen(");
    }

    [Fact]
    public void SplitPaneViewportChrome_ReusesNormalizedScrollbarSpans()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "SplitPaneViewportChrome.cs"));
        var calculateChrome = source[
            source.IndexOf("public static SplitPaneScrollbarChrome CalculateScrollbarChrome", StringComparison.Ordinal)..
            source.IndexOf("public static SplitPaneScrollbarHit? HitTestScrollbar", StringComparison.Ordinal)];

        calculateChrome.Should().Contain("var visibleSpan = Math.Max(1, topRightColumns.Count);");
        calculateChrome.Should().Contain("var visibleSpan = Math.Max(1, bottomLeftRows.Count);");
        calculateChrome.Should().Contain("var maxStartIndex = Math.Max(1, CellAddress.MaxCol - (uint)visibleSpan + 1);");
        calculateChrome.Should().Contain("var maxStartIndex = Math.Max(1, CellAddress.MaxRow - (uint)visibleSpan + 1);");
        calculateChrome.Should().NotContain("(uint)Math.Max(1, topRightColumns.Count)");
        calculateChrome.Should().NotContain("(uint)Math.Max(1, bottomLeftRows.Count)");
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
    public void SplitPaneCellLayoutPlanner_BuildsMetricLookupsAndLazyOccupiedCellsWithoutLinqPipelines()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "SplitPaneCellLayoutPlanner.cs"));
        var calculateLayouts = source[
            source.IndexOf("public static IReadOnlyList<SplitPaneCellLayout> CalculateLayouts", StringComparison.Ordinal)..
            source.IndexOf("private static bool CanOverflowSplitPaneText", StringComparison.Ordinal)];
        var buildOccupiedCells = source[
            source.IndexOf("private static SplitPaneOccupiedCellMap BuildOccupiedCells", StringComparison.Ordinal)..
            source.IndexOf("private static double SumEmptyOverflowColumnWidths", StringComparison.Ordinal)];
        var mergeRangeIndex = source[
            source.IndexOf("public static MergeRangeIndex Create", StringComparison.Ordinal)..
            source.IndexOf("public GridRange? Find", StringComparison.Ordinal)];

        calculateLayouts.Should().Contain("new SplitPaneRowMetricLookup(topRows)");
        calculateLayouts.Should().Contain("new SplitPaneRowMetricLookup(bottomLeftRows)");
        calculateLayouts.Should().Contain("new SplitPaneColumnMetricLookup(leftColumns)");
        calculateLayouts.Should().Contain("new SplitPaneColumnMetricLookup(topRightColumns)");
        calculateLayouts.Should().Contain("private readonly struct SplitPaneRowMetricLookup");
        calculateLayouts.Should().Contain("private readonly struct SplitPaneColumnMetricLookup");
        calculateLayouts.Should().Contain("FindSortedRowMetric(_rows, row, _firstRow, _lastRow)");
        calculateLayouts.Should().Contain("FindSortedColumnMetric(_columns, column, _firstColumn, _lastColumn)");
        calculateLayouts.Should().Contain("var directIndex = row - firstRow;");
        calculateLayouts.Should().Contain("var directIndex = column - firstColumn;");
        calculateLayouts.Should().Contain("while (low <= high)");
        calculateLayouts.Should().Contain("ResolveSplitPaneRegion(isTopPane, isLeftPane)");
        calculateLayouts.Should().Contain("if (cells.Count == 0)");
        calculateLayouts.Should().Contain("var rowHeaderWidth = GridView.CalculateRowHeaderWidth(viewport);");
        calculateLayouts.Should().Contain("var verticalX = dividerLayout.VerticalX ?? rowHeaderWidth;");
        calculateLayouts.Should().Contain("? rowHeaderWidth + column.LeftOffset");
        calculateLayouts.Should().Contain("VisitLayouts(viewport, mergedRegions, editingCell, ref consumer);");
        calculateLayouts.Should().Contain("private struct SplitPaneCellLayoutCollector");
        calculateLayouts.Should().Contain("new SplitPaneCellLayoutList(");
        calculateLayouts.Should().Contain("_cellIndexes = new int[capacity]");
        calculateLayouts.Should().Contain("SplitPaneOccupiedCellMap? occupied = null;");
        calculateLayouts.Should().Contain("occupied ??= BuildOccupiedCells(cells, editingCell)");
        calculateLayouts.Should().Contain("SumEmptyOverflowColumnWidths(cell, colMetrics, occupied.Value)");
        calculateLayouts.Should().Contain("foreach (var cell in cells)");
        calculateLayouts.Should().Contain("consumer.AcceptLayout(new SplitPaneCellLayout");
        calculateLayouts.IndexOf("if (cells.Count == 0)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(calculateLayouts.IndexOf("new SplitPaneRowMetricLookup(topRows)", StringComparison.Ordinal));
        calculateLayouts.IndexOf("var rowHeaderWidth = GridView.CalculateRowHeaderWidth(viewport);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(calculateLayouts.IndexOf("foreach (var cell in cells)", StringComparison.Ordinal));
        calculateLayouts[
            calculateLayouts.IndexOf("foreach (var cell in cells)", StringComparison.Ordinal)..]
            .Should()
            .NotContain("GridView.CalculateRowHeaderWidth(viewport)");
        calculateLayouts.Should().NotContain("spansByRow.Add(cell.Row, spans)");
        buildOccupiedCells.Should().Contain("spansByRow.Add(cell.Row, spans)");
        buildOccupiedCells.Should().Contain("AddOccupiedColumn(spans, cell.Col, ref needsNormalize)");
        buildOccupiedCells.Should().Contain("NormalizeOccupiedColumnSpans(spansByRow)");
        buildOccupiedCells.Should().Contain("new SplitPaneOccupiedCellMap(spansByRow)");
        mergeRangeIndex.Should().Contain("var queryCells = BuildQueryCells(cells);");
        mergeRangeIndex.Should().Contain("mergedRegion.End.Row < queryCells.MinRow");
        mergeRangeIndex.Should().Contain("mergedRegion.Start.Row > queryCells.MaxRow");
        mergeRangeIndex.Should().Contain("mergedRegion.End.Col < queryCells.MinCol");
        mergeRangeIndex.Should().Contain("mergedRegion.Start.Col > queryCells.MaxCol");
        mergeRangeIndex.Should().Contain("foreach (var row in queryCells.Rows)");
        calculateLayouts.Should().NotContain(".ToDictionary(");
        calculateLayouts.Should().NotContain(".Where(");
        calculateLayouts.Should().NotContain(".Select(");
        calculateLayouts.Should().NotContain("new Dictionary<uint, RowMetric>");
        calculateLayouts.Should().NotContain("new Dictionary<uint, ColMetric>");
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_NumericCellsSkipOverflowOccupancyAllocation()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "SplitPaneCellLayoutPlanner.cs"));

        source.Should().Contain("SplitPaneOccupiedCellMap? occupied = null;");
        source.Should().Contain("occupied ??= BuildOccupiedCells(cells, editingCell);");

        var sheetId = SheetId.New();
        var cells = new List<DisplayCell>();
        for (uint col = 1; col <= 80; col++)
            cells.Add(new DisplayCell(1, col, new NumberValue(col), col.ToString(), null, StyleId.Default, null, null));

        var viewport = new ViewportModel(
            cells,
            [new RowMetric(1, 18, 0)],
            Enumerable.Range(1, 80)
                .Select(index => new ColMetric((uint)index, 64, (index - 1) * 64))
                .ToList(),
            SplitPanes: new SplitPaneState(
                2,
                2,
                [new RowMetric(1, 18, 0)],
                Enumerable.Range(1, 80)
                    .Select(index => new ColMetric((uint)index, 64, (index - 1) * 64))
                    .ToList(),
                cells));

        var mergedRegions = new[]
        {
            new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 2))
        };

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var layouts = SplitPaneCellLayoutPlanner.CalculateLayouts(viewport, mergedRegions);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        layouts.Should().HaveCount(79);
        allocatedBytes.Should().BeLessThan(
            45_000,
            "numeric split-pane cells cannot overflow, so the occupied-cell HashSet should stay unallocated");
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_EmptyFormulaCellsStopTextOverflow()
    {
        var cells = new[]
        {
            Cell(1, 1, "long text"),
            new DisplayCell(1, 2, new TextValue(""), "", "IF(A1,\"\",\"\")", StyleId.Default, null),
            Cell(1, 3, "")
        };
        var viewport = new ViewportModel(
            cells,
            [new RowMetric(1, 18, 0)],
            [
                new ColMetric(1, 40, 0),
                new ColMetric(2, 40, 40),
                new ColMetric(3, 40, 80)
            ],
            SplitPanes: new SplitPaneState(
                2,
                2,
                [new RowMetric(1, 18, 0)],
                [
                    new ColMetric(1, 40, 0),
                    new ColMetric(2, 40, 40),
                    new ColMetric(3, 40, 80)
                ],
                cells));

        var layouts = SplitPaneCellLayoutPlanner.CalculateLayouts(viewport);

        layouts.Single(layout => layout.Cell.Col == 1)
            .TextClipRect.Width
            .Should().Be(40);
    }

    [Fact]
    public void RenderSplitPaneCells_UsesPrecomputedLayoutRegionForClipping()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var splitPanes = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.SplitPanes.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private GridRange? FindMerge", StringComparison.Ordinal)];
        var setup = renderSplitPaneCells[..renderSplitPaneCells.IndexOf("var consumer = new SplitPaneCellRenderConsumer", StringComparison.Ordinal)];
        var renderConsumer = renderSplitPaneCells[
            renderSplitPaneCells.IndexOf("private readonly struct SplitPaneCellRenderConsumer", StringComparison.Ordinal)..];

        setup.Should().Contain("var topLeftClip = FrozenClipGeometry(clips.TopLeft)");
        setup.Should().Contain("var bottomRightClip = FrozenClipGeometry(clips.BottomRight)");
        renderSplitPaneCells.Should().Contain("SplitPaneCellLayoutPlanner.VisitLayouts(Viewport, MergedRegions, EditingCell, ref consumer);");
        renderConsumer.Should().Contain("GetSplitPaneClipGeometryForRegion(");
        renderConsumer.Should().Contain("layout.Region");
        renderConsumer.Should().Contain("if (clipGeometry.Rect.Width <= 0 || clipGeometry.Rect.Height <= 0)");
        renderConsumer.IndexOf("if (clipGeometry.Rect.Width <= 0 || clipGeometry.Rect.Height <= 0)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderConsumer.IndexOf("dc.PushClip(clipGeometry);", StringComparison.Ordinal));
        renderConsumer.Should().Contain("grid.RenderSplitPaneCell(dc, layout, gridPen, pixelsPerDip);");
        renderConsumer.Should().NotContain("new RectangleGeometry(clipRect)");
        renderConsumer.Should().NotContain("GetSplitPaneClipRectForCell");
        splitPanes.Should().NotContain("GetSplitPaneClipRectForCell");
        rendering.Should().Contain("geometry.Freeze();");
        splitPanes.Should().Contain("public readonly record struct SplitPaneCellLayout(DisplayCell Cell, Rect Rect, Rect TextClipRect, SplitPaneRegion Region)");
    }

    [Fact]
    public void RenderSplitPaneCells_RespectsHiddenGridLinesForDefaultCellBorders()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("var gridPen = ShowGridLines ? GridPen : null;");
        renderSplitPaneCells.Should().Contain("dc.DrawRectangle(fill, gridPen, rect);");
        renderSplitPaneCells.Should().Contain("DrawBorderEdge(dc, style.BorderTop");
        renderSplitPaneCells.Should().NotContain("dc.DrawRectangle(fill, GridPen, rect);");
    }

    [Fact]
    public void RenderSplitPaneCells_SkipsZeroSizedCellsBeforeDrawingWork()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("if (rect.Width <= 0 || rect.Height <= 0)");
        renderSplitPaneCells.IndexOf("if (rect.Width <= 0 || rect.Height <= 0)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSplitPaneCells.IndexOf("var style = cell.Style;", StringComparison.Ordinal));
        renderSplitPaneCells.IndexOf("if (rect.Width <= 0 || rect.Height <= 0)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSplitPaneCells.IndexOf("dc.PushClip(clipGeometry);", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderSplitPaneCells_SkipsNoOpDefaultBackgroundDraw()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("if (fill is not null || gridPen is not null)");
        renderSplitPaneCells.Should().Contain("dc.DrawRectangle(fill, gridPen, rect);");
        renderSplitPaneCells.IndexOf("if (fill is not null || gridPen is not null)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSplitPaneCells.IndexOf("DrawFillPattern(dc, rect, style", StringComparison.Ordinal));
        renderSplitPaneCells.IndexOf("DrawFillPattern(dc, rect, style", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSplitPaneCells.IndexOf("if (style is not null && HasVisibleCellBorder(style))", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderSplitPaneCells_ClipsConditionalIconTextToAdjustedTextRect()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];
        var conditionalIconBlock = renderSplitPaneCells[
            renderSplitPaneCells.IndexOf("if (cell.ConditionalIcon is { } splitIcon)", StringComparison.Ordinal)..
            renderSplitPaneCells.IndexOf("var hAlign = style?.HorizontalAlignment", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("var textClipRect = layout.TextClipRect;");
        conditionalIconBlock.Should().Contain("rect = iconLayout.TextRect;");
        conditionalIconBlock.Should().Contain("textClipRect = AdjustConditionalIconTextClipRect(layout.TextClipRect, rect);");
        renderSplitPaneCells.Should().Contain("var shouldClipText = ShouldClipText(wrapText, textClipRect, text, textPoint);");
        renderSplitPaneCells.Should().Contain("if (shouldClipText)");
        renderSplitPaneCells.Should().Contain("dc.PushClip(GetCellClipGeometry(textClipRect));");
        renderSplitPaneCells.Should().Contain("private static Rect AdjustConditionalIconTextClipRect(Rect clipRect, Rect textRect)");
    }

    [Fact]
    public void RenderSplitPaneCells_OnlyPushesTextClipWhenTextNeedsClipping()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("var textPoint = new Point(Math.Round(textX), Math.Round(textY));");
        renderSplitPaneCells.Should().Contain("var shouldClipText = ShouldClipText(wrapText, textClipRect, text, textPoint);");
        renderSplitPaneCells.IndexOf("dc.PushClip(GetCellClipGeometry(textClipRect));", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSplitPaneCells.IndexOf("dc.DrawText(text, textPoint);", StringComparison.Ordinal));
        renderSplitPaneCells.LastIndexOf("dc.Pop();", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(renderSplitPaneCells.IndexOf("dc.DrawText(text, textPoint);", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderSplitPaneCells_UsesWrappedTextLayoutCacheForDefaultWrappedCells()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];
        var textSetup = renderSplitPaneCells[
            renderSplitPaneCells.IndexOf("var hAlign = style?.HorizontalAlignment", StringComparison.Ordinal)..
            renderSplitPaneCells.IndexOf("if (style?.ShrinkToFit == true && !wrapText)", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("var wrapText = style?.WrapText == true;");
        renderSplitPaneCells.Should().Contain("var useDefaultTextLayout = CanUseDefaultFormattedText(style, wrapText);");
        renderSplitPaneCells.Should().Contain("var wrapMaxTextWidth = wrapText ? Math.Max(1, rect.Width - 4) : 0;");
        renderSplitPaneCells.Should().Contain("var wrapTextAlignment = TextAlignment.Left;");
        renderSplitPaneCells.Should().Contain("if (!useDefaultTextLayout && wrapText)");
        renderSplitPaneCells.Should().Contain("useDefaultWrappedTextLayout = CanUseDefaultWrappedFormattedText(style);");
        renderSplitPaneCells.Should().Contain("GetDefaultWrappedFormattedText(cell.DisplayText, fontSize, wrapMaxTextWidth, wrapTextAlignment, pixelsPerDip)");
        textSetup.Should().NotContain("CreateCellTypeface");
        textSetup.Should().NotContain("BrushForCellColor");
        renderSplitPaneCells.Should().Contain("text.MaxTextWidth = wrapMaxTextWidth;");
        renderSplitPaneCells.Should().Contain("text.TextAlignment = wrapTextAlignment;");
        renderSplitPaneCells.Should().Contain("if (style?.ShrinkToFit == true && !wrapText)");
        renderSplitPaneCells.Should().NotContain("CanUseDefaultFormattedText(style, wrapText: false)");
        renderSplitPaneCells.Should().NotContain("style.WrapText != true");
    }
}
