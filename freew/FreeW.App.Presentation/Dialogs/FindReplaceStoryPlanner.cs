using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

/// <summary>A resume point in the non-body story walk: a paragraph and an offset inside it.</summary>
public readonly record struct FindReplaceStoryPosition(StoryParagraphAddress Address, int Offset);

/// <summary>A match in a story outside the body (header/footer, note, comment or text box).</summary>
/// <param name="Context">A short excerpt around the match, for a status line.</param>
public sealed record FindReplaceStoryMatch(
    StoryParagraphAddress Address,
    int Start,
    int Length,
    int StoryNumber,
    StoryHeaderFooterSlot Slot,
    string MatchedText,
    string Context)
{
    public DocumentStoryKind Kind => Address.Kind;

    public FindReplaceStoryPosition End => new(Address, Start + Length);
}

/// <summary>Who a tracked replacement is attributed to when Track Changes is on.</summary>
public sealed record FindReplaceRevisionContext(string Author, string? DateXml);

/// <summary>The outcome of planning one story replacement.</summary>
/// <param name="Command">The undoable edit, or null when this match must be skipped.</param>
/// <param name="Resume">Where the walk continues afterwards (always past this match).</param>
public readonly record struct FindReplaceStoryReplacement(IDocumentCommand? Command, Func<FindReplaceStoryPosition> Resume);

/// <summary>
/// Find and Replace over every story outside <see cref="TextDocument.Blocks"/>. These stories are not
/// part of a host's editable body surface, so they are searched and edited directly in the model;
/// each edit is an <see cref="EditStoryParagraphCommand"/> the host runs on its command bus.
/// </summary>
/// <remarks>
/// Matching is per paragraph: a paragraph mark is never part of a story match, and a replacement
/// that would insert one is skipped, because splitting or merging paragraphs needs a different
/// structural edit for each story container.
/// </remarks>
public static class FindReplaceStoryPlanner
{
    private const int ContextRadius = 24;
    private const int MaxReplacements = 100_000;

    /// <summary>
    /// The first match at or after <paramref name="from"/> in canonical story order, or the very first
    /// story match when <paramref name="from"/> is null.
    /// </summary>
    public static FindReplaceStoryMatch? FindNext(
        TextDocument document,
        FindReplaceSearchRequest request,
        FindReplaceStoryPosition? from = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        var criteria = request.ToCriteria();
        if (criteria.IsEmpty)
            return null;

        foreach (var story in DocumentStories.Enumerate(document))
        {
            var minOffset = 0;
            if (from is { } position)
            {
                var order = Compare(story.Address, position.Address);
                if (order < 0)
                    continue;
                if (order == 0)
                    minOffset = Math.Max(0, position.Offset);
            }

            foreach (var (start, length) in FormattedTextSearch.FindAll(document, story.Paragraph, criteria))
            {
                if (start < minOffset)
                    continue;

                var text = story.Paragraph.PlainText;
                return new FindReplaceStoryMatch(
                    story.Address,
                    start,
                    length,
                    story.StoryNumber,
                    story.Slot,
                    text.Substring(start, length),
                    Excerpt(text, start, length));
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="match"/> is still exactly a match (the story may have changed).</summary>
    public static bool IsCurrent(TextDocument document, FindReplaceSearchRequest request, FindReplaceStoryMatch match) =>
        FindNext(document, request, new FindReplaceStoryPosition(match.Address, match.Start)) is { } current
        && current.Address == match.Address
        && current.Start == match.Start
        && current.Length == match.Length;

    /// <summary>
    /// Plans the replacement of one story match. The command is null when the match overlaps a field
    /// or an inline object (rewriting a PAGE field's cached text as if it had been typed would freeze
    /// it), or when the replacement would insert a paragraph mark; the walk still moves past it.
    /// </summary>
    public static FindReplaceStoryReplacement PlanReplacement(
        TextDocument document,
        FindReplaceStoryMatch match,
        FindReplaceReplaceRequest request,
        FindReplaceRevisionContext? tracked = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(request);

        var skip = new FindReplaceStoryPosition(match.Address, match.Start + Math.Max(1, match.Length));
        var plan = FindReplaceDialogPlanner.PlanReplacement(request, match.MatchedText);
        var paragraph = DocumentStories.Resolve(document, match.Address)?.Paragraph;
        if (paragraph is null
            || plan.InsertsParagraphMark
            || (plan.ReplacesText
                && !RevisionEditPlanner.CanReplacePlainTextRange(paragraph, match.Start, match.Length)))
        {
            return new FindReplaceStoryReplacement(null, () => skip);
        }

        var start = match.Start;
        var length = match.Length;
        var resumeOffset = start + (plan.ReplacesText ? plan.Text.Length : length);
        var command = new EditStoryParagraphCommand(
            match.Address,
            target =>
            {
                var formatStart = start;
                var formatEnd = start + length;
                if (plan.ReplacesText)
                {
                    if (tracked is null)
                    {
                        RevisionEditPlanner.TryReplacePlainTextRange(target, start, length, plan.Text);
                        formatEnd = start + plan.Text.Length;
                    }
                    else
                    {
                        // Record the edit as a revision, exactly as the body does: the original is
                        // kept struck through and the replacement is inserted after it.
                        var deleted = RevisionEditPlanner.DeleteRangeAsRevision(
                            target, start, start + length, tracked.Author, tracked.DateXml);
                        var formatting = RevisionEditPlanner.FormattingAtOffset(target, deleted.CaretOffset);
                        var afterInsert = RevisionEditPlanner.InsertTrackedText(
                            target, deleted.CaretOffset, plan.Text, formatting, tracked.Author, tracked.DateXml);
                        formatStart = deleted.CaretOffset;
                        formatEnd = afterInsert;

                        // The struck-through original stays in the paragraph, so the next search has
                        // to start past it too or it re-finds the text it just marked deleted.
                        resumeOffset = afterInsert + (deleted.KeptDeletedText ? length : 0);
                    }
                }

                if (plan.Format is { } format)
                {
                    if (format.HasCharacterFormatting && formatEnd > formatStart)
                    {
                        RevisionEditPlanner.ApplyFormattingRange(
                            target,
                            formatStart,
                            formatEnd,
                            format.ApplyTo,
                            tracked is null ? null : document,
                            tracked?.Author,
                            tracked?.DateXml);
                    }

                    if (format.ParagraphStyleId is { } styleId)
                        target.StyleId = styleId;
                }
            });

        return new FindReplaceStoryReplacement(
            command,
            () => new FindReplaceStoryPosition(match.Address, resumeOffset));
    }

    /// <summary>
    /// Replaces every match in every non-body story, running each edit through
    /// <paramref name="execute"/>. Each replacement resumes past the text it just wrote, so a
    /// replacement that re-creates the search term (Confidential -> Strictly Confidential) cannot
    /// re-find itself. Returns how many matches were replaced.
    /// </summary>
    public static int ReplaceAll(
        TextDocument document,
        FindReplaceReplaceRequest request,
        Action<IDocumentCommand> execute,
        FindReplaceRevisionContext? tracked = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(execute);

        var search = request.ToSearchRequest();
        var count = 0;
        FindReplaceStoryPosition? resume = null;
        for (var visited = 0; visited < MaxReplacements; visited++)
        {
            if (FindNext(document, search, resume) is not { } match)
                break;

            var replacement = PlanReplacement(document, match, request, tracked);
            if (replacement.Command is { } command)
            {
                execute(command);
                count++;
            }

            // Always at or past the match start: past the replacement text when text was written
            // (a deletion resumes AT the start, where the following text now sits), and past the
            // match itself when it was only reformatted or skipped.
            resume = replacement.Resume();
        }

        return count;
    }

    /// <summary>A localized description of where a story match is, e.g. "footer (section 2)".</summary>
    public static string DescribeLocation(FindReplaceStoryMatch match, FindReplaceDialogText? text = null)
    {
        ArgumentNullException.ThrowIfNull(match);
        text ??= FindReplaceDialogText.NeutralEnglish;
        var id = match.Kind switch
        {
            DocumentStoryKind.Footnote => FindReplaceTextId.StoryFootnoteFormat,
            DocumentStoryKind.Endnote => FindReplaceTextId.StoryEndnoteFormat,
            DocumentStoryKind.Comment => FindReplaceTextId.StoryCommentFormat,
            DocumentStoryKind.TextBox => FindReplaceTextId.StoryTextBoxFormat,
            _ => match.Slot switch
            {
                StoryHeaderFooterSlot.Footer => FindReplaceTextId.StoryFooterFormat,
                StoryHeaderFooterSlot.FirstHeader => FindReplaceTextId.StoryFirstHeaderFormat,
                StoryHeaderFooterSlot.FirstFooter => FindReplaceTextId.StoryFirstFooterFormat,
                StoryHeaderFooterSlot.EvenHeader => FindReplaceTextId.StoryEvenHeaderFormat,
                StoryHeaderFooterSlot.EvenFooter => FindReplaceTextId.StoryEvenFooterFormat,
                _ => FindReplaceTextId.StoryHeaderFormat,
            },
        };
        return text.Format(id, match.StoryNumber);
    }

    /// <summary>The status line for a match the host cannot select on screen.</summary>
    public static string DescribeMatch(FindReplaceStoryMatch match, FindReplaceDialogText? text = null)
    {
        text ??= FindReplaceDialogText.NeutralEnglish;
        return text.Format(FindReplaceTextId.FoundInStoryFormat, DescribeLocation(match, text), match.Context);
    }

    private static int Compare(StoryParagraphAddress left, StoryParagraphAddress right) =>
        left.Kind != right.Kind
            ? ((int)left.Kind).CompareTo((int)right.Kind)
            : left.Ordinal.CompareTo(right.Ordinal);

    private static string Excerpt(string text, int start, int length)
    {
        var from = Math.Max(0, start - ContextRadius);
        var to = Math.Min(text.Length, start + length + ContextRadius);
        return string.Concat(
            from > 0 ? "…" : string.Empty,
            text[from..to].Replace('\n', ' ').Replace('\t', ' '),
            to < text.Length ? "…" : string.Empty);
    }
}
