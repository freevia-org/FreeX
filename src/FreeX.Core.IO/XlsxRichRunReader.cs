using System.Globalization;
using System.Text;
using System.Xml.Linq;
using FreeX.Core.Model;
// NOTE: XlsxColorReader resolves theme/indexed to RGB; for rich runs we preserve
// the original color-reference kind via TryReadRunColor below (EE6 fix).

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
            // R18: decode OOXML _xHHHH_ escapes (e.g. _x000D_ -> CR) here — this reads the raw
            // <t> text directly via XLinq, bypassing ClosedXML's own shared-/inline-string reader,
            // which is what normally performs this decode for the plain-text value path.
            var text = DecodeRunText(t?.Value ?? string.Empty);

            bool?  bold          = null;
            bool?  italic        = null;
            bool?  underline     = null;
            bool?  doubleUnderline = null;
            bool?  strikethrough = null;
            string? fontName     = null;
            double? fontSize     = null;
            CellRunColor? fontColor = null;
            var vertAlign        = CellTextRunVertAlign.None;
            int?    charset      = null;
            int?    family       = null;
            string? scheme       = null;

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

                // Underline: <u/> (any non-"none" value counts). "double"/"doubleAccounting"
                // are additionally flagged via DoubleUnderline (R32: previously collapsed to a
                // bare bool, silently downgrading double underline to single on the next save).
                var uEl = rPr.Element(workbookNs + "u");
                if (uEl is not null)
                {
                    var uVal = uEl.Attribute("val")?.Value;
                    underline = !string.Equals(uVal, "none", StringComparison.OrdinalIgnoreCase);
                    if (underline == true)
                        doubleUnderline = string.Equals(uVal, "double", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(uVal, "doubleAccounting", StringComparison.OrdinalIgnoreCase);
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

                // Charset: <charset val="…"/> (R32: raw OOXML charset code, e.g. 128 = ShiftJIS)
                var charsetEl = rPr.Element(workbookNs + "charset");
                var charsetVal = charsetEl?.Attribute("val")?.Value;
                if (!string.IsNullOrWhiteSpace(charsetVal) &&
                    int.TryParse(charsetVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var charsetNum))
                {
                    charset = charsetNum;
                    anyFormatting = true;
                }

                // Family: <family val="…"/> (R32: raw OOXML font-family-numbering code, 0-5)
                var familyEl = rPr.Element(workbookNs + "family");
                var familyVal = familyEl?.Attribute("val")?.Value;
                if (!string.IsNullOrWhiteSpace(familyVal) &&
                    int.TryParse(familyVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var familyNum))
                {
                    family = familyNum;
                    anyFormatting = true;
                }

                // Scheme: <scheme val="major|minor|none"/> (R32: theme-font-following hint)
                var schemeEl = rPr.Element(workbookNs + "scheme");
                var schemeVal = schemeEl?.Attribute("val")?.Value;
                if (!string.IsNullOrWhiteSpace(schemeVal))
                {
                    scheme = schemeVal;
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

                // Font color: <color rgb|theme|indexed|auto …/>
                // Preserve the original reference kind (EE6) rather than resolving to RGB.
                var colorEl = rPr.Element(workbookNs + "color");
                if (colorEl is not null && TryReadRunColor(colorEl, out var parsedRunColor))
                {
                    fontColor = parsedRunColor;
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

            runs.Add(new CellTextRun(text, bold, italic, underline, strikethrough, fontName, fontSize, fontColor, vertAlign,
                doubleUnderline, charset, family, scheme));
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

    /// <summary>
    /// Reads a cell's phonetic-guide (furigana) markup -- the <c>&lt;rPh&gt;</c> run(s) and the
    /// <c>&lt;phoneticPr&gt;</c> element -- from an <c>&lt;is&gt;</c> or <c>&lt;si&gt;</c>
    /// element, captured verbatim as native XML passthrough (see <see cref="CellPhoneticGuide"/>).
    /// Returns <c>null</c> when the element has neither.
    /// </summary>
    public static CellPhoneticGuide? ReadPhoneticGuide(XElement richStringElement, XNamespace workbookNs)
    {
        var rPhElements = richStringElement.Elements(workbookNs + "rPh").ToList();
        var phoneticPrElement = richStringElement.Element(workbookNs + "phoneticPr");
        if (rPhElements.Count == 0 && phoneticPrElement is null)
            return null;

        var runPhoneticXmls = rPhElements.Count == 0
            ? Array.Empty<string>()
            : rPhElements.Select(e => e.ToString(SaveOptions.DisableFormatting)).ToArray();

        return new CellPhoneticGuide(
            runPhoneticXmls,
            phoneticPrElement?.ToString(SaveOptions.DisableFormatting));
    }

    /// <summary>
    /// Reads a <c>&lt;color&gt;</c> element and returns a <see cref="CellRunColor"/> that
    /// preserves the original reference kind (theme index, indexed palette, explicit RGB, or auto)
    /// so the writer can round-trip the same form without flattening to RGB.
    /// </summary>
    private static bool TryReadRunColor(XElement element, out CellRunColor color)
    {
        color = default;

        // auto="1"
        var autoVal = element.Attribute("auto")?.Value;
        if (!string.IsNullOrWhiteSpace(autoVal) &&
            (autoVal == "1" || autoVal.Equals("true", StringComparison.OrdinalIgnoreCase)))
        {
            color = CellRunColor.Auto();
            return true;
        }

        // theme="N" [tint="T"]
        var themeText = element.Attribute("theme")?.Value;
        if (!string.IsNullOrWhiteSpace(themeText) &&
            int.TryParse(themeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var themeIndex))
        {
            var tintText = element.Attribute("tint")?.Value;
            var tint = !string.IsNullOrWhiteSpace(tintText) &&
                       double.TryParse(tintText, NumberStyles.Float, CultureInfo.InvariantCulture, out var t)
                           ? t : 0d;
            color = CellRunColor.FromTheme(themeIndex, tint);
            return true;
        }

        // indexed="N"
        var indexedText = element.Attribute("indexed")?.Value;
        if (!string.IsNullOrWhiteSpace(indexedText) &&
            int.TryParse(indexedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var indexedIndex))
        {
            color = CellRunColor.FromIndexed(indexedIndex);
            return true;
        }

        // rgb="[AA]RRGGBB"
        var rgb = element.Attribute("rgb")?.Value;
        if (!string.IsNullOrWhiteSpace(rgb) &&
            XlsxColorReader.TryReadCellColor(element, out var cellColor))
        {
            color = CellRunColor.FromRgb(cellColor);
            return true;
        }

        return false;
    }

    private static bool ParseBoolAttribute(XElement element, string attributeName, bool defaultWhenPresent)
    {
        var val = element.Attribute(attributeName)?.Value;
        if (string.IsNullOrWhiteSpace(val))
            return defaultWhenPresent;
        return !string.Equals(val, "0", StringComparison.Ordinal) &&
               !string.Equals(val, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Decodes OOXML <c>_xHHHH_</c> escape sequences in a rich run's raw <c>&lt;t&gt;</c> text
    /// (see <c>Free.Shared.Opc.XlsxXmlTextEscaper</c> for the matching encode side). Excel/ClosedXML
    /// escape U+000D (CR) and other XML-invalid characters this way on save, and also pre-escape any
    /// literal <c>_xHHHH_</c>-looking text so it survives a round-trip undecoded. The normal cell-value
    /// read path gets this decode for free from ClosedXML's own shared-/inline-string reader, but rich
    /// runs are parsed directly from the raw XML here, so without this the escape text would be stored
    /// verbatim (e.g. "Line1_x000D_Line2" instead of "Line1\rLine2") and re-escaped on every subsequent
    /// save, compounding.
    /// </summary>
    private static string DecodeRunText(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf("_x", StringComparison.OrdinalIgnoreCase) < 0)
            return text;

        var builder = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (TryReadTextEscape(text, i, out var code, out var consumed))
            {
                builder.Append((char)code);
                i += consumed;
                continue;
            }

            builder.Append(text[i]);
            i++;
        }

        return builder.ToString();
    }

    /// <summary>Parses a <c>_xHHHH_</c> escape at <paramref name="start"/>; HHHH is exactly 4 hex digits.</summary>
    private static bool TryReadTextEscape(string text, int start, out int code, out int consumed)
    {
        code = 0;
        consumed = 0;
        const int length = 7; // "_xHHHH_"
        if (start < 0 || start + length > text.Length)
            return false;
        if (text[start] != '_' || (text[start + 1] != 'x' && text[start + 1] != 'X') || text[start + 6] != '_')
            return false;

        var value = 0;
        for (var j = start + 2; j < start + 6; j++)
        {
            var digit = HexDigit(text[j]);
            if (digit < 0)
                return false;
            value = (value << 4) | digit;
        }

        code = value;
        consumed = length;
        return true;
    }

    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };
}
