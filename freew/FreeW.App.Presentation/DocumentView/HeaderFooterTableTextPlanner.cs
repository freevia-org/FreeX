using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

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
/// Plans table-preserving header/footer text deletion. Core.Model owns paragraph-to-cell addressing;
/// this Presentation layer composes those addresses with shared text-range editing semantics.
/// </summary>
public static class HeaderFooterTableTextPlanner
{
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
            || !HeaderFooterTableParagraphMap.TryResolveAddress(
                story,
                normalized.Value.Start.ParagraphIndex,
                out var startAddress)
            || !HeaderFooterTableParagraphMap.TryResolveAddress(
                story,
                normalized.Value.End.ParagraphIndex,
                out var endAddress))
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

            var firstFlatIndex = HeaderFooterTableParagraphMap.ResolveParagraphIndex(
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
