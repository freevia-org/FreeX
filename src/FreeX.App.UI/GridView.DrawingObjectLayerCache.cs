using System.Windows.Media;

using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private DrawingGroup? _drawingObjectLayerCache;
    private DrawingObjectLayerCacheKey _drawingObjectLayerCacheKey;

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

        var group = new DrawingGroup();
        using (var groupContext = group.Open())
            RenderDrawingObjectLayers(groupContext);

        if (group.CanFreeze)
            group.Freeze();

        _drawingObjectLayerCache = group;
        _drawingObjectLayerCacheKey = key;
        dc.DrawDrawing(group);
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
    }
}
