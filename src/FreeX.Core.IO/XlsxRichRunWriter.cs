using System.Globalization;
using System.Xml.Linq;
using Free.Shared.Opc;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Writes OOXML inline-string rich-run elements (<c>&lt;is&gt;&lt;r&gt;&lt;rPr&gt;…</c>).
/// Extracted here so unit tests can exercise the XML-generation logic directly
/// without going through the full patch-save pipeline.
/// </summary>
internal static class XlsxRichRunWriter
{
    /// <summary>
    /// Creates a <c>&lt;is&gt;</c> element with one <c>&lt;r&gt;</c> child per run.
    /// <para>
    /// <c>&lt;rPr&gt;</c> children are emitted in CT_RPrElt schema order:<br/>
    /// <c>rFont, charset, family, b, i, strike, outline, shadow, condense, extend,
    /// color, sz, u, vertAlign, scheme</c>
    /// (ECMA-376 Part 1 §18.4.4).  Any child not present in a run is simply omitted.
    /// </para>
    /// </summary>
    internal static XElement CreateRichInlineStringElement(
        XNamespace worksheetNs,
        IReadOnlyList<CellTextRun> runs,
        CellPhoneticGuide? phoneticGuide = null)
    {
        var is_ = new XElement(worksheetNs + "is");
        foreach (var run in runs)
        {
            var r = new XElement(worksheetNs + "r");

            // Build <rPr> only when there is at least one non-null property.
            if (run.Bold is not null ||
                run.Italic is not null ||
                run.Underline is not null ||
                run.Strikethrough is not null ||
                run.FontName is not null ||
                run.FontSize is not null ||
                run.FontColor is not null ||
                run.VertAlign != CellTextRunVertAlign.None ||
                run.Charset is not null ||
                run.Family is not null ||
                run.Scheme is not null)
            {
                var rPr = new XElement(worksheetNs + "rPr");

                // Emit rPr children in OOXML CT_RPrElt schema order:
                // rFont, charset, family, b, i, strike, outline, shadow, condense, extend, color, sz, u, vertAlign, scheme
                if (run.FontName is { } rFont)
                    rPr.Add(new XElement(worksheetNs + "rFont",
                        new XAttribute("val", rFont)));

                if (run.Charset is { } charset)
                    rPr.Add(new XElement(worksheetNs + "charset",
                        new XAttribute("val", charset.ToString(CultureInfo.InvariantCulture))));

                if (run.Family is { } family)
                    rPr.Add(new XElement(worksheetNs + "family",
                        new XAttribute("val", family.ToString(CultureInfo.InvariantCulture))));

                if (run.Bold is { } b)
                {
                    var bEl = new XElement(worksheetNs + "b");
                    if (!b) bEl.SetAttributeValue("val", "0");
                    rPr.Add(bEl);
                }

                if (run.Italic is { } i)
                {
                    var iEl = new XElement(worksheetNs + "i");
                    if (!i) iEl.SetAttributeValue("val", "0");
                    rPr.Add(iEl);
                }

                if (run.Strikethrough is { } strike)
                {
                    var strikeEl = new XElement(worksheetNs + "strike");
                    if (!strike) strikeEl.SetAttributeValue("val", "0");
                    rPr.Add(strikeEl);
                }

                if (run.FontColor is { } runColor)
                    rPr.Add(CreateRunColorElement(worksheetNs, runColor));

                if (run.FontSize is { } sz)
                    rPr.Add(new XElement(worksheetNs + "sz",
                        new XAttribute("val", sz.ToString(CultureInfo.InvariantCulture))));

                if (run.Underline is { } u)
                {
                    var uEl = new XElement(worksheetNs + "u");
                    if (!u)
                        uEl.SetAttributeValue("val", "none");
                    else if (run.DoubleUnderline == true)
                        // R32: preserve double/double-accounting underline (read back as "double";
                        // OOXML re-reads this identically to Excel's own doubleAccounting on most
                        // consumers, and matches CellStyle's DoubleUnderline collapse behavior).
                        uEl.SetAttributeValue("val", "double");
                    // When u=true (single) and DoubleUnderline is not true, omit val attribute —
                    // OOXML default is "single".
                    rPr.Add(uEl);
                }

                if (run.VertAlign != CellTextRunVertAlign.None)
                    rPr.Add(new XElement(worksheetNs + "vertAlign",
                        new XAttribute("val",
                            run.VertAlign == CellTextRunVertAlign.Superscript
                                ? "superscript"
                                : "subscript")));

                if (run.Scheme is { } scheme)
                    rPr.Add(new XElement(worksheetNs + "scheme",
                        new XAttribute("val", scheme)));

                r.Add(rPr);
            }

            r.Add(CreateInlineTextElement(worksheetNs, run.Text));
            is_.Add(r);
        }

        if (phoneticGuide is { } guide)
            AppendPhoneticGuide(is_, guide);

        return is_;
    }

    /// <summary>
    /// Re-emits a preserved phonetic guide's <c>&lt;rPh&gt;</c> run(s) and <c>&lt;phoneticPr&gt;</c>
    /// element (see <see cref="CellPhoneticGuide"/>) after the <c>&lt;r&gt;</c> children, per
    /// CT_Rst schema order (<c>t?, r*, rPh*, phoneticPr?</c>). Malformed preserved XML is skipped
    /// defensively rather than throwing, mirroring <c>ConditionalFormatNativeMetadata</c>'s
    /// native-passthrough parsing.
    /// </summary>
    private static void AppendPhoneticGuide(XElement is_, CellPhoneticGuide guide)
    {
        foreach (var rawRPh in guide.RunPhoneticXmls)
        {
            if (TryParseNativeXml(rawRPh) is { } rPhElement)
                is_.Add(rPhElement);
        }

        if (guide.PhoneticPropertiesXml is { } rawPhoneticPr &&
            TryParseNativeXml(rawPhoneticPr) is { } phoneticPrElement)
        {
            is_.Add(phoneticPrElement);
        }
    }

    private static XElement? TryParseNativeXml(string xml)
    {
        try
        {
            return XElement.Parse(xml);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Emits a <c>&lt;color&gt;</c> element that preserves the original color-reference kind
    /// (theme, indexed, rgb, or auto) so round-trips do not flatten theme colors to RGB.
    /// </summary>
    internal static XElement CreateRunColorElement(XNamespace ns, CellRunColor color) =>
        color.Kind switch
        {
            CellRunColorKind.Theme =>
                color.Tint is { } tint && Math.Abs(tint) > 0.000001
                    ? new XElement(ns + "color",
                        new XAttribute("theme", color.ThemeIndex),
                        new XAttribute("tint", tint.ToString("G", CultureInfo.InvariantCulture)))
                    : new XElement(ns + "color",
                        new XAttribute("theme", color.ThemeIndex)),
            CellRunColorKind.Indexed =>
                new XElement(ns + "color",
                    new XAttribute("indexed", color.IndexedIndex)),
            CellRunColorKind.Auto =>
                new XElement(ns + "color",
                    new XAttribute("auto", "1")),
            _ => // Rgb (default)
                new XElement(ns + "color",
                    new XAttribute("rgb",
                        $"FF{color.Rgb.R:X2}{color.Rgb.G:X2}{color.Rgb.B:X2}")),
        };

    /// <summary>
    /// Creates a <c>&lt;t&gt;</c> element for inline text, setting <c>xml:space="preserve"</c>
    /// when the text starts or ends with whitespace (OOXML requirement).
    /// Illegal XML characters (U+0001–U+0008, U+000B–U+000C, U+000E–U+001F, etc.) are
    /// stripped via <see cref="XlsxXmlTextEscaper.EscapeForXml"/>.
    /// </summary>
    internal static XElement CreateInlineTextElement(XNamespace worksheetNs, string text)
    {
        var escaped = XlsxXmlTextEscaper.EscapeForXml(text);
        var t = new XElement(worksheetNs + "t", escaped);
        if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])))
            t.SetAttributeValue(XNamespace.Xml + "space", "preserve");
        return t;
    }
}
