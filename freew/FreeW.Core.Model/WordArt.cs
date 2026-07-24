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
/// effects when read. The original four presets are preserved for backwards compatibility; the expanded
/// set adds eleven further presets bringing the total to fifteen (covering Word's gallery columns A–F).
/// Reader inference order: gradient → GradientFill / GradFillMulti, outline → Outline / ChromeOne / ChromeTwo,
/// shadow → Shadow / ShadowOrange / GlowBlue / GlowGold, reflection → Reflection, bevel → Bevel, else → FillBlue/FillGold/FillWhite.
/// </summary>
public enum WordArtStyle
{
    // ── Original four ────────────────────────────────────────────────────────────────────────────
    FillBlue,
    GradientFill,
    Outline,
    Shadow,
    // ── Extended set (11 additional) ─────────────────────────────────────────────────────────────
    /// <summary>Solid gold / dark-yellow fill, no outline.</summary>
    FillGold,
    /// <summary>White/light fill for dark backgrounds, subtle outline.</summary>
    FillWhite,
    /// <summary>Multi-colour gradient (orange→red→purple), no outline.</summary>
    GradFillMulti,
    /// <summary>Dark outline only (no fill — text appears as letter outlines).</summary>
    ChromeOne,
    /// <summary>White fill + coloured outline (inverted outline style).</summary>
    ChromeTwo,
    /// <summary>Drop shadow with orange accent fill.</summary>
    ShadowOrange,
    /// <summary>Blue outer glow, dark fill.</summary>
    GlowBlue,
    /// <summary>Gold/amber outer glow, dark fill.</summary>
    GlowGold,
    /// <summary>Reflection below text, solid blue fill.</summary>
    Reflection,
    /// <summary>Bevel effect (3-D raised look), accent fill.</summary>
    Bevel,
    /// <summary>Pattern-fill text (diagonal cross hatch over blue).</summary>
    PatternFill,
}

/// <summary>
/// A text transform warp preset applied to the WordArt via <c>a:prstTxWarp/@prst</c>. Maps to the
/// DrawingML preset text shapes that describe how the text path is warped. <see cref=”None”/> (the
/// default) emits no <c>a:prstTxWarp</c> element. All others round-trip through DOCX unchanged.
/// </summary>
public enum WordArtWarp
{
    None,
    ArchUp,           // textArchUp
    ArchDown,         // textArchDown
    Circle,           // textCircle
    Button,           // textButton
    Wave1,            // textWave1
    Wave2,            // textWave2
    Inflate,          // textInflate
    Deflate,          // textDeflate
    InflateBottom,    // textInflateBottom
    ChevronUp,        // textChevron
    ChevronDown,      // textChevronInverted
    FadeRight,        // textFadeRight
    FadeLeft,         // textFadeLeft
    SlantUp,          // textSlantUp
    SlantDown,        // textSlantDown
}

/// <summary>
/// The explicit DrawingML text-fit child on a WordArt <c>wps:bodyPr</c>. <see cref="Unspecified"/>
/// preserves a body property with no fit child; the other values map directly to Word's
/// <c>a:noAutofit</c>, <c>a:spAutoFit</c>, and <c>a:normAutofit</c> elements.
/// </summary>
public enum WordArtTextFitMode
{
    Unspecified,
    NoAutoFit,
    ShapeAutoFit,
    NormalAutoFit,
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
    /// Optional WordprocessingML font family from the WordArt text run. A null value preserves
    /// the document theme/default-font route instead of serializing an explicit <c>w:rFonts</c>.
    /// </summary>
    public string? FontFamily { get; set; }

    /// <summary>
    /// Whether the WordArt text run uses bold formatting. This maps to <c>w:b</c> in the
    /// embedded WordprocessingML run and is consumed by both host renderers.
    /// </summary>
    public bool Bold { get; set; }

    /// <summary>
    /// Authored text-box dimensions in points. Imported floating WordArt uses these extents instead of
    /// re-estimating bounds from its text, preserving Word's anchor geometry.
    /// </summary>
    public double? WidthPt { get; set; }
    public double? HeightPt { get; set; }

    /// <summary>DrawingML rotation in degrees, applied about the WordArt bounds centre.</summary>
    public double RotationAngle { get; set; }

    /// <summary>Whether the WordArt is mirrored horizontally about its bounds centre.</summary>
    public bool FlipH { get; set; }

    /// <summary>Whether the WordArt is mirrored vertically about its bounds centre.</summary>
    public bool FlipV { get; set; }

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

    // ── New W24 fields ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Text warp transform preset. <see cref="WordArtWarp.None"/> (default) emits no
    /// <c>a:prstTxWarp</c>; any other value emits the matching preset token and is recovered on read.
    /// </summary>
    public WordArtWarp Warp { get; set; } = WordArtWarp.None;

    /// <summary>
    /// Explicit DrawingML text-fit behavior for the WordArt body. Word distinguishes an absent fit
    /// child from each authored auto-fit mode, so FreeW retains that distinction on DOCX round-trip.
    /// </summary>
    public WordArtTextFitMode TextFitMode { get; set; } = WordArtTextFitMode.Unspecified;

    /// <summary>
    /// Optional <c>a:normAutofit/@fontScale</c> value in thousandths of a percent. It is meaningful
    /// only for <see cref="WordArtTextFitMode.NormalAutoFit"/> and is retained exactly on round-trip.
    /// </summary>
    public int? NormalAutoFitFontScale { get; set; }

    /// <summary>
    /// Optional <c>a:normAutofit/@lnSpcReduction</c> value in thousandths of a percent. It is meaningful
    /// only for <see cref="WordArtTextFitMode.NormalAutoFit"/> and is retained exactly on round-trip.
    /// </summary>
    public int? NormalAutoFitLineSpacingReduction { get; set; }

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
