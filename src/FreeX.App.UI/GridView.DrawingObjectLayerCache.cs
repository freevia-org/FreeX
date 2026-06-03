using System.Windows.Media;

using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private DrawingGroup? _drawingObjectLayerCache;
    private DrawingObjectLayerCacheKey _drawingObjectLayerCacheKey;
    private DrawingObjectLayerCacheKey _lastDrawingObjectLayerRenderKey;
    private bool _hasLastDrawingObjectLayerRenderKey;

    private readonly record struct DrawingObjectLayerCacheKey(
        ViewportModel Viewport,
        double ActualWidth,
        double ActualHeight,
        double RowHeaderWidth,
        double ColumnHeaderHeight,
        double ZoomFactor,
        GridObjectDisplayMode ObjectDisplayMode,
        WorkbookTheme WorkbookTheme,
        GridRange? SelectedRange,
        IReadOnlyList<ChartModel>? Charts,
        int ChartCount,
        IReadOnlyList<DrawingShapeModel>? DrawingShapes,
        int DrawingShapeCount,
        IReadOnlyList<PictureModel>? Pictures,
        int PictureCount,
        IReadOnlyList<TextBoxModel>? TextBoxes,
        int TextBoxCount,
        IReadOnlyList<SlicerModel>? NativeSlicers,
        int NativeSlicerCount,
        IReadOnlyList<TimelineModel>? NativeTimelines,
        int NativeTimelineCount,
        IReadOnlyList<DrawingObjectZOrderEntry>? DrawingObjectZOrder,
        int DrawingObjectZOrderCount);

    private void RenderDrawingObjectLayersWithCache(DrawingContext dc)
    {
        var key = CreateDrawingObjectLayerCacheKey();
        if (_drawingObjectLayerCache is { } cached && _drawingObjectLayerCacheKey == key)
        {
            dc.DrawDrawing(cached);
            return;
        }

        if (_drawingObjectLayerCache is not null)
            ClearDrawingObjectLayerCache();

        if (!ShouldBuildDrawingObjectLayerCache(key))
        {
            RenderDrawingObjectLayers(dc);
            RememberDrawingObjectLayerRenderKey(key);
            return;
        }

        var group = BuildDrawingObjectLayerCache();

        _drawingObjectLayerCache = group;
        _drawingObjectLayerCacheKey = key;
        RememberDrawingObjectLayerRenderKey(key);
        dc.DrawDrawing(group);
    }

    private bool ShouldBuildDrawingObjectLayerCache(DrawingObjectLayerCacheKey key) =>
        _hasLastDrawingObjectLayerRenderKey && _lastDrawingObjectLayerRenderKey == key;

    private void RememberDrawingObjectLayerRenderKey(DrawingObjectLayerCacheKey key)
    {
        _lastDrawingObjectLayerRenderKey = key;
        _hasLastDrawingObjectLayerRenderKey = true;
    }

    private DrawingGroup BuildDrawingObjectLayerCache()
    {
        var group = new DrawingGroup();
        using (var groupContext = group.Open())
            RenderDrawingObjectLayers(groupContext);

        if (group.CanFreeze)
            group.Freeze();

        return group;
    }

    private void RenderDrawingObjectLayers(DrawingContext dc)
    {
        if (ObjectDisplayMode == GridObjectDisplayMode.Placeholders)
        {
            RenderObjectPlaceholders(dc);
            return;
        }

        if (ObjectDisplayMode != GridObjectDisplayMode.All)
            return;

        RenderCharts(dc);
        if (HasExplicitDrawingObjectZOrder())
        {
            RenderNativeSlicerTimelineControls(dc);
            RenderDrawingObjectsByZOrder(dc);
        }
        else
        {
            RenderDrawingShapes(dc);
            RenderNativeSlicerTimelineControls(dc);
            RenderPictures(dc);
            RenderTextBoxes(dc);
        }
    }

    private DrawingObjectLayerCacheKey CreateDrawingObjectLayerCacheKey() =>
        new(
            Viewport!,
            ActualWidth,
            ActualHeight,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight,
            ZoomFactor,
            ObjectDisplayMode,
            WorkbookTheme,
            SelectedRange,
            Charts,
            Charts?.Count ?? 0,
            DrawingShapes,
            DrawingShapes?.Count ?? 0,
            Pictures,
            Pictures?.Count ?? 0,
            TextBoxes,
            TextBoxes?.Count ?? 0,
            NativeSlicers,
            NativeSlicers?.Count ?? 0,
            NativeTimelines,
            NativeTimelines?.Count ?? 0,
            DrawingObjectZOrder,
            DrawingObjectZOrder?.Count ?? 0);

    private void ClearDrawingObjectLayerCache()
    {
        _drawingObjectLayerCache = null;
        _hasLastDrawingObjectLayerRenderKey = false;
    }
}
