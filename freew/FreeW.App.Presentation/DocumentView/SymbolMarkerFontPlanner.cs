using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Picks the font a list marker made of symbol-font private-use code points (U+F020..U+F0FF, Word's
/// convention for Symbol/Wingdings/Webdings glyphs) must be drawn in. A modelled list keeps only the
/// level text, so the font is recovered render-side from the preserved numbering definitions: the first
/// level whose <c>w:lvlText</c> is the same marker and names a font. Failing that, Word's own default
/// bullet fonts are assumed (Symbol for its round bullet, Wingdings for its other gallery bullets).
/// Never changes the model.
/// </summary>
public static class SymbolMarkerFontPlanner
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>True when <paramref name="text"/> carries a symbol-font private-use code point.</summary>
    public static bool ContainsSymbolCodePoint(string? text) =>
        !string.IsNullOrEmpty(text) && text.Any(ch => ch is >= '' and <= '');

    /// <summary>
    /// The font for <paramref name="markerText"/>, or null when it holds no symbol-font code point and
    /// the paragraph's own font applies.
    /// </summary>
    public static string? ResolveMarkerFont(TextDocument document, string markerText)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!ContainsSymbolCodePoint(markerText))
            return null;

        var trimmed = markerText.Trim();
        if (document.Preserved.OriginalNumbering is { } numbering)
        {
            foreach (var level in numbering.Descendants(W + "lvl"))
            {
                if (!string.Equals(level.Element(W + "lvlText")?.Attribute(W + "val")?.Value, trimmed, StringComparison.Ordinal))
                    continue;

                var fonts = level.Element(W + "rPr")?.Element(W + "rFonts");
                var font = fonts?.Attribute(W + "ascii")?.Value ?? fonts?.Attribute(W + "hAnsi")?.Value;
                if (!string.IsNullOrWhiteSpace(font))
                    return font;
            }
        }

        // Word's bullet gallery: U+F0B7 is Symbol's round bullet; its square, arrow, diamond and check
        // bullets (U+F0A7, U+F0D8, U+F076, U+F0FC, ...) are Wingdings glyphs.
        return trimmed.Contains('') ? "Symbol" : "Wingdings";
    }
}
