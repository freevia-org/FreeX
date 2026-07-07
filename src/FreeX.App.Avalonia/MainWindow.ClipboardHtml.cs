using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Avalonia.Input;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // ── HTML clipboard fragment (parity with WPF's M7 CF_HTML export) ────────
    // Real Excel places an HTML table fragment on the clipboard alongside plain text so that
    // formatting-aware destinations (LibreOffice Writer/Calc, browsers, Word/Outlook) preserve bold,
    // fill colors, alignment, borders, and merged cells instead of receiving flattened TSV text only.
    // WPF's MainWindow.ClipboardCommands.cs BuildHtmlClipboardFragment/BuildCellCss/WrapAsCfHtml do
    // this for the Windows shell (CF_HTML via System.Windows.DataFormats.Html); this is the Avalonia
    // shell's port of that same table/CSS construction, targeting Avalonia's IAsyncDataTransfer/
    // DataFormat API instead of WPF's DataObject. Avalonia has no built-in CF_HTML byte-offset framing
    // (that is a Windows-clipboard-specific convention) — this places the raw HTML fragment under the
    // conventional "text/html" platform format name that GTK/X11, macOS, and browser destinations all
    // recognize directly, plus the Windows-registered "HTML Format" name so the fragment round-trips
    // through the Win32 clipboard too when this shell runs there.
    //
    // Read-side (importing a foreign app's HTML clipboard payload back into styled cells) is NOT
    // implemented here, matching the WPF shell's scope — plain-text paste continues to work
    // unchanged via the existing clipboard text path.

    private static readonly DataFormat<string> HtmlPlatformFormat = DataFormat.CreateStringPlatformFormat("text/html");
    private static readonly DataFormat<string> HtmlWindowsPlatformFormat = DataFormat.CreateStringPlatformFormat("HTML Format");

    /// <summary>
    /// Test-only seam exposing <see cref="BuildHtmlClipboardFragment"/> so tests can drive the exact
    /// production HTML-building logic (through the real session/session-viewport shapes) rather than
    /// asserting on source text. Not used by production code paths (the real copy path is
    /// <see cref="AddClipboardTextAndHtml"/>, called from <c>CopySelectedRangeToClipboardAsync</c>).
    /// </summary>
    internal static string? BuildHtmlClipboardFragmentForTest(ViewportModel viewport, Sheet? sheet, GridRange range, WorkbookTheme theme) =>
        BuildHtmlClipboardFragment(viewport, sheet, range, theme);

    /// <summary>
    /// Builds the <see cref="DataTransferItem"/> list to add to a copy's <see cref="DataTransfer"/> for
    /// the given plain text plus (when buildable) an HTML table fragment for <paramref name="range"/>.
    /// Always includes the plain-text item; the HTML items are added only when the range/sheet yield a
    /// non-empty fragment.
    /// </summary>
    private static void AddClipboardTextAndHtml(DataTransfer transfer, string text, ViewportModel viewport, Sheet? sheet, GridRange range, WorkbookTheme theme)
    {
        transfer.Add(DataTransferItem.CreateText(text));

        var html = BuildHtmlClipboardFragment(viewport, sheet, range, theme);
        if (string.IsNullOrEmpty(html))
            return;

        transfer.Add(DataTransferItem.Create(HtmlPlatformFormat, html));
        transfer.Add(DataTransferItem.Create(HtmlWindowsPlatformFormat, html));
    }

    /// <summary>
    /// Builds an HTML table fragment for <paramref name="range"/>, or <c>null</c> if the range is
    /// empty/invalid. Mirrors WPF's BuildHtmlClipboardFragment cell-by-cell/merge-region handling.
    /// </summary>
    private static string? BuildHtmlClipboardFragment(
        ViewportModel viewport, Sheet? sheet, GridRange range, WorkbookTheme theme)
    {
        if (range.RowCount == 0 || range.ColCount == 0)
            return null;

        var cellLookup = new Dictionary<(uint Row, uint Col), DisplayCell>(viewport.Cells.Count);
        foreach (var cell in viewport.Cells)
            cellLookup[(cell.Row, cell.Col)] = cell;

        // Map merge-region anchor -> region, and mark covered (non-anchor) cells to skip. A copied
        // range can clip a merged region whose anchor (top-left cell) lies outside the copied range
        // entirely (e.g. copy A2:B3 when A1:A3 is merged); in that case synthesize a clipped anchor at
        // the top-left of the region's intersection with the range so the row's column count stays
        // intact instead of silently dropping the covered cell's slot.
        var anchors = new Dictionary<(uint, uint), GridRange>();
        var covered = new HashSet<(uint, uint)>();
        if (sheet is not null)
        {
            foreach (var region in sheet.MergedRegions)
            {
                if (!RangesOverlap(region, range))
                    continue;

                var anchorInRange = region.Start.Row >= range.Start.Row && region.Start.Row <= range.End.Row &&
                                     region.Start.Col >= range.Start.Col && region.Start.Col <= range.End.Col;
                if (anchorInRange)
                {
                    anchors[(region.Start.Row, region.Start.Col)] = region;
                    foreach (var addr in region.AllCells())
                    {
                        if (addr.Row != region.Start.Row || addr.Col != region.Start.Col)
                            covered.Add((addr.Row, addr.Col));
                    }
                }
                else
                {
                    var clippedStartRow = Math.Max(region.Start.Row, range.Start.Row);
                    var clippedStartCol = Math.Max(region.Start.Col, range.Start.Col);
                    var clippedEndRow = Math.Min(region.End.Row, range.End.Row);
                    var clippedEndCol = Math.Min(region.End.Col, range.End.Col);
                    var clippedRegion = new GridRange(
                        new CellAddress(range.Start.Sheet, clippedStartRow, clippedStartCol),
                        new CellAddress(range.Start.Sheet, clippedEndRow, clippedEndCol));
                    anchors[(clippedStartRow, clippedStartCol)] = clippedRegion;
                    foreach (var addr in clippedRegion.AllCells())
                    {
                        if (addr.Row != clippedStartRow || addr.Col != clippedStartCol)
                            covered.Add((addr.Row, addr.Col));
                    }
                }
            }
        }

        var body = new StringBuilder();
        body.Append("<table border=\"1\" cellspacing=\"0\" style=\"border-collapse:collapse\">");
        for (var r = range.Start.Row; r <= range.End.Row; r++)
        {
            body.Append("<tr>");
            for (var c = range.Start.Col; c <= range.End.Col; c++)
            {
                if (covered.Contains((r, c)))
                    continue;

                var spanAttrs = "";
                if (anchors.TryGetValue((r, c), out var region))
                {
                    var colspan = Math.Min(region.ColCount, range.End.Col - c + 1);
                    var rowspan = Math.Min(region.RowCount, range.End.Row - r + 1);
                    if (colspan > 1) spanAttrs += $" colspan=\"{colspan}\"";
                    if (rowspan > 1) spanAttrs += $" rowspan=\"{rowspan}\"";
                }

                cellLookup.TryGetValue((r, c), out var displayCell);
                var css = displayCell.Style is { } cellStyle ? BuildCellCss(cellStyle, theme) : "";
                var styleAttr = css.Length > 0 ? $" style=\"{css}\"" : "";
                var display = EscapeHtml(displayCell.DisplayText ?? "");
                body.Append($"<td{spanAttrs}{styleAttr}>{display}</td>");
            }
            body.Append("</tr>");
        }
        body.Append("</table>");

        return body.ToString();
    }

    private static bool RangesOverlap(GridRange a, GridRange b) =>
        a.Start.Row <= b.End.Row && a.End.Row >= b.Start.Row &&
        a.Start.Col <= b.End.Col && a.End.Col >= b.Start.Col;

    private static string BuildCellCss(CellStyle style, WorkbookTheme theme)
    {
        var sb = new StringBuilder();

        if (style.Bold) sb.Append("font-weight:bold;");
        if (style.Italic) sb.Append("font-style:italic;");
        if (style.Underline || style.DoubleUnderline) sb.Append("text-decoration:underline;");
        if (style.Strikethrough) sb.Append("text-decoration:line-through;");

        var fontName = style.ResolveEffectiveFontName(theme);
        if (!string.Equals(fontName, "Calibri", StringComparison.Ordinal))
            sb.Append($"font-family:'{fontName.Replace("'", "", StringComparison.Ordinal)}';");
        if (Math.Abs(style.FontSize - 11) > 0.001)
            sb.Append($"font-size:{style.FontSize.ToString("0.##", CultureInfo.InvariantCulture)}pt;");

        var fontColor = style.ResolveFontColor(theme);
        if (!fontColor.IsBlack)
            sb.Append($"color:{HexColor(fontColor)};");

        var fill = style.ResolveFillColor(theme);
        if (fill is { } f)
            sb.Append($"background-color:{HexColor(f)};");

        var align = style.HorizontalAlignment switch
        {
            FreeX.Core.Model.HorizontalAlignment.Left => "left",
            FreeX.Core.Model.HorizontalAlignment.Center => "center",
            FreeX.Core.Model.HorizontalAlignment.Right => "right",
            FreeX.Core.Model.HorizontalAlignment.Justify => "justify",
            _ => null,
        };
        if (align is not null)
            sb.Append($"text-align:{align};");

        AppendBorderCss(sb, "top", style.BorderTop);
        AppendBorderCss(sb, "right", style.BorderRight);
        AppendBorderCss(sb, "bottom", style.BorderBottom);
        AppendBorderCss(sb, "left", style.BorderLeft);

        return sb.ToString();
    }

    private static void AppendBorderCss(StringBuilder sb, string edge, CellBorder border)
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
        sb.Append($"border-{edge}:{width} {line} {HexColor(border.Color)};");
    }

    private static string HexColor(CellColor c) =>
        $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string EscapeHtml(string text) =>
        text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
