using System.Diagnostics.CodeAnalysis;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

public readonly record struct DocumentNoteMarkerPosition(
    int BlockIndex,
    int ParagraphOffset,
    int? TableRowIndex = null,
    int? TableGridColumnIndex = null,
    int TableParagraphIndex = 0)
{
    public bool IsTableCell => TableRowIndex.HasValue && TableGridColumnIndex.HasValue;

    public static DocumentNoteMarkerPosition Body(int blockIndex, int paragraphOffset) =>
        new(blockIndex, paragraphOffset);

    public static DocumentNoteMarkerPosition TableCell(
        int blockIndex,
        int rowIndex,
        int gridColumnIndex,
        int paragraphIndex,
        int paragraphOffset) =>
        new(blockIndex, paragraphOffset, rowIndex, gridColumnIndex, paragraphIndex);
}

public static class DocumentNoteNavigationPlanner
{
    /// <summary>
    /// Projects note markers into renderer-neutral document positions. Table columns use logical grid
    /// coordinates so merged cells resolve through the same address convention as both editor hosts.
    /// </summary>
    public static IReadOnlyList<DocumentNoteMarkerPosition> FindMarkers(
        TextDocument document,
        bool footnote)
    {
        ArgumentNullException.ThrowIfNull(document);

        var markers = new List<DocumentNoteMarkerPosition>();
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            switch (document.Blocks[blockIndex])
            {
                case Paragraph paragraph:
                    AddParagraphMarkers(
                        paragraph,
                        footnote,
                        offset => DocumentNoteMarkerPosition.Body(blockIndex, offset),
                        markers);
                    break;
                case Table table:
                    for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                    {
                        foreach (var projectedCell in TableGridProjection.ProjectRow(table.Rows[rowIndex]))
                        {
                            for (var paragraphIndex = 0;
                                 paragraphIndex < projectedCell.Cell.Paragraphs.Count;
                                 paragraphIndex++)
                            {
                                var capturedRow = rowIndex;
                                var capturedColumn = projectedCell.StartColumn;
                                var capturedParagraph = paragraphIndex;
                                AddParagraphMarkers(
                                    projectedCell.Cell.Paragraphs[paragraphIndex],
                                    footnote,
                                    offset => DocumentNoteMarkerPosition.TableCell(
                                        blockIndex,
                                        capturedRow,
                                        capturedColumn,
                                        capturedParagraph,
                                        offset),
                                    markers);
                            }
                        }
                    }

                    break;
            }
        }

        return markers;
    }

    public static int CompareDocumentOrder(
        DocumentNoteMarkerPosition marker,
        DocumentNoteMarkerPosition caret)
    {
        var blockComparison = marker.BlockIndex.CompareTo(caret.BlockIndex);
        if (blockComparison != 0)
            return blockComparison;

        if (marker.IsTableCell && caret.IsTableCell)
        {
            var rowComparison = marker.TableRowIndex!.Value.CompareTo(caret.TableRowIndex!.Value);
            if (rowComparison != 0)
                return rowComparison;

            var columnComparison = marker.TableGridColumnIndex!.Value.CompareTo(caret.TableGridColumnIndex!.Value);
            if (columnComparison != 0)
                return columnComparison;

            var paragraphComparison = marker.TableParagraphIndex.CompareTo(caret.TableParagraphIndex);
            if (paragraphComparison != 0)
                return paragraphComparison;
        }
        else if (marker.IsTableCell != caret.IsTableCell)
        {
            return marker.IsTableCell ? 1 : -1;
        }

        return marker.ParagraphOffset.CompareTo(caret.ParagraphOffset);
    }

    /// <summary>
    /// Selects the adjacent marker from a document-ordered marker list, wrapping at either end.
    /// The host supplies only its framework-specific comparison with the current caret.
    /// </summary>
    public static bool TryFindAdjacent<T>(
        IReadOnlyList<T> orderedMarkers,
        Func<T, int> compareToCaret,
        bool previous,
        [MaybeNullWhen(false)] out T target)
    {
        ArgumentNullException.ThrowIfNull(orderedMarkers);
        ArgumentNullException.ThrowIfNull(compareToCaret);

        if (orderedMarkers.Count == 0)
        {
            target = default;
            return false;
        }

        if (previous)
        {
            for (var index = orderedMarkers.Count - 1; index >= 0; index--)
            {
                if (compareToCaret(orderedMarkers[index]) < 0)
                {
                    target = orderedMarkers[index];
                    return true;
                }
            }

            target = orderedMarkers[^1];
            return true;
        }

        foreach (var marker in orderedMarkers)
        {
            if (compareToCaret(marker) > 0)
            {
                target = marker;
                return true;
            }
        }

        target = orderedMarkers[0];
        return true;
    }

    private static void AddParagraphMarkers(
        Paragraph paragraph,
        bool footnote,
        Func<int, DocumentNoteMarkerPosition> positionAt,
        ICollection<DocumentNoteMarkerPosition> markers)
    {
        var offset = 0;
        foreach (var run in paragraph.Runs)
        {
            if (footnote ? run.FootnoteId.HasValue : run.EndnoteId.HasValue)
                markers.Add(positionAt(offset));
            offset += run.Text.Length;
        }
    }
}
