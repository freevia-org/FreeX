using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia;

public static class AvaloniaInCanvasTextEditAdapter
{
    public static InCanvasShapeTextFormatPlan PlanTextFormat(
        EditingSession editor,
        uint shapeId,
        TableCellTextFormatKind kind,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return InCanvasTextEditPlanner.PlanTextFormat(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            shapeId,
            kind,
            selection);
    }

    public static InCanvasShapeTextValueFormatPlan PlanFontFamily(
        EditingSession editor,
        uint shapeId,
        string? fontFamily,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return InCanvasTextEditPlanner.PlanFontFamily(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            shapeId,
            fontFamily,
            selection);
    }

    public static InCanvasShapeTextValueFormatPlan PlanFontSize(
        EditingSession editor,
        uint shapeId,
        double? sizePt,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return InCanvasTextEditPlanner.PlanFontSize(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            shapeId,
            sizePt,
            selection);
    }

    public static InCanvasShapeTextValueFormatPlan PlanColor(
        EditingSession editor,
        uint shapeId,
        ThemeAwareColor? color,
        (int Start, int End)? selection = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return InCanvasTextEditPlanner.PlanColor(
            editor.CurrentSlideIndex,
            editor.CurrentSlide,
            shapeId,
            color,
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
            "freep-shape-rich-editor",
            "freep-shape-mixed-formatting");
    }
}
