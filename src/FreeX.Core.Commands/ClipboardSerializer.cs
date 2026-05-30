using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class ClipboardSerializer
{
    /// <summary>Serialises the display text of <paramref name="range"/> as spreadsheet-compatible
    /// tab/newline-delimited text.</summary>
    public static string Serialize(ViewportModel viewport, GridRange range)
    {
        var cellLookup = viewport.Cells.ToDictionary(c => (c.Row, c.Col));

        var sb = new StringBuilder();
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

        return sb.ToString();
    }

    private static void AppendTsvCell(StringBuilder sb, string text)
    {
        if (text.Contains('\t') || text.Contains('\n') || text.Contains('"'))
        {
            sb.Append('"');
            sb.Append(text.Replace("\"", "\"\""));
            sb.Append('"');
        }
        else
        {
            sb.Append(text);
        }
    }

    /// <summary>Parses tab/newline-delimited text into a 2-D array of strings.</summary>
    public static string[][] Deserialize(string text)
    {
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
}
