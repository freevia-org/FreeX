namespace FreeW.Core.Model;

/// <summary>
/// Enumerates paragraphs in the serialized main-document story. Nested tables precede a cell's own
/// paragraphs because <c>DocxWriter</c> emits them in that order to preserve Word's required trailing
/// cell paragraph.
/// </summary>
internal static class DocumentBodyParagraphs
{
    public static IEnumerable<DocumentBodyParagraphLocation> Enumerate(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            switch (document.Blocks[blockIndex])
            {
                case Paragraph paragraph:
                    yield return new DocumentBodyParagraphLocation(blockIndex, paragraph);
                    break;
                case Table table:
                    foreach (var tableParagraph in EnumerateTableLocations(table))
                    {
                        yield return new DocumentBodyParagraphLocation(
                            blockIndex,
                            tableParagraph.Paragraph,
                            tableParagraph.Address);
                    }
                    break;
            }
        }
    }

    internal static IEnumerable<Paragraph> EnumerateTable(Table table) =>
        EnumerateTableLocations(table).Select(location => location.Paragraph);

    private static IEnumerable<(Paragraph Paragraph, TableParagraphAddress Address)> EnumerateTableLocations(
        Table table)
    {
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                for (var nestedTableIndex = 0; nestedTableIndex < cell.NestedTables.Count; nestedTableIndex++)
                {
                    foreach (var nested in EnumerateTableLocations(cell.NestedTables[nestedTableIndex]))
                    {
                        yield return (
                            nested.Paragraph,
                            new TableParagraphAddress(
                                rowIndex,
                                cellIndex,
                                ParagraphIndex: -1,
                                nestedTableIndex,
                                nested.Address));
                    }
                }

                for (var paragraphIndex = 0; paragraphIndex < cell.Paragraphs.Count; paragraphIndex++)
                {
                    yield return (
                        cell.Paragraphs[paragraphIndex],
                        new TableParagraphAddress(rowIndex, cellIndex, paragraphIndex));
                }
            }
        }
    }
}

internal readonly record struct DocumentBodyParagraphLocation(
    int BlockIndex,
    Paragraph Paragraph,
    TableParagraphAddress? TableParagraph = null);
