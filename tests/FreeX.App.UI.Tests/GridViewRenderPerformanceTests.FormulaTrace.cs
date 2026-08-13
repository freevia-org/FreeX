using System;
using FluentAssertions;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewRenderPerformanceTests
{
    [Fact]
    public void RenderManualPageBreaks_ScansVisibleMetricsOnce()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Overlays.cs");
        var gridViewSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.cs");
        var propertiesSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Properties.cs");
        var renderManualPageBreaks = source[
            source.IndexOf("private void RenderManualPageBreaks", StringComparison.Ordinal)..
            source.IndexOf("public sealed record PageMarginRulerHandles", StringComparison.Ordinal)];

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
    public void FormulaTraceLayoutPlanner_AvoidsPerArrowLinqMetricScansAndLookupAllocations()
    {
        var source = WorkspaceFileLocator.ReadAllTextWithFailureMessage(
            "Unable to locate shared formula trace planner",
            "src",
            "FreeX.App.Presentation",
            "FormulaAuditing",
            "FormulaTraceOverlayPlanner.cs");
        var adapterSource = AppUiSourceTestSupport.ReadAppUiSources("FormulaTraceLayoutPlanner.cs");
        var overlaysSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Overlays.cs");
        var calculateLayouts = source[
            source.IndexOf("public static IReadOnlyList<FormulaTraceArrowLayout> CalculateLayouts", StringComparison.Ordinal)..
            source.IndexOf("public static void VisitLayouts<TConsumer>", StringComparison.Ordinal)];
        var visitLayouts = source[
            source.IndexOf("public static void VisitLayouts<TConsumer>", StringComparison.Ordinal)..
            source.IndexOf("public static CellAddress? HitTestMarker", StringComparison.Ordinal)];
        var metricLookup = source[
            source.IndexOf("private readonly struct FormulaTraceMetricLookup", StringComparison.Ordinal)..
            source.IndexOf("private static double[] BuildSequentialRowTops", StringComparison.Ordinal)];
        var renderFormulaTrace = overlaysSource[
            overlaysSource.IndexOf("private void RenderFormulaTraceArrows", StringComparison.Ordinal)..
            overlaysSource.IndexOf("public static IReadOnlyList<FormulaTraceArrowLayout> CalculateFormulaTraceArrowLayouts", StringComparison.Ordinal)];
        var createFormulaTraceLayer = overlaysSource[
            overlaysSource.IndexOf("private Drawing CreateFormulaTraceArrowLayerDrawing", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private void DrawFormulaTraceArrow", StringComparison.Ordinal)];

        source.Should().Contain("public interface IFormulaTraceArrowLayoutConsumer");
        source.Should().Contain("public readonly record struct FormulaTraceArrowLayout");
        overlaysSource.Should().NotContain("public readonly record struct FormulaTraceArrowLayout");
        source.Should().Contain("private FormulaTraceArrowLayout[]? _layouts;");
        source.Should().Contain("_layouts ??= GC.AllocateUninitializedArray<FormulaTraceArrowLayout>(_capacity);");
        source.Should().Contain("_count == _layouts.Length ? _layouts");
        source.Should().NotContain("new List<FormulaTraceArrowLayout>(_capacity)");
        calculateLayouts.Should().Contain("var consumer = new FormulaTraceArrowLayoutCollector(arrows.Count);");
        calculateLayouts.Should().Contain("VisitLayouts(viewport, arrows, sheetId, projection, profile, ref consumer);");
        visitLayouts.Should().Contain("where TConsumer : struct, IFormulaTraceArrowLayoutConsumer");
        visitLayouts.Should().Contain("var metrics = new FormulaTraceMetricLookup(viewport, projection);");
        visitLayouts.Should().Contain("for (var i = 0; i < arrows.Count; i++)");
        visitLayouts.Should().Contain("var arrow = arrows[i];");
        visitLayouts.Should().Contain("metrics.TryGetCellRect");
        visitLayouts.Should().Contain("consumer.AcceptLayout(");
        visitLayouts.Should().NotContain("foreach (var arrow in arrows)");
        visitLayouts.Should().NotContain("new List<FormulaTraceArrowLayout>");
        visitLayouts.Should().NotContain("new FormulaTraceArrowLayout");
        source.Should().NotContain("Dictionary<");
        source.Should().NotContain("BuildRowMetricLookup");
        source.Should().NotContain("BuildColMetricLookup");
        adapterSource.Should().Contain("FormulaTraceOverlayPlanner.CalculateLayouts");
        adapterSource.Should().Contain("FormulaTraceOverlayPlanner.VisitLayouts");
        adapterSource.Should().NotContain("for (");
        adapterSource.Should().NotContain("while (");
        renderFormulaTrace.Should().Contain("GetFormulaTraceArrowLayerDrawing(viewport, arrows, FormulaTraceSheetId)");
        createFormulaTraceLayer.Should().Contain("FormulaTraceLayoutPlanner.VisitLayouts(viewport, arrows, sheetId, ref consumer);");
        renderFormulaTrace.Should().NotContain("CalculateFormulaTraceArrowLayouts");
        renderFormulaTrace.Should().NotContain("foreach");
        overlaysSource.Should().Contain("private readonly struct FormulaTraceArrowDrawingConsumer");
        metricLookup.Should().Contain("var rowIndex = FindRowMetricIndex(address.Row);");
        metricLookup.Should().Contain("var columnIndex = FindColumnMetricIndex(address.Col);");
        metricLookup.Should().Contain("_firstRow = _hasRows ? _rows[0].Row : 0;");
        metricLookup.Should().Contain("_lastRow = _hasRows ? _rows[^1].Row : 0;");
        metricLookup.Should().Contain("_firstCol = _hasColumns ? _columns[0].Col : 0;");
        metricLookup.Should().Contain("_lastCol = _hasColumns ? _columns[^1].Col : 0;");
        source.Should().Contain("address < first || address > last");
        source.Should().Contain("var directIndex = address - first;");
        metricLookup.Should().NotContain("TryGetValue");
        metricLookup.Should().NotContain("FirstOrDefault");
        source.Should().Contain("private static int FindMetricIndex<TMetric>");
        source.Should().Contain("while (low <= high)");
    }

    [Fact]
    public void DrawFormulaTraceArrow_ReusesCachedFrozenArrowDrawingsAndArrowHeadGeometry()
    {
        var overlaysSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Overlays.cs");
        var propertiesSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.Properties.cs");
        var renderFormulaTraceArrows = overlaysSource[
            overlaysSource.IndexOf("private void RenderFormulaTraceArrows", StringComparison.Ordinal)..
            overlaysSource.IndexOf("public static IReadOnlyList<FormulaTraceArrowLayout> CalculateFormulaTraceArrowLayouts", StringComparison.Ordinal)];
        var getArrowLayerDrawing = overlaysSource[
            overlaysSource.IndexOf("private Drawing GetFormulaTraceArrowLayerDrawing", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private Drawing CreateFormulaTraceArrowLayerDrawing", StringComparison.Ordinal)];
        var createArrowLayerDrawing = overlaysSource[
            overlaysSource.IndexOf("private Drawing CreateFormulaTraceArrowLayerDrawing", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private void DrawFormulaTraceArrow", StringComparison.Ordinal)];
        var drawFormulaTraceArrow = overlaysSource[
            overlaysSource.IndexOf("private void DrawFormulaTraceArrow", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private Drawing GetFormulaTraceArrowDrawing", StringComparison.Ordinal)];
        var getArrowDrawing = overlaysSource[
            overlaysSource.IndexOf("private Drawing GetFormulaTraceArrowDrawing", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private Drawing CreateFormulaTraceArrowDrawing", StringComparison.Ordinal)];
        var createArrowDrawing = overlaysSource[
            overlaysSource.IndexOf("private Drawing CreateFormulaTraceArrowDrawing", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private Geometry GetFormulaTraceArrowHeadGeometry", StringComparison.Ordinal)];
        var getArrowHeadGeometry = overlaysSource[
            overlaysSource.IndexOf("private Geometry GetFormulaTraceArrowHeadGeometry", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private static Geometry CreateFormulaTraceArrowHeadGeometry", StringComparison.Ordinal)];
        var createArrowHeadGeometry = overlaysSource[
            overlaysSource.IndexOf("private static Geometry CreateFormulaTraceArrowHeadGeometry", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private void ClearFormulaTraceArrowHeadGeometryCache", StringComparison.Ordinal)];

        overlaysSource.Should().Contain("private const int FormulaTraceArrowHeadGeometryCacheLimit = 4096;");
        overlaysSource.Should().Contain("private const int FormulaTraceArrowDrawingCacheLimit = 4096;");
        overlaysSource.Should().Contain("private readonly Dictionary<FormulaTraceArrowHeadGeometryKey, Geometry> _formulaTraceArrowHeadGeometryCache = new();");
        overlaysSource.Should().Contain("private readonly Dictionary<FormulaTraceArrowDrawingKey, Drawing> _formulaTraceArrowDrawingCache = new();");
        overlaysSource.Should().Contain("private FormulaTraceArrowLayerCache? _formulaTraceArrowLayerCache;");
        overlaysSource.Should().Contain("private readonly record struct FormulaTraceArrowHeadGeometryKey(Point Start, Point End);");
        overlaysSource.Should().Contain("private readonly record struct FormulaTraceArrowDrawingKey(Point Start, Point End);");
        overlaysSource.Should().Contain("private sealed record FormulaTraceArrowLayerCache");
        renderFormulaTraceArrows.Should().Contain("dc.DrawDrawing(GetFormulaTraceArrowLayerDrawing(viewport, arrows, FormulaTraceSheetId));");
        getArrowLayerDrawing.Should().Contain("_formulaTraceArrowLayerCache is { } cached");
        getArrowLayerDrawing.Should().Contain("ReferenceEquals(cached.Viewport, viewport)");
        getArrowLayerDrawing.Should().Contain("cached.SheetId.Equals(sheetId)");
        getArrowLayerDrawing.Should().Contain("FormulaTraceArrowsEqual(arrows, cached.Arrows)");
        getArrowLayerDrawing.Should().Contain("CopyFormulaTraceArrows(arrows)");
        createArrowLayerDrawing.Should().Contain("new DrawingGroup()");
        createArrowLayerDrawing.Should().Contain("FormulaTraceLayoutPlanner.VisitLayouts(viewport, arrows, sheetId, ref consumer);");
        createArrowLayerDrawing.Should().Contain("drawing.Freeze();");
        drawFormulaTraceArrow.Should().Contain("GetFormulaTraceArrowDrawing(start, end)");
        drawFormulaTraceArrow.Should().NotContain("CreateFormulaTraceArrowHeadGeometry");
        getArrowDrawing.Should().Contain("_formulaTraceArrowDrawingCache.TryGetValue(key, out var cached)");
        getArrowDrawing.Should().Contain("_formulaTraceArrowDrawingCache.Count >= FormulaTraceArrowDrawingCacheLimit");
        getArrowDrawing.Should().Contain("_formulaTraceArrowDrawingCache.Clear();");
        getArrowDrawing.Should().Contain("_formulaTraceArrowDrawingCache.Add(key, drawing);");
        createArrowDrawing.Should().Contain("new DrawingGroup()");
        createArrowDrawing.Should().Contain("FormulaTraceOverlayGeometryPlanner.CalculateArrowHead");
        createArrowDrawing.Should().Contain("GetFormulaTraceArrowHeadGeometry(start, end, arrowHead)");
        createArrowDrawing.Should().Contain("drawing.Freeze();");
        getArrowHeadGeometry.Should().Contain("_formulaTraceArrowHeadGeometryCache.TryGetValue(key, out var cached)");
        getArrowHeadGeometry.Should().Contain("_formulaTraceArrowHeadGeometryCache.Count >= FormulaTraceArrowHeadGeometryCacheLimit");
        getArrowHeadGeometry.Should().Contain("_formulaTraceArrowHeadGeometryCache.Clear();");
        getArrowHeadGeometry.Should().Contain("_formulaTraceArrowHeadGeometryCache.Add(key, geometry);");
        createArrowHeadGeometry.Should().Contain("new StreamGeometry()");
        createArrowHeadGeometry.Should().Contain("geometry.Freeze();");
        overlaysSource.Should().Contain("_formulaTraceArrowLayerCache = null;");
        overlaysSource.Should().Contain("_formulaTraceArrowHeadGeometryCache.Clear();");
        overlaysSource.Should().Contain("_formulaTraceArrowDrawingCache.Clear();");
        overlaysSource.Should().Contain("private static FormulaTraceArrow[] CopyFormulaTraceArrows");
        overlaysSource.Should().Contain("private static bool FormulaTraceArrowsEqual");
        propertiesSource.Should().Contain("OnFormulaTraceRenderCacheInputChanged");
        propertiesSource.Should().Contain("grid.ClearFormulaTraceArrowHeadGeometryCache();");
        propertiesSource.Should().Contain("FormulaTraceArrowsProperty");
        propertiesSource.Should().Contain("FormulaTraceSheetIdProperty");
    }
}
