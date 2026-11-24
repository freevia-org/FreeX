using System.Xml;
using System.Xml.Linq;

namespace Free.Shared.Drawing;

/// <summary>
/// Preserves native DrawingML theme details while applying modeled theme edits.
/// </summary>
public static class DrawingMlThemeXml
{
    private static readonly XNamespace DrawingNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    // ---- generated theme parts -------------------------------------------------------------------
    //
    // FreeX (xl/theme/theme1.xml), FreeW (word/theme/theme1.xml) and FreeP (ppt/theme/themeN.xml) each
    // carried a private copy of the same a:theme scaffold: the twelve-slot a:clrScheme, the
    // latin/ea/cs a:fontScheme and the phClr placeholder a:fmtScheme entries. The pieces below are that
    // scaffold. What each app puts INTO it stays the caller's decision and is an explicit parameter, never
    // a silent default: which supplement elements follow a:themeElements, whether a generated a:ln carries
    // cap/cmpd/algn (FreeX and FreeW emit them, FreeP does not), and whether a short native fmtScheme is
    // padded (FreeP pads, FreeX keeps a native scheme verbatim; the padding stays in FreeP).

    /// <summary>A generated <c>a:clrScheme</c>: one <c>a:{slot}/a:srgbClr</c> per (slot, hex) pair, in order.</summary>
    public static XElement CreateColorScheme(string name, IEnumerable<KeyValuePair<string, string>> slotColors)
    {
        ArgumentNullException.ThrowIfNull(slotColors);

        return new XElement(DrawingNamespace + "clrScheme",
            new XAttribute("name", name),
            slotColors.Select(slot =>
                new XElement(DrawingNamespace + slot.Key,
                    new XElement(DrawingNamespace + "srgbClr", new XAttribute("val", slot.Value)))));
    }

    /// <summary>
    /// A generated <c>a:fontScheme</c>. CT_FontCollection requires <c>latin</c>, <c>ea</c> and <c>cs</c>
    /// in that order; the east-asian and complex-script faces are empty (inherit), as in Office's own
    /// built-in themes.
    /// </summary>
    public static XElement CreateFontScheme(string name, string majorLatinFont, string minorLatinFont) =>
        new(DrawingNamespace + "fontScheme",
            new XAttribute("name", name),
            CreateFontCollection("majorFont", majorLatinFont),
            CreateFontCollection("minorFont", minorLatinFont));

    private static XElement CreateFontCollection(string collectionName, string latinTypeface) =>
        new(DrawingNamespace + collectionName,
            new XElement(DrawingNamespace + "latin", new XAttribute("typeface", latinTypeface)),
            new XElement(DrawingNamespace + "ea", new XAttribute("typeface", string.Empty)),
            new XElement(DrawingNamespace + "cs", new XAttribute("typeface", string.Empty)));

    /// <summary>The <c>a:solidFill/a:schemeClr val="phClr"</c> placeholder fill every theme style list uses.</summary>
    public static XElement CreateSolidPhClrFill() =>
        new(DrawingNamespace + "solidFill",
            new XElement(DrawingNamespace + "schemeClr", new XAttribute("val", "phClr")));

    /// <summary>
    /// A generated <c>a:ln</c> style-list entry (phClr solid fill, solid dash). When
    /// <paramref name="includeCapCompoundAlign"/> is true the line also carries
    /// <c>cap="flat" cmpd="sng" algn="ctr"</c> (FreeX and FreeW); FreeP omits them.
    /// </summary>
    public static XElement CreateLineStyle(long widthEmu, bool includeCapCompoundAlign)
    {
        var line = new XElement(DrawingNamespace + "ln", new XAttribute("w", widthEmu));
        if (includeCapCompoundAlign)
        {
            line.Add(
                new XAttribute("cap", "flat"),
                new XAttribute("cmpd", "sng"),
                new XAttribute("algn", "ctr"));
        }

        line.Add(
            CreateSolidPhClrFill(),
            new XElement(DrawingNamespace + "prstDash", new XAttribute("val", "solid")));
        return line;
    }

    /// <summary>An <c>a:effectStyle</c> with an empty <c>a:effectLst</c> (no effects).</summary>
    public static XElement CreateEmptyEffectStyle() =>
        new(DrawingNamespace + "effectStyle", new XElement(DrawingNamespace + "effectLst"));

    /// <summary>
    /// The <c>a:fmtScheme</c> (CT_StyleMatrix) wrapper around its four style lists. Each list must hold at
    /// least three entries; supplying them is the caller's responsibility (this does not pad).
    /// </summary>
    public static XElement CreateFormatScheme(
        string name,
        IEnumerable<XElement> fillStyles,
        IEnumerable<XElement> lineStyles,
        IEnumerable<XElement> effectStyles,
        IEnumerable<XElement> backgroundFillStyles) =>
        new(DrawingNamespace + "fmtScheme",
            new XAttribute("name", name),
            new XElement(DrawingNamespace + "fillStyleLst", fillStyles),
            new XElement(DrawingNamespace + "lnStyleLst", lineStyles),
            new XElement(DrawingNamespace + "effectStyleLst", effectStyles),
            new XElement(DrawingNamespace + "bgFillStyleLst", backgroundFillStyles));

    /// <summary>
    /// The <c>a:theme</c> document: <c>a:themeElements</c> (colour, font and format scheme) followed by
    /// the caller's <paramref name="supplementElements"/> (<c>a:objectDefaults</c>,
    /// <c>a:extraClrSchemeLst</c>, ...). <paramref name="declaration"/> is set on the document when not
    /// null (FreeX and FreeP write <c>standalone="yes"</c>; FreeW's part writer supplies its own).
    /// </summary>
    public static XDocument CreateThemeDocument(
        string themeName,
        XElement colorScheme,
        XElement fontScheme,
        XElement formatScheme,
        IEnumerable<XElement> supplementElements,
        XDeclaration? declaration)
    {
        var theme = new XElement(DrawingNamespace + "theme",
            new XAttribute(XNamespace.Xmlns + "a", DrawingNamespace),
            new XAttribute("name", themeName),
            new XElement(DrawingNamespace + "themeElements", colorScheme, fontScheme, formatScheme),
            supplementElements);

        return declaration is null
            ? new XDocument(theme)
            : new XDocument(declaration, theme);
    }

    /// <summary>
    /// Parses a native <c>a:fontScheme</c> and patches only its major and minor Latin typefaces.
    /// Returns <see langword="null"/> when the source is blank, malformed, or is not a DrawingML
    /// font scheme.
    /// </summary>
    public static XElement? TryPatchNativeFontScheme(
        string? fontSchemeXml,
        string majorLatinFont,
        string minorLatinFont)
    {
        if (string.IsNullOrWhiteSpace(fontSchemeXml))
            return null;

        XElement fontScheme;
        try
        {
            fontScheme = XElement.Parse(fontSchemeXml);
        }
        catch (XmlException)
        {
            // Keep malformed native XML non-fatal for WorkbookTheme.WithFonts and
            // PptxPackageWriter. Audited malformed inputs in this lane (partial tags, lone
            // surrogates, and disallowed control characters) all surface here as XmlException,
            // so we intentionally keep the catch narrow and let unexpected failures escape.
            return null;
        }

        if (fontScheme.Name != DrawingNamespace + "fontScheme")
            return null;

        ApplyLatinTypeface(fontScheme, "majorFont", majorLatinFont);
        ApplyLatinTypeface(fontScheme, "minorFont", minorLatinFont);
        return fontScheme;
    }

    /// <summary>
    /// r351: sets one font collection's Latin typeface, creating whatever the source omitted.
    ///
    /// <para>This was a <c>?.</c> chain, so a source <c>a:fontScheme</c> missing <c>a:majorFont</c>
    /// or its <c>a:latin</c> silently swallowed the edit: the caller's chosen typeface was dropped
    /// and the unchanged XML returned as if patched. It also returned XML the schema rejects --
    /// <c>CT_FontCollection</c> requires <c>latin</c>, <c>ea</c> and <c>cs</c>, in that order --
    /// so the saved file was invalid on top of being wrong. Both apps patch through here
    /// (<c>WorkbookTheme.WithFonts</c> and <c>PptxPackageWriter</c>), so both lost the edit.</para>
    ///
    /// <para>Order matters and is enforced rather than assumed: the three required children are
    /// re-seated at the front in schema order, which leaves any script-specific <c>a:font</c>
    /// children that follow them untouched.</para>
    /// </summary>
    private static void ApplyLatinTypeface(XElement fontScheme, string collectionName, string typeface)
    {
        var collection = fontScheme.Element(DrawingNamespace + collectionName);
        if (collection is null)
        {
            collection = new XElement(DrawingNamespace + collectionName);

            // majorFont precedes minorFont in CT_FontScheme.
            if (collectionName == "majorFont")
                fontScheme.AddFirst(collection);
            else
                fontScheme.Add(collection);
        }

        var latin = EnsureChild(collection, "latin");
        var ea = EnsureChild(collection, "ea");
        var cs = EnsureChild(collection, "cs");

        latin.SetAttributeValue("typeface", typeface);

        // Re-seat in schema order (latin, ea, cs), preserving any a:font children after them.
        cs.Remove();
        ea.Remove();
        latin.Remove();
        collection.AddFirst(latin, ea, cs);
    }

    private static XElement EnsureChild(XElement parent, string name)
    {
        var existing = parent.Element(DrawingNamespace + name);
        if (existing is not null)
            return existing;

        var created = new XElement(DrawingNamespace + name, new XAttribute("typeface", string.Empty));
        parent.Add(created);
        return created;
    }
}
