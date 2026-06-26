using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Parses OOXML per-run rich-text formatting from inline-string <c>&lt;is&gt;</c> or
/// shared-string <c>&lt;si&gt;</c> elements.
/// </summary>
internal static class XlsxRichRunReader
{
    /// <summary>
    /// Reads rich-text runs from an <c>&lt;is&gt;</c> or <c>&lt;si&gt;</c> XElement.
    /// Returns <c>null</c> when the element has no <c>&lt;r&gt;</c> children (plain text).
    /// Returns <c>null</c> when there is exactly one run with no per-run formatting deviations
    /// (single-run unstyled text does not need the parallel map).
    /// </summary>
    /// <param name="richStringElement">
    /// The <c>&lt;is&gt;</c> or <c>&lt;si&gt;</c> element.
    /// </param>
    /// <param name="workbookNs">The worksheet/shared-string XML namespace.</param>
    /// <param name="theme">Workbook theme for resolving theme-color references.</param>
    /// <param name="indexedColors">Indexed-color palette for resolving legacy indexed colors.</param>
    public static IReadOnlyList<CellTextRun>? ReadRuns(
        XElement richStringElement,
        XNamespace workbookNs,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        var runElements = richStringElement.Elements(workbookNs + "r").ToList();
        if (runElements.Count == 0)
            return null;

        var runs = new List<CellTextRun>(runElements.Count);
        var anyFormatting = false;

        foreach (var runElement in runElements)
        {
            var rPr = runElement.Element(workbookNs + "rPr");
            var t   = runElement.Element(workbookNs + "t");
            var text = t?.Value ?? string.Empty;

            bool?  bold          = null;
            bool?  italic        = null;
            bool?  underline     = null;
            bool?  strikethrough = null;
            string? fontName     = null;
            double? fontSize     = null;
            CellColor? fontColor = null;
            var vertAlign        = CellTextRunVertAlign.None;

            if (rPr is not null)
            {
                // Bold: <b/> present and not val="0"
                var bEl = rPr.Element(workbookNs + "b");
                if (bEl is not null)
                {
                    bold = ParseBoolAttribute(bEl, "val", defaultWhenPresent: true);
                    anyFormatting = true;
                }

                // Italic: <i/>
                var iEl = rPr.Element(workbookNs + "i");
                if (iEl is not null)
                {
                    italic = ParseBoolAttribute(iEl, "val", defaultWhenPresent: true);
                    anyFormatting = true;
                }

                // Underline: <u/> (any non-"none" value counts)
                var uEl = rPr.Element(workbookNs + "u");
                if (uEl is not null)
                {
                    var uVal = uEl.Attribute("val")?.Value;
                    underline = !string.Equals(uVal, "none", StringComparison.OrdinalIgnoreCase);
                    anyFormatting = true;
                }

                // Strikethrough: <strike/>
                var strikeEl = rPr.Element(workbookNs + "strike");
                if (strikeEl is not null)
                {
                    strikethrough = ParseBoolAttribute(strikeEl, "val", defaultWhenPresent: true);
                    anyFormatting = true;
                }

                // Font name: <rFont val="…"/>
                var rFontEl = rPr.Element(workbookNs + "rFont");
                var rFontVal = rFontEl?.Attribute("val")?.Value;
                if (!string.IsNullOrWhiteSpace(rFontVal))
                {
                    fontName = rFontVal;
                    anyFormatting = true;
                }

                // Font size: <sz val="…"/> in half-points? No — OOXML sz is in points.
                var szEl = rPr.Element(workbookNs + "sz");
                var szVal = szEl?.Attribute("val")?.Value;
                if (!string.IsNullOrWhiteSpace(szVal) &&
                    double.TryParse(szVal, NumberStyles.Float, CultureInfo.InvariantCulture, out var pts) &&
                    pts > 0)
                {
                    fontSize = pts;
                    anyFormatting = true;
                }

                // Font color: <color rgb|theme|indexed …/>
                var colorEl = rPr.Element(workbookNs + "color");
                if (colorEl is not null &&
                    XlsxColorReader.TryReadCellColor(colorEl, theme, indexedColors, out var parsedColor))
                {
                    fontColor = parsedColor;
                    anyFormatting = true;
                }

                // Vertical alignment: <vertAlign val="superscript|subscript"/>
                var vaEl = rPr.Element(workbookNs + "vertAlign");
                var vaVal = vaEl?.Attribute("val")?.Value;
                if (!string.IsNullOrWhiteSpace(vaVal))
                {
                    vertAlign = vaVal.Equals("superscript", StringComparison.OrdinalIgnoreCase)
                        ? CellTextRunVertAlign.Superscript
                        : vaVal.Equals("subscript", StringComparison.OrdinalIgnoreCase)
                            ? CellTextRunVertAlign.Subscript
                            : CellTextRunVertAlign.None;
                    if (vertAlign != CellTextRunVertAlign.None)
                        anyFormatting = true;
                }
            }

            runs.Add(new CellTextRun(text, bold, italic, underline, strikethrough, fontName, fontSize, fontColor, vertAlign));
        }

        // Single unstyled run → no need to populate the parallel map.
        if (runs.Count == 1 && !anyFormatting)
            return null;

        // Multiple runs always qualify, even if individually unstyled,
        // because the split itself is meaningful for later render passes.
        if (runs.Count > 1)
            return runs;

        // Single run with formatting.
        return anyFormatting ? runs : null;
    }

    private static bool ParseBoolAttribute(XElement element, string attributeName, bool defaultWhenPresent)
    {
        var val = element.Attribute(attributeName)?.Value;
        if (string.IsNullOrWhiteSpace(val))
            return defaultWhenPresent;
        return !string.Equals(val, "0", StringComparison.Ordinal) &&
               !string.Equals(val, "false", StringComparison.OrdinalIgnoreCase);
    }
}
