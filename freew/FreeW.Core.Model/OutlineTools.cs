using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free heading-level shift helpers for the outline tools. Given a paragraph's
/// <see cref="Paragraph.StyleId"/>, <see cref="Promote"/> moves it one step toward the top of the
/// outline and <see cref="Demote"/> one step toward the bottom. Heading levels follow the same
/// classification as <see cref="DocumentOutline"/>: <c>Title</c> is the top (level 0) and
/// <c>HeadingN</c> sits at level N.
/// <para>
/// Mapping (documented and covered by tests):
/// <list type="bullet">
/// <item><b>Promote</b> raises a heading one rank toward the top:
/// <c>Heading3 → Heading2 → Heading1 → Title</c>. <c>Title</c> is already the top, so it stays
/// <c>Title</c>. A non-heading / unrecognised / null style has no heading rank to raise, so it is
/// returned unchanged.</item>
/// <item><b>Demote</b> lowers a heading one rank toward the bottom:
/// <c>Title → Heading1 → Heading2 → … → Heading6</c>, capped at <see cref="MaxHeadingLevel"/>
/// (<c>Heading6</c> stays <c>Heading6</c>). A non-heading / unrecognised / null style becomes
/// <c>Heading1</c> (the natural "make this a heading" gesture).</item>
/// </list>
/// </para>
/// The helpers are deterministic and depend only on the style id string, so they are fully unit
/// testable without any document or UI.
/// </summary>
public static class OutlineTools
{
    private const string HeadingPrefix = "Heading";

    /// <summary>The deepest heading level <see cref="Demote"/> will produce (a <c>Heading6</c> cap).</summary>
    public const int MaxHeadingLevel = 6;

    /// <summary>
    /// Returns the next-higher heading style id (one rank toward the top of the outline). See the
    /// type remarks for the full mapping. A non-heading / null style is returned unchanged.
    /// </summary>
    public static string? Promote(string? styleId)
    {
        if (!DocumentOutline.TryGetLevel(styleId, out var level))
            return styleId; // not a heading: nothing to promote

        // Title (level 0) is already the top of the outline.
        if (level <= 0)
            return StyleForLevel(0);

        // Heading1 promotes to Title; deeper headings step up one level.
        return StyleForLevel(level - 1);
    }

    /// <summary>
    /// Returns the next-lower heading style id (one rank toward the bottom of the outline), capped at
    /// <see cref="MaxHeadingLevel"/>. See the type remarks for the full mapping. A non-heading / null
    /// style becomes <c>Heading1</c>.
    /// </summary>
    public static string? Demote(string? styleId)
    {
        if (!DocumentOutline.TryGetLevel(styleId, out var level))
            return StyleForLevel(1); // not a heading: turn it into a top-level heading

        // Title (level 0) demotes to Heading1; deeper headings step down one level, capped.
        var next = Math.Min(level + 1, MaxHeadingLevel);
        return StyleForLevel(next);
    }

    // Build the style id for an outline level: 0 -> Title, N>0 -> "HeadingN".
    private static string StyleForLevel(int level)
    {
        if (BuiltInStyles.FindByOutlineLevel(Math.Max(0, level)) is { } descriptor)
            return descriptor.Id;

        return HeadingPrefix + level.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The contiguous block span "owned" by the heading at <paramref name="headingIndex"/> in
    /// <paramref name="blocks"/>: the heading paragraph itself plus every following block down to (but
    /// not including) the next heading whose outline level is the same or higher (a smaller-or-equal
    /// level number) — i.e. the heading and its whole subtree, matching how the navigation outline and
    /// collapse nest. Returns the half-open range <c>[Start, End)</c> in document order. When
    /// <paramref name="headingIndex"/> does not point at a heading paragraph, an empty range
    /// (<c>Start == End == headingIndex</c>) is returned so callers can treat it as "nothing to move".
    /// </summary>
    public static (int Start, int End) SubtreeRange(IReadOnlyList<Block> blocks, int headingIndex)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        if (headingIndex < 0 || headingIndex >= blocks.Count
            || blocks[headingIndex] is not Paragraph heading
            || !DocumentOutline.TryGetLevel(heading.StyleId, out var headingLevel))
        {
            return (headingIndex, headingIndex); // not a heading: empty span
        }

        var end = headingIndex + 1;
        while (end < blocks.Count)
        {
            if (blocks[end] is Paragraph p
                && DocumentOutline.TryGetLevel(p.StyleId, out var level)
                && level <= headingLevel)
                break; // next same-or-higher heading: the subtree ends here
            end++;
        }
        return (headingIndex, end);
    }

    /// <summary>
    /// Moves the heading-subtree owned by the heading at <paramref name="headingIndex"/> (see
    /// <see cref="SubtreeRange"/>) one position toward the document start (<paramref name="moveUp"/> =
    /// true) or end (false), and returns the reordered block list. "One position" means swapping the
    /// subtree with the adjacent sibling subtree: moving up re-inserts the span immediately before the
    /// preceding heading-subtree; moving down re-inserts it immediately after the following one. The
    /// relative order of the moved blocks (and of the displaced sibling) is preserved. When there is no
    /// sibling subtree in that direction, or the index is not a heading, the original order is returned
    /// unchanged. Pure: the input list is not mutated; a new list is returned.
    /// </summary>
    public static IReadOnlyList<Block> MoveSubtree(IReadOnlyList<Block> blocks, int headingIndex, bool moveUp)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var (start, end) = SubtreeRange(blocks, headingIndex);
        if (end <= start)
            return blocks; // nothing to move (not a heading)

        if (blocks[start] is not Paragraph heading
            || !DocumentOutline.TryGetLevel(heading.StyleId, out var movingLevel))
            return blocks;

        int target;
        if (moveUp)
        {
            // The sibling subtree directly before us starts at the previous heading of the same-or-higher
            // level; we re-insert our span at that heading's index (i.e. before it).
            if (!TryFindPreviousSiblingStart(blocks, start, movingLevel, out target))
                return blocks; // already first sibling — nothing above to move past
        }
        else
        {
            // The sibling subtree directly after us is [end, nextEnd); we re-insert our span after it,
            // which (once our span is removed) lands at index nextEnd - (end - start) == end position
            // after the sibling. Computed below by rebuilding the list explicitly.
            if (end >= blocks.Count)
                return blocks; // already last sibling — nothing below to move past
            if (blocks[end] is not Paragraph nextHeading
                || !DocumentOutline.TryGetLevel(nextHeading.StyleId, out var nextLevel)
                || nextLevel != movingLevel)
                return blocks;
            var nextEnd = SubtreeRange(blocks, end).End;
            target = nextEnd;
        }

        return Reorder(blocks, start, end, target);
    }

    // Find the start index of the sibling subtree immediately preceding the subtree that starts at
    // <paramref name="start"/>: scan backward for the nearest heading whose level is the same or higher
    // (smaller-or-equal level number) than the moving heading's level. Returns false when none exists
    // (the moving heading is already the first at its level in its enclosing scope).
    private static bool TryFindPreviousSiblingStart(
        IReadOnlyList<Block> blocks,
        int start,
        int movingLevel,
        out int siblingStart)
    {
        siblingStart = -1;
        for (var i = start - 1; i >= 0; i--)
        {
            if (blocks[i] is Paragraph p
                && DocumentOutline.TryGetLevel(p.StyleId, out var lvl))
            {
                if (lvl == movingLevel)
                {
                    siblingStart = i;
                    return true;
                }

                if (lvl < movingLevel)
                    return false;
            }
        }
        return false;
    }

    // Produce a new list with the span [start, end) removed and re-inserted so that, in the final list,
    // it sits at the position the original index <paramref name="target"/> referred to. Targets at or
    // before the span move it earlier; targets at or after the span move it later.
    private static IReadOnlyList<Block> Reorder(IReadOnlyList<Block> blocks, int start, int end, int target)
    {
        var moved = new List<Block>(end - start);
        for (var i = start; i < end; i++)
            moved.Add(blocks[i]);

        var rest = new List<Block>(blocks.Count - moved.Count);
        for (var i = 0; i < blocks.Count; i++)
        {
            if (i < start || i >= end)
                rest.Add(blocks[i]);
        }

        // Translate the original target index into an index in the gap-closed `rest` list.
        var insertAt = target <= start ? target : target - (end - start);
        insertAt = Math.Clamp(insertAt, 0, rest.Count);
        rest.InsertRange(insertAt, moved);
        return rest;
    }
}
