using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class ClipboardSerializer
{
    /// <summary>Serialises the display text of <paramref name="range"/> as spreadsheet-compatible
    /// tab/newline-delimited text.</summary>
    public static string Serialize(ViewportModel viewport, GridRange range)
    {
        if (TrySerializeDenseContiguous(viewport.Cells, range, out var denseText))
            return denseText;

        var plan = AnalyzeCells(viewport.Cells, range);
        var sb = new StringBuilder(plan.EstimatedCapacity);
        if (plan.IsRowMajorSorted)
        {
            AppendSortedCells(sb, viewport.Cells, range);
            return sb.ToString();
        }

        var cellLookup = new Dictionary<(uint Row, uint Col), DisplayCell>(viewport.Cells.Count);
        foreach (var cell in viewport.Cells)
            cellLookup.Add((cell.Row, cell.Col), cell);

        AppendLookupCells(sb, cellLookup, range);
        return sb.ToString();
    }

    private static bool TrySerializeDenseContiguous(
        IReadOnlyList<DisplayCell> cells,
        GridRange range,
        out string text)
    {
        text = string.Empty;
        var rowCount = (long)range.End.Row - range.Start.Row + 1;
        var colCount = (long)range.End.Col - range.Start.Col + 1;
        if (rowCount <= 0 || colCount <= 0 || rowCount > int.MaxValue || colCount > int.MaxValue)
            return false;

        var expectedCellCount = rowCount * colCount;
        if (expectedCellCount != cells.Count)
            return false;

        var length = Math.Max(0, rowCount - 1) * 2 + rowCount * Math.Max(0, colCount - 1);
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var expectedRow = range.Start.Row + (uint)(i / colCount);
            var expectedCol = range.Start.Col + (uint)(i % colCount);
            if (cell.Row != expectedRow || cell.Col != expectedCol)
                return false;

            length += GetTsvEncodedLength(GetSerializedFieldText(cell));
            if (length > int.MaxValue)
                return false;
        }

        var state = new DenseSerializationState(cells, (int)colCount);
        text = string.Create((int)length, state, static (destination, state) =>
        {
            var offset = 0;
            for (var i = 0; i < state.Cells.Count; i++)
            {
                if (i > 0)
                {
                    if (i % state.ColumnCount == 0)
                    {
                        destination[offset++] = '\r';
                        destination[offset++] = '\n';
                    }
                    else
                    {
                        destination[offset++] = '\t';
                    }
                }

                AppendTsvCell(destination, ref offset, GetSerializedFieldText(state.Cells[i]));
            }
        });
        return true;
    }

    private static void AppendLookupCells(
        StringBuilder sb,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        GridRange range)
    {
        bool firstRow = true;

        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            if (!firstRow) sb.Append("\r\n");
            firstRow = false;

            bool firstCol = true;
            for (uint c = range.Start.Col; c <= range.End.Col; c++)
            {
                if (!firstCol) sb.Append('\t');
                firstCol = false;

                if (cellLookup.TryGetValue((r, c), out var cell))
                    AppendTsvCell(sb, GetSerializedFieldText(cell));
            }
        }
    }

    private static void AppendSortedCells(StringBuilder sb, IReadOnlyList<DisplayCell> cells, GridRange range)
    {
        var cellIndex = 0;
        bool firstRow = true;

        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            if (!firstRow) sb.Append("\r\n");
            firstRow = false;

            bool firstCol = true;
            for (uint c = range.Start.Col; c <= range.End.Col; c++)
            {
                if (!firstCol) sb.Append('\t');
                firstCol = false;

                while (cellIndex < cells.Count && IsBefore(cells[cellIndex], r, c))
                    cellIndex++;

                if (cellIndex < cells.Count && cells[cellIndex].Row == r && cells[cellIndex].Col == c)
                {
                    AppendTsvCell(sb, GetSerializedFieldText(cells[cellIndex]));
                    cellIndex++;
                }
            }
        }
    }

    /// <summary>Returns the text that should actually be written to the clipboard for
    /// <paramref name="cell"/>, prefixing a leading apostrophe (Excel's text-escape convention,
    /// as consumed by PasteCommandFactory.ParseClipboardValue) when the cell is text-typed but its
    /// DisplayText would otherwise be silently re-coerced into a number/boolean/apostrophe-escape on
    /// a subsequent OS-clipboard paste. Without this, a Text-formatted "00501" round-trips through
    /// Notepad/another window and comes back as the number 501, losing the leading zeros and type.</summary>
    private static string GetSerializedFieldText(DisplayCell cell)
    {
        if (cell.RawValue is not TextValue || string.IsNullOrEmpty(cell.DisplayText))
            return cell.DisplayText;

        return EscapeTextCellForPaste(cell.DisplayText);
    }

    /// <summary>Prefixes a leading apostrophe (Excel's text-escape convention, as consumed by
    /// PasteCommandFactory.ParseClipboardValue) onto <paramref name="displayText"/> when it is known
    /// to belong to a Text-typed cell but would otherwise round-trip through a subsequent paste as a
    /// number/boolean/apostrophe-escape. Shared by the plain-text clipboard path above (<see
    /// cref="GetSerializedFieldText"/>) and by MainWindow.ClipboardCommands' HTML-clipboard-paste
    /// path, which needs the identical escape applied to a &lt;td&gt; carrying the
    /// "mso-number-format:'\@'" text marker (see ClipboardHtmlSerializer.RequiresTextFormatMarker) --
    /// without it, the HTML-preferred paste branch silently re-coerces a Text-formatted "00501" back
    /// into the number 501, even though the plain-text sibling on the same clipboard already carries
    /// this exact escape (R78-services-clipboard-formats-5-1).</summary>
    public static string EscapeTextCellForPaste(string displayText)
    {
        if (string.IsNullOrEmpty(displayText))
            return displayText;

        return RequiresLeadingApostropheEscape(displayText)
            ? "'" + displayText
            : displayText;
    }

    /// <summary>Mirrors every coercion PasteCommandFactory.ParseClipboardValue applies to pasted
    /// plain text (via the shared <see cref="PasteCommandFactory.WouldClipboardTextCoerceToNonTextValue"/>
    /// predicate), so a leading apostrophe is added exactly when omitting it would change the value's
    /// type on the round trip -- including the percent ("45%") and date ("3/4", "12/25") coercions
    /// ParseClipboardValue performs, not just the plain-number/boolean ones.</summary>
    private static bool RequiresLeadingApostropheEscape(string text)
    {
        // A pre-existing leading apostrophe is itself the text-escape marker; without one of our own,
        // ParseClipboardValue would strip it as an escape and change the value.
        if (text.StartsWith('\''))
            return true;

        return PasteCommandFactory.WouldClipboardTextCoerceToNonTextValue(text);
    }

    private static void AppendTsvCell(StringBuilder sb, string text)
    {
        if (!RequiresTsvQuoting(text))
        {
            sb.Append(text);
            return;
        }

        sb.Append('"');
        foreach (var ch in text)
        {
            if (ch == '"')
                sb.Append("\"\"");
            else
                sb.Append(ch);
        }

        sb.Append('"');
    }

    private static void AppendTsvCell(Span<char> destination, ref int offset, string text)
    {
        var span = text.AsSpan();
        if (!RequiresTsvQuoting(text))
        {
            span.CopyTo(destination[offset..]);
            offset += span.Length;
            return;
        }

        destination[offset++] = '"';
        var segmentStart = 0;
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] != '"')
                continue;

            span[segmentStart..i].CopyTo(destination[offset..]);
            offset += i - segmentStart;
            destination[offset++] = '"';
            destination[offset++] = '"';
            segmentStart = i + 1;
        }

        span[segmentStart..].CopyTo(destination[offset..]);
        offset += span.Length - segmentStart;
        destination[offset++] = '"';
    }

    private static int GetTsvEncodedLength(string text)
    {
        var length = text.Length;
        var requiresQuoting = false;
        foreach (var ch in text)
        {
            if (ch is '\t' or '\r' or '\n')
            {
                requiresQuoting = true;
                continue;
            }

            if (ch == '"')
            {
                requiresQuoting = true;
                length++;
            }
        }

        return requiresQuoting ? length + 2 : length;
    }

    private static bool RequiresTsvQuoting(string text)
    {
        foreach (var ch in text)
        {
            if (ch is '\t' or '\r' or '\n' or '"')
                return true;
        }

        return false;
    }

    private static bool IsBefore(DisplayCell cell, uint row, uint col) =>
        cell.Row < row || (cell.Row == row && cell.Col < col);

    private static SerializationPlan AnalyzeCells(IReadOnlyList<DisplayCell> cells, GridRange range)
    {
        var rowCount = (long)range.End.Row - range.Start.Row + 1;
        var colCount = (long)range.End.Col - range.Start.Col + 1;
        var capacity = Math.Max(0, rowCount - 1) * 2 + rowCount * Math.Max(0, colCount - 1);
        var isSorted = true;

        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (i > 0)
            {
                var previous = cells[i - 1];
                if (cell.Row < previous.Row || (cell.Row == previous.Row && cell.Col <= previous.Col))
                    isSorted = false;
            }

            if (cell.Row < range.Start.Row || cell.Row > range.End.Row ||
                cell.Col < range.Start.Col || cell.Col > range.End.Col)
            {
                continue;
            }

            if (capacity < int.MaxValue)
            {
                capacity += cell.DisplayText.Length + 3;
                if (capacity >= int.MaxValue)
                    capacity = int.MaxValue;
            }
        }

        return new SerializationPlan((int)capacity, isSorted);
    }

    private readonly record struct SerializationPlan(int EstimatedCapacity, bool IsRowMajorSorted);

    private readonly record struct DenseSerializationState(IReadOnlyList<DisplayCell> Cells, int ColumnCount);

    /// <summary>Parses tab/newline-delimited text into a 2-D array of strings.</summary>
    public static string[][] Deserialize(string text)
    {
        if (!text.Contains('"'))
            return DeserializePlainText(text);

        text = text.TrimEnd('\r', '\n');
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var atFieldStart = true;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                        atFieldStart = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            if (ch == '"' && atFieldStart && IsProperlyQuotedField(text, i))
            {
                inQuotes = true;
                atFieldStart = false;
                continue;
            }

            if (ch == '\t')
            {
                row.Add(field.ToString());
                field.Clear();
                atFieldStart = true;
                continue;
            }

            if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;

                row.Add(field.ToString());
                field.Clear();
                rows.Add(row.ToArray());
                row.Clear();
                atFieldStart = true;
                continue;
            }

            field.Append(ch);
            atFieldStart = false;
        }

        row.Add(field.ToString());
        rows.Add(row.ToArray());
        return rows.ToArray();
    }

    /// <summary>Returns true when the double-quote at <paramref name="quoteIndex"/> starts a genuine
    /// RFC4180-style quoted field: scanning forward (treating "" as an escaped literal quote) reaches
    /// a closing quote that is immediately followed by a tab, a line break, or the end of the text.
    /// If no such closing quote exists, the character is a stray literal quote (e.g. a quoted saying
    /// pasted from a browser) and must be preserved as data rather than consumed as CSV syntax.</summary>
    private static bool IsProperlyQuotedField(string text, int quoteIndex)
    {
        for (var i = quoteIndex + 1; i < text.Length; i++)
        {
            if (text[i] != '"')
                continue;

            if (i + 1 < text.Length && text[i + 1] == '"')
            {
                i++;
                continue;
            }

            var next = i + 1;
            return next >= text.Length || text[next] is '\t' or '\r' or '\n';
        }

        return false;
    }

    private static string[][] DeserializePlainText(string text)
    {
        var span = text.AsSpan(0, GetTrimmedEndLength(text));
        var rows = new List<string[]>();
        var row = new List<string>(EstimateFirstRowFieldCount(span));
        var fieldStart = 0;
        var searchStart = 0;

        while (searchStart < span.Length)
        {
            var delimiterOffset = span[searchStart..].IndexOfAny('\t', '\r', '\n');
            if (delimiterOffset < 0)
                break;

            var i = searchStart + delimiterOffset;
            var ch = span[i];
            if (ch == '\t')
            {
                row.Add(span[fieldStart..i].ToString());
                fieldStart = i + 1;
                searchStart = fieldStart;
                continue;
            }

            row.Add(span[fieldStart..i].ToString());
            rows.Add(row.ToArray());
            row.Clear();

            if (ch == '\r' && i + 1 < span.Length && span[i + 1] == '\n')
                i++;

            fieldStart = i + 1;
            searchStart = fieldStart;
        }

        row.Add(span[fieldStart..].ToString());
        rows.Add(row.ToArray());
        return rows.ToArray();
    }

    private static int GetTrimmedEndLength(string text)
    {
        var length = text.Length;
        while (length > 0 && text[length - 1] is '\r' or '\n')
            length--;

        return length;
    }

    private static int EstimateFirstRowFieldCount(ReadOnlySpan<char> text)
    {
        var count = 1;
        foreach (var ch in text)
        {
            if (ch == '\t')
            {
                count++;
                continue;
            }

            if (ch is '\r' or '\n')
                break;
        }

        return count;
    }
}
