using FreeP.App.Compositor;

namespace FreeP.App.Rendering.Avalonia;

public static class AvaloniaTableCellEditAdapter
{
    public static TableCellEditState PlanSelectedCell(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return TableCellEditPlanner.PlanSelectedCell(
            editor.CurrentSlide,
            editor.SelectedShapeIds,
            editor.ActiveTableCell);
    }

    public static TableCellEditStartPlan BeginEdit(
        SlideCanvas canvas,
        EditingSession editor,
        uint shapeId,
        int row,
        int col,
        double minimumWidth = 40,
        double minimumHeight = 20)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(editor);

        return TableCellEditPlanner.BeginEdit(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            shapeId,
            row,
            col,
            canvas.CurrentTransform,
            minimumWidth,
            minimumHeight);
    }

    public static InCanvasTextEditDecision CommitRichText(
        InCanvasTableCellTextEditPlanner? editPlanner,
        FreeP.Core.Model.TextBody editedBody) =>
        TableCellEditPlanner.CommitRichText(editPlanner, editedBody);

    public static InCanvasTextEditDecision Cancel(InCanvasTableCellTextEditPlanner? editPlanner) =>
        TableCellEditPlanner.Cancel(editPlanner);
}
