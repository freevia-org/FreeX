using Avalonia.Controls;
using Avalonia.Media;
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

    public static void ApplyRichTextEditorPlan(
        TextBox textBox,
        InCanvasTableCellRichTextEditPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        if (plan is null)
            return;

        textBox.Tag = plan;
        textBox.Classes.Set("freep-table-cell-rich-editor", plan.HasRichFormatting);
        textBox.Classes.Set("freep-table-cell-mixed-formatting", plan.HasMixedFormatting);
        ApplyEditorStyle(textBox, plan.SuggestedEditorStyle);
    }

    public static void ApplyFormatResult(
        TextBox textBox,
        InCanvasTableCellRichTextEditPlan? plan)
    {
        ApplyRichTextEditorPlan(textBox, plan);

        if (plan is null)
            return;

        int textLength = textBox.Text?.Length ?? 0;
        var selection = TableCellEditPlanner.PlanPreservedSelection(plan.Selection, textLength);
        textBox.SelectionStart = selection.Start;
        textBox.SelectionEnd = selection.End;
    }

    private static void ApplyEditorStyle(
        TextBox textBox,
        InCanvasEditorTextStyleState style)
    {
        if (!string.IsNullOrWhiteSpace(style.FontFamily))
            textBox.FontFamily = new FontFamily(style.FontFamily);
        if (style.FontSizePt is { } fontSizePt)
            textBox.FontSize = fontSizePt;

        textBox.FontWeight = style.Bold == true ? FontWeight.Bold : FontWeight.Normal;
        textBox.FontStyle = style.Italic == true ? FontStyle.Italic : FontStyle.Normal;

        textBox.Classes.Set("freep-table-cell-underline", style.Underline == true);
        textBox.BorderThickness = style.Underline == true
            ? new global::Avalonia.Thickness(1.5, 1.5, 1.5, 3.0)
            : new global::Avalonia.Thickness(1.5);

        textBox.Foreground = style.Color is null
            ? textBox.Foreground
            : new SolidColorBrush(Color.FromRgb(
                style.Color.Resolved.R,
                style.Color.Resolved.G,
                style.Color.Resolved.B));
    }
}
