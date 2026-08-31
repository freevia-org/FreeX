namespace FreeP.Core.Model;

/// <summary>
/// Finds every hyperlink hanging off a set of shapes, and orphans the internal slide jumps a
/// presentation cannot resolve.
/// <para>
/// Two situations need the same answer: deleting a slide has to orphan the links that named it,
/// and pasting shapes in from another presentation has to orphan the links whose target belongs
/// to the source deck. Both walked their own copy of the shape tree before this helper existed,
/// which is how a link nested somewhere neither walk reached -- a run inside an inline table --
/// stayed pointing at a slide that was gone.
/// </para>
/// </summary>
public static class SlideHyperlinkTraversal
{
    /// <summary>
    /// Every hyperlink reachable from <paramref name="shapes"/> and their descendants: the
    /// shape-level click action, each text run, each table-cell run, and each run inside an
    /// inline table, at any nesting depth.
    /// </summary>
    public static IEnumerable<Hyperlink> EnumerateHyperlinks(IEnumerable<SlideShape> shapes)
    {
        ArgumentNullException.ThrowIfNull(shapes);

        foreach (var shape in SlideShapeTraversal.EnumerateDepthFirst(shapes))
        {
            if (shape.Hyperlink is { } shapeLink)
                yield return shapeLink;

            foreach (var link in EnumerateBodyHyperlinks(shape.TextBody))
                yield return link;

            if (shape.Table is null)
                continue;

            foreach (var link in EnumerateTableHyperlinks(shape.Table))
                yield return link;
        }
    }

    /// <summary>
    /// Clears every internal slide-jump target that <paramref name="knownSlideIds"/> does not
    /// name, leaving <see cref="Hyperlink.Url"/> and <see cref="Hyperlink.Tooltip"/> intact --
    /// the same orphaning a slide deletion applies to the links that pointed at the removed
    /// slide. Returns how many targets were cleared.
    /// </summary>
    public static int OrphanUnresolvableSlideJumps(
        IEnumerable<SlideShape> shapes,
        IReadOnlyCollection<string> knownSlideIds)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(knownSlideIds);

        var known = knownSlideIds as ISet<string>
            ?? new HashSet<string>(knownSlideIds, StringComparer.Ordinal);
        int cleared = 0;
        foreach (var hyperlink in EnumerateHyperlinks(shapes))
        {
            if (hyperlink.TargetSlideId is not { } targetSlideId || known.Contains(targetSlideId))
                continue;

            hyperlink.TargetSlideId = null;
            cleared++;
        }

        return cleared;
    }

    private static IEnumerable<Hyperlink> EnumerateTableHyperlinks(TableShape table)
    {
        foreach (var cell in table.Rows.SelectMany(row => row.Cells))
        foreach (var link in EnumerateBodyHyperlinks(cell.TextBody))
            yield return link;
    }

    private static IEnumerable<Hyperlink> EnumerateBodyHyperlinks(TextBody? textBody)
    {
        if (textBody is null)
            yield break;

        foreach (var run in textBody.Paragraphs.SelectMany(paragraph => paragraph.Runs))
        {
            if (run.Hyperlink is { } runLink)
                yield return runLink;

            // An inline table's cells hold ordinary text bodies, whose runs can hold their own
            // inline tables -- so this recurses rather than stopping one level down.
            if (run.InlineTable is { } inlineTable)
            {
                foreach (var link in EnumerateTableHyperlinks(inlineTable.Table))
                    yield return link;
            }
        }
    }
}
