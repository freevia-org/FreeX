using System.Globalization;
using System.Text;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Writes the first sheet's used range as a single styled <c>&lt;table&gt;</c>. Cell display values are
/// rendered self-contained (numbers/dates formatted like the delimited-text writer); a compact set of
/// visual attributes is mapped to inline CSS so a re-import recovers bold/italic/underline, font
/// family/size/color, fill color, horizontal alignment, and per-edge borders. Merged regions emit
/// <c>colspan</c>/<c>rowspan</c> and their covered (non-anchor) cells are skipped.
/// </summary>
internal static class HtmlTableWriter
{
    public static void Write(Workbook workbook, Stream stream)
    {
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            NewLine = "\r\n",
        };

        writer.WriteLine("<!DOCTYPE html>");
        writer.WriteLine("<html>");
        writer.WriteLine("<head><meta charset=\"utf-8\"></head>");
        writer.WriteLine("<body>");

        if (workbook.Sheets.Count == 0)
        {
            writer.WriteLine("<table></table>");
            writer.WriteLine("</body>");
            writer.WriteLine("</html>");
            return;
        }

        var sheet = workbook.Sheets[0];
        var used = sheet.GetUsedRange();
        writer.WriteLine("<table border=\"1\" style=\"border-collapse:collapse\">");

        // The emitted grid extent is the value used range EXTENDED to cover formatted-but-empty (style-only)
        // cells: GetUsedRange() ignores them, but their CSS must still round-trip. A sheet with only
        // style-only cells (no values) still emits a grid so that styling is not silently dropped.
        uint endRow = used?.End.Row ?? 0;
        uint endCol = used?.End.Col ?? 0;
        foreach (var ((soRow, soCol), _) in sheet.GetStyleOnlyEntries())
        {
            if (soRow > endRow) endRow = soRow;
            if (soCol > endCol) endCol = soCol;
        }

        if (endRow >= 1 && endCol >= 1)
        {
            // Map anchor → merge region, and mark covered non-anchor cells to skip.
            var anchors = new Dictionary<(uint, uint), GridRange>();
            var covered = new HashSet<(uint, uint)>();
            foreach (var region in sheet.MergedRegions)
            {
                anchors[(region.Start.Row, region.Start.Col)] = region;
                foreach (var addr in region.AllCells())
                {
                    if (addr.Row != region.Start.Row || addr.Col != region.Start.Col)
                        covered.Add((addr.Row, addr.Col));
                }
            }

            // Emit from A1 (row 1, col 1) so the table's grid coordinates are ABSOLUTE: a sheet whose used
            // range starts below/right of A1 keeps that offset on re-import (leading empty rows/cells fill
            // the gap). Without this the first <tr> would reload at row 1 and shift every value.
            for (uint r = 1; r <= endRow; r++)
            {
                writer.Write("<tr>");
                for (uint c = 1; c <= endCol; c++)
                {
                    if (covered.Contains((r, c)))
                        continue;

                    var cell = sheet.GetCell(r, c);
                    // A value cell carries its StyleId; a formatted-but-empty (style-only) cell carries its
                    // style in the sheet's style-only map. Emit CSS for both so styling round-trips even when
                    // the cell has no value.
                    var styleId = cell?.StyleId ?? sheet.GetStyleOnly(r, c);
                    var style = styleId is { } sid ? workbook.GetStyle(sid) : null;
                    var value = cell?.Value ?? BlankValue.Instance;

                    var spanAttrs = "";
                    if (anchors.TryGetValue((r, c), out var region))
                    {
                        uint colspan = region.ColCount;
                        uint rowspan = region.RowCount;
                        if (colspan > 1) spanAttrs += $" colspan=\"{colspan}\"";
                        if (rowspan > 1) spanAttrs += $" rowspan=\"{rowspan}\"";
                    }

                    var css = style is not null ? BuildCss(style, workbook.Theme) : "";
                    var styleAttr = css.Length > 0 ? $" style=\"{css}\"" : "";
                    var display = HtmlText.Escape(DisplayValue(value, style, workbook));
                    if (sheet.Hyperlinks.TryGetValue(new CellAddress(sheet.Id, r, c), out var hyperlink) &&
                        !string.IsNullOrEmpty(hyperlink))
                    {
                        display = $"<a href=\"{HtmlText.Escape(hyperlink)}\">{display}</a>";
                    }
                    writer.Write($"<td{spanAttrs}{styleAttr}>{display}</td>");
                }
                writer.WriteLine("</tr>");
            }
        }

        writer.WriteLine("</table>");
        writer.WriteLine("</body>");
        writer.WriteLine("</html>");
    }

    // ---- value display ----------------------------------------------------------------------------

    private static string DisplayValue(ScalarValue value, CellStyle? style, Workbook workbook)
    {
        // Honor the cell's NumberFormat (e.g. "0%", a date/time pattern, custom currency) for
        // numbers/dates so the exported HTML shows what the user actually sees in the grid ("50%"
        // instead of the raw invariant "0.5", a formatted date instead of a bare OADate serial) —
        // matching PortablePdfPageContentPlanner.GetDisplayText. A cell with no style or the default
        // "General" format keeps the prior self-contained invariant rendering below, so plain
        // number/date round-trips (no explicit format) are unaffected.
        if (style is not null &&
            !string.IsNullOrEmpty(style.NumberFormat) &&
            !string.Equals(style.NumberFormat, "General", StringComparison.OrdinalIgnoreCase) &&
            value is NumberValue or DateTimeValue)
        {
            return NumberFormatter.FormatWithColor(
                value,
                style.NumberFormat,
                workbook.IndexedColors,
                workbook.Theme,
                workbook.Uses1904DateSystem).Text;
        }

        return value switch
        {
            BlankValue => "",
            NumberValue n => FormatNumber(n.Value),
            DateTimeValue d => FormatDate(d),
            BoolValue b => b.Value ? "TRUE" : "FALSE",
            TextValue t => t.Value,
            ErrorValue e => e.Code,
            _ => value.ToString() ?? "",
        };
    }

    private static string FormatNumber(double v)
    {
        if (!double.IsFinite(v))
            return "";
        return v.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string FormatDate(DateTimeValue value)
    {
        if (!double.IsFinite(value.Value))
            return "";
        DateTime dt;
        try { dt = value.ToDateTime(); }
        catch (ArgumentException) { return value.Value.ToString("R", CultureInfo.InvariantCulture); }

        if (dt.Date == new DateTime(1899, 12, 30) && dt.TimeOfDay != TimeSpan.Zero)
            return dt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    // ---- CSS mapping ------------------------------------------------------------------------------

    private static string BuildCss(CellStyle style, WorkbookTheme theme)
    {
        var sb = new StringBuilder();

        if (style.Bold) sb.Append("font-weight:bold;");
        if (style.Italic) sb.Append("font-style:italic;");
        if (style.Underline || style.DoubleUnderline) sb.Append("text-decoration:underline;");

        // Emit the EFFECTIVE (theme-resolved) font name so a cell whose font follows the theme (FontScheme
        // Minor/Major) round-trips the rendered family — the reader has no theme to consult, so what we
        // write is what the cell will display on re-import.
        var fontName = style.ResolveEffectiveFontName(theme);
        if (!string.Equals(fontName, "Calibri", StringComparison.Ordinal))
            sb.Append($"font-family:'{CssSafe(fontName)}';");
        if (Math.Abs(style.FontSize - 11) > 0.001)
            sb.Append($"font-size:{style.FontSize.ToString("0.##", CultureInfo.InvariantCulture)}pt;");

        var fontColor = style.ResolveFontColor(theme);
        if (!fontColor.IsBlack)
            sb.Append($"color:{Hex(fontColor)};");

        var fill = style.ResolveFillColor(theme);
        if (fill is { } f)
            sb.Append($"background-color:{Hex(f)};");

        var align = style.HorizontalAlignment switch
        {
            HorizontalAlignment.Left => "left",
            HorizontalAlignment.Center => "center",
            HorizontalAlignment.Right => "right",
            HorizontalAlignment.Justify => "justify",
            _ => null,
        };
        if (align is not null)
            sb.Append($"text-align:{align};");

        AppendBorder(sb, "top", style.BorderTop, theme);
        AppendBorder(sb, "right", style.BorderRight, theme);
        AppendBorder(sb, "bottom", style.BorderBottom, theme);
        AppendBorder(sb, "left", style.BorderLeft, theme);

        return sb.ToString();
    }

    private static void AppendBorder(StringBuilder sb, string edge, CellBorder border, WorkbookTheme theme)
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
        // Resolve through CellBorder.ResolveColor (mirrors ResolveFontColor/ResolveFillColor
        // above) instead of reading the plain Color field directly, so a border set via the
        // ribbon's Theme Colors picker exports to HTML/MHT in its CURRENT theme color instead
        // of the stale color captured when the file was loaded/authored.
        sb.Append($"border-{edge}:{width} {line} {Hex(border.ResolveColor(theme))};");
    }

    private static string Hex(CellColor c) =>
        $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string CssSafe(string fontName) =>
        fontName.Replace("'", "", StringComparison.Ordinal).Replace("\\", "", StringComparison.Ordinal);
}
