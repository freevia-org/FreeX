using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Parses the first <c>&lt;table&gt;</c> of an HTML document into a sheet: rows from <c>&lt;tr&gt;</c>,
/// cells from <c>&lt;td&gt;</c>/<c>&lt;th&gt;</c>, with <c>colspan</c>/<c>rowspan</c> turned into merged
/// regions. Cell text is entity-decoded and tag-stripped, then coerced to a typed value when numeric.
/// Each cell's inline <c>style</c> attribute is parsed back into a <see cref="CellStyle"/> (the inverse
/// of <see cref="HtmlTableWriter"/>'s CSS mapping): font weight/style/underline, family/size/color,
/// background fill, horizontal alignment, and per-edge border style+color.
/// Hand-rolled (no external HTML dependency) and tolerant of unclosed tags / stray markup.
/// </summary>
internal static class HtmlTableReader
{
    public static void Populate(string html, Workbook workbook, Sheet sheet)
    {
        var tableInner = ExtractFirstTableInner(html);
        if (tableInner is null)
            return;

        // Occupancy carried by an active rowspan: column (1-based) → number of rows still occupied,
        // counting the CURRENT row down. Decremented once at the start of every row before placement.
        var spanRemaining = new Dictionary<uint, int>();
        uint row = 0;

        foreach (var rowInner in EnumerateElements(tableInner, "tr"))
        {
            row++;
            if (row > CellAddress.MaxRow)
                break;

            uint col = 0;
            foreach (var cell in EnumerateCells(rowInner))
            {
                // Advance to the next free column, hopping over any column still occupied by a rowspan
                // started in a previous row.
                col++;
                while (IsOccupied(spanRemaining, col, row) && col <= CellAddress.MaxCol)
                    col++;

                if (col > CellAddress.MaxCol)
                    break;

                var text = HtmlText.DecodeEntities(StripTags(cell.InnerHtml)).Trim();
                // A cell exists in the grid if it has text OR carries styling (a formatted-but-empty cell
                // still round-trips its CSS). Parse the inline style once and register it on the workbook.
                var style = HtmlCssParser.Parse(cell.Style);
                var addr = new CellAddress(sheet.Id, row, col);
                if (text.Length > 0)
                {
                    var newCell = Cell.FromValue(Coerce(text));
                    if (style is not null)
                        newCell.StyleId = workbook.RegisterStyle(style);
                    sheet.SetCell(addr, newCell);
                }
                else if (style is not null)
                {
                    sheet.SetStyleOnly(row, col, workbook.RegisterStyle(style));
                }

                int colspan = Math.Max(1, cell.ColSpan);
                int rowspan = Math.Max(1, cell.RowSpan);
                uint endCol = (uint)Math.Min(col + (long)colspan - 1, CellAddress.MaxCol);
                uint endRow = (uint)Math.Min(row + (long)rowspan - 1, CellAddress.MaxRow);

                if (endCol > col || endRow > row)
                {
                    sheet.AddMergedRegion(new GridRange(
                        new CellAddress(sheet.Id, row, col),
                        new CellAddress(sheet.Id, endRow, endCol)));
                }

                // Mark every column this cell spans as occupied through its last spanned row (endRow), so
                // both the remainder of this row (colspan) and following rows (rowspan) hop over it.
                if (rowspan > 1 || colspan > 1)
                {
                    for (uint cc = col; cc <= endCol; cc++)
                    {
                        spanRemaining.TryGetValue(cc, out var existingLastRow);
                        spanRemaining[cc] = Math.Max(existingLastRow, (int)endRow);
                    }
                }

                col = endCol; // next iteration's col++ moves past the spanned columns
            }
        }
    }

    /// <summary>True if column <paramref name="col"/> is occupied in <paramref name="row"/> by a span.</summary>
    private static bool IsOccupied(Dictionary<uint, int> spanRemaining, uint col, uint row) =>
        GetRemaining(spanRemaining, col, row) > 0;

    /// <summary>
    /// Rows still occupied at <paramref name="col"/> counting from <paramref name="row"/> down. The map
    /// stores the absolute last occupied row; this normalizes it to a remaining count for the current row.
    /// </summary>
    private static int GetRemaining(Dictionary<uint, int> spanRemaining, uint col, uint row)
    {
        if (!spanRemaining.TryGetValue(col, out var lastRow))
            return 0;
        return lastRow >= row ? (int)(lastRow - row + 1) : 0;
    }

    // ---- coercion ---------------------------------------------------------------------------------

    private static ScalarValue Coerce(string text)
    {
        if (text.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            return new BoolValue(true);
        if (text.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            return new BoolValue(false);
        if (text.Length > 1 && text[0] == '#' && IsErrorLiteral(text))
            return new ErrorValue(text.ToUpperInvariant());

        // Plain numeric (invariant): integers, decimals, scientific. Currency/percent kept as text to
        // avoid lossy guesses about the original number format.
        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var num) && double.IsFinite(num))
        {
            return new NumberValue(num);
        }

        // Date/time literals written by HtmlTableWriter.FormatDate: date-only ("yyyy-MM-dd"), date+time
        // ("yyyy-MM-dd HH:mm:ss"), or time-only ("HH:mm:ss", anchored to the OADate epoch day so only the
        // fractional time-of-day part is kept). Without this branch a round-tripped date/time cell reloads
        // as plain text instead of its original serial value.
        if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dateOnly))
        {
            return DateTimeValue.FromDateTime(dateOnly);
        }
        if (DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dateTime))
        {
            return DateTimeValue.FromDateTime(dateTime);
        }
        if (DateTime.TryParseExact(text, "HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.NoCurrentDateDefault, out var timeOnly))
        {
            return new DateTimeValue(timeOnly.TimeOfDay.TotalDays);
        }

        return new TextValue(text);
    }

    private static bool IsErrorLiteral(string text) => text.ToUpperInvariant() switch
    {
        "#DIV/0!" or "#VALUE!" or "#REF!" or "#NAME?" or "#NULL!" or "#N/A" or "#NUM!"
            or "#SPILL!" or "#CALC!" or "#GETTING_DATA" => true,
        _ => false,
    };

    // ---- tiny HTML scanner ------------------------------------------------------------------------

    private sealed record HtmlCell(string InnerHtml, int ColSpan, int RowSpan, string? Style);

    private static IEnumerable<HtmlCell> EnumerateCells(string rowInner)
    {
        int i = 0;
        while (i < rowInner.Length)
        {
            int lt = rowInner.IndexOf('<', i);
            if (lt < 0)
                break;

            // Match a <td ...> or <th ...> opening tag.
            var name = TagNameAt(rowInner, lt);
            if (name is "td" or "th")
            {
                int tagEnd = rowInner.IndexOf('>', lt);
                if (tagEnd < 0)
                    break;
                var attrs = rowInner.Substring(lt, tagEnd - lt + 1);
                int colspan = ReadIntAttr(attrs, "colspan");
                int rowspan = ReadIntAttr(attrs, "rowspan");
                string? style = ReadStringAttr(attrs, "style");

                int closeStart = FindMatchingClose(rowInner, tagEnd + 1, name);
                string inner = closeStart < 0
                    ? rowInner.Substring(tagEnd + 1)
                    : rowInner.Substring(tagEnd + 1, closeStart - (tagEnd + 1));
                yield return new HtmlCell(inner, colspan, rowspan, style);

                i = closeStart < 0 ? rowInner.Length : SkipClosingTag(rowInner, closeStart);
            }
            else
            {
                i = lt + 1;
            }
        }
    }

    /// <summary>Yield the inner HTML of each top-level &lt;tag&gt;…&lt;/tag&gt; element.</summary>
    private static IEnumerable<string> EnumerateElements(string html, string tag)
    {
        int i = 0;
        while (i < html.Length)
        {
            int lt = html.IndexOf('<', i);
            if (lt < 0)
                break;
            if (string.Equals(TagNameAt(html, lt), tag, StringComparison.OrdinalIgnoreCase))
            {
                int tagEnd = html.IndexOf('>', lt);
                if (tagEnd < 0)
                    break;
                int closeStart = FindMatchingClose(html, tagEnd + 1, tag);
                string inner = closeStart < 0
                    ? html.Substring(tagEnd + 1)
                    : html.Substring(tagEnd + 1, closeStart - (tagEnd + 1));
                yield return inner;
                i = closeStart < 0 ? html.Length : SkipClosingTag(html, closeStart);
            }
            else
            {
                i = lt + 1;
            }
        }
    }

    private static string? ExtractFirstTableInner(string html)
    {
        int i = 0;
        while (i < html.Length)
        {
            int lt = html.IndexOf('<', i);
            if (lt < 0)
                return null;
            if (string.Equals(TagNameAt(html, lt), "table", StringComparison.OrdinalIgnoreCase))
            {
                int tagEnd = html.IndexOf('>', lt);
                if (tagEnd < 0)
                    return null;
                int closeStart = FindMatchingClose(html, tagEnd + 1, "table");
                return closeStart < 0
                    ? html.Substring(tagEnd + 1)
                    : html.Substring(tagEnd + 1, closeStart - (tagEnd + 1));
            }
            i = lt + 1;
        }
        return null;
    }

    /// <summary>The element name at a '&lt;' position, or null if it isn't a start/end tag name.</summary>
    private static string? TagNameAt(string s, int ltIndex)
    {
        int i = ltIndex + 1;
        if (i < s.Length && s[i] == '/')
            i++;
        int start = i;
        while (i < s.Length && (char.IsLetterOrDigit(s[i])))
            i++;
        return i > start ? s.Substring(start, i - start).ToLowerInvariant() : null;
    }

    /// <summary>Find the index of the matching <c>&lt;/tag&gt;</c>, honoring nesting. -1 if none.</summary>
    private static int FindMatchingClose(string s, int from, string tag)
    {
        int depth = 0;
        int i = from;
        while (i < s.Length)
        {
            int lt = s.IndexOf('<', i);
            if (lt < 0)
                return -1;
            bool isClose = lt + 1 < s.Length && s[lt + 1] == '/';
            var name = TagNameAt(s, lt);
            if (string.Equals(name, tag, StringComparison.OrdinalIgnoreCase))
            {
                if (isClose)
                {
                    if (depth == 0)
                        return lt;
                    depth--;
                }
                else if (!IsSelfClosing(s, lt))
                {
                    depth++;
                }
            }
            i = lt + 1;
        }
        return -1;
    }

    private static bool IsSelfClosing(string s, int lt)
    {
        int gt = s.IndexOf('>', lt);
        return gt > lt && s[gt - 1] == '/';
    }

    private static int SkipClosingTag(string s, int closeStart)
    {
        int gt = s.IndexOf('>', closeStart);
        return gt < 0 ? s.Length : gt + 1;
    }

    private static int ReadIntAttr(string tag, string attr)
    {
        int idx = tag.IndexOf(attr, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return 1;
        int eq = tag.IndexOf('=', idx);
        if (eq < 0)
            return 1;
        int j = eq + 1;
        while (j < tag.Length && (tag[j] == ' ' || tag[j] == '"' || tag[j] == '\''))
            j++;
        int start = j;
        while (j < tag.Length && char.IsDigit(tag[j]))
            j++;
        return j > start && int.TryParse(tag.AsSpan(start, j - start), out var v) && v > 0 ? v : 1;
    }

    /// <summary>Read a quoted string attribute value (e.g. <c>style="…"</c>), or null when absent.</summary>
    private static string? ReadStringAttr(string tag, string attr)
    {
        int search = 0;
        while (true)
        {
            int idx = tag.IndexOf(attr, search, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;
            // Require a word boundary before the name and an '=' (possibly after spaces) after it, so we
            // don't match the attribute name embedded inside another token.
            bool boundaryBefore = idx == 0 || tag[idx - 1] == ' ' || tag[idx - 1] == '<';
            int after = idx + attr.Length;
            int k = after;
            while (k < tag.Length && tag[k] == ' ') k++;
            if (boundaryBefore && k < tag.Length && tag[k] == '=')
            {
                int q = k + 1;
                while (q < tag.Length && tag[q] == ' ') q++;
                if (q >= tag.Length)
                    return null;
                char quote = tag[q];
                if (quote is '"' or '\'')
                {
                    int end = tag.IndexOf(quote, q + 1);
                    return end < 0 ? null : tag.Substring(q + 1, end - (q + 1));
                }
                // Unquoted value: read up to whitespace or '>'.
                int e = q;
                while (e < tag.Length && tag[e] != ' ' && tag[e] != '>' && tag[e] != '/') e++;
                return tag.Substring(q, e - q);
            }
            search = idx + attr.Length;
        }
    }

    /// <summary>Strip all tags from a fragment, replacing &lt;br&gt; with a newline.</summary>
    private static string StripTags(string fragment)
    {
        if (fragment.IndexOf('<') < 0)
            return fragment;

        var sb = new StringBuilder(fragment.Length);
        int i = 0;
        while (i < fragment.Length)
        {
            char c = fragment[i];
            if (c == '<')
            {
                var name = TagNameAt(fragment, i);
                int gt = fragment.IndexOf('>', i);
                if (gt < 0)
                {
                    break;
                }
                if (name is "br")
                    sb.Append('\n');
                i = gt + 1;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }
}
