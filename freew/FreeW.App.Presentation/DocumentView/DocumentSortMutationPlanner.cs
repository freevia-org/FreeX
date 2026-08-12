using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Toolkit-neutral replacement produced by a document mutation planner. Renderers only resolve their
/// native caret/selection into model coordinates and submit this replacement through their command bus.
/// </summary>
public sealed record DocumentBlockReplacementPlan(
    int StartIndex,
    int RemoveCount,
    IReadOnlyList<Block> Replacement);

/// <summary>
/// Owns the model behavior for Word-style paragraph and table-row sorting. WPF and Avalonia deliberately
/// share this planner so selection semantics, header pinning, mixed-block handling, and table-shell
/// preservation cannot drift between renderer implementations.
/// </summary>
public static class DocumentSortMutationPlanner
{
    public static DocumentBlockReplacementPlan? PlanParagraphSort(
        TextDocument document,
        IReadOnlyList<int> selectedParagraphIndices,
        SortKind kind,
        bool ascending,
        bool caseSensitive,
        bool hasHeaderRow)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedParagraphIndices);
        if (selectedParagraphIndices.Count == 0)
            return null;

        var first = selectedParagraphIndices[0];
        var last = selectedParagraphIndices[^1];
        if (first < 0 || last < first || last >= document.Blocks.Count)
            return null;

        var paragraphs = new List<Paragraph>();
        for (var index = first; index <= last; index++)
        {
            if (document.Blocks[index] is Paragraph paragraph)
                paragraphs.Add(paragraph);
        }
        if (paragraphs.Count < 2)
            return null;

        var sorted = ParagraphSort.Sort(
            paragraphs,
            kind,
            ascending,
            caseSensitive,
            hasHeaderRow);
        var replacement = new List<Block>(last - first + 1);
        var nextSorted = 0;
        for (var index = first; index <= last; index++)
        {
            replacement.Add(document.Blocks[index] is Paragraph
                ? sorted[nextSorted++]
                : document.Blocks[index]);
        }

        return new DocumentBlockReplacementPlan(first, replacement.Count, replacement);
    }

    public static DocumentBlockReplacementPlan? PlanTableRowSort(
        TextDocument document,
        int tableBlockIndex,
        int keyCellIndex,
        SortKind kind,
        bool ascending,
        bool caseSensitive,
        bool hasHeaderRow)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (tableBlockIndex < 0 || tableBlockIndex >= document.Blocks.Count
            || document.Blocks[tableBlockIndex] is not Table table
            || table.Rows.Count < 2)
        {
            return null;
        }

        var sorted = ParagraphSort.SortRows(
            table.Rows,
            Math.Max(0, keyCellIndex),
            kind,
            ascending,
            caseSensitive,
            hasHeaderRow);
        var replacement = TableLayoutOperations.CopyTableWithRows(table, sorted);
        return new DocumentBlockReplacementPlan(tableBlockIndex, 1, [replacement]);
    }
}
