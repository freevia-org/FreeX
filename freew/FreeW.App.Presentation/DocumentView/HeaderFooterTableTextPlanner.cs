using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>Renderer-neutral address of a paragraph inside a preserved header/footer layout table.</summary>
public readonly record struct HeaderFooterTableParagraphAddress(
    int RowIndex,
    int CellIndex,
    int CellParagraphIndex);

/// <summary>One cell-local splice in a table-preserving header/footer selection deletion.</summary>
public sealed record HeaderFooterTableCellDeletePlan(
    int FirstParagraphIndex,
    int RemoveCount,
    IReadOnlyList<Paragraph> ReplacementParagraphs);

/// <summary>A table-preserving deletion, applied from the last cell back to the first.</summary>
public sealed record HeaderFooterTableDeletePlan(
    HeaderFooterTextPosition Caret,
    IReadOnlyList<HeaderFooterTableCellDeletePlan> CellPlans);

/// <summary>
/// Maps the compatibility-flat <see cref="HeaderFooter.Paragraphs"/> list back to the authored table cells.
/// The DOCX reader deliberately stores the same paragraph instances in both projections; keeping the mapping
/// here lets renderer commands preserve that invariant when a paragraph is split or joined.
/// </summary>
public static class HeaderFooterTableTextPlanner
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

    /// <summary>
    /// Decomposes a selection into independent cell-local splices. Text is removed across every selected
    /// cell, but paragraph joins never cross a cell boundary and every cell retains a paragraph.
    /// </summary>
    public static HeaderFooterTableDeletePlan? PlanDelete(
        HeaderFooter story,
        HeaderFooterTextRange selection)
    {
        ArgumentNullException.ThrowIfNull(story);
        if (story.Table is null)
            return null;

        var normalized = HeaderFooterTextSelectionPlanner.Normalize(story, selection.End, selection.Start);
        if (normalized is null
            || !TryResolveAddress(story, normalized.Value.Start.ParagraphIndex, out var startAddress)
            || !TryResolveAddress(story, normalized.Value.End.ParagraphIndex, out var endAddress))
        {
            return null;
        }

        selection = normalized.Value;
        var plans = new List<HeaderFooterTableCellDeletePlan>();
        HeaderFooterTextPosition? caret = null;
        foreach (var entry in EnumerateCells(story))
        {
            if (CompareCell(entry.Address, startAddress) < 0
                || CompareCell(entry.Address, endAddress) > 0
                || entry.Cell.Paragraphs.Count == 0)
            {
                continue;
            }

            var localStart = SameCell(entry.Address, startAddress)
                ? new HeaderFooterTextPosition(startAddress.CellParagraphIndex, selection.Start.Offset)
                : new HeaderFooterTextPosition(0, 0);
            var lastLocalParagraph = entry.Cell.Paragraphs.Count - 1;
            var localEnd = SameCell(entry.Address, endAddress)
                ? new HeaderFooterTextPosition(endAddress.CellParagraphIndex, selection.End.Offset)
                : new HeaderFooterTextPosition(
                    lastLocalParagraph,
                    entry.Cell.Paragraphs[lastLocalParagraph].Runs.Sum(run => run.Text.Length));

            var cellStory = new HeaderFooter();
            cellStory.Paragraphs.AddRange(entry.Cell.Paragraphs);
            var cellPlan = HeaderFooterTextEditPlanner.PlanDelete(
                cellStory,
                new HeaderFooterTextRange(localStart, localEnd));
            if (cellPlan is null)
                continue;

            var firstFlatIndex = ResolveParagraphIndex(
                story,
                entry.Address with { CellParagraphIndex = cellPlan.FirstParagraphIndex });
            if (firstFlatIndex < 0)
                return null;

            plans.Add(new HeaderFooterTableCellDeletePlan(
                firstFlatIndex,
                cellPlan.RemoveCount,
                cellPlan.ReplacementParagraphs));
            caret ??= new HeaderFooterTextPosition(
                firstFlatIndex + cellPlan.Caret.ParagraphIndex - cellPlan.FirstParagraphIndex,
                cellPlan.Caret.Offset);
        }

        return plans.Count > 0 && caret is not null
            ? new HeaderFooterTableDeletePlan(caret.Value, plans)
            : null;
    }

    private static IEnumerable<(HeaderFooterTableParagraphAddress Address, TableCell Cell)> EnumerateCells(
        HeaderFooter story)
    {
        if (story.Table is null)
            yield break;

        for (var rowIndex = 0; rowIndex < story.Table.Rows.Count; rowIndex++)
        {
            var row = story.Table.Rows[rowIndex];
            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                yield return (
                    new HeaderFooterTableParagraphAddress(rowIndex, cellIndex, 0),
                    row.Cells[cellIndex]);
            }
        }
    }

    private static bool SameCell(
        HeaderFooterTableParagraphAddress left,
        HeaderFooterTableParagraphAddress right) =>
        left.RowIndex == right.RowIndex && left.CellIndex == right.CellIndex;

    private static int CompareCell(
        HeaderFooterTableParagraphAddress left,
        HeaderFooterTableParagraphAddress right)
    {
        var rowComparison = left.RowIndex.CompareTo(right.RowIndex);
        return rowComparison != 0 ? rowComparison : left.CellIndex.CompareTo(right.CellIndex);
    }
}
