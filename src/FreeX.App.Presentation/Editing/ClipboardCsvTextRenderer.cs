using System.Text;
using FreeX.Core.Commands;

namespace FreeX.App.Presentation.Editing;

/// <summary>
/// Converts spreadsheet clipboard TSV into the comma-delimited text exposed through CSV clipboard formats.
/// </summary>
public static class ClipboardCsvTextRenderer
{
    public static string Render(string? tsvText)
    {
        if (string.IsNullOrEmpty(tsvText))
            return string.Empty;

        var rows = ClipboardSerializer.Deserialize(tsvText);
        var sb = new StringBuilder(tsvText.Length + 16);
        for (var r = 0; r < rows.Length; r++)
        {
            if (r > 0)
                sb.Append("\r\n");

            var row = rows[r];
            for (var c = 0; c < row.Length; c++)
            {
                if (c > 0)
                    sb.Append(',');

                AppendField(sb, row[c]);
            }
        }

        return sb.ToString();
    }

    private static void AppendField(StringBuilder sb, string field)
    {
        var requiresQuoting = false;
        foreach (var ch in field)
        {
            if (ch is ',' or '"' or '\r' or '\n')
            {
                requiresQuoting = true;
                break;
            }
        }

        if (!requiresQuoting)
        {
            sb.Append(field);
            return;
        }

        sb.Append('"');
        foreach (var ch in field)
        {
            if (ch == '"')
                sb.Append("\"\"");
            else
                sb.Append(ch);
        }

        sb.Append('"');
    }
}
