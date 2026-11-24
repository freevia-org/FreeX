using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    // Paragraph-name bookmarks do not change text offsets. Support their literal
    // same-cell merge while retaining the guard for run-boundary bookmarks and atoms.
    private bool CanDeleteSameCellBookmarkSelection()
    {
        if (SelectedCellRange is not null || _shapeCaret is not null || _hfCaret is not null
            || _cellCaret is not { } caret || _cellAnchor is not { } anchor
            || caret.TableBlock != anchor.TableBlock || caret.TableBlock < 0
            || caret.TableBlock >= _doc.Blocks.Count || _doc.Blocks[caret.TableBlock] is not Table table)
            return false;
        var (start, end) = CompareCellEndpoints(anchor, caret) <= 0 ? (anchor, caret) : (caret, anchor);
        var first = CanonicalCellEntry(table, caret.TableBlock, start.Row, start.Col);
        var last = CanonicalCellEntry(table, caret.TableBlock, end.Row, end.Col);
        return first is { } entry && last is { } final && ReferenceEquals(entry.Cell, final.Cell)
            && CanDeleteSameCellBookmarkRange(entry, start.ParaIdx, start.Offset, end.ParaIdx, end.Offset);
    }

    private bool CanDeleteSameCellBookmarkRange(TableCellEntry entry,
        int startParagraph, int startOffset, int endParagraph, int endOffset)
    {
        if (IsEditingLocked || TrackChangesEnabled || startParagraph < 0 || endParagraph <= startParagraph
            || endParagraph >= entry.Cell.Paragraphs.Count)
            return false;
        var touched = entry.Cell.Paragraphs.Skip(startParagraph).Take(endParagraph - startParagraph + 1).ToArray();
        if (!touched.Any(paragraph => paragraph.BookmarkNames.Count > 0))
            return false;
        foreach (var paragraph in touched)
        {
            if (paragraph.BlockContentControl is not null || paragraph.BlockCustomXml is not null
                || paragraph.BookmarkBoundaries.Count > 0
                || paragraph.Runs.Any(run => !IsUnicodeLiteralRun(run) || run.Ruby is not null
                    || run.PreservedDrawing is not null || run.SubDocument is not null || run.Citation is not null
                    || run.TableFormula is not null || run.CrossReference is not null
                    || run.IsPageBreak || run.IsColumnBreak || run.Text.Length == 0
                    || run.Control is not null || run.CommentId is not null || run.MoveRevisionId is not null
                    || run.Revision != RevisionKind.None || run.FormatRevision is not null
                    || run.Text.Any(IsBodySoftLineBreak)))
                return false;
            var address = new KeyboardParagraph(paragraph, entry.TableBlock, entry.Row, entry.Col);
            if (KeyboardParagraphText(address).Length != paragraph.PlainText.Length)
                return false;
        }
        return IsCellTextElementBoundary(touched[0].PlainText, startOffset)
            && IsCellTextElementBoundary(touched[^1].PlainText, endOffset);
    }

    private static bool IsCellTextElementBoundary(string text, int offset) =>
        offset == text.Length || offset >= 0 && offset < text.Length
            && StringInfo.ParseCombiningCharacters(text).Contains(offset);

    private bool TryDeleteSameCellBookmarkRange(TableCellEntry entry,
        int startParagraph, int startOffset, int endParagraph, int endOffset)
    {
        if (!CanDeleteSameCellBookmarkRange(entry, startParagraph, startOffset, endParagraph, endOffset))
            return false;
        var touched = entry.Cell.Paragraphs.Skip(startParagraph).Take(endParagraph - startParagraph + 1).ToArray();
        var merged = ParagraphWithFormatting(touched[0]);
        merged.Runs.AddRange(CloneCellLiteralRange(touched[0], 0, startOffset));
        merged.Runs.AddRange(CloneCellLiteralRange(touched[^1], endOffset, touched[^1].PlainText.Length));
        merged.BookmarkNames.Clear();
        merged.BookmarkNames.AddRange(touched.SelectMany(paragraph => paragraph.BookmarkNames).Distinct(StringComparer.Ordinal));
        ExecuteTextEditWithCaretHistory(new SpliceCellParagraphsCommand(entry.TableBlock, entry.Row, entry.Col,
            startParagraph, touched.Length, [merged]), () =>
        {
            _cellCaret = (entry.TableBlock, entry.Row, entry.Col, startParagraph, startOffset);
            _cellAnchor = _cellCaret;
            _caret = new DocPosition(entry.TableBlock,
                FindCellGlyphOffset(entry.TableBlock, entry.Row, entry.Col, startParagraph, startOffset));
            _selectionAnchor = _caret;
        });
        return true;
    }

    private static IEnumerable<Run> CloneCellLiteralRange(Paragraph paragraph, int from, int to)
    {
        var position = 0;
        foreach (var run in paragraph.Runs)
        {
            var start = Math.Max(0, from - position);
            var end = Math.Min(run.Text.Length, to - position);
            if (end > start)
                yield return RevisionEditPlanner.CloneRunWithText(run, run.Text[start..end]);
            position += run.Text.Length;
        }
    }
}
