using System.Windows;
using System.Windows.Media;

using FreeX.App.Presentation.DrawingUI;

namespace FreeX.App.UI;

public partial class GridView
{
    private RectangleGeometry? _renderClipGeometryCache;
    private Rect _renderClipGeometryCacheRect;

    protected override void OnRender(DrawingContext dc)
    {
        if (Viewport == null) return;

        RebuildMergeLookup();
        var isLiveResizing = IsLiveResizing;
        var skipHeavyLayers = isLiveResizing || _resizeTarget != ResizeTarget.None;
        dc.PushClip(GetRenderClipGeometry(new Rect(0, 0, GetLogicalViewportWidth(), GetLogicalViewportHeight())));

        // An exception escaping OnRender is fatal in WPF, and the render pass re-runs on every paint:
        // a content-driven fault (a malformed chart model, an undecodable image) would therefore crash
        // the app again and again with no way for the user to get back to their workbook. Degrade this
        // paint instead, and report the fault once per distinct signature so it is still tracked rather
        // than silently swallowed. The clip is popped in `finally` so the drawing context stays balanced.
        try
        {
            RenderHeaders(dc);
            RenderPreSelectionLayersWithCache(dc, skipHeavyLayers, isLiveResizing);
            RenderSelection(dc);
            RenderPostSelectionLayers(dc, skipHeavyLayers);
        }
        catch (Exception ex)
        {
            GridRenderFaultReporter.Report(ex, "grid_render");
        }
        finally
        {
            dc.Pop();
        }

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

    private double GetLogicalViewportWidth()
    {
        var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        return Math.Max(0, ActualWidth / zoom);
    }

    private double GetLogicalViewportHeight()
    {
        var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        return Math.Max(0, ActualHeight / zoom);
    }

    private void RenderPreSelectionLayers(DrawingContext dc, bool skipHeavyLayers, bool isLiveResizing)
    {
        if (!skipHeavyLayers)
            RenderWorksheetBackground(dc);
        RenderGridLines(dc);
        RenderCells(dc);
        RenderSplitPaneCells(dc);
        RenderPivotRowLabelAdornments(dc);
        RenderAutoFilterButtons(dc);
        RenderPivotHeaderDropdownButtons(dc);
        RenderViewportContinuation(dc);
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
            RenderValidationCircles(dc);
            RenderAutofillPreview(dc);
            RenderMarchingAnts(dc);
        }

        RenderFreezeDivider(dc);
        RenderSplitDivider(dc);
        RenderSplitPaneScrollbarChrome(dc);
        RenderResizeLine(dc);
        if (!skipHeavyLayers)
        {
            RenderDrawingObjectLayersWithCache(dc);

            var selectedRect = GetSelectedObjectRect();
            if (!selectedRect.IsEmpty)
            {
                var rotationDegrees = GetSelectedObjectRotationDegrees();
                var liveRect = GetSelectedObjectLiveRect(selectedRect);
                var liveRotationDegrees = GetSelectedObjectLiveRotationDegrees(rotationDegrees);
                if (IsSelectedPictureCropModeActive())
                {
                    var crop = TryResolveLivePictureCrop(SelectedObjectId, out var liveCrop)
                        ? liveCrop
                        : GetSelectedPictureCropRatios();
                    DrawPictureCropHandles(dc, liveRect, crop, liveRotationDegrees);
                }
                else
                    DrawObjectSelectionHandles(dc, liveRect, liveRotationDegrees);
            }
        }

        RenderShapePlacementPreview(dc);
        RenderTextBoxPlacementPreview(dc);
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
            return _shapePlacementDragging || _textBoxPlacementDragging;

        return FormulaTraceArrows is { Count: > 0 } ||
            ValidationCircleCells is { Count: > 0 } ||
            (_autofillDragging && _autofillSourceRange.HasValue && _autofillTarget.HasValue) ||
            ClipboardRange is not null ||
            _shapePlacementDragging ||
            _textBoxPlacementDragging ||
            HasDrawingObjectLayerWork();
    }

    private bool HasDrawingObjectLayerWork()
    {
        if (GridDrawingObjectPlanner.PlanLayerRenderMode(ObjectDisplayMode) == DrawingObjectLayerRenderMode.Hidden)
            return false;

        if (SelectedObjectId != Guid.Empty && SelectedObjectKind != ObjectKind.None)
            return true;

        return Charts is { Count: > 0 } ||
            DrawingShapes is { Count: > 0 } ||
            NativeSlicers is { Count: > 0 } ||
            NativeTimelines is { Count: > 0 } ||
            Pictures is { Count: > 0 } ||
            TextBoxes is { Count: > 0 } ||
            FormControls is { Count: > 0 };
    }
}
