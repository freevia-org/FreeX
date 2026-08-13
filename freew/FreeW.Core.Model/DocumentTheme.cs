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
/// <param name="HeadingAccentColorHex">A darker accent for Heading 3 and deeper role headings (RRGGBB hex).</param>
/// <param name="EffectSetName">DrawingML effect-set name serialised as the theme's <c>a:fmtScheme</c>.</param>
public sealed record DocumentTheme(
    string Name,
    string HeadingFont,
    string BodyFont,
    string PrimaryColorHex,
    string HeadingColorHex,
    string HeadingAccentColorHex,
    string EffectSetName = "Office")
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
    /// The DrawingML colour scheme this theme serialises as (the twelve <c>a:clrScheme</c> slots:
    /// dk1/lt1/dk2/lt2, accent1..6, hlink/folHlink). The theme's small palette drives the first three
    /// accents — accent1 = <see cref="PrimaryColorHex"/>, accent2 = <see cref="HeadingColorHex"/>,
    /// accent3 = <see cref="HeadingAccentColorHex"/>; accent4..6 and the link colours are deterministic
    /// stock values, and the dark/light slots are the conventional black/white pair. Every value is an
    /// uppercase six-hex-digit RRGGBB string (no leading '#'). This is the exact, lossless data the writer
    /// emits and the reader matches on to infer the preset.
    /// </summary>
    public ThemeColorScheme ColorScheme => new(
        Dark1: "000000",
        Light1: "FFFFFF",
        Dark2: "44546A",
        Light2: "E7E6E6",
        Accent1: Hex(PrimaryColorHex),
        Accent2: Hex(HeadingColorHex),
        Accent3: Hex(HeadingAccentColorHex),
        Accent4: "FFC000",
        Accent5: "5B9BD5",
        Accent6: "70AD47",
        Hyperlink: "0563C1",
        FollowedHyperlink: "954F72");

    /// <summary>Normalises a palette colour to an uppercase six-hex-digit RRGGBB string (drops any '#').</summary>
    // Keep this boundary local: the model-facing theme palette uses "#RRGGBB", while theme1.xml uses
    // bare RRGGBB slots. That is narrower than shared ThemeColor (#RRGGBB/#AARRGGBB) and different from
    // DrawingMlRgbColor, whose contract is strict a:srgbClr parsing.
    private static string Hex(string value) => value.TrimStart('#').ToUpperInvariant();

    /// <summary>
    /// Best-effort inference of the closest catalog preset from a parsed theme part. A preset matches when
    /// its three accent colours (accent1..3) and its major/minor fonts equal <paramref name="scheme"/> and
    /// the given fonts (case-insensitive). Returns <see cref="Default"/> ("Office") when nothing matches —
    /// the round-trip is therefore exact for any document FreeW wrote, and degrades gracefully for foreign
    /// themes whose accents/fonts FreeW does not recognise.
    /// </summary>
    public static DocumentTheme InferPreset(ThemeColorScheme scheme, string majorFont, string minorFont, string? effectSetName = null)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        var effectSet = DocumentEffectSet.FindByName(effectSetName ?? string.Empty) ?? DocumentEffectSet.Default;
        foreach (var theme in Catalog)
        {
            var candidate = theme.ColorScheme;
            if (string.Equals(candidate.Accent1, scheme.Accent1, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Accent2, scheme.Accent2, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Accent3, scheme.Accent3, StringComparison.OrdinalIgnoreCase)
                && string.Equals(theme.HeadingFont, majorFont, StringComparison.OrdinalIgnoreCase)
                && string.Equals(theme.BodyFont, minorFont, StringComparison.OrdinalIgnoreCase))
                return string.Equals(effectSet.Name, theme.EffectSetName, StringComparison.OrdinalIgnoreCase)
                    ? theme
                    : theme with { EffectSetName = effectSet.Name };
        }
        return new DocumentTheme(
            "Custom",
            string.IsNullOrWhiteSpace(majorFont) ? Default.HeadingFont : majorFont,
            string.IsNullOrWhiteSpace(minorFont) ? Default.BodyFont : minorFont,
            HashOrDefault(scheme.Accent1, Default.PrimaryColorHex),
            HashOrDefault(scheme.Accent2, Default.HeadingColorHex),
            HashOrDefault(scheme.Accent3, Default.HeadingAccentColorHex),
            effectSet.Name);
    }

    private static string HashOrDefault(string value, string fallback) =>
        value.Length == 6 ? "#" + value.ToUpperInvariant() : fallback;

    /// <summary>
    /// Apply <paramref name="theme"/> to <paramref name="doc"/>'s style catalog and document default,
    /// deterministically. Only the catalog/defaults are touched — body-text runs are left alone and
    /// pick up the new look by inheriting through their styles. Specifically:
    /// <list type="bullet">
    /// <item>The body font becomes the document default run's <see cref="RunFormatting.FontFamily"/> and
    /// the Normal style's run font (so body text reflows in the new face).</item>
    /// <item>The heading font is set on the Title and every registered role heading.</item>
    /// <item>Colours are taken from the palette: the Title gets the primary colour, Heading 1/2 the
    /// heading colour, and Heading 3/4 the darker accent.</item>
    /// </list>
    /// Styles absent from the catalog are skipped (the method never adds styles). Font sizes, weights
    /// and paragraph formatting are preserved — only fonts and colours are rewritten.
    /// </summary>
    public static void Apply(TextDocument doc, DocumentTheme theme)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(theme);

        doc.Theme = theme;

        // Body font: document default run + the Normal style's run face.
        doc.DefaultRun = doc.DefaultRun with { FontFamily = theme.BodyFont };
        foreach (var descriptor in BuiltInStyles.RoleCatalog)
        {
            switch (descriptor.Role)
            {
                case BuiltInStyleRole.Normal:
                    SetRun(doc, descriptor, run => run with { FontFamily = theme.BodyFont });
                    break;
                case BuiltInStyleRole.Title:
                    SetRun(doc, descriptor, run => run with
                    {
                        FontFamily = theme.HeadingFont,
                        ColorHex = theme.PrimaryColorHex,
                    });
                    break;
                case BuiltInStyleRole.Heading:
                    SetRun(doc, descriptor, run => run with
                    {
                        FontFamily = theme.HeadingFont,
                        ColorHex = HeadingColor(theme, descriptor),
                    });
                    break;
            }
        }
    }

    /// <summary>
    /// Apply only the colour palette from <paramref name="theme"/> to <paramref name="doc"/>, preserving the
    /// document's current heading/body fonts. This backs Word's separate Design &gt; Colors surface.
    /// </summary>
    public static void ApplyColors(TextDocument doc, DocumentTheme theme)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(theme);

        doc.Theme = doc.Theme with
        {
            PrimaryColorHex = theme.PrimaryColorHex,
            HeadingColorHex = theme.HeadingColorHex,
            HeadingAccentColorHex = theme.HeadingAccentColorHex,
        };

        foreach (var descriptor in BuiltInStyles.RoleCatalog)
        {
            if (descriptor.Role == BuiltInStyleRole.Title)
                SetRun(doc, descriptor, run => run with { ColorHex = theme.PrimaryColorHex });
            else if (descriptor.Role == BuiltInStyleRole.Heading)
                SetRun(doc, descriptor, run => run with { ColorHex = HeadingColor(theme, descriptor) });
        }
    }

    private static string HeadingColor(DocumentTheme theme, BuiltInStyles.Descriptor descriptor) =>
        descriptor.HeadingLevel is <= 2 ? theme.HeadingColorHex : theme.HeadingAccentColorHex;

    // Rewrite a single catalog style's run formatting in place, if the style exists.
    private static void SetRun(
        TextDocument doc,
        BuiltInStyles.Descriptor descriptor,
        Func<RunFormatting, RunFormatting> transform)
    {
        if (doc.Styles.TryGetValue(descriptor.Id, out var style))
            style.Run = transform(style.Run);
    }
}

/// <summary>
/// The twelve colours of a DrawingML theme colour scheme (<c>a:clrScheme</c>), in OOXML slot order:
/// dark1/light1, dark2/light2, accent1..6, then hyperlink/followedHyperlink. Each value is an uppercase
/// six-hex-digit RRGGBB string with no leading '#'. This is the value object the docx writer serialises
/// into <c>word/theme/theme1.xml</c> and the reader parses back, so a theme's colours survive round-trip
/// even when the preset itself cannot be inferred.
/// </summary>
public sealed record ThemeColorScheme(
    string Dark1,
    string Light1,
    string Dark2,
    string Light2,
    string Accent1,
    string Accent2,
    string Accent3,
    string Accent4,
    string Accent5,
    string Accent6,
    string Hyperlink,
    string FollowedHyperlink);
