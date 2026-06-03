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
    public void RenderCells_UsesMetricDictionariesForExplicitBorderCells()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
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
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
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
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var buildStyleLookup = source[
            source.IndexOf("private static Dictionary<(uint Row, uint Col), CellStyle> BuildRenderCellStyleLookup", StringComparison.Ordinal)..
            source.IndexOf("private RenderCellLookupCache GetRenderCellLookups", StringComparison.Ordinal)];

        source.Should().Contain("private static readonly Dictionary<(uint Row, uint Col), CellStyle> EmptyRenderCellStyleLookup = new(0);");
        buildStyleLookup.Should().Contain("Dictionary<(uint Row, uint Col), CellStyle>? lookup = null;");
        buildStyleLookup.Should().Contain("cell.Style is { } style && HasVisibleCellSurface(style)");
        buildStyleLookup.Should().Contain("lookup ??= new Dictionary<(uint Row, uint Col), CellStyle>(cells.Count);");
        buildStyleLookup.Should().Contain("return lookup ?? EmptyRenderCellStyleLookup;");
        buildStyleLookup.Should().NotContain("var lookup = new Dictionary<(uint Row, uint Col), CellStyle>();");

        var buildLookup = typeof(GridView).GetMethod(
            "BuildRenderCellStyleLookup",
            BindingFlags.NonPublic | BindingFlags.Static);
        buildLookup.Should().NotBeNull();

        var defaultLookup = (IReadOnlyDictionary<(uint Row, uint Col), CellStyle>)buildLookup!.Invoke(
            null,
            [new DisplayCell[] { Cell(1, 1, "default", CellStyle.Default) }])!;
        defaultLookup.Should().BeEmpty();

        var fontOnlyStyle = CellStyle.Default.Clone();
        fontOnlyStyle.Bold = true;
        var fontOnlyLookup = (IReadOnlyDictionary<(uint Row, uint Col), CellStyle>)buildLookup.Invoke(
            null,
            [new DisplayCell[] { Cell(1, 1, "font", fontOnlyStyle) }])!;
        fontOnlyLookup.Should().BeEmpty();

        var fillStyle = CellStyle.Default.Clone();
        fillStyle.FillColor = CellColor.White;
        var fillLookup = (IReadOnlyDictionary<(uint Row, uint Col), CellStyle>)buildLookup.Invoke(
            null,
            [new DisplayCell[] { Cell(1, 1, "fill", fillStyle) }])!;
        fillLookup.Should().ContainKey((1u, 1u));
    }

    [Fact]
    public void RenderCells_ReusesPixelsPerDipAcrossFormattedTextCalls()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        renderCells.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip).Width");
        renderCells.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip);");
    }

    [Fact]
    public void RenderCells_ClipsTextOnlyWhenLaidOutBoundsOverflow()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var shouldClipText = source[
            source.IndexOf("private static bool ShouldClipText(", StringComparison.Ordinal)..
            source.IndexOf("private static Pen UnderlinePenForTextBrush", StringComparison.Ordinal)];

        shouldClipText.Should().Contain("if (wrapText && text.Height > clipRect.Height + tolerance)");
        shouldClipText.Should().Contain("textPoint.X + text.Width > clipRect.Right + tolerance");
        shouldClipText.Should().Contain("textPoint.Y + text.Height > clipRect.Bottom + tolerance");
        shouldClipText.Should().NotContain("style is not null || wrapText");
    }

    [Fact]
    public void RenderCells_SkipsOffscreenCellsBeforeTextLayout()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void RenderCellBackgroundBase", StringComparison.Ordinal)];

        renderCells.Should().Contain("var visibleRight = ActualWidth;");
        renderCells.Should().Contain("var visibleBottom = ActualHeight;");
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
            .Should().BeLessThan(renderCells.IndexOf("var typefaceKey = CreateCellTypefaceKey(style);", StringComparison.Ordinal));
        renderCells.IndexOf("if (!IntersectsVisibleGrid(clipRect, visibleLeft, visibleTop, visibleRight, visibleBottom))", StringComparison.Ordinal)
            .Should().BeLessThan(renderCells.IndexOf("dc.DrawText(text, textPoint);", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderHeaders_ReusesPixelsPerDipAcrossFormattedTextCalls()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Headers.cs"));
        var renderHeaders = source[
            source.IndexOf("private void RenderHeaders(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("internal static string FormatColumnHeader", StringComparison.Ordinal)];

        renderHeaders.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        renderHeaders.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip);");
    }

    [Fact]
    public void RenderHeaders_CachesA1ColumnLabelsAcrossRenderPasses()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Headers.cs"));
        var formatColumnHeader = source[
            source.IndexOf("internal static string FormatColumnHeader", StringComparison.Ordinal)..];

        source.Should().Contain("private static readonly ConcurrentDictionary<uint, string> ColumnHeaderCache = new();");
        formatColumnHeader.Should().Contain("ColumnHeaderCache.GetOrAdd(column");
        formatColumnHeader.Should().Contain("CellAddress.NumberToColumnName(col)");
    }

    [Fact]
    public void RenderHeaders_CachesRowLabelsAcrossRenderPasses()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Headers.cs"));
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
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Headers.cs"));
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
    public void RenderHeaders_WalksSelectionIntervalsInsteadOfScanningRangesPerHeader()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Headers.cs"));
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
        renderFreezeDivider.Should().Contain("FindRowMetric(Viewport.RowMetrics, fp.Rows)");
        renderFreezeDivider.Should().Contain("FindColMetric(Viewport.ColMetrics, fp.Cols)");
        renderFreezeDivider.Should().NotContain("FirstOrDefault");
    }

    [Fact]
    public void RenderHeaders_CachesUnselectedHeaderLayerAcrossSelectionRepaints()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Headers.cs"));
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
        renderHeaders.Should().Contain("RenderHeaderBaseLayer(dc, viewport, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);");
        renderHeaders.Should().Contain("RenderSelectedHeaderLayer(dc, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);");
        renderHeaderBaseLayer.Should().Contain("_headerBaseLayerCache is { } cached && _headerBaseLayerCacheKey == key");
        renderHeaderBaseLayer.Should().Contain("dc.DrawDrawing(cached);");
        renderHeaderBaseLayer.Should().NotContain("SelectedRange");
        renderHeaderBaseLayer.Should().NotContain("SelectedRanges");
        buildHeaderBaseLayer.Should().Contain("RenderHeaderBase(groupContext, viewport, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);");
        buildHeaderBaseLayer.Should().Contain("group.Freeze();");
        source.Should().Contain("private DrawingGroup? _selectedHeaderLayerCache;");
        source.Should().Contain("private SelectedHeaderLayerCacheKey _selectedHeaderLayerCacheKey;");
        renderSelectedHeaderLayer.Should().Contain("_selectedHeaderLayerCache is { } cached && _selectedHeaderLayerCacheKey == key");
        renderSelectedHeaderLayer.Should().Contain("ShouldBuildSelectedHeaderLayerCache(key)");
        renderSelectedHeaderLayer.Should().Contain("RenderSelectedHeaders(dc, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);");
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
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
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
    public void RenderSparklines_AvoidsEmptyRenderAllocations()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Overlays.Sparklines.cs"));
        var renderSparklines = source[
            source.IndexOf("private void RenderSparklines(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static SolidColorBrush FrozenBrush", StringComparison.Ordinal)];

        renderSparklines.Should().Contain("Sparklines is not { Count: > 0 }");
        renderSparklines.Should().Contain("SparklineValues is not { Count: > 0 }");
        renderSparklines.Should().Contain("GetRenderCellLookups(Viewport)");
        renderSparklines.Should().Contain("var rowLookup = lookups.Rows;");
        renderSparklines.Should().Contain("var colLookup = lookups.Columns;");
        source.Should().Contain("private static readonly SolidColorBrush SparklinePositiveBrush");
        source.Should().Contain("private static readonly Pen SparklineLinePen");
        renderSparklines.Should().Contain("DrawLineSparkline(dc, values, rect, SparklineLinePen)");
        renderSparklines.Should().Contain("DrawColumnSparkline(dc, values, rect, sparkline.Kind == SparklineKind.WinLoss, SparklinePositiveBrush, SparklineNegativeBrush)");
        renderSparklines.Should().Contain("dc.PushClip(GetCellClipGeometry(rect));");
        renderSparklines.Should().NotContain("new RectangleGeometry(rect)");
        source.Should().Contain("SparklineLayoutPlanner.VisitLineLayout(values, rect, ref consumer)");
        source.Should().Contain("SparklineLayoutPlanner.VisitColumnLayout(values, rect, winLoss, ref consumer)");
        source.Should().NotContain("BuildSparklineRowMetricLookup");
        source.Should().NotContain("BuildSparklineColumnMetricLookup");
        renderSparklines.Should().NotContain(".ToDictionary(");
        renderSparklines.Should().NotContain(".Select(");
        renderSparklines.Should().NotContain("new SolidColorBrush");
        renderSparklines.Should().NotContain("new Pen");
        source.Should().NotContain("CalculateLineLayout(values, rect)");
        source.Should().NotContain("CalculateColumnLayout(values, rect, winLoss)");
    }

    [Fact]
    public void OnRender_SkipsHeavyVisualLayersDuringLiveResize()
    {
        var properties = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Properties.cs"));
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderDispatch.cs"));
        var onRender = source[
            source.IndexOf("protected override void OnRender", StringComparison.Ordinal)..];

        properties.Should().Contain("public static readonly DependencyProperty IsLiveResizingProperty");
        properties.Should().Contain("FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender)");
        onRender.Should().Contain("var isLiveResizing = IsLiveResizing;");
        onRender.Should().Contain("var skipHeavyLayers = isLiveResizing || _resizeTarget != ResizeTarget.None;");
        onRender.Should().Contain("if (!skipHeavyLayers)");
        onRender.Should().Contain("RenderLiveResizeContinuation(dc);");
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
    public void LiveResizeContinuation_PaintsExpandedGridWithoutViewportRefresh()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
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
    public void LiveResizeContinuation_ReusesPixelsPerDipForSyntheticHeaders()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var continuation = rendering[
            rendering.IndexOf("private void RenderLiveResizeContinuation", StringComparison.Ordinal)..
            rendering.IndexOf("private void RenderSplitPaneCells", StringComparison.Ordinal)];

        continuation.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        continuation.Should().Contain("RenderLiveResizeColumnContinuation(dc, gridRight, gridTop, pixelsPerDip);");
        continuation.Should().Contain("RenderLiveResizeRowContinuation(dc, gridLeft, gridRight, gridBottom, pixelsPerDip);");
        continuation.Should().Contain("DrawLiveResizeHeaderText(dc, FormatColumnHeader(++lastColumn, UseR1C1ReferenceStyle), headerRect, pixelsPerDip);");
        continuation.Should().Contain("DrawLiveResizeHeaderText(dc, FormatRowHeader(++lastRow), headerRect, pixelsPerDip);");
        continuation.Should().NotContain("(++lastRow).ToString");
        continuation.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip);");
    }

    [Fact]
    public void RenderCaches_AreClassLevelFieldsNotLocalAllocations()
    {
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));

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
        gridViewSource.Should().Contain("private RenderCellLookupCache? _renderCellLookupCache;");
        gridViewSource.Should().Contain("private OccupiedCellLookupCache? _occupiedCellLookupCache;");
    }

    [Fact]
    public void DefaultTextLayouts_AreCachedAcrossRenderPasses()
    {
        var cacheSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.TextLayoutCache.cs"));
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var headers = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Headers.cs"));

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
        rendering.Should().Contain("GetDefaultFormattedText(cell.DisplayText, fontSize, pixelsPerDip)");
        rendering.Should().Contain("GetDefaultWrappedFormattedText(cell.DisplayText, fontSize, wrapMaxTextWidth, wrapTextAlignment, pixelsPerDip)");
        headers.Should().Contain("GetDefaultFormattedText(");
    }

    [Fact]
    public void ShrinkToFitTextWidthMeasurements_AreCachedAcrossRenderPasses()
    {
        var cacheSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.TextLayoutCache.cs"));
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));

        cacheSource.Should().Contain("private double MeasureCellTextWidth");
        cacheSource.Should().Contain("private double ResolveCachedShrinkFontSize");
        cacheSource.Should().Contain("_textWidthLayoutCache.TryGetValue");
        cacheSource.Should().Contain("_textWidthLayoutCache.Count >= TextWidthLayoutCacheLimit");
        cacheSource.Should().Contain("_shrinkTextLayoutCache.TryGetValue");
        cacheSource.Should().Contain("_shrinkTextLayoutCache.Count >= ShrinkTextLayoutCacheLimit");
        rendering.Should().Contain("var typefaceKey = CreateCellTypefaceKey(style);");
        rendering.Should().Contain("ResolveCachedShrinkFontSize(");
        cacheSource.Should().Contain("MeasureCellTextWidth(text, typefaceKey, typeface, size, pixelsPerDip)");
        rendering.Should().NotContain("size => new FormattedText(");
    }

    [Fact]
    public void RenderCells_ClearsCachesAtStartOfEachPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("// Pass 1: non-default backgrounds and merged-cell surfaces", StringComparison.Ordinal)];

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
    public void RenderCells_CachesStableViewportLookupsAcrossRepaints()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var cacheSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderLookupCache.cs"));
        var propertiesSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Properties.cs"));
        var renderCells = rendering[
            rendering.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("// Pass 1: non-default backgrounds and merged-cell surfaces", StringComparison.Ordinal)];

        renderCells.Should().Contain("GetRenderCellLookups(viewport)");
        rendering.Should().Contain("ReferenceEquals(cached.Viewport, viewport)");
        rendering.Should().Contain("occupied ??= GetOccupiedCellLookup(viewport, EditingCell);");
        cacheSource.Should().Contain("private sealed record RenderCellLookupCache");
        cacheSource.Should().Contain("private sealed record OccupiedCellLookupCache");
        propertiesSource.Should().Contain("OnViewportChanged");
        propertiesSource.Should().Contain("grid.ClearRenderLookupCache();");
    }

    [Fact]
    public void RenderCells_LazilyBuildsOverflowOccupancyLookup()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var textPass = source[
            source.IndexOf("// Pass 3: text", StringComparison.Ordinal)..
            source.IndexOf("private void RenderCellBackgroundBase", StringComparison.Ordinal)];
        var setup = textPass[..textPass.IndexOf("foreach (var cell in viewport.Cells)", StringComparison.Ordinal)];
        var overflowBlock = textPass[
            textPass.IndexOf("if (canOverflow)", StringComparison.Ordinal)..
            textPass.IndexOf("var typeface = CreateCellTypeface", StringComparison.Ordinal)];

        setup.Should().Contain("HashSet<(uint Row, uint Col)>? occupied = null;");
        setup.Should().NotContain("GetOccupiedCellLookup(viewport, EditingCell)");
        overflowBlock.Should().Contain("occupied ??= GetOccupiedCellLookup(viewport, EditingCell);");
        overflowBlock.Should().Contain("!occupied.Contains((cell.Row, nextCol))");
    }

    [Fact]
    public void RenderCells_BatchesDefaultBackgroundAndGridLines()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
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
        renderCells.Should().Contain("if (bg is null && !merge.HasValue)");
        renderCells.Should().Contain("continue;");
        backgroundBase.Should().Contain("var visibleRight = Math.Min(right, ActualWidth);");
        backgroundBase.Should().Contain("var visibleBottom = Math.Min(bottom, ActualHeight);");
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
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
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
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
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
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("_brushCache, _borderPenCache");
    }

    [Fact]
    public void RenderCells_ReusesFillPatternPensWithinRenderPass()
    {
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var cellStyles = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.CellStyles.cs"));
        var renderCells = rendering[
            rendering.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("// Pass 1: non-default backgrounds and merged-cell surfaces", StringComparison.Ordinal)];
        var drawFillPattern = cellStyles[
            cellStyles.IndexOf("private static void DrawFillPattern", StringComparison.Ordinal)..
            cellStyles.IndexOf("private static Pen FillPatternPenForCellColor", StringComparison.Ordinal)];

        gridViewSource.Should().Contain("private readonly Dictionary<CellColor, Pen> _fillPatternPenCache = new();");
        renderCells.Should().Contain("_fillPatternPenCache.Clear();");
        rendering.Should().Contain("DrawFillPattern(dc, rect, bg, _brushCache, _fillPatternPenCache)");
        cellStyles.Should().Contain("FillPatternPenForCellColor(color, brushCache, fillPatternPenCache)");
        cellStyles.Should().Contain("pen.Freeze();");
        drawFillPattern.Should().NotContain("new Pen(");
        drawFillPattern.Should().NotContain("new CellStyle");
    }

    [Fact]
    public void RenderCells_ReusesTypefacesWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("var typefaceKey = CreateCellTypefaceKey(style);");
        renderCells.Should().Contain("CreateCellTypeface(typefaceKey, _typefaceCache)");
    }

    [Fact]
    public void RenderCells_DelaysCustomTextResourcesUntilCustomLayoutIsNeeded()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void RenderCellBackgroundBase", StringComparison.Ordinal)];
        var textSetup = renderCells[
            renderCells.IndexOf("double fontSize = ToDisplayFontSize", StringComparison.Ordinal)..
            renderCells.IndexOf("if (style?.ShrinkToFit == true && !wrapText)", StringComparison.Ordinal)];

        textSetup.Should().NotContain("CreateCellTypeface");
        textSetup.Should().NotContain("BrushForCellColor");
        renderCells.Should().Contain("if (style?.FontColor is { } fc && !fc.IsBlack)");
        renderCells.Should().Contain("textBrush = BrushForCellColor(fc, _brushCache);");
    }

    [Fact]
    public void RenderCells_ReusesDoubleUnderlinePensWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
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
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "ConditionalIconGlyphRenderer.cs"));
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
    public void ConditionalIconLayoutPlanner_CachesStyleTraitClassification()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "ConditionalIconLayoutPlanner.cs"));
        var resolveGlyphKind = source[
            source.IndexOf("public static ConditionalIconGlyphKind ResolveGlyphKind", StringComparison.Ordinal)..
            source.IndexOf("private static ConditionalIconStyleTraits ResolveStyleTraits", StringComparison.Ordinal)];
        var resolveColor = source[
            source.IndexOf("public static string ResolveColor", StringComparison.Ordinal)..];

        source.Should().Contain("private static readonly ConcurrentDictionary<string, ConditionalIconStyleTraits> StyleTraitCache");
        source.Should().Contain("new(StringComparer.OrdinalIgnoreCase)");
        resolveGlyphKind.Should().Contain("ResolveStyleTraits(icon.Style)");
        resolveGlyphKind.Should().NotContain("Contains(");
        resolveColor.Should().Contain("ResolveStyleTraits(icon.Style).IsGray");
        resolveColor.Should().NotContain("icon.Style.Contains");
    }
}
