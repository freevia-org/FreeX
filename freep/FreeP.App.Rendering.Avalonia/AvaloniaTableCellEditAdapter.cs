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

    public static InCanvasRichTextEditSession BeginSession(TableCellEditStartPlan plan) =>
        InCanvasRichTextEditSession.BeginTableCell(plan);

    public static InCanvasTextEditDecision CommitRichText(
        InCanvasTableCellTextEditPlanner? editPlanner,
        TextBody editedBody) =>
        TableCellEditPlanner.CommitRichText(editPlanner, editedBody);

    public static InCanvasTextEditDecision Cancel(InCanvasTableCellTextEditPlanner? editPlanner) =>
        TableCellEditPlanner.Cancel(editPlanner);

    public static TableCellNavigationPlan PlanNavigation(
        InCanvasRichTextEditSession? session,
        EditingSession editor,
        TableCellNavigationDirection direction)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (session is null)
        {
            return TableCellEditPlanner.PlanNavigation(
                editor.CurrentSlide,
                editor.SelectedShapeIds,
                editor.ActiveTableCell,
                direction);
        }

        return session.PlanTableCellNavigation(
            editor.CurrentSlide,
            editor.SelectedShapeIds,
            editor.ActiveTableCell,
            direction);
    }

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

    public static TableCellParagraphFormatPlan PlanParagraphAlignment(
        EditingSession editor,
        TextAlign alignment,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return TableCellEditPlanner.PlanParagraphAlignment(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            editor.SelectedShapeIds,
            editor.ActiveTableCell,
            alignment,
            selection);
    }

    public static TableCellParagraphFormatPlan PlanParagraphBulletToggle(
        EditingSession editor,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return TableCellEditPlanner.PlanParagraphBulletToggle(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            editor.SelectedShapeIds,
            editor.ActiveTableCell,
            selection);
    }

    public static TableCellParagraphFormatPlan PlanParagraphNumberingToggle(
        EditingSession editor,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return TableCellEditPlanner.PlanParagraphNumberingToggle(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            editor.SelectedShapeIds,
            editor.ActiveTableCell,
            selection);
    }

    public static TableCellParagraphFormatPlan PlanParagraphListPreset(
        EditingSession editor,
        TableCellListPresetDescriptor preset,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(preset);

        return TableCellEditPlanner.PlanParagraphListPreset(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            editor.SelectedShapeIds,
            editor.ActiveTableCell,
            preset,
            selection);
    }

    public static TableCellParagraphFormatPlan PlanParagraphPictureBullet(
        EditingSession editor,
        PresentationPictureBulletPayload payload,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return TableCellEditPlanner.PlanParagraphPictureBullet(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            editor.SelectedShapeIds,
            editor.ActiveTableCell,
            payload,
            selection);
    }

    public static TableCellParagraphFormatPlan PlanParagraphIndent(
        EditingSession editor,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return TableCellEditPlanner.PlanParagraphIndent(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            editor.SelectedShapeIds,
            editor.ActiveTableCell,
            selection);
    }

    public static TableCellParagraphFormatPlan PlanParagraphOutdent(
        EditingSession editor,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return TableCellEditPlanner.PlanParagraphOutdent(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            editor.SelectedShapeIds,
            editor.ActiveTableCell,
            selection);
    }

    internal static void ApplyRichTextEditorPlan(
        AvaloniaRichTextEditor editor,
        InCanvasTableCellRichTextEditPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (plan is null)
            return;

        editor.ApplyPlanMetadata(
            plan,
            "freep-table-cell-rich-editor",
            "freep-table-cell-mixed-formatting");
    }

    internal static void ApplyFormatResult(
        AvaloniaRichTextEditor editor,
        InCanvasTableCellRichTextEditPlan? plan)
    {
        ApplyRichTextEditorPlan(editor, plan);

        if (plan is null)
            return;

        int textLength = editor.Text.Length;
        var selection = TableCellEditPlanner.PlanPreservedSelection(plan.Selection, textLength);
        editor.SelectionStart = selection.Start;
        editor.SelectionEnd = selection.End;
    }
}
