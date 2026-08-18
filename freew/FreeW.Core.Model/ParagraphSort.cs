using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>
/// The data type a sort key is interpreted as, mirroring the subset of Word's "Sort Text" dialog:
/// <see cref="Text"/> compares the raw text, <see cref="Number"/> parses a leading numeric value,
/// and <see cref="Date"/> parses a date/time. Items whose key fails to parse as the requested type
/// fall back to the text comparison so a stray non-numeric/non-date line still lands deterministically.
/// </summary>
public enum SortKind
{
    Text,
    Number,
    Date,
}

/// <summary>
/// Pure, deterministic sort helpers for paragraphs and table rows, matching the subset of Word's
/// "Sort Text" dialog FreeW exposes: sort by visible text as <see cref="SortKind.Text"/>,
/// <see cref="SortKind.Number"/>, or <see cref="SortKind.Date"/>, ascending or descending, with an
/// optional case-sensitive toggle and an optional "has header row" that pins the first item in place.
///
/// <para>
/// Text comparison is culture-invariant ordinal (or ordinal-ignore-case) so results are stable across
/// locales; numbers and dates parse with the invariant culture. The sort is stable — items comparing
/// equal keep their original relative order — and the input collections are never mutated: a new,
/// reordered list of the same item instances is returned, so callers stay in control of how the
/// reordered items are spliced back into the model.
/// </para>
/// </summary>
public static class ParagraphSort
{
    /// <summary>
    /// Return <paramref name="paragraphs"/> reordered by <see cref="Paragraph.PlainText"/> as plain
    /// text. When <paramref name="ascending"/> is false the order is reversed;
    /// <paramref name="caseSensitive"/> selects ordinal vs. ordinal-ignore-case comparison. The same
    /// <see cref="Paragraph"/> instances are returned (never copies), the input is left untouched, and
    /// the sort is stable.
    /// </summary>
    public static IReadOnlyList<Paragraph> Sort(
        IReadOnlyList<Paragraph> paragraphs, bool ascending, bool caseSensitive) =>
        Sort(paragraphs, SortKind.Text, ascending, caseSensitive, hasHeaderRow: false);

    /// <summary>
    /// Return <paramref name="paragraphs"/> reordered by <see cref="Paragraph.PlainText"/>, interpreting
    /// each key as <paramref name="kind"/> (<see cref="SortKind.Text"/>/<see cref="SortKind.Number"/>/
    /// <see cref="SortKind.Date"/>). When <paramref name="hasHeaderRow"/> is true the first paragraph is
    /// left in place and only the rest are reordered. Direction and case follow
    /// <paramref name="ascending"/>/<paramref name="caseSensitive"/>. The same instances are returned,
    /// the input is left untouched, and the sort is stable.
    /// </summary>
    public static IReadOnlyList<Paragraph> Sort(
        IReadOnlyList<Paragraph> paragraphs,
        SortKind kind,
        bool ascending,
        bool caseSensitive,
        bool hasHeaderRow) =>
        SortPinningHeader(paragraphs, hasHeaderRow, p => p.PlainText, kind, ascending, caseSensitive);

    /// <summary>
    /// Return <paramref name="rows"/> reordered by the text of each row's cell at logical grid column
    /// <paramref name="gridColumn"/> (the cell's <see cref="TableCell.PlainText"/>), the same coordinate
    /// space callers resolve the caret/selection into via <see cref="TableGridProjection"/> — NOT a raw
    /// <see cref="TableRow.Cells"/> index, which is meaningless across rows whose merged cells (GridSpan)
    /// differ. Rows with no cell covering that grid column sort as if the key were empty. Direction/case
    /// follow <paramref name="ascending"/>/<paramref name="caseSensitive"/>; comparison is plain text. The
    /// same <see cref="TableRow"/> instances are returned, the input is left untouched, and the sort is
    /// stable.
    /// </summary>
    public static IReadOnlyList<TableRow> SortRows(
        IReadOnlyList<TableRow> rows, int gridColumn, bool ascending, bool caseSensitive) =>
        SortRows(rows, gridColumn, SortKind.Text, ascending, caseSensitive, hasHeaderRow: false);

    /// <summary>
    /// Return <paramref name="rows"/> reordered by the text of each row's cell at logical grid column
    /// <paramref name="gridColumn"/>, interpreting each key as <paramref name="kind"/>. When
    /// <paramref name="hasHeaderRow"/> is true the first row is left in place and only the body rows are
    /// reordered (Word's "Header row" option). <paramref name="gridColumn"/> is a logical grid column, the
    /// same coordinate space callers resolve the caret/selection into via <see cref="TableGridProjection"/>
    /// — each row is independently projected onto the grid so a row whose merged-cell layout (GridSpan)
    /// differs from the row the caller resolved the column from still reads the correct cell, matching
    /// Word: a cell whose span covers <paramref name="gridColumn"/> supplies the key for that row, and a
    /// row with no cell reaching that column (narrower than the grid) sorts as if the key were empty.
    /// Direction/case follow <paramref name="ascending"/>/<paramref name="caseSensitive"/>. The same
    /// instances are returned, the input is left untouched, and the sort is stable.
    /// </summary>
    public static IReadOnlyList<TableRow> SortRows(
        IReadOnlyList<TableRow> rows,
        int gridColumn,
        SortKind kind,
        bool ascending,
        bool caseSensitive,
        bool hasHeaderRow) =>
        SortPinningHeader(rows, hasHeaderRow, r => CellKey(r, gridColumn), kind, ascending, caseSensitive);

    // Sort a list with an optional pinned header: when hasHeaderRow is true (and there is more than one
    // item) the first item stays put and only items[1..] are reordered; otherwise the whole list sorts.
    private static IReadOnlyList<T> SortPinningHeader<T>(
        IReadOnlyList<T> items,
        bool hasHeaderRow,
        Func<T, string> keyOf,
        SortKind kind,
        bool ascending,
        bool caseSensitive)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!hasHeaderRow || items.Count < 2)
            return SortBody(items, keyOf, kind, ascending, caseSensitive);

        var header = items[0];
        var body = new List<T>(items.Count - 1);
        for (var i = 1; i < items.Count; i++)
            body.Add(items[i]);

        var sorted = SortBody(body, keyOf, kind, ascending, caseSensitive);
        var result = new List<T>(items.Count) { header };
        result.AddRange(sorted);
        return result;
    }

    // Stable order of items by their key under the given kind/direction/case. OrderBy/OrderByDescending
    // are stable, so reversing direction is expressed by the comparer choice (not by reversing results).
    private static IReadOnlyList<T> SortBody<T>(
        IReadOnlyList<T> items, Func<T, string> keyOf, SortKind kind, bool ascending, bool caseSensitive)
    {
        var comparer = new SortKeyComparer(kind, caseSensitive);
        return ascending
            ? [.. items.OrderBy(keyOf, comparer)]
            : [.. items.OrderByDescending(keyOf, comparer)];
    }

    // The key text for a row: the plain text of the cell that covers gridColumn once the row's own
    // GridSpans are projected onto the grid (TableGridProjection.At), not a raw Cells[] index -- a raw
    // index resolved against one row (typically the caret's row) is meaningless for any other row whose
    // merged-cell layout differs. A cell whose span covers gridColumn supplies the key for its whole span
    // (matching Word: sorting by a column a merged cell spans reads that merged cell). A row with no cell
    // reaching gridColumn at all (narrower than the grid, or a negative column) sorts as if the key were
    // empty.
    private static string CellKey(TableRow row, int gridColumn) =>
        TableGridProjection.At(row, gridColumn)?.Cell.PlainText ?? string.Empty;

    // Compares string keys interpreted as text, numbers, or dates. Numeric/date keys that fail to parse
    // sort after all parseable ones (and tie-break on text) so the result stays total and deterministic.
    private sealed class SortKeyComparer(SortKind kind, bool caseSensitive) : IComparer<string>
    {
        private readonly StringComparer _text =
            caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        public int Compare(string? x, string? y)
        {
            x ??= string.Empty;
            y ??= string.Empty;
            return kind switch
            {
                SortKind.Number => CompareParsed(x, y, TryParseNumber),
                SortKind.Date => CompareParsed(x, y, TryParseDate),
                _ => _text.Compare(x, y),
            };
        }

        // Compare two keys by a parsed value; unparseable keys sort after parseable ones, and two
        // unparseable (or exactly-equal) keys tie-break on the text comparison for a stable total order.
        private int CompareParsed(string x, string y, ParseKey parse)
        {
            var hasX = parse(x, out var vx);
            var hasY = parse(y, out var vy);
            if (hasX && hasY)
            {
                var cmp = vx.CompareTo(vy);
                return cmp != 0 ? cmp : _text.Compare(x, y);
            }
            if (hasX != hasY)
                return hasX ? -1 : 1; // parseable keys first
            return _text.Compare(x, y);
        }

        private delegate bool ParseKey(string text, out double value);

        // Parse a leading numeric value (currency/grouping tolerated), invariant culture.
        private static bool TryParseNumber(string text, out double value) =>
            double.TryParse(
                text.Trim(),
                NumberStyles.Number | NumberStyles.AllowLeadingSign | NumberStyles.AllowCurrencySymbol,
                CultureInfo.InvariantCulture,
                out value);

        // Parse a date/time, invariant culture; the comparable value is the tick count as a double.
        private static bool TryParseDate(string text, out double value)
        {
            if (DateTime.TryParse(
                    text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                value = date.Ticks;
                return true;
            }
            value = 0;
            return false;
        }
    }
}
