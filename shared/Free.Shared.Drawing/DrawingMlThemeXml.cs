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

        fontScheme.Element(DrawingNamespace + "majorFont")?
            .Element(DrawingNamespace + "latin")?
            .SetAttributeValue("typeface", majorLatinFont);
        fontScheme.Element(DrawingNamespace + "minorFont")?
            .Element(DrawingNamespace + "latin")?
            .SetAttributeValue("typeface", minorLatinFont);
        return fontScheme;
    }
}
