using System;
using System.IO;
using System.Reflection;
using FreeX.App.UI;
using FreeX.Core.Model;
using FluentAssertions;
using System.Windows;

namespace FreeX.App.UI.Tests;

public sealed class GridViewRenderPerformanceTests
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
    public void RenderCells_DoesNotClipStyledTextUnlessItWrapsOrOverflows()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var shouldClipText = source[
            source.IndexOf("private static bool ShouldClipText(", StringComparison.Ordinal)..
            source.IndexOf("private static Pen UnderlinePenForTextBrush", StringComparison.Ordinal)];

        shouldClipText.Should().Contain("if (wrapText)");
        shouldClipText.Should().Contain("textPoint.X + text.Width > clipRect.Right + tolerance");
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
        renderCells.Should().Contain("rect.Left >= visibleRight");
        renderCells.Should().Contain("if (!IntersectsVisibleGrid(clipRect, visibleLeft, visibleTop, visibleRight, visibleBottom))");

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

        source.Should().Contain("private DrawingGroup? _headerBaseLayerCache;");
        source.Should().Contain("private HeaderBaseLayerCacheKey _headerBaseLayerCacheKey;");
        renderHeaders.Should().Contain("RenderHeaderBaseLayer(dc, viewport, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);");
        renderHeaders.Should().Contain("RenderSelectedHeaders(dc, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);");
        renderHeaderBaseLayer.Should().Contain("_headerBaseLayerCache is { } cached && _headerBaseLayerCacheKey == key");
        renderHeaderBaseLayer.Should().Contain("dc.DrawDrawing(cached);");
        renderHeaderBaseLayer.Should().NotContain("SelectedRange");
        renderHeaderBaseLayer.Should().NotContain("SelectedRanges");
        buildHeaderBaseLayer.Should().Contain("RenderHeaderBase(groupContext, viewport, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);");
        buildHeaderBaseLayer.Should().Contain("group.Freeze();");
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
    public void RenderAutofillPreview_ReusesFrozenStaticDashedPen()
    {
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
        var overlaysSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Overlays.cs"));
        var renderAutofill = overlaysSource[
            overlaysSource.IndexOf("private void RenderAutofillPreview", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private void RenderMarchingAnts", StringComparison.Ordinal)];

        gridViewSource.Should().Contain("private static readonly Pen AutofillPreviewPen = MakeAutofillPreviewPen();");
        gridViewSource.Should().Contain("private static Pen MakeAutofillPreviewPen()");
        gridViewSource.Should().Contain("DashStyle = new DashStyle([4.0, 4.0], 0)");
        gridViewSource.Should().Contain("pen.Freeze();");
        renderAutofill.Should().Contain("dc.DrawRectangle(null, AutofillPreviewPen, rect);");
        renderAutofill.Should().NotContain("new Pen");
        renderAutofill.Should().NotContain("new SolidColorBrush");
        renderAutofill.Should().NotContain("new DashStyle");
    }

    [Fact]
    public void RenderMarchingAnts_ReusesCachedPhasePens()
    {
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
        var overlaysSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Overlays.cs"));
        var renderMarchingAnts = overlaysSource[
            overlaysSource.IndexOf("private void RenderMarchingAnts", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private void RenderFormulaTraceArrows", StringComparison.Ordinal)];

        gridViewSource.Should().Contain("private const int MarchingAntsPhaseCount = 16;");
        gridViewSource.Should().Contain("private static readonly Pen[] MarchingAntsBlackPens = CreateMarchingAntsPens(Brushes.Black, 2.5);");
        gridViewSource.Should().Contain("private static readonly Pen[] MarchingAntsCopyOverlayPens = CreateMarchingAntsPens(Brushes.White, 1.5);");
        gridViewSource.Should().Contain("private static readonly Pen[] MarchingAntsCutOverlayPens = CreateMarchingAntsPens(MakeBrush(245, 124, 0), 1.5);");
        gridViewSource.Should().Contain("private static Pen[] CreateMarchingAntsPens");
        gridViewSource.Should().Contain("private static int GetMarchingAntsPhase(double offset)");
        renderMarchingAnts.Should().Contain("var phase = GetMarchingAntsPhase(_marchOffset);");
        renderMarchingAnts.Should().Contain("MarchingAntsBlackPens[phase]");
        renderMarchingAnts.Should().Contain("ClipboardIsCut ? MarchingAntsCutOverlayPens[phase] : MarchingAntsCopyOverlayPens[phase]");
        renderMarchingAnts.Should().NotContain("new Pen");
        renderMarchingAnts.Should().NotContain("new SolidColorBrush");
        renderMarchingAnts.Should().NotContain("new DashStyle");
    }

    [Fact]
    public void ClipboardRangeAnimationTimer_StopsWhenGridUnloads()
    {
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
        var stateSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.State.cs"));
        var constructorStart = gridViewSource.IndexOf("public GridView()", StringComparison.Ordinal);
        var constructor = gridViewSource[
            constructorStart..
            gridViewSource.IndexOf("/// <summary>", constructorStart, StringComparison.Ordinal)];
        var stopMarchTimer = stateSource[
            stateSource.IndexOf("private void StopMarchTimer()", StringComparison.Ordinal)..];

        constructor.Should().Contain("Unloaded += (_, _) => StopMarchTimer();");
        stopMarchTimer.Should().Contain("_marchTimer?.Stop();");
        stopMarchTimer.Should().Contain("_marchTimer = null;");
    }

    [Fact]
    public void GetMarchingAntsPhase_NormalizesAnimationOffset()
    {
        var getPhase = typeof(GridView).GetMethod(
            "GetMarchingAntsPhase",
            BindingFlags.NonPublic | BindingFlags.Static);

        getPhase.Should().NotBeNull();
        getPhase!.Invoke(null, [0d]).Should().Be(0);
        getPhase.Invoke(null, [1.5d]).Should().Be(3);
        getPhase.Invoke(null, [7.5d]).Should().Be(15);
        getPhase.Invoke(null, [8.0d]).Should().Be(0);
        getPhase.Invoke(null, [-0.5d]).Should().Be(15);
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
        onRender.IndexOf("if (ObjectDisplayMode == GridObjectDisplayMode.Placeholders)", StringComparison.Ordinal)
            .Should().BeGreaterThan(onRender.LastIndexOf("if (!skipHeavyLayers)", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectionOnlyInvalidations_ReusePreSelectionLayerCache()
    {
        var properties = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Properties.cs"));
        var dispatch = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderDispatch.cs"));
        var cache = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderSurfaceCache.cs"));
        var onRender = dispatch[
            dispatch.IndexOf("protected override void OnRender", StringComparison.Ordinal)..
            dispatch.IndexOf("private void RenderPreSelectionLayers", StringComparison.Ordinal)];

        properties.Should().Contain("OnSelectionVisualPropertyChanged");
        properties.Should().Contain("grid.MarkSelectionVisualOnlyChange();");
        dispatch.Should().Contain("RenderPreSelectionLayersWithCache(dc, skipHeavyLayers, isLiveResizing);");
        cache.Should().Contain("RenderPreSelectionLayers(dc, skipHeavyLayers, isLiveResizing);");
        cache.Should().Contain("_selectionVisualOnlyChangePending &&");
        cache.Should().Contain("dc.DrawDrawing(cached);");
        cache.Should().Contain("BuildPreSelectionLayerCache(skipHeavyLayers, isLiveResizing)");
        cache.Should().NotContain("SelectedRange");
        cache.Should().NotContain("SelectedRanges");

        onRender.IndexOf("RenderHeaders(dc);", StringComparison.Ordinal)
            .Should().BeLessThan(onRender.IndexOf("RenderPreSelectionLayersWithCache", StringComparison.Ordinal));
        onRender.IndexOf("RenderPreSelectionLayersWithCache", StringComparison.Ordinal)
            .Should().BeLessThan(onRender.IndexOf("RenderSelection(dc);", StringComparison.Ordinal));
        onRender.IndexOf("RenderSelection(dc);", StringComparison.Ordinal)
            .Should().BeLessThan(onRender.IndexOf("RenderPostSelectionLayers", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectionOnlyInvalidations_ReuseRenderClipGeometry()
    {
        var dispatch = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderDispatch.cs"));
        var onRender = dispatch[
            dispatch.IndexOf("protected override void OnRender", StringComparison.Ordinal)..
            dispatch.IndexOf("private RectangleGeometry GetRenderClipGeometry", StringComparison.Ordinal)];
        var getRenderClipGeometry = dispatch[
            dispatch.IndexOf("private RectangleGeometry GetRenderClipGeometry", StringComparison.Ordinal)..
            dispatch.IndexOf("private void RenderPreSelectionLayers", StringComparison.Ordinal)];

        dispatch.Should().Contain("private RectangleGeometry? _renderClipGeometryCache;");
        dispatch.Should().Contain("private Rect _renderClipGeometryCacheRect;");
        onRender.Should().Contain("dc.PushClip(GetRenderClipGeometry(new Rect(0, 0, ActualWidth / zoom, ActualHeight / zoom)));");
        onRender.Should().NotContain("new RectangleGeometry");
        getRenderClipGeometry.Should().Contain("_renderClipGeometryCache is { } cached && _renderClipGeometryCacheRect == clipRect");
        getRenderClipGeometry.Should().Contain("var geometry = new RectangleGeometry(clipRect);");
        getRenderClipGeometry.Should().Contain("geometry.Freeze();");
    }

    [Fact]
    public void SelectionOnlyInvalidations_SkipEmptyPostSelectionLayers()
    {
        var dispatch = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderDispatch.cs"));
        var splitPanes = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.SplitPanes.cs"));
        var drawingObjects = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var renderPostSelectionLayers = dispatch[
            dispatch.IndexOf("private void RenderPostSelectionLayers", StringComparison.Ordinal)..
            dispatch.IndexOf("private bool HasPostSelectionLayerWork", StringComparison.Ordinal)];
        var hasPostSelectionLayerWork = dispatch[
            dispatch.IndexOf("private bool HasPostSelectionLayerWork", StringComparison.Ordinal)..
            dispatch.IndexOf("private bool HasDrawingObjectLayerWork", StringComparison.Ordinal)];
        var hasDrawingObjectLayerWork = dispatch[
            dispatch.IndexOf("private bool HasDrawingObjectLayerWork", StringComparison.Ordinal)..];
        var renderSplitDivider = splitPanes[
            splitPanes.IndexOf("private void RenderSplitDivider", StringComparison.Ordinal)..
            splitPanes.IndexOf("private void RenderSplitDividerHandles", StringComparison.Ordinal)];
        var renderNativeControls = drawingObjects[
            drawingObjects.IndexOf("private void RenderNativeSlicerTimelineControls", StringComparison.Ordinal)..
            drawingObjects.IndexOf("public static bool TryCreateDrawingAnchorRect", StringComparison.Ordinal)];

        renderPostSelectionLayers.Should().Contain("if (!HasPostSelectionLayerWork(skipHeavyLayers))");
        hasPostSelectionLayerWork.Should().Contain("Viewport?.FrozenPanes is not null");
        hasPostSelectionLayerWork.Should().Contain("Viewport?.SplitPanes is not null");
        hasPostSelectionLayerWork.Should().Contain("_resizeTarget != ResizeTarget.None");
        hasPostSelectionLayerWork.Should().Contain("FormulaTraceArrows is { Count: > 0 }");
        hasPostSelectionLayerWork.Should().Contain("ClipboardRange is not null");
        hasPostSelectionLayerWork.Should().Contain("HasDrawingObjectLayerWork()");
        hasDrawingObjectLayerWork.Should().Contain("SelectedObjectId != Guid.Empty && SelectedObjectKind != ObjectKind.None");
        hasDrawingObjectLayerWork.Should().Contain("ObjectDisplayMode == GridObjectDisplayMode.Nothing");
        hasDrawingObjectLayerWork.Should().Contain("Charts is { Count: > 0 }");
        hasDrawingObjectLayerWork.Should().Contain("TextBoxes is { Count: > 0 }");
        renderSplitDivider.Should().Contain("if (Viewport?.SplitPanes is null) return;");
        renderNativeControls.Should().Contain("NativeSlicers is not { Count: > 0 } && NativeTimelines is not { Count: > 0 }");
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
        continuation.Should().Contain("DrawLiveResizeHeaderText(dc, (++lastRow).ToString(CultureInfo.InvariantCulture), headerRect, pixelsPerDip);");
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
        gridViewSource.Should().Contain("private readonly Dictionary<TextWidthLayoutKey, double> _textWidthLayoutCache = new();");
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
        cacheSource.Should().Contain("private static bool CanUseDefaultFormattedText");
        cacheSource.Should().Contain("private static bool CanUseDefaultWrappedFormattedText");
        cacheSource.Should().Contain("_defaultTextLayoutCache.TryGetValue");
        cacheSource.Should().Contain("_defaultWrappedTextLayoutCache.TryGetValue");
        cacheSource.Should().Contain("_defaultTextLayoutCache.Count >= DefaultTextLayoutCacheLimit");
        cacheSource.Should().Contain("_defaultWrappedTextLayoutCache.Count >= DefaultWrappedTextLayoutCacheLimit");
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
        cacheSource.Should().Contain("_textWidthLayoutCache.TryGetValue");
        cacheSource.Should().Contain("_textWidthLayoutCache.Count >= TextWidthLayoutCacheLimit");
        rendering.Should().Contain("var typefaceKey = CreateCellTypefaceKey(style);");
        rendering.Should().Contain("MeasureCellTextWidth(cell.DisplayText, typefaceKey, typeface, size, pixelsPerDip)");
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
        backgroundBase.Should().Contain("dc.DrawRectangle(Brushes.White, null, rect);");
        backgroundBase.Should().Contain("foreach (var row in Viewport.RowMetrics)");
        backgroundBase.Should().Contain("foreach (var column in Viewport.ColMetrics)");
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
    public void ResizeDragInput_ReusesMetricScanHelpersWithoutLinqIterators()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Input.cs"));
        var resizeMove = source[
            source.IndexOf("if (_resizeTarget == ResizeTarget.Column)", StringComparison.Ordinal)..
            source.IndexOf("public static GridAutoScrollRequest CalculateAutofillEdgeScrollIntent", StringComparison.Ordinal)];
        var resizeStart = source[
            source.IndexOf("if (target != ResizeTarget.None)", StringComparison.Ordinal)..
            source.IndexOf("protected override void OnMouseRightButtonDown", StringComparison.Ordinal)];

        resizeMove.Should().Contain("if (Viewport is null)");
        resizeMove.Should().Contain("FindColMetric(Viewport.ColMetrics, _resizeIndex)");
        resizeMove.Should().Contain("FindRowMetric(Viewport.RowMetrics, _resizeIndex)");
        resizeMove.Should().NotContain("Viewport!.ColMetrics");
        resizeMove.Should().NotContain("Viewport!.RowMetrics");
        resizeMove.Should().NotContain("FirstOrDefault");
        resizeStart.Should().Contain("FindColMetric(Viewport!.ColMetrics, index)");
        resizeStart.Should().Contain("FindRowMetric(Viewport!.RowMetrics, index)");
        resizeStart.Should().NotContain(".First(");
    }

    [Fact]
    public void PivotChartFieldButtonHitTest_ScansChartsBackToFrontWithoutLinqIterators()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.HitTesting.cs"));
        var hitTest = source[
            source.IndexOf("public static (ChartModel Chart, string FieldButton)? HitTestPivotChartFieldButton", StringComparison.Ordinal)..];

        hitTest.Should().Contain("for (var i = charts.Count - 1; i >= 0; i--)");
        hitTest.Should().NotContain(".Where(");
        hitTest.Should().NotContain(".Reverse(");
    }

    [Fact]
    public void ChartRenderer_BuildsChartCellLookupWithoutLinqFiltering()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "ChartRenderer.cs"));
        var buildLookup = source[
            source.IndexOf("private static Dictionary<(uint Row, uint Col), DisplayCell> BuildChartCellLookup", StringComparison.Ordinal)..
            source.IndexOf("private static LineSeries CreateLineSeries", StringComparison.Ordinal)];

        buildLookup.Should().Contain("foreach (var cell in viewport.ChartDataCells)");
        buildLookup.Should().Contain("if (cell.SheetId != sheetId)");
        buildLookup.Should().Contain("cell.RawValue");
        buildLookup.Should().NotContain(".Where(");
        buildLookup.Should().NotContain(".Select(");
    }

    [Fact]
    public void RenderCharts_ReusesCachedChartImagesAcrossRepaints()
    {
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
        var drawingSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var cacheSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.ChartRenderCache.cs"));
        var propertiesSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Properties.cs"));

        gridViewSource.Should().Contain("private readonly Dictionary<ChartRenderCacheKey, ImageSource> _chartRenderCache = new();");
        drawingSource.Should().Contain("GetCachedChartImage(chart, Viewport, WorkbookTheme)");
        drawingSource.Should().NotContain("ChartRenderer.Render(chart, Viewport, WorkbookTheme)");
        cacheSource.Should().Contain("_chartRenderCache.TryGetValue");
        cacheSource.Should().Contain("VisualTreeHelper.GetDpi(this)");
        cacheSource.Should().Contain("ZoomFactor > 0 ? ZoomFactor : 1.0");
        cacheSource.Should().Contain("private readonly double _renderScale;");
        cacheSource.Should().Contain("chart.Width * renderScale");
        cacheSource.Should().Contain("chart.Height * renderScale");
        cacheSource.Should().Contain("ChartRenderer.Render(chart, viewport, theme, renderScale)");
        propertiesSource.Should().Contain("OnChartRenderCacheInputChanged");
        propertiesSource.Should().Contain("grid.ClearChartRenderCache();");
    }

    [Fact]
    public void RenderManualPageBreaks_ScansVisibleMetricsOnce()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Overlays.cs"));
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
        var propertiesSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Properties.cs"));
        var renderManualPageBreaks = source[
            source.IndexOf("private void RenderManualPageBreaks", StringComparison.Ordinal)..
            source.IndexOf("public enum FormulaTraceArrowLayoutKind", StringComparison.Ordinal)];

        renderManualPageBreaks.Should().Contain("GetPageBreakLookup(rowPageBreaks, ref _rowPageBreakLookupCache)");
        renderManualPageBreaks.Should().Contain("GetPageBreakLookup(columnPageBreaks, ref _columnPageBreakLookupCache)");
        renderManualPageBreaks.Should().Contain("pageBreaks is IReadOnlySet<uint> set");
        renderManualPageBreaks.Should().Contain("CalculatePageBreakFingerprint(pageBreaks)");
        renderManualPageBreaks.Should().Contain("cache.Fingerprint == fingerprint");
        renderManualPageBreaks.Should().Contain("foreach (var metric in Viewport.RowMetrics)");
        renderManualPageBreaks.Should().Contain("foreach (var metric in Viewport.ColMetrics)");
        renderManualPageBreaks.Should().NotContain("FirstOrDefault");
        gridViewSource.Should().Contain("private PageBreakLookupCache? _rowPageBreakLookupCache;");
        propertiesSource.Should().Contain("OnRowPageBreaksChanged");
        propertiesSource.Should().Contain("OnColumnPageBreaksChanged");
    }

    [Fact]
    public void FormulaTraceLayoutPlanner_AvoidsPerArrowLinqMetricScans()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "FormulaTraceLayoutPlanner.cs"));
        var calculateLayouts = source[
            source.IndexOf("public static IReadOnlyList<FormulaTraceArrowLayout> CalculateLayouts", StringComparison.Ordinal)..
            source.IndexOf("public static CellAddress? HitTestMarker", StringComparison.Ordinal)];
        var tryGetCellRect = source[
            source.IndexOf("private static bool TryGetCellRect", StringComparison.Ordinal)..
            source.IndexOf("private static bool TryGetMarkerHit", StringComparison.Ordinal)];

        calculateLayouts.Should().Contain("new List<FormulaTraceArrowLayout>(arrows.Count)");
        tryGetCellRect.Should().Contain("FindRowMetric(viewport.RowMetrics, address.Row)");
        tryGetCellRect.Should().Contain("FindColMetric(viewport.ColMetrics, address.Col)");
        tryGetCellRect.Should().NotContain("FirstOrDefault");
        source.Should().Contain("private static RowMetric? FindRowMetric");
        source.Should().Contain("private static ColMetric? FindColMetric");
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
            source.IndexOf("private static HashSet<(uint Row, uint Col)> BuildOccupiedCells", StringComparison.Ordinal)..
            source.IndexOf("private static double SumEmptyOverflowColumnWidths", StringComparison.Ordinal)];

        calculateLayouts.Should().Contain("BuildRowLookup(topRows)");
        calculateLayouts.Should().Contain("BuildRowLookup(bottomLeftRows)");
        calculateLayouts.Should().Contain("BuildColumnLookup(leftColumns)");
        calculateLayouts.Should().Contain("BuildColumnLookup(topRightColumns)");
        calculateLayouts.Should().Contain("ResolveSplitPaneRegion(isTopPane, isLeftPane)");
        calculateLayouts.Should().Contain("new List<SplitPaneCellLayout>(cells.Count)");
        calculateLayouts.Should().Contain("HashSet<(uint Row, uint Col)>? occupied = null;");
        calculateLayouts.Should().Contain("occupied ??= BuildOccupiedCells(cells, editingCell)");
        calculateLayouts.Should().Contain("foreach (var cell in cells)");
        calculateLayouts.Should().NotContain("occupied.Add((cell.Row, cell.Col))");
        buildOccupiedCells.Should().Contain("occupied.Add((cell.Row, cell.Col))");
        calculateLayouts.Should().NotContain(".ToDictionary(");
        calculateLayouts.Should().NotContain(".Where(");
        calculateLayouts.Should().NotContain(".Select(");
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_NumericCellsSkipOverflowOccupancyAllocation()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "SplitPaneCellLayoutPlanner.cs"));

        source.Should().Contain("HashSet<(uint Row, uint Col)>? occupied = null;");
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

    [Fact]
    public void RenderSplitPaneCells_UsesWrappedTextLayoutCacheForDefaultWrappedCells()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("var wrapText = style?.WrapText == true;");
        renderSplitPaneCells.Should().Contain("var useDefaultTextLayout = CanUseDefaultFormattedText(style, wrapText);");
        renderSplitPaneCells.Should().Contain("var useDefaultWrappedTextLayout = !useDefaultTextLayout && wrapText && CanUseDefaultWrappedFormattedText(style);");
        renderSplitPaneCells.Should().Contain("GetDefaultWrappedFormattedText(cell.DisplayText, fontSize, wrapMaxTextWidth, wrapTextAlignment, pixelsPerDip)");
        renderSplitPaneCells.Should().Contain("text.MaxTextWidth = wrapMaxTextWidth;");
        renderSplitPaneCells.Should().Contain("text.TextAlignment = wrapTextAlignment;");
        renderSplitPaneCells.Should().Contain("if (style?.ShrinkToFit == true && !wrapText)");
        renderSplitPaneCells.Should().NotContain("CanUseDefaultFormattedText(style, wrapText: false)");
        renderSplitPaneCells.Should().NotContain("style.WrapText != true");
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
