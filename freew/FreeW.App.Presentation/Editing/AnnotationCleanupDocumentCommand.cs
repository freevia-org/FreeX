using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>
/// Couples a model edit with dependent note/comment cleanup in one reversible command. Existing
/// point comments and unrelated unreferenced entries remain intact; lost substantive ranges remove
/// their references across stories. Reference snapshots restore before the wrapped edit's snapshot.
/// </summary>
public sealed class AnnotationCleanupDocumentCommand(
    IDocumentCommand inner,
    IReadOnlyList<Run> affected,
    string label) : IDocumentCommand
{
    private readonly int[] _footnoteCandidates = affected.Select(run => run.FootnoteId).OfType<int>().Distinct().ToArray();
    private readonly int[] _endnoteCandidates = affected.Select(run => run.EndnoteId).OfType<int>().Distinct().ToArray();
    private readonly List<Footnote> _removedFootnotes = [];
    private readonly List<Endnote> _removedEndnotes = [];
    private readonly List<Comment> _removedComments = [];
    private readonly List<ReferenceParagraphSnapshot> _referenceParagraphs = [];
    private sealed record ReferenceParagraphSnapshot(Paragraph Paragraph, Run[] Runs, BookmarkBoundary[] Bookmarks);
    public string Label => label;
    public DocumentCommandMutationKind MutationKind => inner.MutationKind;
    public bool HasEffect(IDocumentCommandContext context) => inner.HasEffect(context);
    public int EstimatedBytes => (int)Math.Min(int.MaxValue, (long)inner.EstimatedBytes + 128
        + EstimateParagraphs(_removedFootnotes.SelectMany(note => note.Content))
        + EstimateParagraphs(_removedEndnotes.SelectMany(note => note.Content))
        + EstimateParagraphs(_removedComments.SelectMany(comment => comment.ThreadInOrder()).SelectMany(comment => comment.Content))
        + _referenceParagraphs.Sum(snapshot => 128L + snapshot.Runs.Sum(run => 128L + run.Text.Length * sizeof(char))));

    private static long EstimateParagraphs(IEnumerable<Paragraph> paragraphs) =>
        paragraphs.Sum(paragraph => 128L + paragraph.Runs.Sum(run => 128L + run.Text.Length * sizeof(char)));

    public void Apply(IDocumentCommandContext context)
    {
        _removedFootnotes.Clear();
        _removedEndnotes.Clear();
        _removedComments.Clear();
        _referenceParagraphs.Clear();
        var document = context.Document;
        var roots = CommentThreadIndex.BuildTopLevelByCommentId(document);
        var rootsWithTextBefore = RealCommentRoots(DisplaySelectionAnchorParagraphs(document), roots);
        inner.Apply(context);
        var remaining = DisplaySelectionAnchorParagraphs(document).SelectMany(paragraph => paragraph.Runs).ToArray();
        foreach (var id in _footnoteCandidates)
            if (!remaining.Any(run => run.FootnoteId == id) && document.Footnotes.Remove(id, out var note))
                _removedFootnotes.Add(note);
        foreach (var id in _endnoteCandidates)
            if (!remaining.Any(run => run.EndnoteId == id) && document.Endnotes.Remove(id, out var note))
                _removedEndnotes.Add(note);
        var paragraphs = DisplaySelectionAnchorParagraphs(document).ToArray();
        rootsWithTextBefore.ExceptWith(RealCommentRoots(paragraphs, roots));
        foreach (var paragraph in paragraphs)
            RemoveReferencesForLostRanges(paragraph, roots, rootsWithTextBefore);
        foreach (var id in rootsWithTextBefore)
            if (document.Comments.Remove(id, out var comment))
                _removedComments.Add(comment);
    }

    private static HashSet<int> RealCommentRoots(IEnumerable<Paragraph> paragraphs, IReadOnlyDictionary<int, Comment> roots) =>
        paragraphs.SelectMany(paragraph => paragraph.Runs).Where(IsRealDisplayCommentAnchor)
            .Select(run => run.CommentId).OfType<int>().Where(roots.ContainsKey).Select(id => roots[id].Id).ToHashSet();

    private void RemoveReferencesForLostRanges(Paragraph paragraph, IReadOnlyDictionary<int, Comment> roots, HashSet<int> lostRoots)
    {
        bool IsLostReference(Run run) => run.IsCommentReference && run.CommentId is { } id
            && roots.TryGetValue(id, out var root) && lostRoots.Contains(root.Id);
        if (!paragraph.Runs.Any(IsLostReference))
            return;
        _referenceParagraphs.Add(new(paragraph, paragraph.Runs.ToArray(), paragraph.BookmarkBoundaries.ToArray()));
        var kept = new List<Run>();
        var boundaryIndexes = new int[paragraph.Runs.Count + 1];
        for (var index = 0; index < paragraph.Runs.Count; index++)
        {
            boundaryIndexes[index] = kept.Count;
            if (!IsLostReference(paragraph.Runs[index]))
                kept.Add(paragraph.Runs[index]);
        }
        boundaryIndexes[^1] = kept.Count;
        var bookmarks = paragraph.BookmarkBoundaries.Select(boundary => boundary with
        {
            RunIndex = boundaryIndexes[Math.Clamp(boundary.RunIndex, 0, paragraph.Runs.Count)]
        }).ToArray();
        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(kept);
        paragraph.BookmarkBoundaries.Clear();
        paragraph.BookmarkBoundaries.AddRange(bookmarks);
    }

    public void Revert(IDocumentCommandContext context)
    {
        // These snapshots describe post-inner-edit reference cleanup. Restore them FIRST: the
        // inner command's pre-edit source paragraph/bookmarks must win when both touch it.
        foreach (var snapshot in _referenceParagraphs)
        {
            snapshot.Paragraph.Runs.Clear();
            snapshot.Paragraph.Runs.AddRange(snapshot.Runs);
            snapshot.Paragraph.BookmarkBoundaries.Clear();
            snapshot.Paragraph.BookmarkBoundaries.AddRange(snapshot.Bookmarks);
        }
        inner.Revert(context);
        foreach (var note in _removedFootnotes) context.Document.Footnotes[note.Id] = note;
        foreach (var note in _removedEndnotes) context.Document.Endnotes[note.Id] = note;
        foreach (var comment in _removedComments) context.Document.Comments[comment.Id] = comment;
    }

    private static IEnumerable<Paragraph> DisplaySelectionAnchorParagraphs(TextDocument document) =>
        TextDocumentStoryTraversal.EnumerateParagraphs(document, TextDocumentStorySubset.All,
            TextDocumentStoryTraversalOptions.IncludeTextBoxes | TextDocumentStoryTraversalOptions.IncludeShapeTextBoxes
            | TextDocumentStoryTraversalOptions.IncludeNestedTables);

    private static bool IsRealDisplayCommentAnchor(Run run) => !run.IsCommentReference
        && (run.Text.Length > 0 || RevisionEditPlanner.RunCarriesNonTextContent(run));

    /// <summary>Whether replacement text remains in an original range that survives the planned deletion.</summary>
    public static bool ReplacementKeepsExistingCommentAnchor(TextDocument document, Paragraph target,
        DocumentDisplaySelectionPlan plan, bool tracked, string author)
    {
        var literalRanges = plan.LiteralRanges.ToDictionary(slice => slice.Span.SourceRunIndex);
        var atomicIndexes = plan.WholeAtomicRuns.Select(span => span.SourceRunIndex).ToHashSet();
        var firstIndex = literalRanges.Keys.Concat(atomicIndexes).Min();
        if (target.Runs[firstIndex].CommentId is not { } commentId)
            return true;
        var roots = CommentThreadIndex.BuildTopLevelByCommentId(document);
        if (!roots.TryGetValue(commentId, out var selectedRoot))
            return true;
        foreach (var paragraph in DisplaySelectionAnchorParagraphs(document))
        {
            for (var index = 0; index < paragraph.Runs.Count; index++)
            {
                var run = paragraph.Runs[index];
                if (!IsRealDisplayCommentAnchor(run) || run.CommentId is not { } id
                    || !roots.TryGetValue(id, out var root) || root.Id != selectedRoot.Id)
                    continue;
                if (!ReferenceEquals(paragraph, target))
                    return true;
                if (atomicIndexes.Contains(index))
                    continue;
                if (!literalRanges.TryGetValue(index, out var slice)
                    || slice.ModelLength < run.Text.Length || run.Control is not null
                    || tracked && !(run.Revision == RevisionKind.Inserted
                        && string.Equals(run.RevisionAuthor, author, StringComparison.Ordinal)))
                    return true;
            }
        }
        // Replacing the complete original range removes its comment, rather than making the newly
        // typed text a fresh anchor. A partial replacement still belongs to the surviving range.
        return false;
    }
}
