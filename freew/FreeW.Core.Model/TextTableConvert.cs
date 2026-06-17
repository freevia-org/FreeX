namespace FreeW.Core.Model;

/// <summary>
/// Pure, deterministic converters between a run of paragraphs and a <see cref="Table"/>, splitting
/// (or joining) on a single delimiter character. Text-to-table splits each paragraph's plain text on
/// the delimiter into cells, padding short rows so every row has the same column count. Table-to-text
/// joins each row's cell text with the delimiter, one paragraph per row. Both build fresh model
/// objects from the source text only (formatting is intentionally not carried), so the result is fully
/// determined by the input.
/// </summary>
public static class TextTableConvert
{
    /// <summary>
    /// Build a <see cref="Table"/> from <paramref name="paragraphs"/>: each paragraph becomes one row,
    /// its <see cref="Paragraph.PlainText"/> split on <paramref name="delimiter"/> into cells. The
    /// column count is the widest row's; shorter rows are padded with empty cells so the table is
    /// rectangular. An empty input yields a table with a single empty cell so the result is always a
    /// usable table.
    /// </summary>
    public static Table TextToTable(IReadOnlyList<Paragraph> paragraphs, char delimiter)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);

        // Split every paragraph first so we can size the grid to the widest row before building cells.
        var split = paragraphs.Select(p => p.PlainText.Split(delimiter)).ToList();
        var columns = split.Count == 0 ? 1 : Math.Max(1, split.Max(parts => parts.Length));

        var table = new Table();
        if (split.Count == 0)
        {
            // No source paragraphs: emit a single empty cell so callers always get a valid table.
            var only = new TableRow();
            only.Cells.Add(new TableCell(string.Empty));
            table.Rows.Add(only);
            return table;
        }

        foreach (var parts in split)
        {
            var row = new TableRow();
            for (var c = 0; c < columns; c++)
                row.Cells.Add(new TableCell(c < parts.Length ? parts[c] : string.Empty));
            table.Rows.Add(row);
        }
        return table;
    }

    /// <summary>
    /// Flatten <paramref name="table"/> into paragraphs: one paragraph per row, the row's cell texts
    /// (<see cref="TableCell.PlainText"/>) joined by <paramref name="delimiter"/>. A cell spanning
    /// multiple paragraphs contributes its newline-joined plain text. The output is a fresh list of new
    /// <see cref="Paragraph"/> instances.
    /// </summary>
    public static IReadOnlyList<Paragraph> TableToText(Table table, char delimiter)
    {
        ArgumentNullException.ThrowIfNull(table);

        var result = new List<Paragraph>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            var line = string.Join(delimiter, row.Cells.Select(c => c.PlainText));
            result.Add(new Paragraph(line));
        }
        return result;
    }
}
