using System.Windows;
using System.Windows.Media;

namespace FreeX.App.UI;

public partial class GridView
{
    protected override void OnRender(DrawingContext dc)
    {
        if (Viewport == null) return;

        RebuildMergeLookup();
        var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        var isLiveResizing = IsLiveResizing;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth / zoom, ActualHeight / zoom)));

        RenderHeaders(dc);
        if (!isLiveResizing)
            RenderWorksheetBackground(dc);
        RenderGridLines(dc);
        RenderCells(dc);
        RenderSplitPaneCells(dc);
        if (isLiveResizing)
            RenderLiveResizeContinuation(dc);
        if (!isLiveResizing)
        {
            RenderWorksheetViewOverlay(dc);
            RenderSparklines(dc);
            RenderQuickAnalysisPreview(dc);
        }
        RenderSelection(dc);
        if (!isLiveResizing)
        {
            RenderFormulaTraceArrows(dc);
            RenderAutofillPreview(dc);
            RenderMarchingAnts(dc);
        }
        RenderFreezeDivider(dc);
        RenderSplitDivider(dc);
        RenderSplitPaneScrollbarChrome(dc);
        RenderResizeLine(dc);
        if (!isLiveResizing)
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

        dc.Pop();
    }
}
