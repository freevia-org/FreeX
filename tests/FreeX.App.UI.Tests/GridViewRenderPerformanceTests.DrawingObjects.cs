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
    public void DrawingObjectRenderAndHitTest_ReusesMetricLookupsForAnchoredObjects()
    {
        var drawingObjects = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");
        var pictures = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.Pictures.cs");
        var objectDrag = AppUiSourceTestSupport.ReadAppUiSources("GridView.ObjectDrag.cs");
        var planner = AppUiSourceTestSupport.ReadAppUiSources("GridDrawingObjectPlanner.cs");
        var sharedPlanner = WorkspaceFileLocator.ReadAllTextWithFailureMessage(
            "Unable to locate workspace file",
            "src",
            "FreeX.App.Presentation",
            "DrawingUI",
            "DrawingObjectViewportPlanner.cs");

        drawingObjects.Should().Contain("var metricLookups = GetRenderMetricLookups(Viewport);");
        pictures.Should().Contain("var metricLookups = GetRenderMetricLookups(Viewport);");
        objectDrag.Should().Contain("var metricLookups = GetRenderMetricLookups(Viewport);");
        drawingObjects.Should().Contain("metricLookups,");
        pictures.Should().Contain("metricLookups,");
        objectDrag.Should().Contain("metricLookups,");
        planner.Should().Contain("IReadOnlyDictionary<uint, RowMetric> rows");
        planner.Should().Contain("DrawingObjectViewportPlanner.TryCreateAnchoredObjectRect(");
        sharedPlanner.Should().Contain("rows.TryGetValue(anchor.Row");
        sharedPlanner.Should().Contain("columns.TryGetValue(anchor.Col");
    }

    [Fact]
    public void DrawingObjectRenderableBounds_StopAtFirstMetricPastViewport()
    {
        var drawingObjects = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");
        var planner = WorkspaceFileLocator.ReadAllTextWithFailureMessage(
            "Unable to locate workspace file",
            "src",
            "FreeX.App.Presentation",
            "DrawingUI",
            "DrawingObjectViewportPlanner.cs");
        var rowMethod = planner[
            planner.IndexOf("private static uint FindLastRenderableRow", StringComparison.Ordinal)..
            planner.IndexOf("private static uint FindLastRenderableColumn", StringComparison.Ordinal)];
        var columnMethod = planner[
            planner.IndexOf("private static uint FindLastRenderableColumn", StringComparison.Ordinal)..
            planner.IndexOf("private static bool HasInvalidAnchorPoint", StringComparison.Ordinal)];

        drawingObjects.Should().Contain("DrawingObjectViewportPlanner.GetRenderableAnchorBounds(");
        rowMethod.Should().Contain("if (columnHeaderHeight + row.TopOffset >= visibleBottom)");
        rowMethod.Should().Contain("break;");
        rowMethod.Should().NotContain("columnHeaderHeight + row.TopOffset < visibleBottom && row.Row > lastRow");
        columnMethod.Should().Contain("if (rowHeaderWidth + column.LeftOffset >= visibleRight)");
        columnMethod.Should().Contain("break;");
        columnMethod.Should().NotContain("rowHeaderWidth + column.LeftOffset < visibleRight && column.Col > lastColumn");
    }

    [Fact]
    public void RenderTextBoxes_ReusesNamedClipRectForText()
    {
        var drawingObjects = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");
        var renderTextBox = drawingObjects[
            drawingObjects.IndexOf("private void RenderTextBox(", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void RenderDrawingShapes", StringComparison.Ordinal)];

        renderTextBox.Should().Contain("var textWidth = Math.Max(1, rect.Width - 8);");
        renderTextBox.Should().Contain("var textHeight = Math.Max(1, rect.Height - 8);");
        renderTextBox.Should().Contain("var textClipRect = new Rect(rect.Left + 4, rect.Top + 4, textWidth, textHeight);");
        renderTextBox.Should().Contain("dc.PushClip(GetDrawingObjectClipGeometry(textClipRect));");
        renderTextBox.Should().NotContain("GetDrawingObjectClipGeometry(new Rect");
        renderTextBox.Should().NotContain("new RectangleGeometry");
    }

    [Fact]
    public void PivotChartFieldButtonHitTest_ScansChartsBackToFrontWithoutLinqIterators()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.HitTesting.cs");
        var hitTest = source[
            source.IndexOf("public static (ChartModel Chart, string FieldButton)? HitTestPivotChartFieldButton", StringComparison.Ordinal)..];

        hitTest.Should().Contain("for (var i = charts.Count - 1; i >= 0; i--)");
        hitTest.Should().NotContain(".Where(");
        hitTest.Should().NotContain(".Reverse(");
    }

    [Fact]
    public void ChartRenderer_DelegatesChartCellLookupPolicyToPresentation()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("ChartRenderer.cs");
        var buildLookup = source[
            source.IndexOf("private static Dictionary<(uint Row, uint Col), DisplayCell> BuildChartCellLookup", StringComparison.Ordinal)..
            source.IndexOf("private static LineSeries CreateLineSeries", StringComparison.Ordinal)];
        var buildPlotModel = source[
            source.IndexOf("private static PlotModel? BuildPlotModel", StringComparison.Ordinal)..
            source.IndexOf("private static Dictionary<(uint Row, uint Col), DisplayCell> BuildChartCellLookup", StringComparison.Ordinal)];

        buildPlotModel.Should().Contain("var dataPointCapacity = GetDataPointCapacity(dataStartRow, endRow);");
        buildPlotModel.Should().Contain("new List<string>(chart.FirstColIsCategories ? dataPointCapacity : 0)");
        buildPlotModel.Should().Contain("new List<PieDataLabelPoint>(dataPointCapacity)");
        buildPlotModel.Should().Contain("ChartViewportCellAccessorBuilder.Resolve(");
        buildPlotModel.Should().Contain("ChartViewportCellAccessorBuilder.BuildValueAccessor(");
        buildPlotModel.Should().Contain("ChartLayoutRequestBuilder.TryResolveData(");
        buildLookup.Should().Contain("foreach (var cell in resolved.Values)");
        buildLookup.Should().Contain("cell.RawValue");
        buildLookup.Should().Contain("new Dictionary<(uint Row, uint Col), DisplayCell>(resolved.Count)");
        buildLookup.Should().NotContain("viewport.ChartDataCells");
        buildLookup.Should().NotContain("IsInChartDataRange");
        buildLookup.Should().NotContain(".Where(");
        buildLookup.Should().NotContain(".Select(");
    }

    [Fact]
    public void RenderCharts_ReusesCachedChartImagesAcrossRepaints()
    {
        var gridViewSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.cs");
        var drawingSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");
        var cacheSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.ChartRenderCache.cs");
        var propertiesSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Properties.cs");
        var renderCharts = drawingSource[
            drawingSource.IndexOf("private void RenderCharts", StringComparison.Ordinal)..
            drawingSource.IndexOf("private void RenderTextBoxes", StringComparison.Ordinal)];
        var getCachedChartImage = cacheSource[
            cacheSource.IndexOf("private ImageSource? GetCachedChartImage", StringComparison.Ordinal)..
            cacheSource.IndexOf("private void ClearChartRenderCache", StringComparison.Ordinal)];

        gridViewSource.Should().Contain("private readonly Dictionary<ChartRenderCacheKey, ImageSource> _chartRenderCache = new();");
        drawingSource.Should().Contain("GetCachedChartImage(chart, Viewport, WorkbookTheme, renderScale)");
        drawingSource.Should().NotContain("ChartRenderer.Render(chart, Viewport, WorkbookTheme)");
        cacheSource.Should().Contain("_chartRenderCache.TryGetValue");
        renderCharts.Should().Contain("var dpi = VisualTreeHelper.GetDpi(this);");
        renderCharts.Should().Contain("var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;");
        renderCharts.Should().Contain("var renderScale = Math.Clamp(Math.Max(dpi.DpiScaleX, dpi.DpiScaleY) * zoom, 0.25, 4.0);");
        getCachedChartImage.Should().Contain("double renderScale");
        getCachedChartImage.Should().NotContain("VisualTreeHelper.GetDpi(this)");
        getCachedChartImage.Should().NotContain("ZoomFactor > 0 ? ZoomFactor : 1.0");
        cacheSource.Should().Contain("private readonly double _renderScale;");
        cacheSource.Should().Contain("chart.Width * renderScale");
        cacheSource.Should().Contain("chart.Height * renderScale");
        cacheSource.Should().Contain("ChartRenderer.Render(chart, viewport, theme, renderScale)");
        propertiesSource.Should().Contain("OnChartRenderCacheInputChanged");
        propertiesSource.Should().Contain("grid.ClearChartRenderCache();");
    }

    [Fact]
    public void RenderNativeControls_ReusesPixelsPerDipAcrossClippedTextCalls()
    {
        var drawingSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");
        var renderNativeControls = drawingSource[
            drawingSource.IndexOf("private void RenderNativeSlicerTimelineControls", StringComparison.Ordinal)..
            drawingSource.IndexOf("public static bool TryCreateDrawingAnchorRect", StringComparison.Ordinal)];
        var drawNativeSlicer = drawingSource[
            drawingSource.IndexOf("private void DrawNativeSlicerControl", StringComparison.Ordinal)..
            drawingSource.IndexOf("private void DrawNativeTimelineControl", StringComparison.Ordinal)];
        var drawNativeTimeline = drawingSource[
            drawingSource.IndexOf("private void DrawNativeTimelineControl", StringComparison.Ordinal)..
            drawingSource.IndexOf("private void DrawNativeControlFrame", StringComparison.Ordinal)];
        var drawNativeFrame = drawingSource[
            drawingSource.IndexOf("private void DrawNativeControlFrame", StringComparison.Ordinal)..
            drawingSource.IndexOf("private void DrawClippedText", StringComparison.Ordinal)];
        var drawClippedText = drawingSource[
            drawingSource.IndexOf("private void DrawClippedText", StringComparison.Ordinal)..
            drawingSource.IndexOf("private static string GetNativeControlCaption", StringComparison.Ordinal)];

        renderNativeControls.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        renderNativeControls.Should().Contain("DrawNativeSlicerControl(dc, controlRect, slicer, pixelsPerDip);");
        renderNativeControls.Should().Contain("DrawNativeTimelineControl(dc, controlRect, timeline, pixelsPerDip);");
        // The slicer frame is drawn via the themed DrawNativeControlFrame overload (style colors +
        // showCaption), still threading the cached pixelsPerDip into its clipped-text calls.
        drawNativeSlicer.Should().Contain("GetNativeControlCaption(slicer.Caption, slicer.Name, slicer.DrawingShapeName),");
        drawNativeSlicer.Should().Contain("boldHeader: isAccentStyle);");
        drawNativeSlicer.Should().Contain("DrawClippedText(dc, caption, tileRect, itemTextBrush, 10, verticalPadding: 1, pixelsPerDip);");
        drawNativeTimeline.Should().Contain("TimelineLayoutBuilder.Build(");
        drawNativeTimeline.Should().Contain("SlicerTimelineGranularity.Resolve(timeline)");
        drawingSource.Should().NotContain("private static TimelineGranularity ResolveTimelineGranularity");
        drawNativeTimeline.Should().Contain("layout.Caption,");
        drawNativeTimeline.Should().Contain("DrawClippedText(dc, layout.DateLabel, ToRect(layout.DateLabelRect)");
        drawNativeTimeline.Should().Contain("ToRect(layout.SelectionRect)");
        drawNativeTimeline.Should().Contain("DrawTimelineHandle(dc, layout.StartHandle);");
        drawNativeTimeline.Should().Contain("DrawTimelineHandle(dc, layout.EndHandle);");
        drawNativeTimeline.Should().Contain("pixelsPerDip);");
        drawNativeFrame.Should().Contain("DrawClippedText(dc, caption, new Rect");
        drawNativeFrame.Should().Contain("pixelsPerDip, isBold: boldHeader);");
        drawClippedText.Should().Contain("double pixelsPerDip,");
        drawClippedText.Should().Contain("GetDrawingObjectText(");
        drawClippedText.Should().NotContain("VisualTreeHelper.GetDpi(this)");
    }

    [Fact]
    public void RenderObjectPlaceholders_ReusesPixelsPerDipAcrossPlaceholderLabels()
    {
        var drawingSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");
        var renderObjectPlaceholders = drawingSource[
            drawingSource.IndexOf("private void RenderObjectPlaceholders", StringComparison.Ordinal)..
            drawingSource.IndexOf("public static string CreateObjectPlaceholderLabel", StringComparison.Ordinal)];
        var drawObjectPlaceholder = drawingSource[
            drawingSource.IndexOf("private void DrawObjectPlaceholder", StringComparison.Ordinal)..
            drawingSource.IndexOf("private static void DrawPlaceholderDiagonals", StringComparison.Ordinal)];

        renderObjectPlaceholders.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        renderObjectPlaceholders.Should().Contain("DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderMetadata(\"Chart\", chart.Name, index).Label, pixelsPerDip);");
        renderObjectPlaceholders.Should().Contain("DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderMetadata(\"Shape\", shape.Name, index).Label, pixelsPerDip);");
        renderObjectPlaceholders.Should().Contain("DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderMetadata(\"Picture\", picture.Name, index).Label, pixelsPerDip);");
        renderObjectPlaceholders.Should().Contain("DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderMetadata(\"Text Box\", textBox.Name, index).Label, pixelsPerDip);");
        renderObjectPlaceholders.Should().Contain("DrawObjectPlaceholder(dc, controlRect, CreateObjectPlaceholderMetadata(\"Slicer\"");
        renderObjectPlaceholders.Should().Contain("DrawObjectPlaceholder(dc, controlRect, CreateObjectPlaceholderMetadata(\"Timeline\"");
        drawObjectPlaceholder.Should().Contain("double pixelsPerDip)");
        drawObjectPlaceholder.Should().Contain("GetDrawingObjectText(");
        drawObjectPlaceholder.Should().Contain("var textClipRect = new Rect(rect.Left + 4, rect.Top + 4, textWidth, textHeight);");
        drawObjectPlaceholder.Should().Contain("dc.PushClip(GetDrawingObjectClipGeometry(textClipRect));");
        drawObjectPlaceholder.Should().NotContain("VisualTreeHelper.GetDpi(this)");
        drawObjectPlaceholder.Should().NotContain("new RectangleGeometry");
        drawObjectPlaceholder.Should().NotContain("GetDrawingObjectClipGeometry(new Rect");
    }

    [Fact]
    public void DrawingObjectLayers_ReuseFrozenDrawingLayerAcrossStableRepaints()
    {
        var dispatch = AppUiSourceTestSupport.ReadAppUiSources("GridView.RenderDispatch.cs");
        var cache = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjectLayerCache.cs");
        var properties = AppUiSourceTestSupport.ReadAppUiSources("GridView.Properties.cs");
        var renderPostSelectionLayers = dispatch[
            dispatch.IndexOf("private void RenderPostSelectionLayers", StringComparison.Ordinal)..
            dispatch.IndexOf("private bool HasPostSelectionLayerWork", StringComparison.Ordinal)];

        renderPostSelectionLayers.Should().Contain("RenderDrawingObjectLayersWithCache(dc);");
        renderPostSelectionLayers.Should().NotContain("RenderDrawingShapes(dc);");
        renderPostSelectionLayers.Should().NotContain("RenderTextBoxes(dc);");
        cache.Should().Contain("private DrawingGroup? _drawingObjectLayerCache;");
        cache.Should().Contain("private readonly record struct DrawingObjectLayerCacheKey");
        cache.Should().Contain("dc.DrawDrawing(cached);");
        cache.Should().Contain("ShouldBuildDrawingObjectLayerCache(key)");
        cache.Should().Contain("RenderDrawingObjectLayers(dc);");
        cache.Should().Contain("RememberDrawingObjectLayerRenderKey(key);");
        cache.Should().Contain("BuildDrawingObjectLayerCache()");
        cache.Should().Contain("RenderDrawingObjectLayers(groupContext);");
        cache.Should().Contain("group.Freeze();");
        cache.Should().Contain("_hasLastDrawingObjectLayerRenderKey && _lastDrawingObjectLayerRenderKey == key");
        cache.Should().NotContain("CellAddress? PictureSelectionAnchor");
        cache.Should().NotContain("GetPictureSelectionAnchorForLayerCache()");
        cache.Should().NotContain("GridRange? SelectedRange");
        cache.Should().Contain("IReadOnlyList<DrawingShapeModel>? DrawingShapes");
        cache.Should().Contain("IReadOnlyList<TextBoxModel>? TextBoxes");
        cache.Should().Contain("IReadOnlyList<PictureModel>? Pictures");
        cache.Should().Contain("private void ClearDrawingObjectLayerCache()");
        properties.Should().Contain("OnDrawingObjectLayerInputChanged");
        properties.Should().Contain("grid.ClearDrawingObjectLayerCache();");
        properties.Should().Contain("new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingObjectLayerInputChanged)");
    }
}
