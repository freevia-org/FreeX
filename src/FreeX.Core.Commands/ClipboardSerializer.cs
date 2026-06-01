using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class ClipboardSerializer
{
    /// <summary>Serialises the display text of <paramref name="range"/> as spreadsheet-compatible
    /// tab/newline-delimited text.</summary>
    public static string Serialize(ViewportModel viewport, GridRange range)
    {
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
                    AppendTsvCell(sb, cell.DisplayText);
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
                    AppendTsvCell(sb, cells[cellIndex].DisplayText);
                    cellIndex++;
                }
            }
        }
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
                capacity += cell.DisplayText.Length + 2;
                if (capacity >= int.MaxValue)
                    capacity = int.MaxValue;
            }
        }

        return new SerializationPlan((int)capacity, isSorted);
    }

    private readonly record struct SerializationPlan(int EstimatedCapacity, bool IsRowMajorSorted);

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

            if (ch == '"' && atFieldStart)
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
