namespace FreeW.Core.Model;

/// <summary>
/// Shared, editor-agnostic planning for live Track Changes text edits inside one body paragraph.
/// Editors supply caret offsets and active formatting; this helper mutates the paragraph's run list
/// while preserving every run mark carried by split text.
/// </summary>
public static class RevisionEditPlanner
{
    public readonly record struct DeleteResult(int CaretOffset, bool KeptDeletedText);

    public readonly record struct InsertOptions(
        RevisionKind Revision = RevisionKind.None,
        string? RevisionAuthor = null,
        string? RevisionDateXml = null,
        string? HyperlinkUrl = null,
        string? HyperlinkAnchor = null,
        string? HyperlinkTooltip = null);

    public static RunFormatting FormattingAtOffset(Paragraph paragraph, int offset)
        => RunAtOffset(paragraph, offset)?.Formatting ?? RunFormatting.Default;

    public static InsertOptions LinkAtOffset(Paragraph paragraph, int offset)
    {
        var run = RunAtOffset(paragraph, offset);
        return run is null
            ? default
            : new InsertOptions(
                HyperlinkUrl: run.HyperlinkUrl,
                HyperlinkAnchor: run.HyperlinkAnchor,
                HyperlinkTooltip: run.HyperlinkTooltip);
    }

    public static int InsertText(
        Paragraph paragraph,
        int offset,
        string text,
        RunFormatting formatting,
        InsertOptions options = default)
    {
        var target = Math.Clamp(offset, 0, paragraph.PlainText.Length);
        if (string.IsNullOrEmpty(text))
            return target;

        var insertion = new Run(text, formatting)
        {
            Revision = options.Revision,
            RevisionAuthor = options.RevisionAuthor,
            RevisionDateXml = options.RevisionDateXml,
            HyperlinkUrl = options.HyperlinkUrl,
            HyperlinkAnchor = options.HyperlinkAnchor,
            HyperlinkTooltip = options.HyperlinkTooltip,
        };

        var insertionOffset = InsertRunAtOffset(paragraph, target, insertion);
        return insertionOffset + text.Length;
    }

    public static int InsertTrackedText(
        Paragraph paragraph,
        int offset,
        string text,
        RunFormatting formatting,
        string author,
        string? dateXml,
        string? hyperlinkUrl = null,
        string? hyperlinkAnchor = null,
        string? hyperlinkTooltip = null) =>
        InsertText(
            paragraph,
            offset,
            text,
            formatting,
            new InsertOptions(
                RevisionKind.Inserted,
                author,
                dateXml,
                hyperlinkUrl,
                hyperlinkAnchor,
                hyperlinkTooltip));

    public static DeleteResult DeleteRangeAsRevision(
        Paragraph paragraph,
        int startOffset,
        int endOffset,
        string author,
        string? dateXml)
    {
        var textLength = paragraph.PlainText.Length;
        var lo = Math.Clamp(Math.Min(startOffset, endOffset), 0, textLength);
        var hi = Math.Clamp(Math.Max(startOffset, endOffset), 0, textLength);
        if (hi <= lo)
            return new DeleteResult(lo, KeptDeletedText: false);

        var bookmarkPositions = BookmarkBoundaryMapper.Capture(paragraph);
        var rebuilt = new List<Run>();
        var position = 0;
        var keptDeletedText = false;

        foreach (var source in paragraph.Runs)
        {
            var length = source.Text.Length;
            var runStart = position;
            var runEnd = runStart + length;
            position = runEnd;

            if (length == 0 || runEnd <= lo || runStart >= hi)
            {
                rebuilt.Add(CloneRunWithText(source, source.Text));
                continue;
            }

            var localStart = Math.Max(lo, runStart) - runStart;
            var localEnd = Math.Min(hi, runEnd) - runStart;

            if (localStart > 0)
                rebuilt.Add(CloneRunWithText(source, source.Text[..localStart]));

            var coveredText = source.Text[localStart..localEnd];
            if (source.Revision == RevisionKind.Inserted
                && string.Equals(source.RevisionAuthor, author, StringComparison.Ordinal))
            {
                // Word treats deleting your own unaccepted insertion as taking it back entirely.
            }
            else
            {
                var covered = CloneRunWithText(source, coveredText);
                if (covered.Revision != RevisionKind.Deleted)
                {
                    covered.Revision = RevisionKind.Deleted;
                    covered.RevisionAuthor = author;
                    covered.RevisionDateXml = dateXml;
                }
                keptDeletedText = true;
                rebuilt.Add(covered);
            }

            if (localEnd < length)
                rebuilt.Add(CloneRunWithText(source, source.Text[localEnd..]));
        }

        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(rebuilt);
        BookmarkBoundaryMapper.Restore(paragraph, bookmarkPositions);
        return new DeleteResult(lo, keptDeletedText);
    }

    public static bool MarkRevisionRange(
        Paragraph paragraph,
        int startOffset,
        int endOffset,
        RevisionKind kind,
        string author,
        string? dateXml)
    {
        if (kind == RevisionKind.None)
            return false;

        var textLength = paragraph.PlainText.Length;
        var lo = Math.Clamp(Math.Min(startOffset, endOffset), 0, textLength);
        var hi = Math.Clamp(Math.Max(startOffset, endOffset), 0, textLength);
        if (hi <= lo)
            return false;

        var bookmarkPositions = BookmarkBoundaryMapper.Capture(paragraph);
        var rebuilt = new List<Run>();
        var position = 0;
        var marked = false;

        foreach (var source in paragraph.Runs)
        {
            var length = source.Text.Length;
            var runStart = position;
            var runEnd = runStart + length;
            position = runEnd;

            if (length == 0 || runEnd <= lo || runStart >= hi)
            {
                rebuilt.Add(CloneRunWithText(source, source.Text));
                continue;
            }

            var localStart = Math.Max(lo, runStart) - runStart;
            var localEnd = Math.Min(hi, runEnd) - runStart;

            if (localStart > 0)
                rebuilt.Add(CloneRunWithText(source, source.Text[..localStart]));

            var covered = CloneRunWithText(source, source.Text[localStart..localEnd]);
            covered.Revision = kind;
            covered.RevisionAuthor = author;
            covered.RevisionDateXml = dateXml;
            rebuilt.Add(covered);
            marked = true;

            if (localEnd < length)
                rebuilt.Add(CloneRunWithText(source, source.Text[localEnd..]));
        }

        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(rebuilt);
        BookmarkBoundaryMapper.Restore(paragraph, bookmarkPositions);
        return marked;
    }

    /// <summary>
    /// Applies character formatting to the exact plain-text range while preserving every non-text run
    /// payload and remapping bookmark boundaries across any run splits.
    /// </summary>
    public static bool ApplyFormattingRange(
        Paragraph paragraph,
        int startOffset,
        int endOffset,
        Func<RunFormatting, RunFormatting> transform,
        TextDocument? document = null,
        string? revisionAuthor = null,
        string? revisionDateXml = null)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(transform);

        var textLength = paragraph.PlainText.Length;
        var lo = Math.Clamp(Math.Min(startOffset, endOffset), 0, textLength);
        var hi = Math.Clamp(Math.Max(startOffset, endOffset), 0, textLength);
        if (hi <= lo)
            return false;

        var bookmarkPositions = BookmarkBoundaryMapper.Capture(paragraph);
        var rebuilt = new List<Run>();
        var position = 0;
        var changed = false;

        foreach (var source in paragraph.Runs)
        {
            var length = source.Text.Length;
            var runStart = position;
            var runEnd = runStart + length;
            position = runEnd;

            if (length == 0 || runEnd <= lo || runStart >= hi)
            {
                rebuilt.Add(CloneRunWithText(source, source.Text));
                continue;
            }

            var localStart = Math.Max(lo, runStart) - runStart;
            var localEnd = Math.Min(hi, runEnd) - runStart;

            if (localStart > 0)
                rebuilt.Add(CloneRunWithText(source, source.Text[..localStart]));

            var covered = CloneRunWithText(source, source.Text[localStart..localEnd]);
            var formatting = transform(source.Formatting);
            covered.Formatting = formatting;
            if (formatting != source.Formatting)
            {
                changed = true;
                if (document is { TrackRevisions: true, DoNotTrackFormatting: false }
                    && covered.FormatRevision is null)
                {
                    covered.FormatRevision = new FormatRevision(
                        source.Formatting,
                        string.IsNullOrWhiteSpace(revisionAuthor) ? "FreeW User" : revisionAuthor.Trim(),
                        revisionDateXml);
                }
            }
            rebuilt.Add(covered);

            if (localEnd < length)
                rebuilt.Add(CloneRunWithText(source, source.Text[localEnd..]));
        }

        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(rebuilt);
        BookmarkBoundaryMapper.Restore(paragraph, bookmarkPositions);
        return changed;
    }

    public static Run CloneRunWithText(Run source, string text) => new(text, source.Formatting)
    {
        Image = source.Image,
        Equation = source.Equation,
        Shape = source.Shape,
        WordArt = source.WordArt,
        Chart = source.Chart,
        EmbeddedObject = source.EmbeddedObject,
        SmartArt = source.SmartArt,
        PreservedDrawing = source.PreservedDrawing,
        DrawingGroup = source.DrawingGroup,
        // r163: a run carries its character-style link like any other property; a copier that
        // omits it silently unlinks the run the next time anything reformats the paragraph.
        StyleId = source.StyleId,
        HyperlinkUrl = source.HyperlinkUrl,
        HyperlinkAnchor = source.HyperlinkAnchor,
        HyperlinkTooltip = source.HyperlinkTooltip,
        SubDocument = source.SubDocument,
        FieldKind = source.FieldKind,
        FieldLocked = source.FieldLocked,
        FieldCodeVisible = source.FieldCodeVisible,
        TableFormula = source.TableFormula,
        Citation = source.Citation,
        CrossReference = source.CrossReference,
        ComplexField = source.ComplexField,
        FootnoteId = source.FootnoteId,
        EndnoteId = source.EndnoteId,
        CommentId = source.CommentId,
        IsCommentReference = source.IsCommentReference,
        IsPageBreak = source.IsPageBreak,
        IsColumnBreak = source.IsColumnBreak,
        Revision = source.Revision,
        Control = source.Control,
        RevisionAuthor = source.RevisionAuthor,
        RevisionDateXml = source.RevisionDateXml,
        FormatRevision = source.FormatRevision,
        MoveRevisionId = source.MoveRevisionId,
        Ruby = text == source.Text ? source.Ruby : null
    };

    /// <summary>Locates the run whose text contains <paramref name="offset"/> (clamped to the paragraph's
    /// length), the same lookup <see cref="FormattingAtOffset"/> uses -- exposed publicly so callers that
    /// also need the run's identity (e.g. <see cref="Run.StyleId"/> for a "formatting at the caret" probe)
    /// don't have to duplicate the walk. Null only when the paragraph has no runs at all.</summary>
    public static Run? RunAtOffset(Paragraph paragraph, int offset)
    {
        var textLength = paragraph.Runs.Sum(r => r.Text.Length);
        if (textLength == 0)
            return paragraph.Runs.LastOrDefault();

        var target = Math.Clamp(offset - 1, 0, textLength - 1);
        var position = 0;
        foreach (var run in paragraph.Runs)
        {
            var length = run.Text.Length;
            if (length == 0)
                continue;
            if (target < position + length)
                return run;
            position += length;
        }

        return paragraph.Runs.LastOrDefault();
    }

    public static int InsertRunAtOffset(Paragraph paragraph, int offset, Run insertedRun)
    {
        var targetOffset = Math.Clamp(offset, 0, paragraph.PlainText.Length);
        var consumed = 0;
        for (var i = 0; i < paragraph.Runs.Count; i++)
        {
            var run = paragraph.Runs[i];
            var runLength = run.Text.Length;
            if (targetOffset > consumed + runLength)
            {
                consumed += runLength;
                continue;
            }

            var local = targetOffset - consumed;
            if (local <= 0)
            {
                paragraph.Runs.Insert(i, insertedRun);
            }
            else if (local >= runLength)
            {
                paragraph.Runs.Insert(i + 1, insertedRun);
            }
            else if (run.Ruby is not null || run.Control is not null)
            {
                // A content control is one semantic run (a w:sdt): splitting its text in two would emit
                // the field twice on save, each half claiming to be the whole control. Anything inserted
                // mid-field goes after the intact run, which is also what the ruby case below needs.
                // A ruby annotation is one semantic run: splitting its base text would either duplicate
                // or discard the phonetic payload. XE fields are page anchors, so placing the hidden mark
                // after the intact ruby run preserves both the annotation and the same page identity.
                paragraph.Runs.Insert(i + 1, insertedRun);
                return consumed + runLength;
            }
            else
            {
                var before = CloneRunWithText(run, run.Text[..local]);
                var after = CloneRunWithText(run, run.Text[local..]);
                paragraph.Runs.RemoveAt(i);
                paragraph.Runs.Insert(i, before);
                paragraph.Runs.Insert(i + 1, insertedRun);
                paragraph.Runs.Insert(i + 2, after);
            }
            return targetOffset;
        }

        paragraph.Runs.Add(insertedRun);
        return targetOffset;
    }

    /// <summary>
    /// True when <paramref name="run"/> carries content that is not plain text -- a field, an
    /// image, an equation, a shape, a chart, an embedded object, SmartArt, a content control, a
    /// ruby annotation or a sub-document reference. Such a run must never have its text rewritten
    /// by Find &amp; Replace: a PAGE field's Text is a cached rendering, not something the user
    /// typed, and an image run's Text is empty and contributes nothing to the match in the first
    /// place.
    /// </summary>
    public static bool RunCarriesNonTextContent(Run run) =>
        run.FieldKind != RunFieldKind.None
        || run.ComplexField is not null
        || run.TableFormula is not null
        || run.CrossReference is not null
        || run.Image is not null
        || run.Equation is not null
        || run.Shape is not null
        || run.WordArt is not null
        || run.Chart is not null
        || run.EmbeddedObject is not null
        || run.SmartArt is not null
        || run.PreservedDrawing is not null
        || run.DrawingGroup is not null
        || run.Control is not null
        || run.Ruby is not null
        || run.SubDocument is not null;

    /// <summary>
    /// Replaces the plain-text span [<paramref name="start"/>, start + <paramref name="length"/>)
    /// of <paramref name="paragraph"/> with <paramref name="replacement"/>, touching ONLY the runs
    /// that span actually covers and leaving every other run in the paragraph exactly as it was.
    /// Returns false without modifying anything when the span overlaps a run carrying non-text
    /// content, so the caller can skip that match rather than corrupt it.
    ///
    /// r180: this used to concatenate the paragraph's text, splice, and write back a SINGLE run
    /// built from the matched run's formatting -- `Runs.Clear(); Runs.Add(one)`. That destroyed
    /// every other run in the paragraph. The common casualty is the ordinary Word footer
    /// "Page {PAGE} of {NUMPAGES}": replacing any text in that footer froze the page numbers into
    /// literal text. Mixed formatting, hyperlinks and images went the same way, and none of it was
    /// anywhere near the match. Six review lenses reported it independently.
    ///
    /// The runs are mutated IN PLACE rather than rebuilt, so formatting, hyperlink target,
    /// revision marks and every other run property survive without this method having to know
    /// they exist -- which is what made the rebuild lossy in the first place.
    /// </summary>
    /// <summary>
    /// Whether <see cref="TryReplacePlainTextRange"/> would apply, without changing anything.
    /// </summary>
    public static bool CanReplacePlainTextRange(
        Paragraph paragraph,
        int start,
        int length)
    {
        if (start < 0 || length < 0)
            return false;

        var end = start + length;
        var consumed = 0;
        var covers = false;
        foreach (var run in paragraph.Runs)
        {
            var runLength = run.Text.Length;
            if (Math.Min(end, consumed + runLength) > Math.Max(start, consumed))
            {
                if (RunCarriesNonTextContent(run))
                    return false;
                covers = true;
            }

            consumed += runLength;
        }

        return covers && end <= consumed;
    }

    public static bool TryReplacePlainTextRange(
        Paragraph paragraph,
        int start,
        int length,
        string replacement)
    {
        if (start < 0 || length < 0)
            return false;

        var end = start + length;
        var covered = new List<(Run Run, int Start, int Length)>();
        var consumed = 0;

        foreach (var run in paragraph.Runs)
        {
            var runLength = run.Text.Length;
            var overlapStart = Math.Max(start, consumed);
            var overlapEnd = Math.Min(end, consumed + runLength);
            if (overlapEnd > overlapStart)
            {
                if (RunCarriesNonTextContent(run))
                    return false;

                covered.Add((run, overlapStart - consumed, overlapEnd - overlapStart));
            }

            consumed += runLength;
        }

        if (covered.Count == 0 || end > consumed)
            return false;

        // r181: BookmarkBoundary.RunIndex is a POSITIONAL index into paragraph.Runs, so it
        // goes stale the moment a run is removed below. Every sibling routine that can shrink
        // a run list (RevisionEditPlanner.DeleteRangeAsRevision, MarkRevisionRange,
        // ApplyFormattingRange) brackets its rebuild with this mapper for exactly that reason;
        // a bookmark in a header or footer is an ordinary round-trippable shape, and without
        // this its anchor silently slid on the next save.
        var bookmarkPositions = BookmarkBoundaryMapper.Capture(paragraph);

        // The replacement text lands in the FIRST covered run, so it inherits that run's
        // formatting -- the same choice Word makes. Later covered runs lose only the characters
        // the match actually consumed.
        for (var i = covered.Count - 1; i >= 1; i--)
        {
            var (run, runStart, runLength) = covered[i];
            run.Text = string.Concat(run.Text[..runStart], run.Text[(runStart + runLength)..]);
        }

        var (first, firstStart, firstLength) = covered[0];
        first.Text = string.Concat(
            first.Text[..firstStart],
            replacement,
            first.Text[(firstStart + firstLength)..]);

        // Drop runs THIS MATCH emptied, but never one that carries content of its own -- an
        // image or field run legitimately has empty text. r181: scoped to the covered runs;
        // it used to sweep the whole paragraph, so an unrelated empty run the document had
        // always contained would disappear on any replacement elsewhere in it.
        var emptied = covered
            .Select(entry => entry.Run)
            .Where(run => run.Text.Length == 0 && !RunCarriesNonTextContent(run))
            .ToList();
        foreach (var run in emptied)
        {
            if (paragraph.Runs.Count > 1)
                paragraph.Runs.Remove(run);
        }

        BookmarkBoundaryMapper.Restore(paragraph, bookmarkPositions);
        return true;
    }
}
