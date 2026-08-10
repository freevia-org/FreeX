using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Recovers the first table in a CF_HTML payload as a renderer-neutral grid of cell text.
/// </summary>
public static class HtmlClipboardTableParser
{
    private const int MaxHtmlPasteSpan = (int)CellAddress.MaxCol;

    private static readonly Regex MsoTextNumberFormatRegex = new(
        @"mso-number-format\s*:\s*[""']\\?@[""']",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses the first table in <paramref name="htmlPayload"/>, or returns <c>null</c> when no
    /// pasteable table is present.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>>? Parse(string? htmlPayload)
    {
        if (string.IsNullOrEmpty(htmlPayload))
            return null;

        var fragment = ExtractFragment(htmlPayload);
        var tableInner = ExtractFirstTableInner(fragment);
        if (tableInner is null)
            return null;

        // Keyed by 1-based column, with the last 0-based row occupied by an active rowspan.
        var rowSpanRemaining = new Dictionary<int, int>();
        var rows = new List<List<string>>();
        var rowIndex = -1;

        foreach (var rowInner in EnumerateElements(tableInner, "tr"))
        {
            rowIndex++;
            var cells = new List<string>();
            var col = 0;

            foreach (var cellInfo in EnumerateCells(rowInner))
            {
                col++;
                while (rowSpanRemaining.TryGetValue(col, out var occupiedThroughRow) &&
                       occupiedThroughRow >= rowIndex)
                {
                    EnsureColumn(cells, col);
                    col++;
                }

                var text = DecodeCellText(cellInfo.InnerHtml);
                if (cellInfo.IsTextFormat)
                    text = ClipboardSerializer.EscapeTextCellForPaste(text);

                var colSpan = Math.Max(1, cellInfo.ColSpan);
                var rowSpan = Math.Max(1, cellInfo.RowSpan);
                var endCol = col + colSpan - 1;

                // The paste grid has no merged-cell concept, so repeat a colspan cell's text.
                for (var c = col; c <= endCol; c++)
                {
                    EnsureColumn(cells, c);
                    cells[c - 1] = text;
                }

                if (rowSpan > 1)
                {
                    for (var c = col; c <= endCol; c++)
                        rowSpanRemaining[c] = rowIndex + rowSpan - 1;
                }

                col = endCol;
            }

            if (cells.Count > 0)
                rows.Add(cells);
        }

        return rows.Count > 0
            ? rows.Cast<IReadOnlyList<string>>().ToList()
            : null;
    }

    private static void EnsureColumn(List<string> row, int col)
    {
        while (row.Count < col)
            row.Add(string.Empty);
    }

    private static string ExtractFragment(string html)
    {
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        var start = html.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        var end = html.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);
        return start >= 0 && end > start
            ? html[(start + startMarker.Length)..end]
            : html;
    }

    private static string? ExtractFirstTableInner(string html)
    {
        var i = 0;
        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0)
                return null;

            if (string.Equals(TagNameAt(html, lt), "table", StringComparison.OrdinalIgnoreCase))
            {
                var tagEnd = html.IndexOf('>', lt);
                if (tagEnd < 0)
                    return null;

                var closeStart = FindMatchingClose(html, tagEnd + 1, "table");
                return closeStart < 0 ? html[(tagEnd + 1)..] : html[(tagEnd + 1)..closeStart];
            }

            i = lt + 1;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateElements(string html, string tag)
    {
        var i = 0;
        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0)
                yield break;

            if (string.Equals(TagNameAt(html, lt), tag, StringComparison.OrdinalIgnoreCase))
            {
                var tagEnd = html.IndexOf('>', lt);
                if (tagEnd < 0)
                    yield break;

                var closeStart = FindMatchingClose(html, tagEnd + 1, tag);
                yield return closeStart < 0 ? html[(tagEnd + 1)..] : html[(tagEnd + 1)..closeStart];
                i = closeStart < 0 ? html.Length : SkipClosingTag(html, closeStart);
            }
            else
            {
                i = lt + 1;
            }
        }
    }

    private static IEnumerable<HtmlCellSpan> EnumerateCells(string rowInner)
    {
        var i = 0;
        while (i < rowInner.Length)
        {
            var lt = rowInner.IndexOf('<', i);
            if (lt < 0)
                yield break;

            var name = TagNameAt(rowInner, lt);
            if (name is "td" or "th")
            {
                var tagEnd = rowInner.IndexOf('>', lt);
                if (tagEnd < 0)
                    yield break;

                var tagContent = rowInner[(lt + 1)..tagEnd];
                var closeStart = FindMatchingClose(rowInner, tagEnd + 1, name);
                var inner = closeStart < 0
                    ? rowInner[(tagEnd + 1)..]
                    : rowInner[(tagEnd + 1)..closeStart];

                yield return new HtmlCellSpan(
                    inner,
                    ParseSpanAttribute(tagContent, "colspan"),
                    ParseSpanAttribute(tagContent, "rowspan"),
                    MsoTextNumberFormatRegex.IsMatch(tagContent));

                i = closeStart < 0 ? rowInner.Length : SkipClosingTag(rowInner, closeStart);
            }
            else
            {
                i = lt + 1;
            }
        }
    }

    private static int ParseSpanAttribute(string tagContent, string attributeName)
    {
        var searchFrom = 0;
        while (searchFrom < tagContent.Length)
        {
            var idx = tagContent.IndexOf(attributeName, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return 1;

            var afterIdx = idx + attributeName.Length;
            if (idx != 0 && !char.IsWhiteSpace(tagContent[idx - 1]))
            {
                searchFrom = afterIdx;
                continue;
            }

            var p = afterIdx;
            while (p < tagContent.Length && char.IsWhiteSpace(tagContent[p]))
                p++;
            if (p >= tagContent.Length || tagContent[p] != '=')
            {
                searchFrom = afterIdx;
                continue;
            }

            p++;
            while (p < tagContent.Length && char.IsWhiteSpace(tagContent[p]))
                p++;
            if (p < tagContent.Length && (tagContent[p] == '"' || tagContent[p] == '\''))
                p++;

            var digitsStart = p;
            while (p < tagContent.Length && char.IsDigit(tagContent[p]))
                p++;

            return int.TryParse(
                       tagContent[digitsStart..p],
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out var value) && value > 0
                ? Math.Min(value, MaxHtmlPasteSpan)
                : 1;
        }

        return 1;
    }

    private static string DecodeCellText(string innerHtml)
    {
        var text = new StringBuilder(innerHtml.Length);
        var i = 0;
        while (i < innerHtml.Length)
        {
            var c = innerHtml[i];
            if (c != '<')
            {
                text.Append(c);
                i++;
                continue;
            }

            var name = TagNameAt(innerHtml, i);
            var tagEnd = innerHtml.IndexOf('>', i);
            if (tagEnd < 0)
                break;

            if (name is "br")
            {
                text.Append('\n');
            }
            else if (name is "img")
            {
                var alt = ExtractAttributeValue(innerHtml[(i + 1)..tagEnd], "alt");
                if (!string.IsNullOrEmpty(alt))
                    text.Append(alt);
            }

            i = tagEnd + 1;
        }

        return System.Net.WebUtility.HtmlDecode(text.ToString()).Trim();
    }

    private static string? ExtractAttributeValue(string tagContent, string attributeName)
    {
        var searchFrom = 0;
        while (searchFrom < tagContent.Length)
        {
            var idx = tagContent.IndexOf(attributeName, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;

            var afterIdx = idx + attributeName.Length;
            if (idx != 0 && !char.IsWhiteSpace(tagContent[idx - 1]))
            {
                searchFrom = afterIdx;
                continue;
            }

            var p = afterIdx;
            while (p < tagContent.Length && char.IsWhiteSpace(tagContent[p]))
                p++;
            if (p >= tagContent.Length || tagContent[p] != '=')
            {
                searchFrom = afterIdx;
                continue;
            }

            p++;
            while (p < tagContent.Length && char.IsWhiteSpace(tagContent[p]))
                p++;
            if (p >= tagContent.Length || (tagContent[p] != '"' && tagContent[p] != '\''))
            {
                searchFrom = afterIdx;
                continue;
            }

            var quote = tagContent[p];
            var valueStart = p + 1;
            var valueEnd = tagContent.IndexOf(quote, valueStart);
            return valueEnd < 0 ? null : tagContent[valueStart..valueEnd];
        }

        return null;
    }

    private static string? TagNameAt(string text, int ltIndex)
    {
        var i = ltIndex + 1;
        if (i < text.Length && text[i] == '/')
            i++;

        var start = i;
        while (i < text.Length && char.IsLetterOrDigit(text[i]))
            i++;

        return i > start ? text[start..i].ToLowerInvariant() : null;
    }

    private static int FindMatchingClose(string text, int from, string tag)
    {
        var depth = 0;
        var i = from;
        while (i < text.Length)
        {
            var lt = text.IndexOf('<', i);
            if (lt < 0)
                return -1;

            var isClose = lt + 1 < text.Length && text[lt + 1] == '/';
            var name = TagNameAt(text, lt);
            if (string.Equals(name, tag, StringComparison.OrdinalIgnoreCase))
            {
                if (isClose)
                {
                    if (depth == 0)
                        return lt;
                    depth--;
                }
                else if (!IsSelfClosing(text, lt))
                {
                    depth++;
                }
            }

            i = lt + 1;
        }

        return -1;
    }

    private static bool IsSelfClosing(string text, int lt)
    {
        var tagEnd = text.IndexOf('>', lt);
        return tagEnd > lt && text[tagEnd - 1] == '/';
    }

    private static int SkipClosingTag(string text, int closeStart)
    {
        var tagEnd = text.IndexOf('>', closeStart);
        return tagEnd < 0 ? text.Length : tagEnd + 1;
    }

    private readonly record struct HtmlCellSpan(
        string InnerHtml,
        int ColSpan,
        int RowSpan,
        bool IsTextFormat);
}
