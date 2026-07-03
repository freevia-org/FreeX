using FreeP.App.Compositor;
using FreeP.Core.Model;

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

    public static TableCellTextFormatPlan PlanTextFormat(
        EditingSession editor,
        TableCellTextFormatKind kind,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return TableCellEditPlanner.PlanTextFormat(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            editor.SelectedShapeIds,
            editor.ActiveTableCell,
            kind,
            selection);
    }

    public static TableCellTextValueFormatPlan PlanFontFamily(
        EditingSession editor,
        string? fontFamily,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return TableCellEditPlanner.PlanFontFamily(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            editor.SelectedShapeIds,
            editor.ActiveTableCell,
            fontFamily,
            selection);
    }

    public static TableCellTextValueFormatPlan PlanFontSize(
        EditingSession editor,
        double? sizePt,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return TableCellEditPlanner.PlanFontSize(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            editor.SelectedShapeIds,
            editor.ActiveTableCell,
            sizePt,
            selection);
    }

    public static TableCellTextValueFormatPlan PlanColor(
        EditingSession editor,
        ThemeAwareColor? color,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return TableCellEditPlanner.PlanColor(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            editor.SelectedShapeIds,
            editor.ActiveTableCell,
            color,
            selection);
    }
}
