using System.Globalization;

namespace FreeP.App.Compositor;

public enum InCanvasTextNavigationKey
{
    Left,
    Right,
    Home,
    End,
}

public enum InCanvasTextVerticalDirection
{
    Up,
    Down,
}

/// <summary>
/// A renderer-measured caret position on one visual line. Logical positions are the shared
/// editor offsets; X is in the renderer's common editor coordinate space.
/// </summary>
public readonly record struct InCanvasTextVisualCaret(int LogicalPosition, double X);

/// <summary>
/// Renderer-measured geometry for one wrapped visual line. The host supplies these points;
/// navigation and boundary selection remain shared.
/// </summary>
public sealed record InCanvasTextVisualLineGeometry(
    int Start,
    int End,
    IReadOnlyList<InCanvasTextVisualCaret> Carets)
{
    public InCanvasTextVisualCaret FirstCaret => Carets[0];

    public InCanvasTextVisualCaret LastCaret => Carets[^1];

    public bool Contains(int logicalPosition) =>
        logicalPosition >= Start && logicalPosition <= End;
}

public readonly record struct InCanvasTextVerticalNavigationResult(
    int LogicalPosition,
    double PreferredX,
    int VisualLineIndex,
    bool Moved);

/// <summary>
/// Framework-neutral logical navigation for the Avalonia in-canvas editor, matching the
/// corresponding native WPF RichTextBox semantics. Visual line movement remains renderer-owned.
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
            InCanvasTextNavigationKey.Left => MoveGraphemeLeft(value, position),
            InCanvasTextNavigationKey.Right => MoveGraphemeRight(value, position),
            InCanvasTextNavigationKey.Home => MoveParagraphBoundary(value, position, end: false),
            InCanvasTextNavigationKey.End => MoveParagraphBoundary(value, position, end: true),
            _ => position,
        };
    }

    /// <summary>
    /// Moves one position left to the start of the preceding grapheme cluster (text element),
    /// so a surrogate pair (e.g. an emoji) or a base character plus its combining marks is
    /// crossed as a single step rather than splitting it -- matching native RichTextBox caret
    /// semantics.
    /// </summary>
    private static int MoveGraphemeLeft(string text, int position)
    {
        if (position <= 0)
            return 0;

        int previousElementStart = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            int elementStart = enumerator.ElementIndex;
            if (elementStart >= position)
                break;
            previousElementStart = elementStart;
        }

        return previousElementStart;
    }

    /// <summary>
    /// Moves one position right to the start of the following grapheme cluster (text element),
    /// so a surrogate pair or a base character plus its combining marks is crossed as a single
    /// step rather than leaving the caret mid-pair/mid-mark.
    /// </summary>
    private static int MoveGraphemeRight(string text, int position)
    {
        if (position >= text.Length)
            return text.Length;

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            int elementStart = enumerator.ElementIndex;
            string element = (string)enumerator.Current;
            int elementEnd = elementStart + element.Length;
            if (position >= elementStart && position < elementEnd)
                return elementEnd;
        }

        return text.Length;
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

    public static InCanvasTextVerticalNavigationResult MoveCaretVertically(
        IReadOnlyList<InCanvasTextVisualLineGeometry> lines,
        int caret,
        InCanvasTextVerticalDirection direction,
        double? preferredX = null,
        int? currentVisualLineIndex = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
            return new(caret, preferredX ?? 0, 0, false);

        int currentLine = currentVisualLineIndex is int supplied
            && supplied >= 0
            && supplied < lines.Count
            ? supplied
            : FindVerticalLine(lines, caret, direction);
        var current = lines[currentLine];
        double x = preferredX ?? FindCaret(current, caret).X;
        int targetLine = currentLine + (direction == InCanvasTextVerticalDirection.Up ? -1 : 1);
        if (targetLine < 0)
            return new(caret, x, currentLine, false);
        if (targetLine >= lines.Count)
            return new(caret, x, currentLine, false);

        var target = lines[targetLine];
        var targetCaret = target.Carets
            .OrderBy(point => Math.Abs(point.X - x))
            .ThenBy(point => point.LogicalPosition)
            .First();
        return new(targetCaret.LogicalPosition, x, targetLine, true);
    }

    public static int MoveCaretToVisualLineBoundary(
        IReadOnlyList<InCanvasTextVisualLineGeometry> lines,
        int caret,
        bool end)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
            return caret;

        int lineIndex = FindBoundaryLine(lines, caret);
        return end
            ? lines[lineIndex].LastCaret.LogicalPosition
            : lines[lineIndex].FirstCaret.LogicalPosition;
    }

    private static int FindVerticalLine(
        IReadOnlyList<InCanvasTextVisualLineGeometry> lines,
        int caret,
        InCanvasTextVerticalDirection direction)
    {
        var candidates = lines
            .Select((line, index) => (line, index))
            .Where(item => item.line.Contains(caret))
            .ToArray();
        if (candidates.Length == 0)
            return caret < lines[0].Start ? 0 : lines.Count - 1;

        // A wrapped-line boundary belongs to the line being left, so Down crosses forward
        // and Up crosses backward while preserving the same logical caret offset.
        if (direction == InCanvasTextVerticalDirection.Down)
        {
            var leaving = candidates.FirstOrDefault(item => item.line.End == caret);
            return candidates.Any(item => item.line.End == caret)
                ? leaving.index
                : candidates[0].index;
        }

        var entering = candidates.LastOrDefault(item => item.line.Start == caret);
        return candidates.Any(item => item.line.Start == caret)
            ? entering.index
            : candidates[^1].index;
    }

    private static int FindBoundaryLine(
        IReadOnlyList<InCanvasTextVisualLineGeometry> lines,
        int caret)
    {
        var exactStart = lines
            .Select((line, index) => (line, index))
            .Where(item => item.line.Start == caret)
            .Select(item => item.index)
            .LastOrDefault(-1);
        if (exactStart >= 0)
            return exactStart;

        var containing = lines
            .Select((line, index) => (line, index))
            .LastOrDefault(item => item.line.Contains(caret));
        return containing.index;
    }

    private static InCanvasTextVisualCaret FindCaret(
        InCanvasTextVisualLineGeometry line,
        int caret) =>
        line.Carets
            .OrderBy(point => Math.Abs(point.LogicalPosition - caret))
            .ThenBy(point => point.LogicalPosition)
            .First();

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
