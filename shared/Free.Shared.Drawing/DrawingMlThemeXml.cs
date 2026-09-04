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
