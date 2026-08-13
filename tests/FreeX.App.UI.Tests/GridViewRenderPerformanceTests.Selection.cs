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
    public void RenderSelectionAndHeaders_FastPathSingleCellSelectionsWithMetricLookups()
    {
        var headerSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.Headers.cs");
        var renderSelectedHeaders = headerSource[
            headerSource.IndexOf("private void RenderSelectedHeaders(", StringComparison.Ordinal)..
            headerSource.IndexOf("private void DrawColumnHeader(", StringComparison.Ordinal)];
        renderSelectedHeaders.Should().Contain("TryRenderSingleCellSelectedHeaders(");
        renderSelectedHeaders.Should().Contain("TryGetSingleCellSelectedHeaderRange(");
        renderSelectedHeaders.Should().Contain("GetRenderMetricLookups(viewport)");
        renderSelectedHeaders.Should().Contain("lookups.Columns.TryGetValue(range.Start.Col");
        renderSelectedHeaders.Should().Contain("lookups.Rows.TryGetValue(range.Start.Row");
        renderSelectedHeaders.IndexOf("TryRenderSingleCellSelectedHeaders", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSelectedHeaders.IndexOf("BuildColumnHeaderSelectionIntervals", StringComparison.Ordinal));

        var selectionSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.Selection.cs");
        var renderSelectionRange = selectionSource[
            selectionSource.IndexOf("private void RenderSelectionRange(", StringComparison.Ordinal)..
            selectionSource.IndexOf("private static void DrawSelectionHandle", StringComparison.Ordinal)];
        renderSelectionRange.Should().Contain("CalculateSelectionRangeLayout(Viewport, range, rowHeaderWidth, columnHeaderHeight)");

        var singleCellLayout = selectionSource[
            selectionSource.IndexOf("private SelectionMarqueeLayoutPlanner.SelectionMarqueeLayout? CalculateSelectionRangeLayout", StringComparison.Ordinal)..
            selectionSource.IndexOf("private static bool IsSingleCellRange", StringComparison.Ordinal)];
        singleCellLayout.Should().Contain("IsSingleCellRange(range)");
        singleCellLayout.Should().Contain("CalculateVisibleSingleCellSelectionLayout(viewport, range, rowHeaderWidth, columnHeaderHeight)");
        singleCellLayout.Should().Contain("ViewportGeometryPlanner.TryGetCellBounds(");
        singleCellLayout.IndexOf("IsSingleCellRange(range)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(singleCellLayout.IndexOf("CalculateVisibleSelectionLayout", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderAutofillPreview_ReusesFrozenStaticDashedPen()
    {
        var gridViewSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.cs");
        var overlaysSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Overlays.cs");
        var renderAutofill = overlaysSource[
            overlaysSource.IndexOf("private void RenderAutofillPreview", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private void RenderMarchingAnts", StringComparison.Ordinal)];

        gridViewSource.Should().Contain("private static readonly Pen AutofillPreviewPen = MakeAutofillPreviewPen();");
        gridViewSource.Should().Contain("private static Pen MakeAutofillPreviewPen()");
        gridViewSource.Should().Contain("DashStyle = new DashStyle([4.0, 4.0], 0)");
        gridViewSource.Should().Contain("pen.Freeze();");
        renderAutofill.Should().Contain("CalculateVisibleSelectionLayout(");
        renderAutofill.Should().Contain("previewLayout.HasTopEdge");
        renderAutofill.Should().Contain("previewLayout.HasBottomEdge");
        renderAutofill.Should().Contain("previewLayout.HasLeftEdge");
        renderAutofill.Should().Contain("previewLayout.HasRightEdge");
        renderAutofill.Should().Contain("dc.DrawLine(AutofillPreviewPen");
        renderAutofill.Should().NotContain("GetRangePixels(vp");
        renderAutofill.Should().NotContain("new Pen");
        renderAutofill.Should().NotContain("new SolidColorBrush");
        renderAutofill.Should().NotContain("new DashStyle");
    }

    [Fact]
    public void RenderMarchingAnts_ReusesCachedPhasePens()
    {
        var gridViewSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.cs");
        var overlaysSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Overlays.cs");
        var renderMarchingAnts = overlaysSource[
            overlaysSource.IndexOf("private void RenderMarchingAnts", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private void RenderFormulaTraceArrows", StringComparison.Ordinal)];

        gridViewSource.Should().Contain("private const int MarchingAntsPhaseCount = 16;");
        gridViewSource.Should().Contain("private static readonly Pen[] MarchingAntsBlackPens = CreateMarchingAntsPens(Brushes.Black, 2.5);");
        gridViewSource.Should().Contain("private static readonly Pen[] MarchingAntsCopyOverlayPens = CreateMarchingAntsPens(Brushes.White, 1.5);");
        gridViewSource.Should().Contain("private static readonly Pen[] MarchingAntsCutOverlayPens = MarchingAntsCopyOverlayPens;");
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
        var gridViewSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.cs");
        var stateSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.State.cs");
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
    public void SelectionOnlyInvalidations_ReusePreSelectionLayerCache()
    {
        var properties = AppUiSourceTestSupport.ReadAppUiSources("GridView.Properties.cs");
        var dispatch = AppUiSourceTestSupport.ReadAppUiSources("GridView.RenderDispatch.cs");
        var cache = AppUiSourceTestSupport.ReadAppUiSources("GridView.RenderSurfaceCache.cs");
        var onRender = dispatch[
            dispatch.IndexOf("protected override void OnRender", StringComparison.Ordinal)..
            dispatch.IndexOf("private void RenderPreSelectionLayers", StringComparison.Ordinal)];

        properties.Should().Contain("OnSelectionVisualPropertyChanged");
        properties.Should().Contain("grid.MarkSelectionVisualOnlyChange();");
        dispatch.Should().Contain("RenderPreSelectionLayersWithCache(dc, skipHeavyLayers, isLiveResizing);");
        cache.Should().Contain("RenderPreSelectionLayers(dc, skipHeavyLayers, isLiveResizing);");
        cache.Should().Contain("ShouldBuildPreSelectionLayerCache(key)");
        cache.Should().Contain("_selectionVisualOnlyChangePending ||");
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
        onRender.IndexOf("RenderPostSelectionLayers", StringComparison.Ordinal)
            .Should().BeLessThan(onRender.IndexOf("_selectionVisualOnlyChangePending = false;", StringComparison.Ordinal));
    }

    [Fact]
    public void StableRenderInvalidations_WarmAndReusePreSelectionLayerCache()
    {
        var cache = AppUiSourceTestSupport.ReadAppUiSources("GridView.RenderSurfaceCache.cs");
        var renderWithCache = cache[
            cache.IndexOf("private void RenderPreSelectionLayersWithCache", StringComparison.Ordinal)..
            cache.IndexOf("private static bool CanCachePreSelectionLayers", StringComparison.Ordinal)];

        renderWithCache.Should().Contain("_preSelectionLayerCache is { } cached");
        renderWithCache.Should().Contain("_preSelectionLayerCacheKey == key");
        renderWithCache.Should().Contain("dc.DrawDrawing(cached);");
        renderWithCache.Should().Contain("ShouldBuildPreSelectionLayerCache(key)");
        renderWithCache.Should().Contain("RememberPreSelectionLayerRenderKey(key);");
        cache.Should().Contain("IReadOnlyList<DisplayCell> Cells");
        cache.Should().Contain("IReadOnlyList<RowMetric> RowMetrics");
        cache.Should().Contain("IReadOnlyList<ColMetric> ColMetrics");
        cache.Should().Contain("SplitPaneState? SplitPanes");
        cache.Should().Contain("viewport.Cells");
        cache.Should().Contain("viewport.RowMetrics");
        cache.Should().Contain("viewport.ColMetrics");
        cache.Should().NotContain("ViewportModel Viewport");
        cache.Should().Contain("bool SkipHeavyLayers");
        cache.Should().Contain("private static bool CanCachePreSelectionLayers(bool skipHeavyLayers, bool isLiveResizing) =>");
        cache.Should().Contain("!isLiveResizing;");
        cache.Should().NotContain("!skipHeavyLayers &&");
        cache.Should().Contain("_hasLastPreSelectionLayerRenderKey && _lastPreSelectionLayerRenderKey == key");
        cache.Should().Contain("_selectionVisualOnlyChangePending ||");
        cache.Should().Contain("_hasLastPreSelectionLayerRenderKey = false;");
    }

    [Fact]
    public void PreSelectionLayerCacheKey_IsStableAcrossEquivalentViewportWrappers()
    {
        RunOnStaThread(() =>
        {
            var method = typeof(GridView).GetMethod(
                "CreatePreSelectionLayerCacheKey",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var rows = new[] { new RowMetric(1, 20, 0) };
            var columns = new[] { new ColMetric(1, 64, 0) };
            var cells = new[] { Cell(1, 1, "value") };
            var grid = new GridView
            {
                Width = 320,
                Height = 240,
                Viewport = new ViewportModel(cells, rows, columns)
            };
            grid.Measure(new Size(320, 240));
            grid.Arrange(new Rect(0, 0, 320, 240));

            var firstKey = method!.Invoke(grid, [false]);
            grid.Viewport = new ViewportModel(cells, rows, columns);
            var wrappedKey = method.Invoke(grid, [false]);
            grid.Viewport = new ViewportModel(new[] { Cell(1, 1, "value") }, rows, columns);
            var changedCellsKey = method.Invoke(grid, [false]);

            wrappedKey.Should().Be(firstKey);
            changedCellsKey.Should().NotBe(firstKey);
        });
    }

    [Fact]
    public void SelectionOnlyInvalidations_ReuseRenderClipGeometry()
    {
        var dispatch = AppUiSourceTestSupport.ReadAppUiSources("GridView.RenderDispatch.cs");
        var onRender = dispatch[
            dispatch.IndexOf("protected override void OnRender", StringComparison.Ordinal)..
            dispatch.IndexOf("private RectangleGeometry GetRenderClipGeometry", StringComparison.Ordinal)];
        var getRenderClipGeometry = dispatch[
            dispatch.IndexOf("private RectangleGeometry GetRenderClipGeometry", StringComparison.Ordinal)..
            dispatch.IndexOf("private void RenderPreSelectionLayers", StringComparison.Ordinal)];

        dispatch.Should().Contain("private RectangleGeometry? _renderClipGeometryCache;");
        dispatch.Should().Contain("private Rect _renderClipGeometryCacheRect;");
        onRender.Should().Contain("dc.PushClip(GetRenderClipGeometry(new Rect(0, 0, GetLogicalViewportWidth(), GetLogicalViewportHeight())));");
        onRender.Should().NotContain("new RectangleGeometry");
        getRenderClipGeometry.Should().Contain("_renderClipGeometryCache is { } cached && _renderClipGeometryCacheRect == clipRect");
        getRenderClipGeometry.Should().Contain("var geometry = new RectangleGeometry(clipRect);");
        getRenderClipGeometry.Should().Contain("geometry.Freeze();");
    }

    [Fact]
    public void SelectionOnlyInvalidations_SkipEmptyPostSelectionLayers()
    {
        var dispatch = AppUiSourceTestSupport.ReadAppUiSources("GridView.RenderDispatch.cs");
        var splitPanes = AppUiSourceTestSupport.ReadAppUiSources("GridView.SplitPanes.cs");
        var drawingObjects = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");
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
        hasDrawingObjectLayerWork.Should().Contain("GridDrawingObjectPlanner.PlanLayerRenderMode(ObjectDisplayMode) == DrawingObjectLayerRenderMode.Hidden");
        hasDrawingObjectLayerWork.Should().Contain("Charts is { Count: > 0 }");
        hasDrawingObjectLayerWork.Should().Contain("TextBoxes is { Count: > 0 }");
        renderSplitDivider.Should().Contain("if (Viewport?.SplitPanes is null) return;");
        renderNativeControls.Should().Contain("NativeSlicers is not { Count: > 0 } && NativeTimelines is not { Count: > 0 }");
    }

    [Fact]
    public void ResizeDragInput_ReusesMetricScanHelpersWithoutLinqIterators()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
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
}
