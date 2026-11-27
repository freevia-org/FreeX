namespace FreeW.Core.Model;

public enum StoryParagraphAtomicEditPolicy
{
    RejectPartial,
    ExpandSelection,
}

/// <summary>A detached same-paragraph edit. Apply only inside the caller's snapshot command.</summary>
public sealed record StoryParagraphRunEditPlan(
    IReadOnlyList<Run> Runs,
    IReadOnlyList<BookmarkBoundary> BookmarkBoundaries,
    int StartOffset,
    int EndOffset,
    int CaretOffset)
{
    public void ApplyTo(Paragraph paragraph)
    {
        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(Runs);
        paragraph.BookmarkBoundaries.Clear();
        paragraph.BookmarkBoundaries.AddRange(BookmarkBoundaries);
    }
}

/// <summary>
/// Plans source-preserving story edits without changing the live paragraph or any live run. Text
/// offsets alone cannot identify boundaries around empty runs, so their source order is mapped before
/// coalescing and then restored with the shared bookmark mapper's zero-width ordinals.
/// </summary>
public static class StoryParagraphRunEditPlanner
{
    public static StoryParagraphRunEditPlan? PlanReplace(
        Paragraph source,
        int startOffset,
        int endOffset,
        IReadOnlyList<Run> insertedRuns,
        StoryParagraphAtomicEditPolicy atomicPolicy = StoryParagraphAtomicEditPolicy.RejectPartial,
        ContentControl? previousControl = null,
        ContentControl? replacementControl = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(insertedRuns);
        var length = source.PlainText.Length;
        if (startOffset < 0 || endOffset < startOffset || endOffset > length
            || startOffset == endOffset && insertedRuns.Count == 0)
            return null;

        if (startOffset == endOffset)
        {
            var emptyOffset = 0;
            for (var index = 0; index < source.Runs.Count; index++)
            {
                var run = source.Runs[index];
                if (emptyOffset == startOffset && run.Text.Length == 0 && run.Control is not null
                    && insertedRuns.Any(item => ReferenceEquals(item.Control, run.Control)
                        || previousControl is not null && ReferenceEquals(run.Control, previousControl)
                            && ReferenceEquals(item.Control, replacementControl)))
                    return PlanReplaceRuns(source, index, 1, insertedRuns, previousControl, replacementControl);
                emptyOffset += run.Text.Length;
            }
        }

        var start = startOffset;
        var end = endOffset;
        var offset = 0;
        foreach (var run in source.Runs)
        {
            var next = offset + run.Text.Length;
            if (IsAtomic(run) && (start > offset && start < next || end > offset && end < next))
            {
                if (atomicPolicy != StoryParagraphAtomicEditPolicy.ExpandSelection || startOffset == endOffset)
                    return null;
                start = Math.Min(start, offset);
                end = Math.Max(end, next);
            }
            offset = next;
        }

        var runs = new List<Run>();
        var boundaries = new int[source.Runs.Count + 1];
        var inserted = false;
        var insertionLength = insertedRuns.Sum(run => run.Text.Length);
        var position = 0;
        for (var index = 0; index < source.Runs.Count; index++)
        {
            var run = source.Runs[index];
            var runEnd = position + run.Text.Length;
            // A collapsed insertion precedes the original empty runs at its cut. A replacement
            // leaves empty anchors at its leading boundary in place, before the new text.
            if (!inserted && start == end && position >= start)
                Insert();
            boundaries[index] = runs.Count;
            if (!inserted && start < end && run.Text.Length > 0 && position >= start)
                Insert();

            if (run.Text.Length == 0)
            {
                runs.Add(Copy(run, run.Text));
            }
            else if (start == end && position < start && start < runEnd)
            {
                runs.Add(Copy(run, run.Text[..(start - position)]));
                Insert();
                runs.Add(Copy(run, run.Text[(start - position)..]));
            }
            else if (runEnd <= start || position >= end)
            {
                runs.Add(Copy(run, run.Text));
            }
            else
            {
                var head = Math.Max(0, start - position);
                var tail = Math.Min(run.Text.Length, end - position);
                if (head > 0)
                    runs.Add(Copy(run, run.Text[..head]));
                if (!inserted)
                    Insert();
                if (tail < run.Text.Length)
                    runs.Add(Copy(run, run.Text[tail..]));
                else if (head == 0 && run.Control is not null
                    && !insertedRuns.Any(item => ReferenceEquals(item.Control, run.Control)
                        || previousControl is not null && ReferenceEquals(run.Control, previousControl)
                            && ReferenceEquals(item.Control, replacementControl)))
                    runs.Add(Copy(run, string.Empty));
            }
            position = runEnd;
        }
        if (!inserted)
            Insert();
        boundaries[^1] = runs.Count;
        return Finish(source, runs, boundaries, start, end, start + insertionLength,
            previousControl, replacementControl);

        void Insert()
        {
            foreach (var item in insertedRuns)
                runs.Add(Copy(item, item.Text));
            inserted = true;
        }

        Run Copy(Run item, string text) => Clone(item, text, previousControl, replacementControl);
    }

    /// <summary>Replaces explicit whole source runs, including a field whose cached text is empty.</summary>
    public static StoryParagraphRunEditPlan? PlanReplaceRuns(
        Paragraph source, int firstRun, int removeCount, IReadOnlyList<Run> insertedRuns,
        ContentControl? previousControl = null, ContentControl? replacementControl = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(insertedRuns);
        if (firstRun < 0 || removeCount <= 0 || firstRun > source.Runs.Count - removeCount)
            return null;
        var runs = new List<Run>();
        var boundaries = new int[source.Runs.Count + 1];
        var start = source.Runs.Take(firstRun).Sum(run => run.Text.Length);
        var end = start + source.Runs.Skip(firstRun).Take(removeCount).Sum(run => run.Text.Length);
        for (var index = 0; index <= source.Runs.Count; index++)
        {
            boundaries[index] = runs.Count;
            if (index == firstRun)
                runs.AddRange(insertedRuns.Select(run => RevisionEditPlanner.CloneRunWithText(run, run.Text)));
            if (index < source.Runs.Count && (index < firstRun || index >= firstRun + removeCount))
                runs.Add(RevisionEditPlanner.CloneRunWithText(source.Runs[index], source.Runs[index].Text));
        }
        return Finish(source, runs, boundaries, start, end,
            start + insertedRuns.Sum(run => run.Text.Length), previousControl, replacementControl);
    }

    /// <summary>Only metadata appropriate to newly typed text is inherited from the adjacent literal.</summary>
    public static Run TypingRun(Paragraph source, int offset, string text)
    {
        ArgumentNullException.ThrowIfNull(source);
        offset = Math.Clamp(offset, 0, source.PlainText.Length);
        var emptyOffset = 0;
        Run? emptyControl = null;
        foreach (var run in source.Runs)
        {
            if (emptyOffset == offset && run.Text.Length == 0 && run.Control is not null && !IsAtomic(run))
            {
                emptyControl = run;
                break;
            }
            emptyOffset += run.Text.Length;
        }
        var candidates = source.Runs.Where(run => run.Text.Length > 0 || IsAtomic(run)).ToArray();
        var position = 0;
        var index = candidates.Length;
        for (var current = 0; current < candidates.Length; current++)
        {
            if (offset <= position) { index = current; break; }
            position += candidates[current].Text.Length;
            if (offset < position) { index = current + 1; break; }
        }
        var previous = index > 0 ? candidates[index - 1] : null;
        var next = index < candidates.Length ? candidates[index] : null;
        var prototype = emptyControl ?? (previous is not null && !IsAtomic(previous) ? previous
            : next is not null && !IsAtomic(next) ? next : null);
        // Only character formatting has a remote fallback. A field between the caret and a
        // literal prevents inheriting that literal's control, hyperlink or character-style identity.
        var formatting = prototype?.Formatting
            ?? candidates.FirstOrDefault(run => !IsAtomic(run))?.Formatting ?? RunFormatting.Default;
        return new Run(text, formatting)
        {
            StyleId = prototype?.StyleId,
            HyperlinkUrl = prototype?.HyperlinkUrl,
            HyperlinkAnchor = prototype?.HyperlinkAnchor,
            HyperlinkTooltip = prototype?.HyperlinkTooltip,
            Control = prototype?.Control,
        };
    }

    public static bool IsAtomic(Run run) =>
        run.FieldKind != RunFieldKind.None || run.ComplexField is not null || run.TableFormula is not null
        || run.CrossReference is not null || run.Citation is not null || run.Image is not null
        || run.Equation is not null || run.Shape is not null || run.WordArt is not null || run.Chart is not null
        || run.EmbeddedObject is not null || run.SmartArt is not null || run.PreservedDrawing is not null
        || run.PreservedDrawingPreview is not null || run.DrawingGroup is not null || run.Ruby is not null
        || run.SubDocument is not null || run.FootnoteId is not null || run.EndnoteId is not null
        || run.IsCommentReference || run.IsPageBreak || run.IsColumnBreak;

    private static Run Clone(Run source, string text, ContentControl? previous, ContentControl? replacement)
    {
        var clone = RevisionEditPlanner.CloneRunWithText(source, text);
        if (previous is not null && ReferenceEquals(clone.Control, previous))
            clone.Control = replacement;
        return clone;
    }

    private static StoryParagraphRunEditPlan Finish(
        Paragraph source, List<Run> runs, int[] boundaryIndexes, int start, int end, int caret,
        ContentControl? previousControl, ContentControl? replacementControl)
    {
        var offsets = new int[runs.Count + 1];
        for (var index = 0; index < runs.Count; index++)
            offsets[index + 1] = offsets[index] + runs[index].Text.Length;
        var positions = BookmarkBoundaryMapper.Capture(source).Select(position =>
        {
            var mapped = boundaryIndexes[Math.Clamp(position.Boundary.RunIndex, 0, source.Runs.Count)];
            var ordinal = 0;
            for (var index = mapped - 1; index >= 0 && runs[index].Text.Length == 0; index--)
                ordinal++;
            var boundary = position.Boundary;
            if (previousControl is not null && ReferenceEquals(boundary.OwnerControl, previousControl))
                boundary = boundary with { OwnerControl = replacementControl };
            return new BookmarkBoundaryMapper.Position(boundary, offsets[mapped], ordinal);
        }).ToArray();
        for (var index = 0; index + 1 < runs.Count; index++)
        {
            if (!CanCoalesce(runs[index], runs[index + 1]))
                continue;
            runs[index] = RevisionEditPlanner.CloneRunWithText(runs[index], runs[index].Text + runs[index + 1].Text);
            runs.RemoveAt(index + 1);
            index--;
        }
        var detached = new Paragraph();
        detached.Runs.AddRange(runs);
        BookmarkBoundaryMapper.Restore(detached, positions);
        return new(detached.Runs.ToArray(), detached.BookmarkBoundaries.ToArray(), start, end, caret);
    }

    private static bool CanCoalesce(Run left, Run right) =>
        left.Text.Length > 0 && right.Text.Length > 0 && !IsAtomic(left) && !IsAtomic(right)
        && left.Formatting == right.Formatting && left.StyleId == right.StyleId
        && left.HyperlinkUrl == right.HyperlinkUrl && left.HyperlinkAnchor == right.HyperlinkAnchor
        && left.HyperlinkTooltip == right.HyperlinkTooltip && ReferenceEquals(left.Control, right.Control)
        && left.CommentId == right.CommentId && left.Revision == right.Revision
        && left.RevisionAuthor == right.RevisionAuthor && left.RevisionDateXml == right.RevisionDateXml
        && left.MoveRevisionId == right.MoveRevisionId && left.FormatRevision == right.FormatRevision
        && left.NestedRevision == right.NestedRevision && left.FieldLocked == right.FieldLocked
        && left.FieldCodeVisible == right.FieldCodeVisible;
}
