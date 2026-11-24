using System.Globalization;

namespace FreeW.App.Presentation.Editing;

public enum DocumentTextNavigationIntent
{
    None,
    PreviousCharacter,
    NextCharacter,
    PreviousWord,
    NextWord,
    NextWordStart,
    LineStart,
    LineEnd,
    PreviousParagraph,
    NextParagraph,
    NextParagraphStart,
    StoryStart,
    StoryEnd,
}

public readonly record struct DocumentTextNavigationPosition(int ParagraphIndex, int Offset);

public readonly record struct DocumentTextNavigationPlan(
    DocumentTextNavigationIntent Intent,
    bool ExtendSelection);

/// <summary>
/// Word keyboard navigation with distinct Command, Control, and Option modifiers. Logical
/// positions remain independent of layout; hosts supply visual line edges from their glyphs.
/// </summary>
public static class DocumentTextNavigationPlanner
{
    public static DocumentTextNavigationPlan Plan(
        DocumentEditorInputKey key,
        bool control,
        bool command,
        bool option,
        bool shift)
    {
        var intent = key switch
        {
            DocumentEditorInputKey.Left when command => DocumentTextNavigationIntent.LineStart,
            DocumentEditorInputKey.Right when command => DocumentTextNavigationIntent.LineEnd,
            DocumentEditorInputKey.Left when control || option => DocumentTextNavigationIntent.PreviousWord,
            DocumentEditorInputKey.Right when option => DocumentTextNavigationIntent.NextWord,
            DocumentEditorInputKey.Right when control => DocumentTextNavigationIntent.NextWordStart,
            DocumentEditorInputKey.Left => DocumentTextNavigationIntent.PreviousCharacter,
            DocumentEditorInputKey.Right => DocumentTextNavigationIntent.NextCharacter,
            DocumentEditorInputKey.Up when command || control || option => DocumentTextNavigationIntent.PreviousParagraph,
            DocumentEditorInputKey.Down when control && !command && !option => DocumentTextNavigationIntent.NextParagraphStart,
            DocumentEditorInputKey.Down when command || control || option => DocumentTextNavigationIntent.NextParagraph,
            DocumentEditorInputKey.Home when command || control => DocumentTextNavigationIntent.StoryStart,
            DocumentEditorInputKey.End when command || control => DocumentTextNavigationIntent.StoryEnd,
            DocumentEditorInputKey.Home => DocumentTextNavigationIntent.LineStart,
            DocumentEditorInputKey.End => DocumentTextNavigationIntent.LineEnd,
            _ => DocumentTextNavigationIntent.None,
        };
        return new(intent, shift);
    }

    public static DocumentTextNavigationPosition Move(
        IReadOnlyList<string> paragraphs,
        DocumentTextNavigationPosition current,
        DocumentTextNavigationIntent intent)
    {
        if (paragraphs.Count == 0)
            return default;
        var paragraph = Math.Clamp(current.ParagraphIndex, 0, paragraphs.Count - 1);
        var text = paragraphs[paragraph];
        var offset = Math.Clamp(current.Offset, 0, text.Length);
        var previous = Math.Max(0, paragraph - 1);
        var next = Math.Min(paragraphs.Count - 1, paragraph + 1);
        return intent switch
        {
            DocumentTextNavigationIntent.StoryStart => new(0, 0),
            DocumentTextNavigationIntent.StoryEnd => new(paragraphs.Count - 1, paragraphs[^1].Length),
            DocumentTextNavigationIntent.LineStart => new(paragraph, 0),
            DocumentTextNavigationIntent.LineEnd => new(paragraph, text.Length),
            DocumentTextNavigationIntent.PreviousParagraph => new(offset > 0 ? paragraph : previous, 0),
            DocumentTextNavigationIntent.NextParagraph => offset < text.Length
                ? new(paragraph, text.Length)
                : new(next, paragraphs[next].Length),
            DocumentTextNavigationIntent.NextParagraphStart => new(next, next > paragraph ? 0 : text.Length),
            DocumentTextNavigationIntent.PreviousCharacter => offset > 0
                ? new(paragraph, PreviousElement(text, offset))
                : new(previous, paragraph > 0 ? paragraphs[previous].Length : 0),
            DocumentTextNavigationIntent.NextCharacter => offset < text.Length
                ? new(paragraph, NextElement(text, offset))
                : new(next, next > paragraph ? 0 : text.Length),
            DocumentTextNavigationIntent.PreviousWord => offset > 0
                ? new(paragraph, PreviousWord(text, offset))
                : new(previous, paragraph > 0 ? PreviousWord(paragraphs[previous], paragraphs[previous].Length) : 0),
            DocumentTextNavigationIntent.NextWord or DocumentTextNavigationIntent.NextWordStart => offset < text.Length
                ? new(paragraph, NextWord(text, offset, intent == DocumentTextNavigationIntent.NextWordStart))
                : new(next, next > paragraph
                    ? intent == DocumentTextNavigationIntent.NextWordStart ? 0 : NextWord(paragraphs[next], 0, false)
                    : text.Length),
            _ => new(paragraph, offset),
        };
    }

    private static int PreviousWord(string text, int offset)
    {
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var boundary = Array.BinarySearch(boundaries, offset);
        boundary = boundary >= 0 ? boundary - 1 : ~boundary - 1;
        while (boundary >= 0 && WordClass(text, boundaries[boundary]) == 0)
            boundary--;
        if (boundary < 0)
            return 0;
        var kind = WordClass(text, boundaries[boundary]);
        while (boundary >= 0 && WordClass(text, boundaries[boundary]) == kind)
            boundary--;
        return boundaries[boundary + 1];
    }

    private static int NextWord(string text, int offset, bool startOfNextWord)
    {
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        if (!startOfNextWord)
        {
            while (offset < text.Length && WordClass(text, offset) == 0)
                offset = NextElement(text, offset, boundaries);
        }
        if (offset < text.Length)
        {
            var kind = WordClass(text, offset);
            while (offset < text.Length && WordClass(text, offset) == kind)
                offset = NextElement(text, offset, boundaries);
        }
        if (startOfNextWord)
        {
            while (offset < text.Length && WordClass(text, offset) == 0)
                offset = NextElement(text, offset, boundaries);
        }
        return offset;
    }

    private static int WordClass(string text, int offset) =>
        char.IsWhiteSpace(text, offset) ? 0
        : char.IsLetterOrDigit(text, offset)
            || CharUnicodeInfo.GetUnicodeCategory(text, offset) is
                UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark or UnicodeCategory.ConnectorPunctuation ? 1
        : 2;

    private static int PreviousElement(string text, int offset)
    {
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        for (var index = boundaries.Length - 1; index >= 0; index--)
            if (boundaries[index] < offset)
                return boundaries[index];
        return 0;
    }

    private static int NextElement(string text, int offset, int[]? boundaries = null)
    {
        // Pointer/automation positions can already lie inside a surrogate pair or a ZWJ
        // sequence. Segment the entire paragraph, rather than treating that interior
        // offset as the start of a fresh text element.
        boundaries ??= StringInfo.ParseCombiningCharacters(text);
        var index = Array.BinarySearch(boundaries, offset);
        index = index >= 0 ? index + 1 : ~index;
        return index < boundaries.Length ? boundaries[index] : text.Length;
    }
}
