using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed record ClipboardHtmlPayload(string Fragment, string CfHtml);

/// <summary>Builds the shared HTML table fragment and CF_HTML wrapper used by clipboard hosts.</summary>
public static class ClipboardHtmlSerializer
{
    public static ClipboardHtmlPayload? Serialize(
        ViewportModel viewport,
        Sheet? sheet,
        GridRange range,
        WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        if (range.RowCount == 0 || range.ColCount == 0)
            return null;

        var cellLookup = new Dictionary<(uint Row, uint Col), DisplayCell>(viewport.Cells.Count);
        foreach (var cell in viewport.Cells)
            cellLookup[(cell.Row, cell.Col)] = cell;

        var anchors = new Dictionary<(uint Row, uint Col), GridRange>();
        var covered = new HashSet<(uint Row, uint Col)>();
        if (sheet is not null)
        {
            foreach (var region in sheet.MergedRegions)
            {
                if (!RangesOverlap(region, range))
                    continue;

                var anchorInRange = region.Start.Row >= range.Start.Row && region.Start.Row <= range.End.Row &&
                                    region.Start.Col >= range.Start.Col && region.Start.Col <= range.End.Col;
                var clippedRegion = anchorInRange
                    ? region
                    : new GridRange(
                        new CellAddress(range.Start.Sheet, Math.Max(region.Start.Row, range.Start.Row), Math.Max(region.Start.Col, range.Start.Col)),
                        new CellAddress(range.Start.Sheet, Math.Min(region.End.Row, range.End.Row), Math.Min(region.End.Col, range.End.Col)));

                anchors[(clippedRegion.Start.Row, clippedRegion.Start.Col)] = clippedRegion;
                foreach (var address in clippedRegion.AllCells())
                {
                    if (address.Row != clippedRegion.Start.Row || address.Col != clippedRegion.Start.Col)
                        covered.Add((address.Row, address.Col));
                }
            }
        }

        var body = new StringBuilder();
        body.Append("<table border=\"1\" cellspacing=\"0\" style=\"border-collapse:collapse\">");
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            body.Append("<tr>");
            for (var col = range.Start.Col; col <= range.End.Col; col++)
            {
                if (covered.Contains((row, col)))
                    continue;

                var spanAttributes = string.Empty;
                if (anchors.TryGetValue((row, col), out var merge))
                {
                    var colspan = Math.Min(merge.ColCount, range.End.Col - col + 1);
                    var rowspan = Math.Min(merge.RowCount, range.End.Row - row + 1);
                    if (colspan > 1) spanAttributes += $" colspan=\"{colspan}\"";
                    if (rowspan > 1) spanAttributes += $" rowspan=\"{rowspan}\"";
                }

                cellLookup.TryGetValue((row, col), out var displayCell);
                var css = displayCell.Style is { } style ? BuildCellCss(style, theme) : string.Empty;
                var styleAttribute = css.Length == 0 ? string.Empty : $" style=\"{css}\"";
                var display = EscapeHtml(displayCell.DisplayText ?? string.Empty);
                body.Append($"<td{spanAttributes}{styleAttribute}>{display}</td>");
            }
            body.Append("</tr>");
        }
        body.Append("</table>");

        var fragment = body.ToString();
        return new ClipboardHtmlPayload(fragment, WrapAsCfHtml(fragment));
    }

    public static string WrapAsCfHtml(string fragment)
    {
        const string placeholderHeader =
            "Version:0.9\r\n" +
            "StartHTML:0000000000\r\n" +
            "EndHTML:0000000000\r\n" +
            "StartFragment:0000000000\r\n" +
            "EndFragment:0000000000\r\n";
        const string htmlStart = "<html><body>\r\n<!--StartFragment-->";
        const string htmlEnd = "<!--EndFragment-->\r\n</body></html>";

        var startHtml = Utf8Length(placeholderHeader);
        var startFragment = startHtml + Utf8Length(htmlStart);
        var endFragment = startFragment + Utf8Length(fragment);
        var endHtml = endFragment + Utf8Length(htmlEnd);
        var header =
            "Version:0.9\r\n" +
            $"StartHTML:{startHtml:D10}\r\n" +
            $"EndHTML:{endHtml:D10}\r\n" +
            $"StartFragment:{startFragment:D10}\r\n" +
            $"EndFragment:{endFragment:D10}\r\n";
        return header + htmlStart + fragment + htmlEnd;
    }

    private static bool RangesOverlap(GridRange first, GridRange second) =>
        first.Start.Row <= second.End.Row && first.End.Row >= second.Start.Row &&
        first.Start.Col <= second.End.Col && first.End.Col >= second.Start.Col;

    private static string BuildCellCss(CellStyle style, WorkbookTheme theme)
    {
        var css = new StringBuilder();
        if (style.Bold) css.Append("font-weight:bold;");
        if (style.Italic) css.Append("font-style:italic;");
        if (style.Underline || style.DoubleUnderline) css.Append("text-decoration:underline;");
        if (style.Strikethrough) css.Append("text-decoration:line-through;");

        var fontName = style.ResolveEffectiveFontName(theme);
        if (!string.Equals(fontName, "Calibri", StringComparison.Ordinal))
            css.Append($"font-family:'{fontName.Replace("'", string.Empty, StringComparison.Ordinal)}';");
        if (Math.Abs(style.FontSize - 11) > 0.001)
            css.Append($"font-size:{style.FontSize.ToString("0.##", CultureInfo.InvariantCulture)}pt;");

        var fontColor = style.ResolveFontColor(theme);
        if (!fontColor.IsBlack)
            css.Append($"color:{HexColor(fontColor)};");
        if (style.ResolveFillColor(theme) is { } fill)
            css.Append($"background-color:{HexColor(fill)};");

        var alignment = style.HorizontalAlignment switch
        {
            HorizontalAlignment.Left => "left",
            HorizontalAlignment.Center => "center",
            HorizontalAlignment.Right => "right",
            HorizontalAlignment.Justify => "justify",
            _ => null,
        };
        if (alignment is not null)
            css.Append($"text-align:{alignment};");

        AppendBorderCss(css, "top", style.BorderTop);
        AppendBorderCss(css, "right", style.BorderRight);
        AppendBorderCss(css, "bottom", style.BorderBottom);
        AppendBorderCss(css, "left", style.BorderLeft);
        return css.ToString();
    }

    private static void AppendBorderCss(StringBuilder css, string edge, CellBorder border)
    {
        if (border.Style == BorderStyle.None)
            return;

        var (width, line) = border.Style switch
        {
            BorderStyle.Thin => ("1px", "solid"),
            BorderStyle.Medium => ("2px", "solid"),
            BorderStyle.Thick => ("3px", "solid"),
            BorderStyle.Dashed => ("1px", "dashed"),
            BorderStyle.Dotted => ("1px", "dotted"),
            BorderStyle.Double => ("3px", "double"),
            _ => ("1px", "solid"),
        };
        css.Append($"border-{edge}:{width} {line} {HexColor(border.Color)};");
    }

    private static string HexColor(CellColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static int Utf8Length(string text) => Encoding.UTF8.GetByteCount(text);
}
