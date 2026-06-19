namespace FreeW.Core.Model;

/// <summary>
/// Pure helper for Word-style "Combine Documents": compare the shared original against two revised
/// copies and return a single document that carries tracked changes from both reviewers.
/// </summary>
public static class DocumentCombine
{
    /// <summary>
    /// Combine revisions from <paramref name="revisedA"/> and <paramref name="revisedB"/> against the
    /// shared <paramref name="original"/>. Reviewer A's comparison forms the main document; reviewer B's
    /// revision-bearing blocks are appended so no second-reviewer changes are silently dropped.
    /// </summary>
    public static TextDocument Combine(
        TextDocument original,
        TextDocument revisedA,
        string authorA,
        TextDocument revisedB,
        string authorB,
        string? dateXml = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(revisedA);
        ArgumentNullException.ThrowIfNull(authorA);
        ArgumentNullException.ThrowIfNull(revisedB);
        ArgumentNullException.ThrowIfNull(authorB);

        var combined = DocumentCompare.Compare(original, revisedA, authorA, dateXml);
        var reviewerB = DocumentCompare.Compare(original, revisedB, authorB, dateXml);

        foreach (var block in DocumentMerge.CloneBlocks(reviewerB).Where(HasRevisions))
            combined.Blocks.Add(block);

        return combined;
    }

    private static bool HasRevisions(Block block) => block switch
    {
        Paragraph paragraph => paragraph.Runs.Any(run => run.Revision != RevisionKind.None),
        Table table => table.Rows
            .SelectMany(row => row.Cells)
            .SelectMany(cell => cell.Paragraphs)
            .SelectMany(paragraph => paragraph.Runs)
            .Any(run => run.Revision != RevisionKind.None),
        _ => false
    };
}
