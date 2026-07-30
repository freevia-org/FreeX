namespace FreeP.App.Compositor;

/// <summary>
/// Shared logical contract for a pointer drag in an in-canvas rich editor.
/// Hosts only translate framework pointer coordinates into logical offsets;
/// anchor direction and document bounds stay identical across WPF/Avalonia.
/// </summary>
public static class InCanvasRichTextPointerSelectionPlanner
{
    public static InCanvasEditorTextSelection Plan(
        int anchor,
        int caret,
        int textLength)
    {
        if (textLength < 0)
            throw new ArgumentOutOfRangeException(nameof(textLength));

        return new(
            Math.Clamp(anchor, 0, textLength),
            Math.Clamp(caret, 0, textLength));
    }

    public static (int Start, int End) Normalize(
        int anchor,
        int caret,
        int textLength)
    {
        var selection = Plan(anchor, caret, textLength);
        return (
            Math.Min(selection.Start, selection.End),
            Math.Max(selection.Start, selection.End));
    }
}
