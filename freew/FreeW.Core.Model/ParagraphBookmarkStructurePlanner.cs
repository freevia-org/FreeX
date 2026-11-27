namespace FreeW.Core.Model;

/// <summary>Preserves bookmark ownership when paragraph structure changes after run cloning.</summary>
internal static class ParagraphBookmarkStructurePlanner
{
    public static void DistributeAfterSplit(Paragraph source, Paragraph first, Paragraph second, int split)
    {
        var positions = BookmarkBoundaryMapper.Capture(source);
        // Match body paragraph editing: starts at the cut go right, ends go left. A collapsed
        // pair stays together on the right, in source order, rather than ending before it starts.
        var collapsedPairs = positions.GroupBy(position => position.Boundary.PairKey, StringComparer.Ordinal)
            .Where(group => group.Any(position => position.Boundary.Kind == BookmarkBoundaryKind.Start)
                && group.Any(position => position.Boundary.Kind == BookmarkBoundaryKind.End)
                && group.All(position => position.TextOffset == split))
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var firstKeys = positions.Where(position => position.TextOffset < split
                || position.TextOffset == split && position.Boundary.Kind == BookmarkBoundaryKind.End
                    && !collapsedPairs.Contains(position.Boundary.PairKey))
            .Select(position => (position.Boundary.PairKey, position.Boundary.Kind)).ToHashSet();
        // Range cloning already remaps run indexes, including zero-width run ordinals. Only
        // partition those mapped boundaries here; clamping a marker into both halves invents a range.
        first.BookmarkBoundaries.RemoveAll(boundary => !firstKeys.Contains((boundary.PairKey, boundary.Kind)));
        second.BookmarkBoundaries.RemoveAll(boundary => firstKeys.Contains((boundary.PairKey, boundary.Kind)));
        // Legacy whole-paragraph names stay with the first paragraph, as in body editing.
        second.BookmarkNames.Clear();
    }

    public static void AppendAfterMerge(Paragraph merged, Paragraph later, int runShift)
    {
        foreach (var name in later.BookmarkNames)
            if (!merged.BookmarkNames.Contains(name, StringComparer.Ordinal))
                merged.BookmarkNames.Add(name);
        merged.BookmarkBoundaries.AddRange(later.BookmarkBoundaries.Select(boundary => boundary with
        {
            RunIndex = runShift + Math.Clamp(boundary.RunIndex, 0, later.Runs.Count),
        }));
    }
}
