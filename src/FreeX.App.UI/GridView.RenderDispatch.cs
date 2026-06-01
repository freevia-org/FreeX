using System.Windows;
using System.Windows.Media;

namespace FreeX.App.UI;

public partial class GridView
{
    private RectangleGeometry? _renderClipGeometryCache;
    private Rect _renderClipGeometryCacheRect;

    protected override void OnRender(DrawingContext dc)
    {
        if (Viewport == null) return;

        RebuildMergeLookup();
        var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        var isLiveResizing = IsLiveResizing;
        var skipHeavyLayers = isLiveResizing || _resizeTarget != ResizeTarget.None;
        dc.PushClip(GetRenderClipGeometry(new Rect(0, 0, ActualWidth / zoom, ActualHeight / zoom)));

        RenderHeaders(dc);
        RenderPreSelectionLayersWithCache(dc, skipHeavyLayers, isLiveResizing);
        RenderSelection(dc);
        RenderPostSelectionLayers(dc, skipHeavyLayers);

        dc.Pop();
        _selectionVisualOnlyChangePending = false;
    }

    private RectangleGeometry GetRenderClipGeometry(Rect clipRect)
    {
        if (_renderClipGeometryCache is { } cached && _renderClipGeometryCacheRect == clipRect)
            return cached;

        var geometry = new RectangleGeometry(clipRect);
        if (geometry.CanFreeze)
            geometry.Freeze();

        _renderClipGeometryCache = geometry;
        _renderClipGeometryCacheRect = clipRect;
        return geometry;
    }

    private void RenderPreSelectionLayers(DrawingContext dc, bool skipHeavyLayers, bool isLiveResizing)
    {
        if (!skipHeavyLayers)
            RenderWorksheetBackground(dc);
        RenderGridLines(dc);
        RenderCells(dc);
        RenderSplitPaneCells(dc);
        if (isLiveResizing)
            RenderLiveResizeContinuation(dc);
        if (!skipHeavyLayers)
        {
            RenderWorksheetViewOverlay(dc);
            RenderSparklines(dc);
            RenderQuickAnalysisPreview(dc);
        }
    }

    private void RenderPostSelectionLayers(DrawingContext dc, bool skipHeavyLayers)
    {
        if (!HasPostSelectionLayerWork(skipHeavyLayers))
            return;

        if (!skipHeavyLayers)
        {
            RenderFormulaTraceArrows(dc);
            RenderAutofillPreview(dc);
            RenderMarchingAnts(dc);
        }

        RenderFreezeDivider(dc);
        RenderSplitDivider(dc);
        RenderSplitPaneScrollbarChrome(dc);
        RenderResizeLine(dc);
        if (!skipHeavyLayers)
        {
            if (ObjectDisplayMode == GridObjectDisplayMode.Placeholders)
            {
                RenderObjectPlaceholders(dc);
            }
            else if (ObjectDisplayMode == GridObjectDisplayMode.All)
            {
                RenderCharts(dc);
                RenderDrawingShapes(dc);
                RenderNativeSlicerTimelineControls(dc);
                RenderPictures(dc);
                RenderTextBoxes(dc);
            }

            var selectedRect = GetSelectedObjectRect();
            if (!selectedRect.IsEmpty)
            {
                if (_objectDragKind != ObjectDragKind.None)
                    RenderObjectDragPreview(dc, selectedRect);
                else
                    DrawObjectSelectionHandles(dc, selectedRect);
            }
        }
    }

    private bool HasPostSelectionLayerWork(bool skipHeavyLayers)
    {
        if (Viewport?.FrozenPanes is not null ||
            Viewport?.SplitPanes is not null ||
            _resizeTarget != ResizeTarget.None)
        {
            return true;
        }

        if (skipHeavyLayers)
            return false;

        return FormulaTraceArrows is { Count: > 0 } ||
            (_autofillDragging && _autofillSourceRange.HasValue && _autofillTarget.HasValue) ||
            ClipboardRange is not null ||
            HasDrawingObjectLayerWork();
    }

    private bool HasDrawingObjectLayerWork()
    {
        if (SelectedObjectId != Guid.Empty && SelectedObjectKind != ObjectKind.None)
            return true;

        if (ObjectDisplayMode == GridObjectDisplayMode.Nothing)
            return false;

        return Charts is { Count: > 0 } ||
            DrawingShapes is { Count: > 0 } ||
            NativeSlicers is { Count: > 0 } ||
            NativeTimelines is { Count: > 0 } ||
            Pictures is { Count: > 0 } ||
            TextBoxes is { Count: > 0 };
    }
}
