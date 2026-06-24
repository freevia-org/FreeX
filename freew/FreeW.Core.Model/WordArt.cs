namespace FreeW.Core.Model;

// MODEL-DESIGN CHOICE (roadmap item X2, WordArt / decorative text):
// WordArt is modelled as an OPTIONAL INLINE RUN MARK (Run.WordArt) exactly like Run.Shape / Run.Equation /
// Run.Image â€” the established FreeW pattern for every inline feature, so WordArt flows through the existing
// run sequence (table cells, headers/footers, hyperlink/comment wrapping) with zero new plumbing.
//
// In modern Word, WordArt is a text box (wps:wsp) whose run text carries DrawingML *text effects* on its
// a:rPr (gradient/solid fill, outline, shadow/glow). Rather than reuse the full Shape model (with its
// arbitrary paragraph body, geometry and fill), WordArt is a deliberately LIGHTWEIGHT record: a single text
// string, a font size, and a chosen STYLE PRESET (a small enum). The writer expands the preset into the
// concrete a:rPr effect elements; the reader infers the preset back from which effects are present. This
// keeps the round-trip lossless for what FreeW models (text + preset + size) while staying far simpler than
// arbitrary effect editing. We deliberately stop here: no per-glyph effect editing and no text-warp
// (a:prstTxWarp) geometry.

/// <summary>
/// A WordArt decorative-text style preset. Each preset maps to a fixed bundle of DrawingML text effects
/// applied to the WordArt run's <c>a:rPr</c> when written, and is inferred back from the presence of those
/// effects when read:
/// <list type="bullet">
/// <item><see cref="FillBlue"/> â€” a solid blue text fill (<c>a:solidFill</c>), no outline/shadow.</item>
/// <item><see cref="GradientFill"/> â€” a two-stop gradient text fill (<c>a:gradFill</c>).</item>
/// <item><see cref="Outline"/> â€” a solid fill plus a coloured text outline (<c>a:ln</c>).</item>
/// <item><see cref="Shadow"/> â€” a solid fill plus an outer shadow (<c>a:effectLst</c>/<c>a:outerShdw</c>).</item>
/// </list>
/// </summary>
public enum WordArtStyle
{
    FillBlue,
    GradientFill,
    Outline,
    Shadow
}

/// <summary>
/// WordArt decorative text carried inline by a <see cref="Run"/> (via <see cref="Run.WordArt"/>), mirroring
/// <see cref="Shape"/> and <see cref="InlineImage"/>. It serialises as an inline <c>w:drawing</c> wrapping a
/// <c>wps:wsp</c> text box whose single text run carries DrawingML text effects (chosen by
/// <see cref="Style"/>) on its <c>a:rPr</c>. Round-trips the text, the chosen style preset and the font size.
/// </summary>
public sealed class WordArt
{
    /// <summary>The decorative text content.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The style preset that selects which DrawingML text effects are applied.</summary>
    public WordArtStyle Style { get; set; } = WordArtStyle.FillBlue;

    /// <summary>Font size in points (defaults to a typical WordArt heading size).</summary>
    public double FontSizePt { get; set; } = 36;

    /// <summary>
    /// Accessibility description (maps onto <c>wp:docPr/@descr</c>). Null means no alt text.
    /// Mirrors <see cref="InlineImage.AltText"/> and <see cref="Shape.AltText"/>.
    /// </summary>
    public string? AltText { get; set; }

    /// <summary>
    /// Floating-position state. Null (the default) means the WordArt is inline.
    /// Set <see cref="FloatingPlacement.Wrapping"/> to any non-Inline value to make it float.
    /// </summary>
    public FloatingPlacement? Placement { get; set; }

    /// <summary>True when this WordArt is floating (non-null Placement with Wrapping != Inline).</summary>
    public bool IsFloating => Placement?.IsFloating ?? false;

    public WordArt() { }

    public WordArt(string text, WordArtStyle style = WordArtStyle.FillBlue, double fontSizePt = 36)
    {
        Text = text;
        Style = style;
        FontSizePt = fontSizePt;
    }

    /// <summary>Creates a WordArt with the given text, style preset and (optional) font size.</summary>
    public static WordArt Create(string text, WordArtStyle style = WordArtStyle.FillBlue, double fontSizePt = 36) =>
        new(text, style, fontSizePt);
}
