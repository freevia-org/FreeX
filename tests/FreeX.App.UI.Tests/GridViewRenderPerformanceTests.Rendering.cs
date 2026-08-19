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
    public void RenderCells_UsesMetricDictionariesForExplicitBorderCells()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
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
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var setup = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("// Pass 1: non-default backgrounds and merged-cell surfaces", StringComparison.Ordinal)];

        setup.Should().Contain("GetRenderCellLookups(viewport)");
        setup.Should().Contain("var styleLookup = lookups.Styles;");
        setup.Should().Contain("var rowLookupAll = lookups.Rows;");
        setup.Should().Contain("var colLookupAll = lookups.Columns;");
        setup.Should().NotContain(".Where(");
        setup.Should().NotContain(".ToDictionary(");

        source.Should().Contain("lookup.Add((cell.Row, cell.Col), style)");
        source.Should().Contain("lookup.Add(row.Row, row)");
        source.Should().Contain("lookup.Add(column.Col, column)");
    }

    [Fact]
    public void RenderCells_LazilyAllocatesStyleLookupForDefaultStyledViewports()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var buildStyleLookup = source[
            source.IndexOf("private static Dictionary<(uint Row, uint Col), CellStyle> BuildRenderCellStyleLookup", StringComparison.Ordinal)..
            source.IndexOf("private RenderCellLookupCache GetRenderCellLookups", StringComparison.Ordinal)];

        source.Should().Contain("private static readonly Dictionary<(uint Row, uint Col), CellStyle> EmptyRenderCellStyleLookup = new(0);");
        buildStyleLookup.Should().Contain("Dictionary<(uint Row, uint Col), CellStyle>? lookup = null;");
        // R114-render-theme-color-reresolution: HasVisibleCellSurface now takes the active
        // WorkbookTheme too, so a cell whose fill was set purely via a Theme Color picker
        // (FillThemeColor with no baked FillColor -- see StyleDiff.Apply) is not silently dropped.
        buildStyleLookup.Should().Contain("cell.Style is { } style && HasVisibleCellSurface(style, theme)");
        buildStyleLookup.Should().Contain("lookup ??= new Dictionary<(uint Row, uint Col), CellStyle>(cells.Count);");
        buildStyleLookup.Should().Contain("return lookup ?? EmptyRenderCellStyleLookup;");
        buildStyleLookup.Should().NotContain("var lookup = new Dictionary<(uint Row, uint Col), CellStyle>();");

        var buildLookup = typeof(GridView).GetMethod(
            "BuildRenderCellStyleLookup",
            BindingFlags.NonPublic | BindingFlags.Static);
        buildLookup.Should().NotBeNull();

        var defaultLookup = (IReadOnlyDictionary<(uint Row, uint Col), CellStyle>)buildLookup!.Invoke(
            null,
            [new DisplayCell[] { Cell(1, 1, "default", CellStyle.Default) }, WorkbookTheme.Office])!;
        defaultLookup.Should().BeEmpty();

        var fontOnlyStyle = CellStyle.Default.Clone();
        fontOnlyStyle.Bold = true;
        var fontOnlyLookup = (IReadOnlyDictionary<(uint Row, uint Col), CellStyle>)buildLookup.Invoke(
            null,
            [new DisplayCell[] { Cell(1, 1, "font", fontOnlyStyle) }, WorkbookTheme.Office])!;
        fontOnlyLookup.Should().BeEmpty();

        var fillStyle = CellStyle.Default.Clone();
        fillStyle.FillColor = CellColor.White;
        var fillLookup = (IReadOnlyDictionary<(uint Row, uint Col), CellStyle>)buildLookup.Invoke(
            null,
            [new DisplayCell[] { Cell(1, 1, "fill", fillStyle) }, WorkbookTheme.Office])!;
        fillLookup.Should().ContainKey((1u, 1u));

        // R114 sibling coverage: a cell whose fill was set PURELY via a Theme Color reference
        // (no baked FillColor at all) must also be included -- this is exactly the reachability
        // gap the theme parameter fixes (StyleDiff.Apply leaves FillColor untouched when only
        // FillThemeColor is set).
        var themeOnlyFillStyle = CellStyle.Default.Clone();
        themeOnlyFillStyle.FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2);
        var themeOnlyFillLookup = (IReadOnlyDictionary<(uint Row, uint Col), CellStyle>)buildLookup.Invoke(
            null,
            [new DisplayCell[] { Cell(1, 1, "theme-fill", themeOnlyFillStyle) }, WorkbookTheme.Office])!;
        themeOnlyFillLookup.Should().ContainKey((1u, 1u),
            "a cell whose fill was set purely via FillThemeColor (no baked FillColor) must not be silently dropped from the render lookup");
    }

    [Fact]
    public void RenderCells_ReusesPixelsPerDipAcrossFormattedTextCalls()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        renderCells.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip).Width");
        renderCells.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip);");
    }

    [Fact]
    public void RenderCells_DrawsConditionalDataBarsBeforeIconsAndText()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources(
            "GridView.Rendering.cs",
            "GridView.ConditionalDataBars.cs",
            "GridView.ConditionalIcons.cs");
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void RenderStyledAndMergedCellSurfaces", StringComparison.Ordinal)];
        var splitPaneCell = source[
            source.IndexOf("private void RenderSplitPaneCell(", StringComparison.Ordinal)..
            source.IndexOf("private readonly struct SplitPaneCellRenderConsumer", StringComparison.Ordinal)];

        source.Should().Contain("public static void DrawConditionalDataBar");
        source.Should().Contain("cell.ConditionalDataBar is not null");
        renderCells.IndexOf("DrawConditionalDataBar(dc, dataBar, rect, _brushCache)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderCells.IndexOf("if (cell.ConditionalIcon is { } icon)", StringComparison.Ordinal));
        splitPaneCell.IndexOf("DrawConditionalDataBar(dc, splitDataBar, rect, _brushCache)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(splitPaneCell.IndexOf("if (cell.ConditionalIcon is { } splitIcon)", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderCells_ClipsTextOnlyWhenLaidOutBoundsOverflow()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var shouldClipText = source[
            source.IndexOf("private static bool ShouldClipText(", StringComparison.Ordinal)..
            source.IndexOf("private static Pen UnderlinePenForTextBrush", StringComparison.Ordinal)];

        shouldClipText.Should().Contain("CellTextOrientationLayoutPlanner.ShouldClip(");
        shouldClipText.Should().Contain("new CellTextLayoutRect(clipRect.Left, clipRect.Top, clipRect.Width, clipRect.Height)");
        shouldClipText.Should().Contain("text.Height");
        shouldClipText.Should().Contain("new CellTextOrientationLayout(");
        shouldClipText.Should().NotContain("style is not null || wrapText");
    }

    [Fact]
    public void RenderCells_SkipsOffscreenCellsBeforeTextLayout()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void RenderCellBackgroundBase", StringComparison.Ordinal)];

        renderCells.Should().Contain("var visibleRight = GetLogicalViewportWidth();");
        renderCells.Should().Contain("var visibleBottom = GetLogicalViewportHeight();");
        source.Should().Contain("private static bool IntersectsVisibleGrid");
        renderCells.Should().Contain("var cellTop = rowMetric.TopOffset + columnHeaderHeight;");
        renderCells.Should().Contain("if (cellTop >= visibleBottom) continue;");
        renderCells.Should().Contain("var cellLeft = colMetric.LeftOffset + rowHeaderWidth;");
        renderCells.Should().Contain("if (cellLeft >= visibleRight) continue;");
        renderCells.Should().Contain("var hasMergedText = _mergeLookup.Count > 0;");
        renderCells.Should().Contain("var cellMerge = hasMergedText ? FindMerge(cell.Row, cell.Col) : null;");
        renderCells.Should().Contain("rect.Left >= visibleRight");
        renderCells.Should().Contain("if (!IntersectsVisibleGrid(clipRect, visibleLeft, visibleTop, visibleRight, visibleBottom))");

        renderCells.IndexOf("if (cellTop >= visibleBottom) continue;", StringComparison.Ordinal)
            .Should().BeLessThan(renderCells.IndexOf("if (!colLookup.TryGetValue(cell.Col, out var colMetric)) continue;", StringComparison.Ordinal));
        renderCells.IndexOf("rect.Left >= visibleRight", StringComparison.Ordinal)
            .Should().BeLessThan(renderCells.IndexOf("var typefaceKey = CreateCellTypefaceKeyWithTheme(style);", StringComparison.Ordinal));
        renderCells.IndexOf("if (!IntersectsVisibleGrid(clipRect, visibleLeft, visibleTop, visibleRight, visibleBottom))", StringComparison.Ordinal)
            .Should().BeLessThan(renderCells.IndexOf("DrawCellText(dc, text, textLayout, style, textBrush, _underlinePenCache,", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderHeaders_ReusesPixelsPerDipAcrossFormattedTextCalls()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.Headers.cs");
        var renderHeaders = source[
            source.IndexOf("private void RenderHeaders(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("internal static string FormatColumnHeader", StringComparison.Ordinal)];

        renderHeaders.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        renderHeaders.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip);");
    }

    [Fact]
    public void RenderHeaders_CachesA1ColumnLabelsAcrossRenderPasses()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.Headers.cs");
        var formatColumnHeader = source[
            source.IndexOf("internal static string FormatColumnHeader", StringComparison.Ordinal)..];

        source.Should().Contain("private static readonly ConcurrentDictionary<uint, string> ColumnHeaderCache = new();");
        formatColumnHeader.Should().Contain("ColumnHeaderCache.GetOrAdd(column");
        formatColumnHeader.Should().Contain("CellAddress.NumberToColumnName(col)");
    }

    [Fact]
    public void RenderHeaders_CachesRowLabelsAcrossRenderPasses()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.Headers.cs");
        var drawRowHeader = source[
            source.IndexOf("private void DrawRowHeader", StringComparison.Ordinal)..
            source.IndexOf("private static IReadOnlyList<HeaderSelectionInterval>", StringComparison.Ordinal)];
        var formatRowHeader = source[
            source.IndexOf("internal static string FormatRowHeader", StringComparison.Ordinal)..];

        source.Should().Contain("private static readonly ConcurrentDictionary<uint, string> RowHeaderCache = new();");
        drawRowHeader.Should().Contain("FormatRowHeader(row.Row)");
        drawRowHeader.Should().NotContain("row.Row.ToString");
        formatRowHeader.Should().Contain("RowHeaderCache.GetOrAdd(row");
        formatRowHeader.Should().Contain("rowNumber.ToString(CultureInfo.InvariantCulture)");

        var formatter = typeof(GridView).GetMethod(
            "FormatRowHeader",
            BindingFlags.NonPublic | BindingFlags.Static);

        formatter.Should().NotBeNull();
        formatter!.Invoke(null, [1u]).Should().Be("1");
        formatter.Invoke(null, [1_048_576u]).Should().Be("1048576");
    }

    [Fact]
    public void RenderHeaders_CachesHeaderTextDrawingAcrossSelectionRepaints()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.Headers.cs");
        var drawColumnHeader = source[
            source.IndexOf("private void DrawColumnHeader", StringComparison.Ordinal)..
            source.IndexOf("private void DrawRowHeader", StringComparison.Ordinal)];
        var drawRowHeader = source[
            source.IndexOf("private void DrawRowHeader", StringComparison.Ordinal)..
            source.IndexOf("private void DrawHeaderText", StringComparison.Ordinal)];
        var drawHeaderText = source[
            source.IndexOf("private void DrawHeaderText", StringComparison.Ordinal)..
            source.IndexOf("private static IReadOnlyList<HeaderSelectionInterval>", StringComparison.Ordinal)];

        source.Should().Contain("private readonly Dictionary<HeaderTextDrawingKey, DrawingGroup> _headerTextDrawingCache = new();");
        drawColumnHeader.Should().Contain("DrawHeaderText(dc, textValue, text, 11, pixelsPerDip");
        drawRowHeader.Should().Contain("DrawHeaderText(dc, textValue, text, 11, pixelsPerDip");
        drawColumnHeader.Should().NotContain("dc.DrawText");
        drawRowHeader.Should().NotContain("dc.DrawText");
        drawHeaderText.Should().Contain("_headerTextDrawingCache.TryGetValue(key");
        drawHeaderText.Should().Contain("groupContext.DrawText(formattedText, origin);");
        drawHeaderText.Should().Contain("dc.DrawDrawing(drawing);");
    }

    [Fact]
    public void RenderHeaders_SkipsRowNumberTextForPartiallyClippedBottomRows()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.Headers.cs");
        var cacheKey = source[
            source.IndexOf("private readonly record struct HeaderBaseLayerCacheKey", StringComparison.Ordinal)..
            source.IndexOf("private readonly record struct HeaderTextDrawingKey", StringComparison.Ordinal)];
        var renderHeaders = source[
            source.IndexOf("private void RenderHeaders(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void RenderHeaderBaseLayer", StringComparison.Ordinal)];
        var drawRowHeader = source[
            source.IndexOf("private void DrawRowHeader", StringComparison.Ordinal)..
            source.IndexOf("private void DrawHeaderText", StringComparison.Ordinal)];

        cacheKey.Should().Contain("double VisibleBottom");
        renderHeaders.Should().Contain("var visibleBottom = GetRenderVisibleBottom();");
        drawRowHeader.Should().Contain("if (!ShouldDrawRowHeaderText(rect, visibleBottom))");
        drawRowHeader.Should().Contain("return;");
        source.Should().Contain("internal static bool ShouldDrawRowHeaderText(Rect rowHeaderRect, double visibleBottom)");
        source.Should().Contain("rowHeaderRect.Bottom <= visibleBottom");

        GridView.ShouldDrawRowHeaderText(new Rect(0, 18, 30, 20), visibleBottom: 38).Should().BeTrue();
        GridView.ShouldDrawRowHeaderText(new Rect(0, 18, 30, 20), visibleBottom: 37.5).Should().BeFalse();
    }

    [Fact]
    public void LiveResizeContinuation_DoesNotDrawTextForPartialTrailingRowHeaders()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var continuation = source[
            source.IndexOf("private void RenderViewportRowContinuation", StringComparison.Ordinal)..
            source.IndexOf("private void DrawViewportContinuationHorizontalGridLines", StringComparison.Ordinal)];

        continuation.Should().Contain("var height = Math.Min(rowHeight, viewportHeight - y);");
        continuation.Should().Contain("lastRow++;");
        continuation.Should().Contain("if (height >= rowHeight)");
        continuation.Should().Contain("DrawLiveResizeHeaderText(dc, FormatRowHeader(lastRow), headerRect, pixelsPerDip);");
    }

    [Fact]
    public void RenderHeaders_WalksSelectionIntervalsInsteadOfScanningRangesPerHeader()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.Headers.cs");
        var renderSelectedHeaders = source[
            source.IndexOf("private void RenderSelectedHeaders(", StringComparison.Ordinal)..
            source.IndexOf("private void DrawColumnHeader(", StringComparison.Ordinal)];
        var buildSelectionIntervals = source[
            source.IndexOf("private static IReadOnlyList<HeaderSelectionInterval> BuildHeaderSelectionIntervals(", StringComparison.Ordinal)..
            source.IndexOf("private static bool IsHeaderSelected(", StringComparison.Ordinal)];
        var isHeaderSelected = source[
            source.IndexOf("private static bool IsHeaderSelected(", StringComparison.Ordinal)..
            source.IndexOf("internal static string FormatColumnHeader", StringComparison.Ordinal)];
        var renderFreezeDivider = source[
            source.IndexOf("private void RenderFreezeDivider(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void RenderHeaders(DrawingContext dc)", StringComparison.Ordinal)];

        renderSelectedHeaders.Should().Contain("BuildColumnHeaderSelectionIntervals(selectedRanges, selRange)");
        renderSelectedHeaders.Should().Contain("BuildRowHeaderSelectionIntervals(selectedRanges, selRange)");
        renderSelectedHeaders.Should().Contain("IsHeaderSelected(col.Col, columnIntervals, ref columnIntervalIndex)");
        renderSelectedHeaders.Should().Contain("IsHeaderSelected(row.Row, rowIntervals, ref rowIntervalIndex)");
        buildSelectionIntervals.Should().Contain("if (selectedRanges.Count == 1)");
        buildSelectionIntervals.Should().Contain("return [selector(selectedRanges[0])];");
        buildSelectionIntervals.Should().Contain("intervals.Sort");
        isHeaderSelected.Should().Contain("while (intervalIndex < intervals.Count && index > intervals[intervalIndex].End)");
        renderSelectedHeaders.Should().NotContain(".Any(");
        renderSelectedHeaders.Should().NotContain("foreach (var range in selectedRanges)");
        // R43-render-frozen-header-2-3: the exact-match lookup was replaced with a
        // nearest-preceding-entry fallback so the divider still draws when the
        // frozen-boundary row/column is hidden and absent from RowMetrics/ColMetrics.
        renderFreezeDivider.Should().Contain("FindLastRowMetricAtOrBefore(Viewport.RowMetrics, fp.Rows)");
        renderFreezeDivider.Should().Contain("FindLastColMetricAtOrBefore(Viewport.ColMetrics, fp.Cols)");
        renderFreezeDivider.Should().NotContain("FirstOrDefault");
    }

    [Fact]
    public void RenderHeaders_CachesUnselectedHeaderLayerAcrossSelectionRepaints()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.Headers.cs");
        var renderHeaders = source[
            source.IndexOf("private void RenderHeaders(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void RenderHeaderBaseLayer(", StringComparison.Ordinal)];
        var renderHeaderBaseLayer = source[
            source.IndexOf("private void RenderHeaderBaseLayer(", StringComparison.Ordinal)..
            source.IndexOf("private DrawingGroup BuildHeaderBaseLayerCache(", StringComparison.Ordinal)];
        var buildHeaderBaseLayer = source[
            source.IndexOf("private DrawingGroup BuildHeaderBaseLayerCache(", StringComparison.Ordinal)..
            source.IndexOf("private void RenderHeaderBase(", StringComparison.Ordinal)];
        var renderSelectedHeaderLayer = source[
            source.IndexOf("private void RenderSelectedHeaderLayer(", StringComparison.Ordinal)..
            source.IndexOf("private SelectedHeaderLayerCacheKey CreateSelectedHeaderLayerCacheKey", StringComparison.Ordinal)];

        source.Should().Contain("private DrawingGroup? _headerBaseLayerCache;");
        source.Should().Contain("private HeaderBaseLayerCacheKey _headerBaseLayerCacheKey;");
        renderHeaders.Should().Contain("RenderHeaderBaseLayer(dc, viewport, rowHeaderWidth, columnHeaderHeight, rowOutlineWidth, columnOutlineHeight, visibleBottom, pixelsPerDip);");
        renderHeaders.Should().Contain("RenderSelectedHeaderLayer(dc, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, rowOutlineWidth, columnOutlineHeight, visibleBottom, pixelsPerDip);");
        renderHeaderBaseLayer.Should().Contain("_headerBaseLayerCache is { } cached && _headerBaseLayerCacheKey == key");
        renderHeaderBaseLayer.Should().Contain("dc.DrawDrawing(cached);");
        renderHeaderBaseLayer.Should().NotContain("SelectedRange");
        renderHeaderBaseLayer.Should().NotContain("SelectedRanges");
        buildHeaderBaseLayer.Should().Contain("RenderHeaderBase(groupContext, viewport, rowHeaderWidth, columnHeaderHeight, rowOutlineWidth, columnOutlineHeight, visibleBottom, pixelsPerDip);");
        buildHeaderBaseLayer.Should().Contain("group.Freeze();");
        source.Should().Contain("private DrawingGroup? _selectedHeaderLayerCache;");
        source.Should().Contain("private SelectedHeaderLayerCacheKey _selectedHeaderLayerCacheKey;");
        renderSelectedHeaderLayer.Should().Contain("_selectedHeaderLayerCache is { } cached && _selectedHeaderLayerCacheKey == key");
        renderSelectedHeaderLayer.Should().Contain("ShouldBuildSelectedHeaderLayerCache(key)");
        renderSelectedHeaderLayer.Should().Contain("RenderSelectedHeaders(dc, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, rowOutlineWidth, columnOutlineHeight, visibleBottom, pixelsPerDip);");
        source.Should().Contain("_hasLastSelectedHeaderLayerRenderKey && _lastSelectedHeaderLayerRenderKey == key");
        source.Should().Contain("BuildSelectedHeaderLayerCache(");
        source.Should().Contain("CalculateGridRangeListSignature(selectedRanges)");
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
    public void CalculateRowHeaderWidth_UsesLastVisibleRowWithoutMetricScan()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.cs");
        var calculateRowHeaderWidth = source[
            source.IndexOf("public static double CalculateRowHeaderWidth", StringComparison.Ordinal)..
            source.IndexOf("private const double ResizeHitZone", StringComparison.Ordinal)];

        calculateRowHeaderWidth.Should().Contain("viewport.RowMetrics[^1].Row");
        calculateRowHeaderWidth.Should().NotContain("foreach (var row in viewport.RowMetrics)");
        calculateRowHeaderWidth.Should().NotContain(".Max(");
        GridView.CalculateRowHeaderWidth(null).Should().Be(GridView.RowHeaderWidth);
        GridView.CalculateRowHeaderWidth(new ViewportModel([], [], [])).Should().Be(GridView.RowHeaderWidth);
        GridView.CalculateRowHeaderWidth(new ViewportModel(
            [],
            [new RowMetric(999, 20, 0), new RowMetric(1_000, 20, 20)],
            [])).Should().Be(36);
        GridView.CalculateRowHeaderWidth(new ViewportModel(
            [],
            [new RowMetric(999_999, 20, 0), new RowMetric(1_000_000, 20, 20)],
            [])).Should().Be(54);
    }

    [Fact]
    public void OutlineGroups_AddHeaderGuttersOnlyWhenPresent()
    {
        var plain = new ViewportModel(
            [],
            [new RowMetric(10, 20, 0)],
            [new ColMetric(1, 64, 0)]);

        var withRowOutline = plain with
        {
            RowOutlineGroups = [new OutlineGroupRange(1, 2, 3, 4, IsCollapsed: false)]
        };
        var withColumnOutline = plain with
        {
            ColumnOutlineGroups = [new OutlineGroupRange(2, 2, 3, 4, IsCollapsed: false)]
        };

        GridView.CalculateRowHeaderWidth(plain).Should().Be(GridView.RowHeaderWidth);
        GridView.CalculateColumnHeaderHeight(plain).Should().Be(GridView.ColHeaderHeight);
        GridView.CalculateRowHeaderWidth(withRowOutline).Should().Be(GridView.RowHeaderWidth + 26);
        GridView.CalculateColumnHeaderHeight(withColumnOutline).Should().Be(GridView.ColHeaderHeight + 40);
    }

    [Fact]
    public void OutlineGroupToggleHitTest_ReturnsRowAndColumnRequests()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(4, 20, 20)],
            [new ColMetric(1, 64, 0), new ColMetric(4, 64, 64)],
            RowOutlineGroups: [new OutlineGroupRange(1, 2, 3, 4, IsCollapsed: true)],
            ColumnOutlineGroups: [new OutlineGroupRange(1, 2, 3, 4, IsCollapsed: false)]);
        var rowHeaderWidth = GridView.CalculateRowHeaderWidth(viewport);
        var columnHeaderHeight = GridView.CalculateColumnHeaderHeight(viewport);

        GridView.TryHitTestOutlineGroupToggle(
                viewport,
                new Point(13, columnHeaderHeight + 30),
                rowHeaderWidth,
                columnHeaderHeight,
                out var rowRequest)
            .Should().BeTrue();
        rowRequest.Should().Be(new GridOutlineGroupToggleRequest(GridOutlineGroupAxis.Rows, 1, 2, 3, Collapse: false));

        GridView.TryHitTestOutlineGroupToggle(
                viewport,
                new Point(rowHeaderWidth + 96, 13),
                rowHeaderWidth,
                columnHeaderHeight,
                out var columnRequest)
            .Should().BeTrue();
        columnRequest.Should().Be(new GridOutlineGroupToggleRequest(GridOutlineGroupAxis.Columns, 1, 2, 3, Collapse: true));
    }

    // Regression coverage for the WPF numbered "Show Outline Level N" gutter buttons
    // (DrawRowOutlineLevelButtons/DrawColumnOutlineLevelButtons in GridView.Rendering.Headers.cs):
    // those buttons render whenever a second outline level exists, but before this test/fix there
    // was no hit-test for them anywhere in GridView.Input.cs/HitTesting.cs, so clicking one did
    // nothing. Uses independent single-axis viewports (rather than both axes at once) so the
    // corner-region row- and column-level button rects, which do overlap at level 1 in the shared
    // top-left gutter box, cannot make the assertions ambiguous.
    [Fact]
    public void OutlineLevelButtonHitTest_ReturnsRowAndColumnRequests()
    {
        var rowOnlyViewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(4, 20, 20)],
            [new ColMetric(1, 64, 0)],
            RowOutlineGroups: [new OutlineGroupRange(1, 2, 3, 4, IsCollapsed: true)]);
        var rowOnlyRowHeaderWidth = GridView.CalculateRowHeaderWidth(rowOnlyViewport);
        var rowOnlyColumnHeaderHeight = GridView.CalculateColumnHeaderHeight(rowOnlyViewport);

        GridView.TryHitTestOutlineLevelButton(
                rowOnlyViewport,
                new Point(13, 9),
                rowOnlyRowHeaderWidth,
                rowOnlyColumnHeaderHeight,
                out var rowRequest)
            .Should().BeTrue();
        rowRequest.Should().Be(new GridOutlineLevelButtonRequest(GridOutlineGroupAxis.Rows, 1));

        // Well outside every button rect: neither axis should claim it.
        GridView.TryHitTestOutlineLevelButton(
                rowOnlyViewport,
                new Point(100, 100),
                rowOnlyRowHeaderWidth,
                rowOnlyColumnHeaderHeight,
                out _)
            .Should().BeFalse();

        var columnOnlyViewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0)],
            [new ColMetric(1, 64, 0), new ColMetric(4, 64, 64)],
            ColumnOutlineGroups: [new OutlineGroupRange(1, 2, 3, 4, IsCollapsed: false)]);
        var columnOnlyRowHeaderWidth = GridView.CalculateRowHeaderWidth(columnOnlyViewport);
        var columnOnlyColumnHeaderHeight = GridView.CalculateColumnHeaderHeight(columnOnlyViewport);

        GridView.TryHitTestOutlineLevelButton(
                columnOnlyViewport,
                new Point(15, 13),
                columnOnlyRowHeaderWidth,
                columnOnlyColumnHeaderHeight,
                out var columnRequest)
            .Should().BeTrue();
        columnRequest.Should().Be(new GridOutlineLevelButtonRequest(GridOutlineGroupAxis.Columns, 1));
    }

    // Sibling no-regression check: the +/- toggle boxes sit at different positions than the
    // numbered level buttons (per-group vs. one fixed row/column near the corner), so adding the
    // level-button hit test must not change what the existing toggle hit test claims.
    [Fact]
    public void OutlineLevelButtonHitTest_DoesNotDisruptOutlineGroupToggleHitTest()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(4, 20, 20)],
            [new ColMetric(1, 64, 0), new ColMetric(4, 64, 64)],
            RowOutlineGroups: [new OutlineGroupRange(1, 2, 3, 4, IsCollapsed: true)],
            ColumnOutlineGroups: [new OutlineGroupRange(1, 2, 3, 4, IsCollapsed: false)]);
        var rowHeaderWidth = GridView.CalculateRowHeaderWidth(viewport);
        var columnHeaderHeight = GridView.CalculateColumnHeaderHeight(viewport);

        GridView.TryHitTestOutlineGroupToggle(
                viewport,
                new Point(13, columnHeaderHeight + 30),
                rowHeaderWidth,
                columnHeaderHeight,
                out var rowRequest)
            .Should().BeTrue();
        rowRequest.Should().Be(new GridOutlineGroupToggleRequest(GridOutlineGroupAxis.Rows, 1, 2, 3, Collapse: false));

        GridView.TryHitTestOutlineGroupToggle(
                viewport,
                new Point(rowHeaderWidth + 96, 13),
                rowHeaderWidth,
                columnHeaderHeight,
                out var columnRequest)
            .Should().BeTrue();
        columnRequest.Should().Be(new GridOutlineGroupToggleRequest(GridOutlineGroupAxis.Columns, 1, 2, 3, Collapse: true));

        // The toggle-box click point is down the row/column body, far from the corner
        // level-button boxes, so the new level-button hit test must not also claim it.
        GridView.TryHitTestOutlineLevelButton(
                viewport,
                new Point(13, columnHeaderHeight + 30),
                rowHeaderWidth,
                columnHeaderHeight,
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public void RenderSparklines_AvoidsEmptyRenderAllocations()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Overlays.Sparklines.cs");
        // Slice only the interactive entry point; the public print/PDF helper below deliberately
        // owns a one-shot uncached clip geometry when no renderer cache is supplied.
        var renderSparklinesStart = source.IndexOf("private void RenderSparklines(DrawingContext dc)", StringComparison.Ordinal);
        var renderSparklines = source[
            renderSparklinesStart..
            source.IndexOf("public static void DrawSparklineIntoCell", renderSparklinesStart, StringComparison.Ordinal)];

        // Early-out guards are still in place — no work done when there's nothing to render.
        renderSparklines.Should().Contain("Sparklines is not { Count: > 0 }");
        renderSparklines.Should().Contain("SparklineValues is not { Count: > 0 }");

        // Cell-lookup path is reused (no per-sparkline separate lookup builds).
        renderSparklines.Should().Contain("GetRenderCellLookups(Viewport)");
        renderSparklines.Should().Contain("var rowLookup = lookups.Rows;");
        renderSparklines.Should().Contain("var colLookup = lookups.Columns;");

        // Visible-grid intersection guard is still present.
        renderSparklines.Should().Contain("var visibleLeft = ActualRowHeaderWidth;");
        renderSparklines.Should().Contain("var visibleTop = EffectiveColHeaderHeight;");
        renderSparklines.Should().Contain("var visibleRight = ActualWidth;");
        renderSparklines.Should().Contain("var visibleBottom = ActualHeight;");
        renderSparklines.Should().Contain("IntersectsVisibleGrid(rect, visibleLeft, visibleTop, visibleRight, visibleBottom)");

        // Clip and draw calls exist.
        renderSparklines.Should().Contain("dc.PushClip(GetCellClipGeometry(rect));");
        renderSparklines.Should().Contain("DrawLineSparkline(dc, sparkline, values, rect,");
        renderSparklines.Should().Contain("DrawColumnSparkline(");

        // Visibility intersection happens before the clip push (performance invariant).
        renderSparklines.IndexOf("IntersectsVisibleGrid(rect", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSparklines.IndexOf("dc.PushClip(GetCellClipGeometry(rect));", StringComparison.Ordinal));

        // No inline geometry allocation inside the hot loop.
        renderSparklines.Should().NotContain("new RectangleGeometry(rect)");
        renderSparklines.Should().NotContain(".ToDictionary(");
        renderSparklines.Should().NotContain(".Select(");
        // Brushes are obtained from the shared cache, not allocated inline in the render loop.
        renderSparklines.Should().NotContain("new SolidColorBrush");
        renderSparklines.Should().NotContain("new Pen(");

        // Engine calls are still used (no layout math re-implemented here).
        source.Should().Contain("SparklineLayoutPlanner.VisitLineLayout(values, rect, ref consumer");
        source.Should().Contain("SparklineLayoutPlanner.VisitColumnLayout(values, rect, winLoss, ref consumer");
        source.Should().NotContain("BuildSparklineRowMetricLookup");
        source.Should().NotContain("BuildSparklineColumnMetricLookup");
        source.Should().NotContain("CalculateLineLayout(values, rect)");
        source.Should().NotContain("CalculateColumnLayout(values, rect, winLoss)");
    }

    [Fact]
    public void OnRender_SkipsHeavyVisualLayersDuringLiveResize()
    {
        var properties = AppUiSourceTestSupport.ReadAppUiSources("GridView.Properties.cs");
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.RenderDispatch.cs");
        var onRender = source[
            source.IndexOf("protected override void OnRender", StringComparison.Ordinal)..];

        properties.Should().Contain("public static readonly DependencyProperty IsLiveResizingProperty");
        properties.Should().Contain("FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender)");
        onRender.Should().Contain("var isLiveResizing = IsLiveResizing;");
        onRender.Should().Contain("var skipHeavyLayers = isLiveResizing || _resizeTarget != ResizeTarget.None;");
        onRender.Should().Contain("if (!skipHeavyLayers)");
        onRender.Should().Contain("RenderViewportContinuation(dc);");
        onRender.Should().Contain("RenderCells(dc);");
        onRender.Should().Contain("RenderSelection(dc);");

        onRender.IndexOf("RenderCells(dc);", StringComparison.Ordinal)
            .Should().BeLessThan(onRender.IndexOf("RenderWorksheetViewOverlay(dc);", StringComparison.Ordinal));
        onRender.IndexOf("RenderSelection(dc);", StringComparison.Ordinal)
            .Should().BeLessThan(onRender.IndexOf("RenderFormulaTraceArrows(dc);", StringComparison.Ordinal));
        onRender.IndexOf("RenderDrawingObjectLayersWithCache(dc);", StringComparison.Ordinal)
            .Should().BeGreaterThan(onRender.LastIndexOf("if (!skipHeavyLayers)", StringComparison.Ordinal));
    }

    [Fact]
    public void ViewportContinuation_PaintsExpandedGridWithoutViewportRefresh()
    {
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var continuation = rendering[
            rendering.IndexOf("private void RenderViewportContinuation", StringComparison.Ordinal)..
            rendering.IndexOf("private void RenderSplitPaneCells", StringComparison.Ordinal)];

        continuation.Should().Contain("var viewportWidth = GetLogicalViewportWidth();");
        continuation.Should().Contain("var viewportHeight = GetLogicalViewportHeight();");
        continuation.Should().Contain("viewportWidth > gridRight");
        continuation.Should().Contain("viewportHeight > gridBottom");
        continuation.Should().Contain("RenderViewportColumnContinuation");
        continuation.Should().Contain("RenderViewportRowContinuation");
        continuation.Should().Contain("DrawViewportContinuationHorizontalGridLines");
        continuation.Should().Contain("DrawViewportContinuationVerticalGridLines");
        continuation.Should().Contain("dc.DrawRectangle(Brushes.White, null");
        continuation.Should().NotContain("UpdateViewport");
        continuation.Should().NotContain("Viewport =");
    }

    [Fact]
    public void ViewportContinuation_ReusesPixelsPerDipForSyntheticHeaders()
    {
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var continuation = rendering[
            rendering.IndexOf("private void RenderViewportContinuation", StringComparison.Ordinal)..
            rendering.IndexOf("private void RenderSplitPaneCells", StringComparison.Ordinal)];

        continuation.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        continuation.Should().Contain("RenderViewportColumnContinuation(dc, gridRight, gridTop, viewportWidth, viewportHeight, pixelsPerDip);");
        continuation.Should().Contain("RenderViewportRowContinuation(dc, gridLeft, gridRight, gridBottom, viewportHeight, pixelsPerDip);");
        continuation.Should().Contain("DrawLiveResizeHeaderText(dc, FormatColumnHeader(++lastColumn, UseR1C1ReferenceStyle), headerRect, pixelsPerDip);");
        continuation.Should().Contain("lastRow++;");
        continuation.Should().Contain("DrawLiveResizeHeaderText(dc, FormatRowHeader(lastRow), headerRect, pixelsPerDip);");
        continuation.Should().NotContain("(++lastRow).ToString");
        continuation.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip);");
    }

    [Fact]
    public void ZoomedOutViewport_UsesLogicalExtentForPaintBounds()
    {
        var dispatch = AppUiSourceTestSupport.ReadAppUiSources("GridView.RenderDispatch.cs");
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");

        dispatch.Should().Contain("GetLogicalViewportWidth()");
        dispatch.Should().Contain("GetLogicalViewportHeight()");
        dispatch.Should().Contain("ActualWidth / zoom");
        dispatch.Should().Contain("ActualHeight / zoom");
        dispatch.Should().NotContain("ActualWidth / zoom, ActualHeight / zoom");

        rendering.Should().Contain("var visibleRight = GetLogicalViewportWidth();");
        rendering.Should().Contain("var visibleBottom = GetLogicalViewportHeight();");
        rendering.Should().Contain("var visibleRight = Math.Min(right, GetLogicalViewportWidth());");
        rendering.Should().Contain("var visibleBottom = Math.Min(bottom, GetLogicalViewportHeight());");
    }

    [Fact]
    public void RenderCaches_AreClassLevelFieldsNotLocalAllocations()
    {
        var gridViewSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.cs");

        gridViewSource.Should().Contain("private readonly Dictionary<CellColor, SolidColorBrush> _brushCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<CellBorder, Pen> _borderPenCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<CellTypefaceKey, Typeface> _typefaceCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<Brush, Pen> _underlinePenCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<DefaultTextLayoutKey, FormattedText> _defaultTextLayoutCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<DefaultWrappedTextLayoutKey, FormattedText> _defaultWrappedTextLayoutCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<CellStyle, bool> _defaultTextLayoutStyleCache = new(CellStyleReferenceComparer.Instance);");
        gridViewSource.Should().Contain("private readonly Dictionary<TextWidthLayoutKey, double> _textWidthLayoutCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<ShrinkTextLayoutKey, double> _shrinkTextLayoutCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<Rect, RectangleGeometry> _cellClipGeometryCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<Rect, Geometry> _commentIndicatorGeometryCache = new();");
        gridViewSource.Should().Contain("private RenderCellLookupCache? _renderCellLookupCache;");
        gridViewSource.Should().Contain("private OccupiedCellLookupCache? _occupiedCellLookupCache;");
    }

    [Fact]
    public void RenderCells_ReusesCommentIndicatorGeometriesAcrossRenderPasses()
    {
        var gridViewSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.cs");
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var drawCommentIndicator = rendering[
            rendering.IndexOf("private void DrawCommentIndicator", StringComparison.Ordinal)..
            rendering.IndexOf("private static bool ShouldClipText", StringComparison.Ordinal)];

        gridViewSource.Should().Contain("private readonly Dictionary<Rect, Geometry> _commentIndicatorGeometryCache = new();");
        rendering.Should().Contain("private const int CommentIndicatorGeometryCacheLimit = 16384;");
        drawCommentIndicator.Should().Contain("dc.DrawGeometry(CommentIndicatorBrush(kind), null, GetCommentIndicatorGeometry(rect));");
        drawCommentIndicator.Should().Contain("_commentIndicatorGeometryCache.TryGetValue(rect, out var cached)");
        drawCommentIndicator.Should().Contain("_commentIndicatorGeometryCache.Count >= CommentIndicatorGeometryCacheLimit");
        drawCommentIndicator.Should().Contain("_commentIndicatorGeometryCache.Clear();");
        drawCommentIndicator.Should().Contain("CreateCommentIndicatorGeometry(rect)");
        drawCommentIndicator.Should().Contain("_commentIndicatorGeometryCache.Add(rect, geometry);");
        drawCommentIndicator.Should().Contain("geometry.Freeze();");
        drawCommentIndicator.Should().NotContain("dc.DrawGeometry(Brushes.Red, null, geometry);");
    }

    [Fact]
    public void DefaultTextLayouts_AreCachedAcrossRenderPasses()
    {
        var cacheSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.TextLayoutCache.cs");
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var headers = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.Headers.cs");

        cacheSource.Should().Contain("private FormattedText GetDefaultFormattedText");
        cacheSource.Should().Contain("private FormattedText GetDefaultWrappedFormattedText");
        cacheSource.Should().Contain("private bool CanUseDefaultFormattedText");
        cacheSource.Should().Contain("private bool CanUseDefaultWrappedFormattedText");
        cacheSource.Should().Contain("_defaultTextLayoutStyleCache.TryGetValue");
        cacheSource.Should().Contain("_defaultTextLayoutCache.TryGetValue");
        cacheSource.Should().Contain("_defaultWrappedTextLayoutCache.TryGetValue");
        cacheSource.Should().Contain("_defaultTextLayoutCache.Count >= DefaultTextLayoutCacheLimit");
        cacheSource.Should().Contain("_defaultWrappedTextLayoutCache.Count >= DefaultWrappedTextLayoutCacheLimit");
        rendering.Should().Contain("_defaultTextLayoutStyleCache.Clear();");
        rendering.Should().Contain("CanUseDefaultFormattedText(style, wrapText)");
        rendering.Should().Contain("CanUseDefaultWrappedFormattedText(style)");
        rendering.Should().Contain("GetDefaultFormattedText(renderText, fontSize, pixelsPerDip)");
        rendering.Should().Contain("GetDefaultWrappedFormattedText(renderText, fontSize, wrapMaxTextWidth, wrapTextAlignment, pixelsPerDip)");
        // Header labels (column letters/row numbers/outline glyphs) use their own cached
        // FormattedText helper -- GetDefaultHeaderFormattedText, colored with HeaderTextBrush --
        // rather than sharing GetDefaultFormattedText/TextBrush with cell content, so that header
        // text (chrome) can react to Windows High Contrast independently of cell text (document
        // data). See GridView.TextLayoutCache.cs and ApplyHighContrastChromePalette in GridView.cs.
        headers.Should().Contain("GetDefaultHeaderFormattedText(");
        cacheSource.Should().Contain("private FormattedText GetDefaultHeaderFormattedText");
        cacheSource.Should().Contain("_defaultHeaderTextLayoutCache.TryGetValue");
        cacheSource.Should().Contain("_defaultHeaderTextLayoutCache.Count >= DefaultTextLayoutCacheLimit");
    }

    [Fact]
    public void ShrinkToFitTextWidthMeasurements_AreCachedAcrossRenderPasses()
    {
        var cacheSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.TextLayoutCache.cs");
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");

        cacheSource.Should().Contain("private double MeasureCellTextWidth");
        cacheSource.Should().Contain("private double ResolveCachedShrinkFontSize");
        cacheSource.Should().Contain("_textWidthLayoutCache.TryGetValue");
        cacheSource.Should().Contain("_textWidthLayoutCache.Count >= TextWidthLayoutCacheLimit");
        cacheSource.Should().Contain("_shrinkTextLayoutCache.TryGetValue");
        cacheSource.Should().Contain("_shrinkTextLayoutCache.Count >= ShrinkTextLayoutCacheLimit");
        rendering.Should().Contain("var typefaceKey = CreateCellTypefaceKeyWithTheme(style);");
        rendering.Should().Contain("ResolveCachedShrinkFontSize(");
        cacheSource.Should().Contain("MeasureCellTextWidth(text, typefaceKey, typeface, size, pixelsPerDip)");
        rendering.Should().NotContain("size => new FormattedText(");
    }

    [Fact]
    public void RenderCells_TrimsPersistentRenderCachesAtPaintBoundary()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("// Pass 1: non-default backgrounds and merged-cell surfaces", StringComparison.Ordinal)];

        renderCells.Should().Contain("if (Viewport?.SplitPanes is null)");
        renderCells.Should().Contain("TrimRenderCachesIfOversized();");
        source.Should().Contain("private void TrimRenderCachesIfOversized()");
        source.Should().Contain("private const int RenderCacheSizeLimit = 512;");
        source.Should().Contain("if (_brushCache.Count >= RenderCacheSizeLimit)");
        source.Should().Contain("_brushCache.Clear();");
        source.Should().Contain("if (_borderPenCache.Count >= RenderCacheSizeLimit)");
        source.Should().Contain("_borderPenCache.Clear();");
        source.Should().Contain("if (_typefaceCache.Count >= RenderCacheSizeLimit)");
        source.Should().Contain("_typefaceCache.Clear();");
        source.Should().Contain("if (_underlinePenCache.Count >= RenderCacheSizeLimit)");
        source.Should().Contain("_underlinePenCache.Clear();");
        renderCells.Should().NotContain("new Dictionary<CellColor, SolidColorBrush>");
        renderCells.Should().NotContain("new Dictionary<CellBorder, Pen>");
        renderCells.Should().NotContain("new Dictionary<CellTypefaceKey, Typeface>");
        renderCells.Should().NotContain("new Dictionary<Brush, Pen>");
    }

    [Fact]
    public void RenderCells_CachesStableViewportLookupsAcrossRepaints()
    {
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var cacheSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.RenderLookupCache.cs");
        var propertiesSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Properties.cs");
        var renderCells = rendering[
            rendering.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("// Pass 1: non-default backgrounds and merged-cell surfaces", StringComparison.Ordinal)];

        renderCells.Should().Contain("GetRenderCellLookups(viewport)");
        rendering.Should().Contain("ReferenceEquals(cached.Cells, viewport.Cells)");
        rendering.Should().Contain("ReferenceEquals(cached.RowMetrics, viewport.RowMetrics)");
        rendering.Should().Contain("ReferenceEquals(cached.ColMetrics, viewport.ColMetrics)");
        rendering.Should().Contain("occupied ??= GetOccupiedCellLookup(viewport, EditingCell);");
        cacheSource.Should().Contain("private sealed record RenderCellLookupCache");
        cacheSource.Should().Contain("IReadOnlyList<DisplayCell> Cells");
        cacheSource.Should().Contain("IReadOnlyList<RowMetric> RowMetrics");
        cacheSource.Should().Contain("IReadOnlyList<ColMetric> ColMetrics");
        cacheSource.Should().Contain("private sealed record OccupiedCellLookupCache");
        propertiesSource.Should().Contain("OnViewportChanged");
        propertiesSource.Should().NotContain("grid.ClearRenderLookupCache();");
    }

    [Fact]
    public void RenderCellLookups_AreReusedWhenViewportWrapperChangesButSourcesDoNot()
    {
        RunOnStaThread(() =>
        {
            var method = typeof(GridView).GetMethod(
                "GetRenderCellLookups",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var rows = new[] { new RowMetric(1, 20, 0) };
            var columns = new[] { new ColMetric(1, 64, 0) };
            var cells = new[] { Cell(1, 1, "value") };
            var grid = new GridView();
            var firstViewport = new ViewportModel(cells, rows, columns);
            var wrappedViewport = new ViewportModel(cells, rows, columns);
            var changedCellsViewport = new ViewportModel(new[] { Cell(1, 1, "value") }, rows, columns);

            var firstLookup = method!.Invoke(grid, [firstViewport]);
            var wrappedLookup = method.Invoke(grid, [wrappedViewport]);
            var changedCellsLookup = method.Invoke(grid, [changedCellsViewport]);

            wrappedLookup.Should().BeSameAs(firstLookup);
            changedCellsLookup.Should().NotBeSameAs(firstLookup);
        });
    }

    [Fact]
    public void RenderCells_LazilyBuildsOverflowOccupancyLookup()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var textPass = source[
            source.IndexOf("// Pass 3: text", StringComparison.Ordinal)..
            source.IndexOf("private void RenderCellBackgroundBase", StringComparison.Ordinal)];
        var setup = textPass[..textPass.IndexOf("foreach (var cell in viewport.Cells)", StringComparison.Ordinal)];
        var beforeTextLayout = textPass[
            textPass.IndexOf("bool canOverflow = CanOverflowCellText", StringComparison.Ordinal)..
            textPass.IndexOf("FormattedText text;", StringComparison.Ordinal)];
        var overflowBlock = textPass[
            textPass.IndexOf("double clipLeft = rect.Left;", StringComparison.Ordinal)..
            textPass.IndexOf("var shouldClipText = ShouldClipText", StringComparison.Ordinal)];

        setup.Should().Contain("HashSet<(uint Row, uint Col)>? occupied = null;");
        setup.Should().NotContain("GetOccupiedCellLookup(viewport, EditingCell)");
        beforeTextLayout.Should().NotContain("GetOccupiedCellLookup(viewport, EditingCell)");
        overflowBlock.Should().Contain("var overflowRight = canOverflow && textLayout.Bounds.Right > rect.Right;");
        overflowBlock.Should().Contain("occupied ??= GetOccupiedCellLookup(viewport, EditingCell);");
        overflowBlock.Should().Contain("var overflowLeft = canOverflow && textLayout.Bounds.Left < rect.Left && colMetric.Col > 1;");
        overflowBlock.Should().Contain("ViewportGeometryPlanner.CalculateOverflowAvailability(");
        overflowBlock.Should().Contain("ViewportOverflowTraversal.LogicalColumns");
        overflowBlock.Should().Contain("occupiedCells.Contains((cell.Row, column))");
        overflowBlock.Should().Contain("var clipRect = new Rect(clipLeft, rect.Top, renderWidth, rect.Height);");
    }

    [Fact]
    public void RenderCells_BatchesDefaultBackgroundAndGridLines()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("// Pass 2: explicit cell borders", StringComparison.Ordinal)];
        var backgroundBase = source[
            source.IndexOf("private void RenderCellBackgroundBase", StringComparison.Ordinal)..
            source.IndexOf("private static Dictionary<(uint Row, uint Col), CellStyle>", StringComparison.Ordinal)];

        renderCells.Should().Contain("var rowHeaderWidth = ActualRowHeaderWidth;");
        renderCells.Should().Contain("var columnHeaderHeight = EffectiveColHeaderHeight;");
        renderCells.Should().Contain("RenderCellBackgroundBase(dc, rowHeaderWidth, columnHeaderHeight);");
        renderCells.Should().Contain("var hasCellSurfaces = styleLookup.Count > 0;");
        renderCells.Should().Contain("var hasMergedSurfaces = _mergeLookup.Count > 0;");
        renderCells.Should().Contain("if (hasCellSurfaces || hasMergedSurfaces)");
        renderCells.Should().Contain("RenderStyledAndMergedCellSurfaces(");
        renderCells.Should().NotContain("foreach (var rowMetric in viewport.RowMetrics)");
        renderCells.Should().NotContain("foreach (var colMetric in viewport.ColMetrics)");

        var surfacePass = source[
            source.IndexOf("private void RenderStyledAndMergedCellSurfaces", StringComparison.Ordinal)..
            source.IndexOf("private void DrawCellSurface", StringComparison.Ordinal)];
        surfacePass.Should().Contain("foreach (var entry in styleLookup)");
        surfacePass.Should().Contain("FindMerge(row, column).HasValue");
        surfacePass.Should().Contain("foreach (var entry in _mergeLookup)");
        surfacePass.Should().Contain("entry.Key.Row != merge.Start.Row");
        surfacePass.Should().Contain("styleLookup.TryGetValue((merge.Start.Row, merge.Start.Col), out var bg)");
        surfacePass.Should().NotContain("foreach (var rowMetric in viewport.RowMetrics)");
        surfacePass.Should().NotContain("foreach (var colMetric in viewport.ColMetrics)");
        backgroundBase.Should().Contain("var visibleRight = Math.Min(right, GetLogicalViewportWidth());");
        backgroundBase.Should().Contain("var visibleBottom = Math.Min(bottom, GetLogicalViewportHeight());");
        backgroundBase.Should().Contain("dc.DrawRectangle(Brushes.White, null, rect);");
        backgroundBase.Should().Contain("foreach (var row in Viewport.RowMetrics)");
        backgroundBase.Should().Contain("if (y > visibleBottom)");
        backgroundBase.Should().Contain("dc.DrawLine(GridPen, new Point(left, y), new Point(visibleRight, y));");
        backgroundBase.Should().Contain("foreach (var column in Viewport.ColMetrics)");
        backgroundBase.Should().Contain("if (x > visibleRight)");
        backgroundBase.Should().Contain("dc.DrawLine(GridPen, new Point(x, top), new Point(x, visibleBottom));");
    }

    [Fact]
    public void RenderCells_SkipsDefaultLookingStyleBordersBeforeMetricLookup()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var borderPass = source[
            source.IndexOf("// Pass 2: explicit cell borders", StringComparison.Ordinal)..
            source.IndexOf("// Pass 2b: comment/note indicators", StringComparison.Ordinal)];

        borderPass.Should().Contain("cell.Style is not { } style || !HasVisibleCellBorder(style)");
        borderPass.IndexOf("!HasVisibleCellBorder(style)", StringComparison.Ordinal)
            .Should().BeLessThan(borderPass.IndexOf("rowLookupAll.TryGetValue(cell.Row", StringComparison.Ordinal));

        var hasVisibleBorder = typeof(GridView).GetMethod(
            "HasVisibleCellBorder",
            BindingFlags.NonPublic | BindingFlags.Static);
        hasVisibleBorder.Should().NotBeNull();
        hasVisibleBorder!.Invoke(null, [CellStyle.Default]).Should().Be(false);

        var borderedStyle = CellStyle.Default.Clone();
        borderedStyle.BorderBottom = new CellBorder(BorderStyle.Thin, CellColor.Black);
        hasVisibleBorder.Invoke(null, [borderedStyle]).Should().Be(true);
    }

    [Fact]
    public void RenderCells_ReusesCellColorBrushesWithinRenderPass()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources(
            "GridView.Rendering.cs",
            "GridView.Rendering.CellStyles.cs");
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void DrawCommentIndicator", StringComparison.Ordinal)];

        // The shared fill materialization plan resolves theme colors; WPF only materializes that
        // portable plan through its bounded brush cache.
        renderCells.Should().Contain("CellFillMaterializationPlanner.Plan(");
        renderCells.Should().Contain("BuildCellBackgroundBrush(fillPlan, _brushCache)");
        source.Should().Contain("BrushForCellColor(color, brushCache)");
        renderCells.Should().Contain("BrushForCellColor(fc, _brushCache)");
        renderCells.Should().NotContain("new SolidColorBrush");
    }

    [Fact]
    public void RenderCells_ReusesBorderPensWithinRenderPass()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("_brushCache, _borderPenCache");
    }

    [Fact]
    public void RenderCells_ReusesFillPatternPensAcrossBoundedRenderCaches()
    {
        var gridViewSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.cs");
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var cellStyles = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.CellStyles.cs");
        var drawFillPattern = cellStyles[
            cellStyles.IndexOf("private static void DrawFillPattern", StringComparison.Ordinal)..
            cellStyles.IndexOf("private static Pen FillPatternPenForCellColor", StringComparison.Ordinal)];

        gridViewSource.Should().Contain("private readonly Dictionary<CellColor, Pen> _fillPatternPenCache = new();");
        rendering.Should().Contain("if (_fillPatternPenCache.Count >= RenderCacheSizeLimit)");
        rendering.Should().Contain("_fillPatternPenCache.Clear();");
        rendering.Should().Contain("DrawFillPattern(dc, rect, fillPlan, _brushCache, _fillPatternPenCache)");
        cellStyles.Should().Contain("CellFillMaterializationPlan fillPlan");
        cellStyles.Should().Contain("FillPatternPenForCellColor(color, brushCache, fillPatternPenCache)");
        cellStyles.Should().Contain("pen.Freeze();");
        drawFillPattern.Should().NotContain("new Pen(");
        drawFillPattern.Should().NotContain("new CellStyle");
    }

    [Fact]
    public void RenderCells_ReusesTypefacesWithinRenderPass()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("var typefaceKey = CreateCellTypefaceKeyWithTheme(style);");
        renderCells.Should().Contain("CreateCellTypeface(typefaceKey, _typefaceCache)");
    }

    [Fact]
    public void RenderCells_DelaysCustomTextResourcesUntilCustomLayoutIsNeeded()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void RenderCellBackgroundBase", StringComparison.Ordinal)];
        var textSetup = renderCells[
            renderCells.IndexOf("double fontSize = ToDisplayFontSize", StringComparison.Ordinal)..
            renderCells.IndexOf("if (style?.ShrinkToFit == true && !wrapText)", StringComparison.Ordinal)];

        textSetup.Should().NotContain("CreateCellTypeface");
        textSetup.Should().NotContain("BrushForCellColor");
        // R114-render-theme-color-reresolution: the eligibility check now re-resolves
        // FontThemeColor against the active theme instead of reading the raw baked style.FontColor.
        renderCells.Should().Contain("if (style?.ResolveFontColor(WorkbookTheme) is { } fc && !fc.IsBlack)");
        renderCells.Should().Contain("textBrush = BrushForCellColor(fc, _brushCache);");
    }

    [Fact]
    public void RenderCells_ReusesDoubleUnderlinePensWithinRenderPass()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("DrawCellText(dc, text, textLayout, style, textBrush, _underlinePenCache,");
        source.Should().Contain("UnderlinePenForTextBrush(textBrush, underlinePenCache)");
        source.Should().Contain("private static Pen UnderlinePenForTextBrush");
        source.Should().Contain("pen.Freeze();");
        renderCells.Should().NotContain("new Pen(textBrush");
    }

    [Fact]
    public void ConditionalIconGlyphRenderer_ReusesFrozenBrushesAndPens()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("ConditionalIconGlyphRenderer.cs");
        var drawMethod = source[
            source.IndexOf("public static void Draw", StringComparison.Ordinal)..
            source.IndexOf("private static ConditionalIconAppearance ResolveAppearance", StringComparison.Ordinal)];

        source.Should().Contain("private static readonly SolidColorBrush IconDarkRedBrush");
        source.Should().Contain("private static readonly Pen OutlinePen");
        source.Should().Contain("private static readonly Pen WhiteThinPen");
        source.Should().Contain("private static readonly ConcurrentDictionary<ConditionalIconAppearanceKey, ConditionalIconAppearance> AppearanceCache");
        drawMethod.Should().Contain("if (rect.Width <= 0 || rect.Height <= 0)");
        drawMethod.IndexOf("if (rect.Width <= 0 || rect.Height <= 0)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(drawMethod.IndexOf("var appearance = ResolveAppearance(icon);", StringComparison.Ordinal));
        drawMethod.Should().Contain("var appearance = ResolveAppearance(icon);");
        drawMethod.Should().NotContain("ResolveColor(icon)");
        drawMethod.Should().NotContain("ResolveGlyphKind(icon)");
        source.Should().Contain("brush.Freeze();");
        source.Should().Contain("pen.Freeze();");
        source.Should().NotContain("new BrushConverter");
        source.Should().NotContain("new Pen(Brushes.White");
        source.Should().NotContain("var outline = new Pen");
    }

    [Fact]
    public void ConditionalIconLayoutPlanner_ForwardsClassificationToSharedResolver()
    {
        // The style-traits cache + palette mapping now live in the portable
        // ConditionalIconGlyphResolver (FreeX.App.Presentation). The WPF planner must forward to it
        // rather than re-inline the classification, so both hosts share one source of truth.
        var source = AppUiSourceTestSupport.ReadAppUiSources("ConditionalIconLayoutPlanner.cs");

        source.Should().Contain("ConditionalIconGlyphResolver.ResolveGlyphKind(icon.Style)");
        source.Should().Contain("ConditionalIconGlyphResolver.ResolveIconColor(icon.Style, icon.IconIndex, icon.IconCount)");
        source.Should().NotContain("StyleTraitCache");
        source.Should().NotContain("Contains(");
    }
}
