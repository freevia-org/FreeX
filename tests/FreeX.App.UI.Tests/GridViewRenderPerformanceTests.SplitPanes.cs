using System;
using System.Reflection;
using FreeX.App.UI;
using FreeX.Core.Model;
using FluentAssertions;
using System.Windows;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewRenderPerformanceTests
{
    [Fact]
    public void RenderSplitPaneCells_ReusesDoubleUnderlinePensAcrossBoundedRenderCaches()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var renderSplitPaneCells = source[
            source.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("TrimRenderCachesIfOversized();");
        source.Should().Contain("if (_underlinePenCache.Count >= RenderCacheSizeLimit)");
        source.Should().Contain("_underlinePenCache.Clear();");
        renderSplitPaneCells.Should().Contain("DrawCellText(dc, text, textLayout, style, textBrush, _underlinePenCache,");
        source.Should().Contain("if (style?.DoubleUnderline == true)");
        source.Should().Contain("UnderlinePenForTextBrush(textBrush, underlinePenCache)");
        source.Should().Contain("dc.DrawLine(underlinePen, new Point(drawPoint.X, uY), new Point(drawPoint.X + text.Width, uY));");
        renderSplitPaneCells.Should().NotContain("new Pen(textBrush");
    }

    [Fact]
    public void CalculateSplitDividerLayout_AvoidsLinqMetricScans()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.SplitPanes.cs");
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
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.SplitPanes.cs");
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
        var gridViewSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.cs");
        var splitPanesSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.SplitPanes.cs");
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
    public void SplitPaneViewportChrome_ReusesSharedScrollbarPlanner()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("SplitPaneViewportChrome.cs");
        var calculateChrome = source[
            source.IndexOf("public static SplitPaneScrollbarChrome CalculateScrollbarChrome", StringComparison.Ordinal)..
            source.IndexOf("public static SplitPaneScrollbarHit? HitTestScrollbar", StringComparison.Ordinal)];

        calculateChrome.Should().Contain("SplitPanePointerPlanner.CalculateScrollbarChrome(");
        calculateChrome.Should().Contain("GridView.CalculateRowHeaderWidth(viewport)");
        calculateChrome.Should().Contain("GridView.ColHeaderHeight");
        calculateChrome.Should().NotContain("var visibleSpan =");
        calculateChrome.Should().NotContain("var maxStartIndex =");
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
        // r47 extended the r46 vertical-split (column-crossing) merge fix to also cover the
        // horizontal split (row-crossing): this merge spans rows 1..MaxRow, so it crosses BOTH the
        // row split (TopRows ends at row 2) and the column split (LeftColumns ends at col 2). Row
        // 500_000 is visible in the bottom-left pane (via the BottomLeftRows fallback to
        // viewport.RowMetrics), so the merge now legitimately gets a 3rd quadrant layout there too -
        // same anchor cell, stripped content, clipped to the bottom-left pane's box. The 4th
        // possible quadrant (bottom-right) stays absent because the merge's columns (1-2) never
        // reach the right pane's only visible column (10). The test's actual intent - that a
        // 500,000-row-tall merge's layout cost stays bounded to the handful of visible cells/panes,
        // not proportional to the merge's row span - is unaffected: still O(visible cells), just up
        // to 4 quadrants per crossing merge instead of 2.
        layouts.Select(layout => (layout.Cell.Row, layout.Cell.Col, layout.Rect))
            .Should().Equal(
                (1u, 1u, new Rect(rowHeaderWidth, GridView.ColHeaderHeight, 144, 40)),
                (1u, 1u, new Rect(rowHeaderWidth, GridView.ColHeaderHeight + 40, 144, 18)),
                (1u, 10u, new Rect(rowHeaderWidth + 144, GridView.ColHeaderHeight, 64, 18)));
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_IsAThinPortablePlannerAdapter()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("SplitPaneCellLayoutPlanner.cs");
        source.Should().Contain("ViewportGeometryPlanner.CalculateSplitPaneLayouts(");
        source.Should().Contain("ViewportGeometryPlanner.VisitSplitPaneLayouts(");
        source.Should().Contain("private static Rect ToWpf(LayoutRect rect)");
        source.Should().NotContain("SplitPaneOccupiedCellMap");
        source.Should().NotContain("MergeRangeIndex");
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_NumericCellsSkipOverflowOccupancyAllocation()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("SplitPaneCellLayoutPlanner.cs");

        source.Should().Contain("ViewportGeometryPlanner.CalculateSplitPaneLayouts(");
        source.Should().NotContain("BuildOccupiedCells");

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
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var splitPanes = AppUiSourceTestSupport.ReadAppUiSources("GridView.SplitPanes.cs");
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
        renderConsumer.Should().Contain("grid.RenderSplitPaneCell(dc, layout, gridPen, pixelsPerDip, borderStyleLookup);");
        renderConsumer.Should().NotContain("new RectangleGeometry(clipRect)");
        renderConsumer.Should().NotContain("GetSplitPaneClipRectForCell");
        splitPanes.Should().NotContain("GetSplitPaneClipRectForCell");
        rendering.Should().Contain("geometry.Freeze();");
        splitPanes.Should().Contain("public readonly record struct SplitPaneCellLayout(DisplayCell Cell, Rect Rect, Rect TextClipRect, SplitPaneRegion Region)");
    }

    [Fact]
    public void RenderSplitPaneCells_RespectsHiddenGridLinesForDefaultCellBorders()
    {
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("var gridPen = ShowGridLines ? GridPen : null;");
        renderSplitPaneCells.Should().Contain("dc.DrawRectangle(fill, gridPen, rect);");
        // R80 fix: split-pane borders now resolve shared-edge precedence (ResolveBorderEdgeWinner)
        // against the actual neighbor instead of drawing style.BorderTop/etc. unconditionally.
        renderSplitPaneCells.Should().Contain("var topWinner = ResolveBorderEdgeWinner(style.BorderTop, neighborBottom);");
        renderSplitPaneCells.Should().Contain("DrawBorderEdge(dc, topWinner");
        renderSplitPaneCells.Should().NotContain("dc.DrawRectangle(fill, GridPen, rect);");
    }

    [Fact]
    public void RenderSplitPaneCells_SkipsZeroSizedCellsBeforeDrawingWork()
    {
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
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
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("if (fill is not null || gridPen is not null)");
        renderSplitPaneCells.Should().Contain("dc.DrawRectangle(fill, gridPen, rect);");
        renderSplitPaneCells.IndexOf("if (fill is not null || gridPen is not null)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSplitPaneCells.IndexOf("DrawFillPattern(dc, rect, fillPlan", StringComparison.Ordinal));
        renderSplitPaneCells.IndexOf("DrawFillPattern(dc, rect, fillPlan", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSplitPaneCells.IndexOf("if (style is not null && HasVisibleCellBorder(style))", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderSplitPaneCells_ClipsConditionalIconTextToAdjustedTextRect()
    {
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];
        var conditionalIconBlock = renderSplitPaneCells[
            renderSplitPaneCells.IndexOf("if (cell.ConditionalIcon is { } splitIcon)", StringComparison.Ordinal)..
            renderSplitPaneCells.IndexOf("var hAlign = style?.HorizontalAlignment", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("var textClipRect = layout.TextClipRect;");
        conditionalIconBlock.Should().Contain("rect = iconLayout.TextRect;");
        conditionalIconBlock.Should().Contain("textClipRect = AdjustConditionalIconTextClipRect(layout.TextClipRect, rect);");
        renderSplitPaneCells.Should().Contain("var shouldClipText = ShouldClipText(wrapText, textClipRect, text, textLayout);");
        renderSplitPaneCells.Should().Contain("if (shouldClipText)");
        renderSplitPaneCells.Should().Contain("dc.PushClip(GetCellClipGeometry(textClipRect));");
        renderSplitPaneCells.Should().Contain("private static Rect AdjustConditionalIconTextClipRect(Rect clipRect, Rect textRect)");
    }

    [Fact]
    public void RenderSplitPaneCells_OnlyPushesTextClipWhenTextNeedsClipping()
    {
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("var textLayout = CalculateCellTextRenderLayout(");
        renderSplitPaneCells.Should().Contain("var shouldClipText = ShouldClipText(wrapText, textClipRect, text, textLayout);");
        renderSplitPaneCells.IndexOf("dc.PushClip(GetCellClipGeometry(textClipRect));", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSplitPaneCells.IndexOf("DrawCellText(dc, text, textLayout, style, textBrush, _underlinePenCache,", StringComparison.Ordinal));
        renderSplitPaneCells.LastIndexOf("dc.Pop();", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(renderSplitPaneCells.IndexOf("DrawCellText(dc, text, textLayout, style, textBrush, _underlinePenCache,", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderSplitPaneCells_UsesWrappedTextLayoutCacheForDefaultWrappedCells()
    {
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];
        var textSetup = renderSplitPaneCells[
            renderSplitPaneCells.IndexOf("var hAlign = style?.HorizontalAlignment", StringComparison.Ordinal)..
            renderSplitPaneCells.IndexOf("if (style?.ShrinkToFit == true && !wrapText)", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("var wrapText = style?.WrapText == true;");
        renderSplitPaneCells.Should().Contain("var useDefaultTextLayout = !hasSplitRichRuns && !isEffectivelyRightToLeft && CanUseDefaultFormattedText(style, wrapText);");
        renderSplitPaneCells.Should().Contain("var wrapMaxTextWidth = wrapText ? Math.Max(1, rect.Width - 4 - indentPx) : 0;");
        renderSplitPaneCells.Should().Contain("var wrapTextAlignment = TextAlignment.Left;");
        renderSplitPaneCells.Should().Contain("if (!useDefaultTextLayout && wrapText)");
        renderSplitPaneCells.Should().Contain("useDefaultWrappedTextLayout = !hasSplitRichRuns && !isEffectivelyRightToLeft && CanUseDefaultWrappedFormattedText(style);");
        renderSplitPaneCells.Should().Contain("GetDefaultWrappedFormattedText(renderText, fontSize, wrapMaxTextWidth, wrapTextAlignment, pixelsPerDip)");
        textSetup.Should().NotContain("CreateCellTypeface");
        textSetup.Should().NotContain("BrushForCellColor");
        renderSplitPaneCells.Should().Contain("text.MaxTextWidth = wrapMaxTextWidth;");
        renderSplitPaneCells.Should().Contain("text.TextAlignment = wrapTextAlignment;");
        renderSplitPaneCells.Should().Contain("if (style?.ShrinkToFit == true && !wrapText)");
        renderSplitPaneCells.Should().NotContain("CanUseDefaultFormattedText(style, wrapText: false)");
        renderSplitPaneCells.Should().NotContain("style.WrapText != true");
    }
}
