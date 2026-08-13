namespace FreeW.Core.Model;

/// <summary>Address of a paragraph inside a preserved header/footer layout table.</summary>
public readonly record struct HeaderFooterTableParagraphAddress(
    int RowIndex,
    int CellIndex,
    int CellParagraphIndex);

/// <summary>
/// Maps the compatibility-flat <see cref="HeaderFooter.Paragraphs"/> list back to authored table cells.
/// Both projections intentionally contain the same paragraph instances, and mutations must preserve that
/// invariant regardless of which renderer initiated them.
/// </summary>
public static class HeaderFooterTableParagraphMap
{
    public static bool TryResolveAddress(
        HeaderFooter story,
        int paragraphIndex,
        out HeaderFooterTableParagraphAddress address)
    {
        ArgumentNullException.ThrowIfNull(story);
        address = default;
        if (story.Table is null
            || paragraphIndex < 0
            || paragraphIndex >= story.Paragraphs.Count)
        {
            return false;
        }

        var paragraph = story.Paragraphs[paragraphIndex];
        for (var rowIndex = 0; rowIndex < story.Table.Rows.Count; rowIndex++)
        {
            var row = story.Table.Rows[rowIndex];
            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                for (var cellParagraphIndex = 0; cellParagraphIndex < cell.Paragraphs.Count; cellParagraphIndex++)
                {
                    if (!ReferenceEquals(cell.Paragraphs[cellParagraphIndex], paragraph))
                        continue;

                    address = new HeaderFooterTableParagraphAddress(
                        rowIndex,
                        cellIndex,
                        cellParagraphIndex);
                    return true;
                }
            }
        }

        return false;
    }

    public static int ResolveParagraphIndex(
        HeaderFooter story,
        HeaderFooterTableParagraphAddress address)
    {
        ArgumentNullException.ThrowIfNull(story);
        if (story.Table is null
            || address.RowIndex < 0
            || address.RowIndex >= story.Table.Rows.Count)
        {
            return -1;
        }

        var row = story.Table.Rows[address.RowIndex];
        if (address.CellIndex < 0 || address.CellIndex >= row.Cells.Count)
            return -1;

        var cell = row.Cells[address.CellIndex];
        if (address.CellParagraphIndex < 0 || address.CellParagraphIndex >= cell.Paragraphs.Count)
            return -1;

        var paragraph = cell.Paragraphs[address.CellParagraphIndex];
        for (var paragraphIndex = 0; paragraphIndex < story.Paragraphs.Count; paragraphIndex++)
        {
            if (ReferenceEquals(story.Paragraphs[paragraphIndex], paragraph))
                return paragraphIndex;
        }

        return -1;
    }

    /// <summary>
    /// Returns true when a flat paragraph splice is either outside a preserved table or wholly contained
    /// in one cell. A splice must never merge or remove authored cells.
    /// </summary>
    public static bool CanSplice(HeaderFooter story, int firstParagraphIndex, int removeCount)
    {
        ArgumentNullException.ThrowIfNull(story);
        if (story.Table is null)
            return true;
        if (removeCount <= 0
            || firstParagraphIndex < 0
            || firstParagraphIndex + removeCount > story.Paragraphs.Count
            || !TryResolveAddress(story, firstParagraphIndex, out var first))
        {
            return false;
        }

        for (var index = firstParagraphIndex + 1; index < firstParagraphIndex + removeCount; index++)
        {
            if (!TryResolveAddress(story, index, out var current)
                || current.RowIndex != first.RowIndex
                || current.CellIndex != first.CellIndex
                || current.CellParagraphIndex != first.CellParagraphIndex + index - firstParagraphIndex)
            {
                return false;
            }
        }

        return true;
    }

    public static bool AreInSameCell(HeaderFooter story, int firstParagraphIndex, int secondParagraphIndex) =>
        TryResolveAddress(story, firstParagraphIndex, out var first)
        && TryResolveAddress(story, secondParagraphIndex, out var second)
        && first.RowIndex == second.RowIndex
        && first.CellIndex == second.CellIndex;
}
