namespace FreeW.Core.Model;

/// <summary>
/// A built-in document theme: a named colour/font scheme that, when applied, rewrites the document's
/// style catalog so headings, the title and body text take on a coordinated look (mirroring Word's
/// "Themes" / "Colors + Fonts" pairing). A theme is deliberately small and pure data — a heading font,
/// a body font, and a tiny palette (a primary colour for the Title, plus two heading colours).
/// <see cref="Apply"/> is the only behaviour and it is deterministic.
/// </summary>
/// <param name="Name">The theme's display name (shown in the Design ribbon dropdown).</param>
/// <param name="HeadingFont">Font family for Title + Heading styles.</param>
/// <param name="BodyFont">Font family for Normal body text and the document default run.</param>
/// <param name="PrimaryColorHex">Primary accent colour (RRGGBB hex) used for the Title style.</param>
/// <param name="HeadingColorHex">Colour for Heading 1 / Heading 2 (RRGGBB hex).</param>
/// <param name="HeadingAccentColorHex">A darker accent for Heading 3 (RRGGBB hex).</param>
public sealed record DocumentTheme(
    string Name,
    string HeadingFont,
    string BodyFont,
    string PrimaryColorHex,
    string HeadingColorHex,
    string HeadingAccentColorHex)
{
    /// <summary>
    /// The built-in theme catalog, in display order. "Office" reproduces the model's default
    /// styles (so it is a no-op baseline); the rest pick distinct font pairings and palettes.
    /// </summary>
    public static readonly IReadOnlyList<DocumentTheme> Catalog =
    [
        // The current FreeW defaults — applying this is a sensible, neutral baseline.
        new DocumentTheme("Office", "Calibri", "Calibri", "#000000", "#2F5496", "#1F3864"),
        // Cool grey-blue with a slab serif heading font.
        new DocumentTheme("Slate", "Cambria", "Calibri", "#264653", "#2A9D8F", "#1D3557"),
        // Warm, high-contrast scheme inspired by Word's "Berlin" theme.
        new DocumentTheme("Berlin", "Trebuchet MS", "Trebuchet MS", "#C00000", "#D2691E", "#8B2500"),
        // Vivid magenta/teal "Ion" pairing with a Georgia body face.
        new DocumentTheme("Ion", "Century Gothic", "Georgia", "#B5179E", "#7209B7", "#3A0CA3"),
    ];

    /// <summary>The default theme ("Office"), matching the model's built-in style defaults.</summary>
    public static DocumentTheme Default => Catalog[0];

    /// <summary>Find a theme by (case-insensitive) name, or null when no theme matches.</summary>
    public static DocumentTheme? FindByName(string name) =>
        Catalog.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Apply <paramref name="theme"/> to <paramref name="doc"/>'s style catalog and document default,
    /// deterministically. Only the catalog/defaults are touched — body-text runs are left alone and
    /// pick up the new look by inheriting through their styles. Specifically:
    /// <list type="bullet">
    /// <item>The body font becomes the document default run's <see cref="RunFormatting.FontFamily"/> and
    /// the Normal style's run font (so body text reflows in the new face).</item>
    /// <item>The heading font is set on the Title and every Heading* style.</item>
    /// <item>Colours are taken from the palette: the Title gets the primary colour, Heading 1/2 the
    /// heading colour, and Heading 3 the darker accent.</item>
    /// </list>
    /// Styles absent from the catalog are skipped (the method never adds styles). Font sizes, weights
    /// and paragraph formatting are preserved — only fonts and colours are rewritten.
    /// </summary>
    public static void Apply(TextDocument doc, DocumentTheme theme)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(theme);

        // Body font: document default run + the Normal style's run face.
        doc.DefaultRun = doc.DefaultRun with { FontFamily = theme.BodyFont };
        SetRun(doc, "Normal", run => run with { FontFamily = theme.BodyFont });

        // Title: primary accent colour + heading font.
        SetRun(doc, "Title", run => run with { FontFamily = theme.HeadingFont, ColorHex = theme.PrimaryColorHex });

        // Headings: heading font throughout, palette colours by level.
        SetRun(doc, "Heading1", run => run with { FontFamily = theme.HeadingFont, ColorHex = theme.HeadingColorHex });
        SetRun(doc, "Heading2", run => run with { FontFamily = theme.HeadingFont, ColorHex = theme.HeadingColorHex });
        SetRun(doc, "Heading3", run => run with { FontFamily = theme.HeadingFont, ColorHex = theme.HeadingAccentColorHex });
    }

    // Rewrite a single catalog style's run formatting in place, if the style exists.
    private static void SetRun(TextDocument doc, string styleId, Func<RunFormatting, RunFormatting> transform)
    {
        if (doc.Styles.TryGetValue(styleId, out var style))
            style.Run = transform(style.Run);
    }
}
