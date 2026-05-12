using System.Text;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static class ClipboardSerializer
{
    /// <summary>Serialises the display text of <paramref name="range"/> as Excel-compatible
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
                    sb.Append(cell.DisplayText);
            }
        }

        return sb.ToString();
    }

    /// <summary>Parses tab/newline-delimited text into a 2-D array of strings.</summary>
    public static string[][] Deserialize(string text)
    {
        var rows = text.Split(["\r\n", "\n"], StringSplitOptions.None);
        return rows.Select(r => r.Split('\t')).ToArray();
    }
}
