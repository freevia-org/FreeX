using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private static bool HasBodyBookmarks(Paragraph paragraph) =>
        paragraph.BookmarkNames.Count > 0 || paragraph.BookmarkBoundaries.Count > 0;

    private static bool SupportsBookmarkedBodyStructure(Paragraph paragraph) =>
        paragraph.BlockContentControl is null && paragraph.DropCap is null
        && paragraph.PreservedNumbering is null && paragraph.ParagraphFormatRevision is null
        && paragraph.BookmarkBoundaries.All(boundary => boundary.OwnerControl is null)
        && paragraph.SpanningFieldStart is null && paragraph.SpanningFieldOwner is null && !paragraph.EndsSpanningField
        && paragraph.Runs.All(run => !IsDisplaySelectionAtomicRun(run) && run.Control is null
            && run.Revision == RevisionKind.None
            && run.CommentId is null && !run.IsCommentReference && run.MoveRevisionId is null);

    // A boundary merge concatenates complete runs; unlike splitting, it never divides a
    // content control backed by literal runs or removes its identity, content or deletion lock.
    private static bool SupportsPreservedBodyMerge(Paragraph paragraph) =>
        paragraph.BlockContentControl is null && paragraph.DropCap is null
        && paragraph.PreservedNumbering is null && paragraph.ParagraphFormatRevision is null
        && paragraph.SpanningFieldStart is null && paragraph.SpanningFieldOwner is null && !paragraph.EndsSpanningField
        && paragraph.Runs.All(run => !IsDisplaySelectionAtomicRun(run)
            && run.Revision == RevisionKind.None
            && run.CommentId is null && !run.IsCommentReference && run.MoveRevisionId is null);

    private static bool NeedsPreservedBodyMerge(Paragraph paragraph) =>
        HasBodyBookmarks(paragraph) || paragraph.Runs.Any(run => run.Control is not null);

    private bool TrySplitBookmarkedBodyParagraph()
    {
        if (IsEditingLocked || TrackChangesEnabled || _cellCaret is not null || _hfCaret is not null
            || _shapeCaret is not null || _selectedFloating is not null || _selectedInlineObject is not null
            || SelectedCellRange is not null)
            return false;
        var selection = NormalizedSelection();
        var start = selection?.Start ?? _caret;
        var end = selection?.End ?? _caret;
        if (start.Block != end.Block || start.Block < 0 || start.Block >= _doc.Blocks.Count
            || _doc.Blocks[start.Block] is not Paragraph source
            || !HasBodyBookmarks(source) || !SupportsBookmarkedBodyStructure(source))
            return false;
        var text = source.PlainText;
        if (start.Offset < 0 || end.Offset > text.Length
            || SplitsBookmarkStructureSurrogate(text, start.Offset) || SplitsBookmarkStructureSurrogate(text, end.Offset))
            return false;

        var remaining = DocumentModelCloner.CloneParagraph(source, RevisionClonePolicy.Preserve);
        if (end.Offset > start.Offset)
            TextElementDeletionPlanner.Apply(remaining, new(start.Offset, end.Offset - start.Offset), trackChanges: false, author: string.Empty, dateXml: null);

        if (remaining.PlainText.Length == 0 && remaining.Formatting.ListKind != ListKind.None)
        {
            var formatting = remaining.Formatting;
            remaining.Formatting = formatting.ListLevel == 0
                ? formatting with { ListKind = ListKind.None, ListLevel = 0 }
                : formatting with { ListLevel = formatting.ListLevel - 1 };
            ExecuteTextEditWithCaretHistory(new ReplaceBlocksCommand(start.Block, 1, [remaining]), () =>
            {
                _caret = new DocPosition(start.Block, 0);
                _selectionAnchor = _caret;
            });
            return true;
        }

        var split = start.Offset;
        var first = DocumentModelCloner.CloneParagraphTextRange(remaining, 0, split, RevisionClonePolicy.Preserve);
        var second = DocumentModelCloner.CloneParagraphTextRange(remaining, split, remaining.PlainText.Length,
            RevisionClonePolicy.Preserve);
        DistributeSplitBookmarkBoundaries(remaining, first, second, split);
        // The existing paragraph mark (including its section break) belongs to the final half.
        first.SectionBreak = null;
        first.MarkRevision = RevisionKind.None;
        first.MarkRevisionAuthor = null;
        first.MarkRevisionDateXml = null;
        second.BookmarkNames.Clear();
        if (source.StyleId is { } styleId && _doc.Styles.TryGetValue(styleId, out var style)
            && style.NextStyleId is { } nextStyle && _doc.Styles.ContainsKey(nextStyle))
            second.StyleId = nextStyle;
        ExecuteTextEditWithCaretHistory(new ReplaceBlocksCommand(start.Block, 1, [first, second]), () =>
        {
            _caret = new DocPosition(start.Block + 1, 0);
            _selectionAnchor = _caret;
        });
        return true;
    }

    private static void DistributeSplitBookmarkBoundaries(Paragraph source, Paragraph first, Paragraph second, int split)
    {
        var offsets = new int[source.Runs.Count + 1];
        for (var index = 0; index < source.Runs.Count; index++)
            offsets[index + 1] = offsets[index] + source.Runs[index].Text.Length;
        var positions = source.BookmarkBoundaries.Select(boundary =>
            (Boundary: boundary, Offset: offsets[Math.Clamp(boundary.RunIndex, 0, source.Runs.Count)])).ToArray();
        // A bookmark entirely removed by the selection remains a collapsed pair, in order,
        // on the second paragraph rather than placing its End before its Start.
        var collapsedPairs = positions.GroupBy(item => item.Boundary.PairKey)
            .Where(group => group.Any(item => item.Boundary.Kind == BookmarkBoundaryKind.Start)
                && group.Any(item => item.Boundary.Kind == BookmarkBoundaryKind.End)
                && group.All(item => item.Offset == split))
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var firstKeys = positions.Where(item => item.Offset < split
                || item.Offset == split && item.Boundary.Kind == BookmarkBoundaryKind.End
                    && !collapsedPairs.Contains(item.Boundary.PairKey))
            .Select(item => (item.Boundary.PairKey, item.Boundary.Kind)).ToHashSet();
        first.BookmarkBoundaries.RemoveAll(boundary => !firstKeys.Contains((boundary.PairKey, boundary.Kind)));
        second.BookmarkBoundaries.RemoveAll(boundary => firstKeys.Contains((boundary.PairKey, boundary.Kind)));
    }

    private bool TryMergePreservedBodyBoundary(bool backward)
    {
        if (IsEditingLocked || TrackChangesEnabled || _cellCaret is not null || _hfCaret is not null
            || _shapeCaret is not null || _selectedFloating is not null || _selectedInlineObject is not null
            || SelectedCellRange is not null || NormalizedSelection() is not null)
            return false;
        var earlierIndex = backward ? _caret.Block - 1 : _caret.Block;
        var laterIndex = earlierIndex + 1;
        if (earlierIndex < 0 || laterIndex >= _doc.Blocks.Count
            || _doc.Blocks[earlierIndex] is not Paragraph earlier || _doc.Blocks[laterIndex] is not Paragraph later
            || _caret.Offset != (backward ? 0 : earlier.PlainText.Length)
            || !NeedsPreservedBodyMerge(earlier) && !NeedsPreservedBodyMerge(later)
            || !SupportsPreservedBodyMerge(earlier) || !SupportsPreservedBodyMerge(later)
            || !ReferenceEquals(earlier.BlockCustomXml, later.BlockCustomXml))
            return false;

        var merged = DocumentModelCloner.CloneParagraph(earlier, RevisionClonePolicy.Preserve);
        var tail = DocumentModelCloner.CloneParagraph(later, RevisionClonePolicy.Preserve);
        var runShift = merged.Runs.Count;
        merged.Runs.AddRange(tail.Runs);
        foreach (var name in tail.BookmarkNames)
            if (!merged.BookmarkNames.Contains(name, StringComparer.Ordinal)) merged.BookmarkNames.Add(name);
        merged.BookmarkBoundaries.AddRange(tail.BookmarkBoundaries.Select(boundary => boundary with
        {
            RunIndex = runShift + Math.Clamp(boundary.RunIndex, 0, tail.Runs.Count),
        }));
        merged.SectionBreak = tail.SectionBreak;
        merged.MarkFormatting = tail.MarkFormatting;
        merged.MarkRevision = tail.MarkRevision;
        merged.MarkRevisionAuthor = tail.MarkRevisionAuthor;
        merged.MarkRevisionDateXml = tail.MarkRevisionDateXml;
        var seam = earlier.PlainText.Length;
        ExecuteTextEditWithCaretHistory(new ReplaceBlocksCommand(earlierIndex, 2, [merged]), () =>
        {
            _caret = new DocPosition(earlierIndex, seam);
            _selectionAnchor = _caret;
        });
        return true;
    }

    private static bool SplitsBookmarkStructureSurrogate(string text, int offset) =>
        offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]);
}
