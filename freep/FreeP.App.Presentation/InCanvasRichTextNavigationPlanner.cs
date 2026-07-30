namespace FreeP.App.Compositor;

public enum InCanvasTextNavigationKey
{
    Left,
    Right,
    Home,
    End,
}

/// <summary>
/// Framework-neutral logical navigation used by both in-canvas rich-text editors.
/// Visual line movement remains a renderer concern; document and word movement does not.
/// </summary>
public static class InCanvasRichTextNavigationPlanner
{
    public static int MoveCaret(
        string? text,
        int caret,
        InCanvasTextNavigationKey key,
        bool control = false)
    {
        string value = text ?? string.Empty;
        int position = Math.Clamp(caret, 0, value.Length);

        if (control)
        {
            return key switch
            {
                InCanvasTextNavigationKey.Home => 0,
                InCanvasTextNavigationKey.End => value.Length,
                InCanvasTextNavigationKey.Left => MoveWordLeft(value, position),
                InCanvasTextNavigationKey.Right => MoveWordRight(value, position),
                _ => position,
            };
        }

        return key switch
        {
            InCanvasTextNavigationKey.Left => Math.Max(0, position - 1),
            InCanvasTextNavigationKey.Right => Math.Min(value.Length, position + 1),
            InCanvasTextNavigationKey.Home => MoveParagraphBoundary(value, position, end: false),
            InCanvasTextNavigationKey.End => MoveParagraphBoundary(value, position, end: true),
            _ => position,
        };
    }

    public static int ResolveSelectionAnchor(int selectionStart, int selectionEnd, int caret)
    {
        if (selectionStart == selectionEnd)
            return caret;
        if (caret == selectionStart)
            return selectionEnd;
        if (caret == selectionEnd)
            return selectionStart;

        int lower = Math.Min(selectionStart, selectionEnd);
        int upper = Math.Max(selectionStart, selectionEnd);
        return caret <= lower ? upper : lower;
    }

    private static int MoveParagraphBoundary(string text, int position, bool end)
    {
        if (end)
        {
            int newline = text.IndexOf('\n', position);
            return newline < 0 ? text.Length : newline;
        }

        int previousNewline = text.LastIndexOf('\n', Math.Max(0, position - 1));
        return previousNewline < 0 ? 0 : previousNewline + 1;
    }

    private static int MoveWordLeft(string text, int position)
    {
        while (position > 0 && char.IsWhiteSpace(text[position - 1]))
            position--;
        while (position > 0 && !char.IsWhiteSpace(text[position - 1]))
            position--;
        return position;
    }

    private static int MoveWordRight(string text, int position)
    {
        while (position < text.Length && !char.IsWhiteSpace(text[position]))
            position++;
        while (position < text.Length && char.IsWhiteSpace(text[position]))
            position++;
        return position;
    }
}
