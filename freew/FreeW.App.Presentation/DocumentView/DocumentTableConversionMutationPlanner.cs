using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Plans text/table block replacements without toolkit state. Renderers supply only model selection or
/// caret coordinates, execute the returned plan through their undo bus, and update native caret visuals.
/// </summary>
public static class DocumentTableConversionMutationPlanner
{
    public static DocumentBlockReplacementPlan? PlanTextToTable(
        TextDocument document,
        IReadOnlyList<int> selectedParagraphIndices,
        char delimiter)
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
        if (paragraphs.Count == 0)
            return null;

        var table = TextTableConvert.TextToTable(paragraphs, delimiter);
        return new DocumentBlockReplacementPlan(first, last - first + 1, [table]);
    }

    public static DocumentBlockReplacementPlan? PlanTableToText(
        TextDocument document,
        int tableBlockIndex,
        char delimiter)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (tableBlockIndex < 0 || tableBlockIndex >= document.Blocks.Count
            || document.Blocks[tableBlockIndex] is not Table table)
        {
            return null;
        }

        var paragraphs = TextTableConvert.TableToText(table, delimiter);
        return new DocumentBlockReplacementPlan(tableBlockIndex, 1, [.. paragraphs]);
    }
}
