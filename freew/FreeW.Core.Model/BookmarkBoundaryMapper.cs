namespace FreeW.Core.Model;

/// <summary>Maps run-relative bookmark boundaries through operations that rebuild paragraph runs.</summary>
internal static class BookmarkBoundaryMapper
{
    internal sealed record Position(BookmarkBoundary Boundary, int TextOffset, int ZeroWidthOrdinal);

    public static IReadOnlyList<Position> Capture(Paragraph paragraph)
    {
        if (paragraph.BookmarkBoundaries.Count == 0)
            return [];

        var offsets = new int[paragraph.Runs.Count + 1];
        for (var index = 0; index < paragraph.Runs.Count; index++)
            offsets[index + 1] = offsets[index] + paragraph.Runs[index].Text.Length;

        return paragraph.BookmarkBoundaries.Select(boundary =>
        {
            var runIndex = Math.Clamp(boundary.RunIndex, 0, paragraph.Runs.Count);
            var zeroWidthOrdinal = 0;
            for (var index = runIndex - 1; index >= 0 && paragraph.Runs[index].Text.Length == 0; index--)
                zeroWidthOrdinal++;
            return new Position(boundary, offsets[runIndex], zeroWidthOrdinal);
        }).ToList();
    }

    public static void Restore(
        Paragraph paragraph,
        IReadOnlyList<Position> positions,
        Func<int, int>? mapOffset = null,
        Func<Run, bool>? advancesOffset = null)
    {
        if (positions.Count == 0)
            return;

        advancesOffset ??= static _ => true;
        paragraph.BookmarkBoundaries.Clear();
        foreach (var position in positions)
        {
            var offset = Math.Max(0, mapOffset?.Invoke(position.TextOffset) ?? position.TextOffset);
            var runIndex = EnsureRunBoundary(paragraph, offset, advancesOffset);
            var remainingZeroWidthRuns = position.ZeroWidthOrdinal;
            while (remainingZeroWidthRuns > 0
                && runIndex < paragraph.Runs.Count
                && paragraph.Runs[runIndex].Text.Length == 0)
            {
                runIndex++;
                remainingZeroWidthRuns--;
            }
            paragraph.BookmarkBoundaries.Add(position.Boundary with { RunIndex = runIndex });
        }
    }

    public static void CopyMapped(
        Paragraph source,
        Paragraph target,
        Func<Run, bool>? targetAdvancesOffset = null)
    {
        Restore(target, Capture(source), advancesOffset: targetAdvancesOffset);
    }

    internal static int EnsureRunBoundaryAtTextOffset(Paragraph paragraph, int textOffset) =>
        EnsureRunBoundary(paragraph, textOffset, static _ => true);

    private static int EnsureRunBoundary(Paragraph paragraph, int targetOffset, Func<Run, bool> advancesOffset)
    {
        var offset = 0;
        for (var index = 0; index < paragraph.Runs.Count; index++)
        {
            var run = paragraph.Runs[index];
            if (!advancesOffset(run))
                continue;

            if (targetOffset <= offset)
                return index;

            var nextOffset = offset + run.Text.Length;
            if (targetOffset < nextOffset)
            {
                var localOffset = targetOffset - offset;
                var head = RevisionEditPlanner.CloneRunWithText(run, run.Text[..localOffset]);
                var tail = RevisionEditPlanner.CloneRunWithText(run, run.Text[localOffset..]);
                paragraph.Runs[index] = head;
                paragraph.Runs.Insert(index + 1, tail);
                return index + 1;
            }

            offset = nextOffset;
            if (targetOffset == offset)
                return index + 1;
        }

        return paragraph.Runs.Count;
    }
}
