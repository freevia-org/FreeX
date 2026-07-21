using System.Windows.Media;

using FreeX.App.Presentation.DrawingUI;
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
        IReadOnlyList<ChartModel>? Charts,
        int ChartCount,
        IReadOnlyList<DrawingShapeModel>? DrawingShapes,
        int DrawingShapeCount,
        int DrawingShapeStamp,
        IReadOnlyList<PictureModel>? Pictures,
        int PictureCount,
        int PictureStamp,
        IReadOnlyList<TextBoxModel>? TextBoxes,
        int TextBoxCount,
        int TextBoxStamp,
        IReadOnlyList<SlicerModel>? NativeSlicers,
        int NativeSlicerCount,
        IReadOnlyList<TimelineModel>? NativeTimelines,
        int NativeTimelineCount,
        IReadOnlyList<DrawingObjectZOrderEntry>? DrawingObjectZOrder,
        int DrawingObjectZOrderCount,
        IReadOnlyList<FormControlModel>? FormControls,
        int FormControlCount,
        int FormControlStamp);

    private void RenderDrawingObjectLayersWithCache(DrawingContext dc)
    {
        if (HasLiveObjectTransformPreview())
        {
            RenderDrawingObjectLayers(dc);
            return;
        }

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
        var renderMode = GridDrawingObjectPlanner.PlanLayerRenderMode(ObjectDisplayMode);
        if (renderMode == DrawingObjectLayerRenderMode.Placeholders)
        {
            RenderObjectPlaceholders(dc);
            return;
        }

        if (renderMode != DrawingObjectLayerRenderMode.Objects)
            return;

        if (HasExplicitDrawingObjectZOrder())
        {
            // R60-render-drawing-shapes-6-1: charts used to be hard-coded to render behind every
            // shape/picture/textbox via an unconditional RenderCharts(dc) pass here. When the sheet
            // carries an explicit drawing z-order, charts now draw as part of
            // RenderDrawingObjectsByZOrder, in their recorded stacking position, instead of always
            // being forced to the bottom.
            RenderNativeSlicerTimelineControls(dc);
            RenderDrawingObjectsByZOrder(dc);
        }
        else
        {
            RenderCharts(dc);
            RenderDrawingShapes(dc);
            RenderNativeSlicerTimelineControls(dc);
            RenderPictures(dc);
            RenderTextBoxes(dc);
        }

        RenderFormControls(dc);
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
            Charts,
            Charts?.Count ?? 0,
            DrawingShapes,
            DrawingShapes?.Count ?? 0,
            GridDrawingObjectPlanner.CalculateDrawingShapeRenderStamp(DrawingShapes),
            Pictures,
            Pictures?.Count ?? 0,
            GridDrawingObjectPlanner.CalculatePictureRenderStamp(Pictures),
            TextBoxes,
            TextBoxes?.Count ?? 0,
            GridDrawingObjectPlanner.CalculateTextBoxRenderStamp(TextBoxes),
            NativeSlicers,
            NativeSlicers?.Count ?? 0,
            NativeTimelines,
            NativeTimelines?.Count ?? 0,
            DrawingObjectZOrder,
            DrawingObjectZOrder?.Count ?? 0,
            FormControls,
            FormControls?.Count ?? 0,
            CalculateFormControlLayerStamp(FormControls));

    private static int CalculateFormControlLayerStamp(IReadOnlyList<FormControlModel>? controls)
    {
        if (controls is null || controls.Count == 0)
            return 0;

        var hash = new HashCode();
        foreach (var control in controls)
        {
            hash.Add(control.Kind);
            hash.Add(control.Anchor);
            hash.Add(control.AnchorOffsets);
            hash.Add(control.Name);
            hash.Add(control.IsChecked);
            hash.Add(control.Value);
            hash.Add(control.SelectedIndex);
            hash.Add(control.SelectedText);
        }

        return hash.ToHashCode();
    }

    private void ClearDrawingObjectLayerCache()
    {
        _drawingObjectLayerCache = null;
        _hasLastDrawingObjectLayerRenderKey = false;
    }
}
