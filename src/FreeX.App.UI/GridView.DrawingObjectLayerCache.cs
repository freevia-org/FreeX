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
            CalculateDrawingShapeLayerStamp(DrawingShapes),
            Pictures,
            Pictures?.Count ?? 0,
            CalculatePictureLayerStamp(Pictures),
            TextBoxes,
            TextBoxes?.Count ?? 0,
            CalculateTextBoxLayerStamp(TextBoxes),
            NativeSlicers,
            NativeSlicers?.Count ?? 0,
            NativeTimelines,
            NativeTimelines?.Count ?? 0,
            DrawingObjectZOrder,
            DrawingObjectZOrder?.Count ?? 0,
            FormControls,
            FormControls?.Count ?? 0,
            CalculateFormControlLayerStamp(FormControls));

    private static int CalculateDrawingShapeLayerStamp(IReadOnlyList<DrawingShapeModel>? shapes)
    {
        if (shapes is null || shapes.Count == 0)
            return 0;

        var hash = new HashCode();
        foreach (var shape in shapes)
        {
            hash.Add(shape.Id);
            hash.Add(shape.Anchor);
            hash.Add(shape.Kind);
            hash.Add(shape.Width);
            hash.Add(shape.Height);
            hash.Add(shape.RotationDegrees);
            hash.Add(shape.FlipHorizontal);
            hash.Add(shape.FlipVertical);
            hash.Add(shape.IsVisible);
            hash.Add(shape.HasFill);
            hash.Add(shape.FillColor);
            hash.Add(shape.OutlineColor);
            hash.Add(shape.GradientFillEndColor);
            hash.Add(shape.GradientFillDirection);
            hash.Add(shape.FillThemeColor);
            hash.Add(shape.OutlineThemeColor);
            hash.Add(shape.HasShadowEffect);
            hash.Add(shape.EffectPreset);
            hash.Add(shape.UsesThemeEffects);
        }

        return hash.ToHashCode();
    }

    private static int CalculatePictureLayerStamp(IReadOnlyList<PictureModel>? pictures)
    {
        if (pictures is null || pictures.Count == 0)
            return 0;

        var hash = new HashCode();
        foreach (var picture in pictures)
        {
            hash.Add(picture.Id);
            hash.Add(picture.Anchor);
            hash.Add(picture.Kind);
            hash.Add(picture.Width);
            hash.Add(picture.Height);
            hash.Add(picture.RotationDegrees);
            hash.Add(picture.FlipHorizontal);
            hash.Add(picture.FlipVertical);
            hash.Add(picture.IsVisible);
            hash.Add(picture.CropLeft);
            hash.Add(picture.CropTop);
            hash.Add(picture.CropRight);
            hash.Add(picture.CropBottom);
            hash.Add(picture.ImageBytes?.Length ?? 0);
            hash.Add(picture.ContentType);
            hash.Add(picture.SourceRowCount);
            hash.Add(picture.SourceColumnCount);
            hash.Add(picture.Cells.Count);
        }

        return hash.ToHashCode();
    }

    private static int CalculateTextBoxLayerStamp(IReadOnlyList<TextBoxModel>? textBoxes)
    {
        if (textBoxes is null || textBoxes.Count == 0)
            return 0;

        var hash = new HashCode();
        foreach (var textBox in textBoxes)
        {
            hash.Add(textBox.Id);
            hash.Add(textBox.Anchor);
            hash.Add(textBox.Text);
            hash.Add(textBox.Width);
            hash.Add(textBox.Height);
            hash.Add(textBox.RotationDegrees);
            hash.Add(textBox.FlipHorizontal);
            hash.Add(textBox.FlipVertical);
            hash.Add(textBox.IsVisible);
            hash.Add(textBox.HasFill);
            hash.Add(textBox.FillColor);
            hash.Add(textBox.OutlineColor);
            hash.Add(textBox.FillThemeColor);
            hash.Add(textBox.OutlineThemeColor);
        }

        return hash.ToHashCode();
    }

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
