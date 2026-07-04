using FreeP.App.Compositor;
using FreeP.Core.Model;
using Avalonia.Controls;
using Avalonia.Media;

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

    public static void ApplyRichTextEditorPlan(
        TextBox textBox,
        InCanvasTableCellRichTextEditPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        if (plan is null)
            return;

        textBox.Tag = plan;
        textBox.Classes.Set("freep-shape-rich-editor", plan.HasRichFormatting);
        textBox.Classes.Set("freep-shape-mixed-formatting", plan.HasMixedFormatting);
        ApplyEditorStyle(textBox, plan.SuggestedEditorStyle);
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

        textBox.Classes.Set("freep-shape-underline", style.Underline == true);
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
