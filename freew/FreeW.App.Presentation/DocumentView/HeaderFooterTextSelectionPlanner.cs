using System.Text;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>A renderer-neutral caret or selection endpoint inside one header/footer story.</summary>
public readonly record struct HeaderFooterTextPosition(int ParagraphIndex, int Offset);

/// <summary>A normalized, non-empty range inside one header/footer story.</summary>
public readonly record struct HeaderFooterTextRange(
    HeaderFooterTextPosition Start,
    HeaderFooterTextPosition End);

/// <summary>
/// Plans caret movement and selection semantics for header/footer stories. Renderers own pointer geometry,
/// while this shared layer owns model addressing, paragraph-boundary navigation, normalization, and text
/// extraction so WPF and Avalonia can expose the same editing contract.
/// </summary>
public static class HeaderFooterTextSelectionPlanner
{
    public static HeaderFooterTextPosition Clamp(HeaderFooter story, HeaderFooterTextPosition position)
    {
        ArgumentNullException.ThrowIfNull(story);
        if (story.Paragraphs.Count == 0)
            return new HeaderFooterTextPosition(0, 0);

        var paragraphIndex = Math.Clamp(position.ParagraphIndex, 0, story.Paragraphs.Count - 1);
        var length = story.Paragraphs[paragraphIndex].Runs.Sum(run => run.Text.Length);
        return new HeaderFooterTextPosition(paragraphIndex, Math.Clamp(position.Offset, 0, length));
    }

    public static HeaderFooterTextRange? Normalize(
        HeaderFooter story,
        HeaderFooterTextPosition caret,
        HeaderFooterTextPosition? selectionAnchor)
    {
        ArgumentNullException.ThrowIfNull(story);
        if (selectionAnchor is null)
            return null;

        caret = Clamp(story, caret);
        var anchor = Clamp(story, selectionAnchor.Value);
        if (caret == anchor)
            return null;

        return Compare(anchor, caret) <= 0
            ? new HeaderFooterTextRange(anchor, caret)
            : new HeaderFooterTextRange(caret, anchor);
    }

    public static HeaderFooterTextPosition MoveHorizontal(
        HeaderFooter story,
        HeaderFooterTextPosition position,
        int delta)
    {
        ArgumentNullException.ThrowIfNull(story);
        position = Clamp(story, position);
        if (story.Paragraphs.Count == 0 || delta == 0)
            return position;

        var direction = Math.Sign(delta);
        for (var remaining = Math.Abs(delta); remaining > 0; remaining--)
        {
            var paragraphLength = ParagraphLength(story, position.ParagraphIndex);
            if (direction < 0)
            {
                position = position.Offset > 0
                    ? position with { Offset = position.Offset - 1 }
                    : position.ParagraphIndex > 0
                        ? new HeaderFooterTextPosition(
                            position.ParagraphIndex - 1,
                            ParagraphLength(story, position.ParagraphIndex - 1))
                        : position;
            }
            else
            {
                position = position.Offset < paragraphLength
                    ? position with { Offset = position.Offset + 1 }
                    : position.ParagraphIndex + 1 < story.Paragraphs.Count
                        ? new HeaderFooterTextPosition(position.ParagraphIndex + 1, 0)
                        : position;
            }
        }

        return position;
    }

    public static HeaderFooterTextPosition MoveToParagraphEdge(
        HeaderFooter story,
        HeaderFooterTextPosition position,
        bool toStart)
    {
        ArgumentNullException.ThrowIfNull(story);
        position = Clamp(story, position);
        return position with { Offset = toStart ? 0 : ParagraphLength(story, position.ParagraphIndex) };
    }

    public static HeaderFooterTextPosition MoveVertical(
        HeaderFooter story,
        HeaderFooterTextPosition position,
        int paragraphDelta)
    {
        ArgumentNullException.ThrowIfNull(story);
        position = Clamp(story, position);
        if (story.Paragraphs.Count == 0 || paragraphDelta == 0)
            return position;

        var paragraphIndex = Math.Clamp(
            position.ParagraphIndex + paragraphDelta,
            0,
            story.Paragraphs.Count - 1);
        return new HeaderFooterTextPosition(
            paragraphIndex,
            Math.Min(position.Offset, ParagraphLength(story, paragraphIndex)));
    }

    public static string GetText(HeaderFooter story, HeaderFooterTextRange range)
    {
        ArgumentNullException.ThrowIfNull(story);
        var normalized = Normalize(story, range.End, range.Start);
        if (normalized is null)
            return string.Empty;

        range = normalized.Value;
        var text = new StringBuilder();
        for (var paragraphIndex = range.Start.ParagraphIndex;
             paragraphIndex <= range.End.ParagraphIndex;
             paragraphIndex++)
        {
            if (paragraphIndex > range.Start.ParagraphIndex)
                text.Append('\n');

            var paragraphText = story.Paragraphs[paragraphIndex].PlainText;
            var start = paragraphIndex == range.Start.ParagraphIndex ? range.Start.Offset : 0;
            var end = paragraphIndex == range.End.ParagraphIndex ? range.End.Offset : paragraphText.Length;
            start = Math.Clamp(start, 0, paragraphText.Length);
            end = Math.Clamp(end, start, paragraphText.Length);
            text.Append(paragraphText.AsSpan(start, end - start));
        }

        return text.ToString();
    }

    public static int Compare(HeaderFooterTextPosition left, HeaderFooterTextPosition right)
    {
        var paragraphComparison = left.ParagraphIndex.CompareTo(right.ParagraphIndex);
        return paragraphComparison != 0 ? paragraphComparison : left.Offset.CompareTo(right.Offset);
    }

    private static int ParagraphLength(HeaderFooter story, int paragraphIndex) =>
        paragraphIndex >= 0 && paragraphIndex < story.Paragraphs.Count
            ? story.Paragraphs[paragraphIndex].Runs.Sum(run => run.Text.Length)
            : 0;
}
