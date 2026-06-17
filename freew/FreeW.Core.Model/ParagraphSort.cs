namespace FreeW.Core.Model;

/// <summary>
/// Pure, deterministic sort helpers for paragraphs and table rows. Sorting is by visible text
/// (<see cref="Paragraph.PlainText"/> / a row's key-column cell text), using a culture-invariant
/// ordinal (or ordinal-ignore-case) comparison so results are stable across locales. The sort is
/// stable: items comparing equal keep their original relative order. The input collections are never
/// mutated — a new, reordered list of the same item instances is returned — so callers stay in
/// control of how the reordered items are spliced back into the model.
/// </summary>
public static class ParagraphSort
{
    /// <summary>
    /// Return <paramref name="paragraphs"/> reordered by <see cref="Paragraph.PlainText"/>. When
    /// <paramref name="ascending"/> is false the order is reversed; <paramref name="caseSensitive"/>
    /// selects ordinal vs. ordinal-ignore-case comparison. The same <see cref="Paragraph"/> instances
    /// are returned (never copies), the input is left untouched, and the sort is stable.
    /// </summary>
    public static IReadOnlyList<Paragraph> Sort(
        IReadOnlyList<Paragraph> paragraphs, bool ascending, bool caseSensitive)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        var comparer = KeyComparer(caseSensitive);
        // OrderBy is a stable sort; reverse the comparer (not the result) so ties keep original order.
        return ascending
            ? [.. paragraphs.OrderBy(p => p.PlainText, comparer)]
            : [.. paragraphs.OrderByDescending(p => p.PlainText, comparer)];
    }

    /// <summary>
    /// Return <paramref name="rows"/> reordered by the text of each row's cell in column
    /// <paramref name="keyColumn"/> (the cell's <see cref="TableCell.PlainText"/>). Rows that are too
    /// short to have that column sort as if the key were empty. When <paramref name="ascending"/> is
    /// false the order is reversed; <paramref name="caseSensitive"/> selects ordinal vs.
    /// ordinal-ignore-case comparison. The same <see cref="TableRow"/> instances are returned, the
    /// input is left untouched, and the sort is stable.
    /// </summary>
    public static IReadOnlyList<TableRow> SortRows(
        IReadOnlyList<TableRow> rows, int keyColumn, bool ascending, bool caseSensitive)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var comparer = KeyComparer(caseSensitive);
        return ascending
            ? [.. rows.OrderBy(r => CellKey(r, keyColumn), comparer)]
            : [.. rows.OrderByDescending(r => CellKey(r, keyColumn), comparer)];
    }

    // The key text for a row: the plain text of its cell in keyColumn, or empty when the row has no
    // such column (a negative index or a ragged short row both fall back to the empty key).
    private static string CellKey(TableRow row, int keyColumn) =>
        keyColumn >= 0 && keyColumn < row.Cells.Count ? row.Cells[keyColumn].PlainText : string.Empty;

    private static StringComparer KeyComparer(bool caseSensitive) =>
        caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
}
