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
    {
        if (paragraph.Runs.Count == 0)
            return RunFormatting.Default;

        var textLength = paragraph.Runs.Sum(r => r.Text.Length);
        if (textLength == 0)
            return paragraph.Runs[^1].Formatting;

        var target = Math.Clamp(offset - 1, 0, textLength - 1);
        var position = 0;
        foreach (var run in paragraph.Runs)
        {
            var length = run.Text.Length;
            if (length == 0)
                continue;
            if (target < position + length)
                return run.Formatting;
            position += length;
        }

        return paragraph.Runs[^1].Formatting;
    }

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

        InsertRunAtOffset(paragraph, target, insertion);
        return target + text.Length;
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
        HyperlinkUrl = source.HyperlinkUrl,
        HyperlinkAnchor = source.HyperlinkAnchor,
        HyperlinkTooltip = source.HyperlinkTooltip,
        FieldKind = source.FieldKind,
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
        FormatRevision = source.FormatRevision
    };

    private static Run? RunAtOffset(Paragraph paragraph, int offset)
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

    public static void InsertRunAtOffset(Paragraph paragraph, int offset, Run insertedRun)
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
            else
            {
                var before = CloneRunWithText(run, run.Text[..local]);
                var after = CloneRunWithText(run, run.Text[local..]);
                paragraph.Runs.RemoveAt(i);
                paragraph.Runs.Insert(i, before);
                paragraph.Runs.Insert(i + 1, insertedRun);
                paragraph.Runs.Insert(i + 2, after);
            }
            return;
        }

        paragraph.Runs.Add(insertedRun);
    }
}
