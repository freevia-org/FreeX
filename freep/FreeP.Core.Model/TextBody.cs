namespace FreeP.Core.Model;

// ── Tab stops ──────────────────────────────────────────────────────────────────

/// <summary>Horizontal alignment of text at a tab stop.</summary>
public enum TabStopAlignment
{
    Left = 0,
    Center = 1,
    Right = 2,
    Decimal = 3
}

/// <summary>Leader glyph pattern used to fill the gap before text at an RTF tab stop.</summary>
public enum TabStopLeader
{
    None = 0,
    Dots = 1,
    Hyphens = 2,
    Underscore = 3,
    ThickLine = 4,
    Equal = 5
}

/// <summary>
/// A single tab stop in a paragraph's tab stop list.
/// Position is in EMU from the text body's left inset (matches a:tab pos= semantics).
/// </summary>
public sealed class TabStop
{
    /// <summary>Tab stop position in EMU from the left edge of the text area.</summary>
    public long PositionEmu { get; set; }

    /// <summary>Alignment of text at this stop.</summary>
    public TabStopAlignment Alignment { get; set; } = TabStopAlignment.Left;

    /// <summary>Optional leader pattern authored by an external rich-text source.</summary>
    public TabStopLeader Leader { get; set; } = TabStopLeader.None;
}

// ── Text autofit ────────────────────────────────────────────────────────────────

/// <summary>
/// Which <c>a:bodyPr</c> autofit child element is present, corresponding to the
/// mutually-exclusive OOXML choices for text-frame autofit behavior.
/// </summary>
public enum TextAutoFitKind
{
    /// <summary>No autofit element (or explicit <c>a:noAutofit</c>): text may overflow/clip.</summary>
    None = 0,

    /// <summary><c>a:normAutofit</c>: shrink the TEXT (font size / line spacing) to fit the fixed box.</summary>
    Normal = 1,

    /// <summary><c>a:spAutoFit</c>: grow the SHAPE to fit its text; the text itself is never shrunk.</summary>
    Shape = 2
}

// ── Vertical text orientation ──────────────────────────────────────────────────

/// <summary>
/// Text orientation for a body — corresponds to <c>a:bodyPr vert=</c>.
/// </summary>
public enum TextVerticalType
{
    /// <summary>Normal horizontal text (default). OOXML: "horz".</summary>
    Horizontal = 0,

    /// <summary>Text rotated 90° clockwise (top-to-bottom reading order). OOXML: "vert".</summary>
    Vertical = 1,

    /// <summary>Text rotated 270° clockwise (bottom-to-top). OOXML: "vert270".</summary>
    Vertical270 = 2,

    /// <summary>East-Asian vertical (each glyph upright, stacked). OOXML: "eaVert".</summary>
    EastAsianVertical = 3,

    /// <summary>WordArt stacked vertical (each glyph upright). OOXML: "wordArtVert".</summary>
    WordArtVertical = 4,

    /// <summary>WordArt stacked vertical RTL. OOXML: "wordArtVertRtl".</summary>
    WordArtVerticalRtl = 5
}

/// <summary>
/// A hyperlink target.  Exactly one of <see cref="Url"/> or <see cref="TargetSlideId"/> is set.
/// </summary>
public sealed class Hyperlink
{
    /// <summary>External URL (http/https/mailto).  Set for external hyperlinks.</summary>
    public string? Url { get; set; }

    /// <summary>
    /// Internal slide jump target.  Value is the <see cref="Slide.Id"/> of the destination slide.
    /// Set for in-presentation jump links (<c>ppaction://hlinksldjump</c>).
    /// </summary>
    public string? TargetSlideId { get; set; }

    /// <summary>Optional tooltip text shown on hover.</summary>
    public string? Tooltip { get; set; }

    /// <summary>True if this is an external link (Url is set).</summary>
    public bool IsExternal => Url is not null;
}

/// <summary>Horizontal text alignment within a paragraph.</summary>
public enum TextAlign
{
    Left = 0,
    Center = 1,
    Right = 2,
    Justify = 3,
    Distributed = 4
}

/// <summary>Bullet/list type for a paragraph.</summary>
public enum BulletKind
{
    None = 0,
    Auto = 1,     // numbered/auto list
    Char = 2,     // single character bullet (e.g. "•")
    Image = 3     // image bullet (future)
}

/// <summary>
/// Auto-number format for an Auto bullet paragraph.
/// Corresponds to <c>a:buAutoNum type=</c> attribute values.
/// </summary>
public enum AutoNumType
{
    /// <summary>Arabic numerals with period: 1. 2. 3.</summary>
    ArabicPeriod = 0,
    /// <summary>Arabic numerals with close-paren: 1) 2) 3)</summary>
    ArabicParenR = 1,
    /// <summary>Arabic numerals with surrounding parens: (1) (2) (3)</summary>
    ArabicParenBoth = 2,
    /// <summary>Uppercase Roman numeral with period: I. II. III.</summary>
    RomanUcPeriod = 3,
    /// <summary>Lowercase Roman numeral with period: i. ii. iii.</summary>
    RomanLcPeriod = 4,
    /// <summary>Uppercase Roman numeral with close-paren: I) II)</summary>
    RomanUcParenR = 5,
    /// <summary>Lowercase Roman numeral with close-paren: i) ii)</summary>
    RomanLcParenR = 6,
    /// <summary>Uppercase alpha with period: A. B. C.</summary>
    AlphaUcPeriod = 7,
    /// <summary>Lowercase alpha with period: a. b. c.</summary>
    AlphaLcPeriod = 8,
    /// <summary>Uppercase alpha with close-paren: A) B)</summary>
    AlphaUcParenR = 9,
    /// <summary>Lowercase alpha with close-paren: a) b)</summary>
    AlphaLcParenR = 10,
    /// <summary>Uppercase alpha with surrounding parens: (A) (B)</summary>
    AlphaUcParenBoth = 11,
    /// <summary>Lowercase alpha with surrounding parens: (a) (b)</summary>
    AlphaLcParenBoth = 12,
}

/// <summary>
/// A field run inside a paragraph — corresponds to <c>a:fld</c> in OOXML.
/// Examples: type="slidenum", type="datetime1", type="footer".
/// The <see cref="CachedText"/> is the value baked in by PowerPoint on save (used as
/// the deterministic default for rendering without a live date/slide-number source).
/// </summary>
public sealed class FieldRun
{
    /// <summary>Field type string from a:fld type= attribute, e.g. "slidenum", "datetime1", "datetime14".</summary>
    public string FieldType { get; set; } = string.Empty;

    /// <summary>
    /// Native DrawingML field identity from <c>a:fld/@id</c>. PowerPoint uses this
    /// value to keep an authored field stable across save/update cycles. New fields
    /// may leave it null and the package writer will allocate one.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Authored <c>a:fld/@dirty</c> state. Null means the source omitted the token,
    /// which is distinct from an explicit false value for package round-tripping.
    /// </summary>
    public bool? Dirty { get; set; }

    /// <summary>Authored field-run language tag from <c>a:fld/a:rPr/@lang</c>.</summary>
    public string? Language { get; set; }

    /// <summary>Authored field-run alternate language tag from <c>a:fld/a:rPr/@altLang</c>.</summary>
    public string? AlternateLanguage { get; set; }

    /// <summary>Authored field-run dirty state from <c>a:fld/a:rPr/@dirty</c>.</summary>
    public bool? RunDirty { get; set; }

    /// <summary>Authored field-run proofing suppression from <c>a:fld/a:rPr/@noProof</c>.</summary>
    public bool? NoProof { get; set; }

    /// <summary>Authored field-run spelling-error marker from <c>a:fld/a:rPr/@err</c>.</summary>
    public bool? Error { get; set; }

    /// <summary>Authored field-run Japanese-character layout flag from <c>a:fld/a:rPr/@kumimoji</c>.</summary>
    public bool? Kumimoji { get; set; }

    /// <summary>Authored field-run smart-tag cleanup flag from <c>a:fld/a:rPr/@smtClean</c>.</summary>
    public bool? SmartTagClean { get; set; }

    /// <summary>Authored field-run character-height normalization flag from <c>a:fld/a:rPr/@normalizeH</c>.</summary>
    public bool? NormalizeHeight { get; set; }

    /// <summary>Authored field-run character spacing in hundredths of a point from <c>a:fld/a:rPr/@spc</c>.</summary>
    public int? CharacterSpacingHundredthsPt { get; set; }

    /// <summary>Authored field-run kerning threshold in hundredths of a point from <c>a:fld/a:rPr/@kern</c>.</summary>
    public int? KerningThresholdHundredthsPt { get; set; }

    /// <summary>Authored field-run baseline offset from <c>a:fld/a:rPr/@baseline</c>.</summary>
    public int? BaselineOffset { get; set; }

    /// <summary>Authored field-run character direction from <c>a:fld/a:rPr/@rtl</c>.</summary>
    public bool? RightToLeft { get; set; }

    /// <summary>Authored field-run capitalization from <c>a:fld/a:rPr/@cap</c>.</summary>
    public RunTextCaps Caps { get; set; }

    /// <summary>
    /// Optional source field instruction retained by external clipboard formats such as RTF.
    /// Native PowerPoint fields use <see cref="FieldType"/> and do not serialize this value.
    /// </summary>
    public string? Instruction { get; set; }

    /// <summary>Cached text baked by PowerPoint (the value rendered if no live resolver is available).</summary>
    public string CachedText { get; set; } = string.Empty;

    /// <summary>Font/formatting properties (same as Run). May be null → inherit.</summary>
    public string? FontFamily { get; set; }
    public double? FontSizePt { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }

    /// <summary>Authored DrawingML underline token from <c>a:rPr/@u</c>; null preserves omission.</summary>
    public string? UnderlineStyleToken { get; set; }

    /// <summary>Authored DrawingML strike token from <c>a:rPr/@strike</c>; null preserves omission.</summary>
    public string? StrikeStyleToken { get; set; }

    public bool Underline { get; set; }
    public bool Strikethrough { get; set; }

    /// <summary>Explicit color or null to inherit.</summary>
    public SrgbColor? Color { get; set; }
}

/// <summary>
/// Per-run text shadow descriptor (from <c>a:rPr/a:effectLst/a:outerShdw</c>).
/// Parallel to <see cref="ShapeEffects"/> outer shadow but scoped to a single glyph run.
/// </summary>
public sealed class RunTextShadow
{
    /// <summary>Shadow color (resolved or raw).</summary>
    public ThemeAwareColor Color { get; set; } = new ThemeAwareColor(new SrgbColor(0, 0, 0));

    /// <summary>Alpha (0–255).</summary>
    public byte Alpha { get; set; } = 128;

    /// <summary>Blur radius in points (from a:outerShdw @blurRad in EMU / 12700).</summary>
    public double BlurPt { get; set; } = 2.0;

    /// <summary>Distance in points (from a:outerShdw @dist in EMU / 12700).</summary>
    public double DistPt { get; set; } = 2.0;

    /// <summary>Direction in degrees clockwise from right (from a:outerShdw @dir / 60000).</summary>
    public double DirDeg { get; set; } = 45.0;
}

/// <summary>
/// Per-run text reflection descriptor (from <c>a:rPr/a:effectLst/a:reflection</c>).
/// This captures the visible mirror transform used by WordArt styles.
/// </summary>
public sealed class RunTextReflection
{
    /// <summary>Reflection opacity at the glyph edge (0-255).</summary>
    public byte Alpha { get; set; } = 128;

    /// <summary>Blur radius in points (from a:reflection @blurRad in EMU / 12700).</summary>
    public double BlurPt { get; set; }

    /// <summary>Distance in points (from a:reflection @dist in EMU / 12700).</summary>
    public double DistPt { get; set; }

    /// <summary>Direction in degrees clockwise from right (from a:reflection @dir / 60000).</summary>
    public double DirDeg { get; set; } = 90.0;

    /// <summary>Vertical scale from a:reflection @sy. Negative values mirror vertically.</summary>
    public double ScaleY { get; set; } = -1.0;

    /// <summary>Normalized end position of the reflection fade from a:reflection @endPos.</summary>
    public double EndPos { get; set; } = 1.0;
}

/// <summary>
/// Per-run text glow descriptor (from <c>a:rPr/a:effectLst/a:glow</c>).
/// </summary>
public sealed class RunTextGlow
{
    /// <summary>Glow color (resolved or theme-aware).</summary>
    public ThemeAwareColor Color { get; set; } = new ThemeAwareColor(new SrgbColor(0, 0, 0));

    /// <summary>Alpha (0-255).</summary>
    public byte Alpha { get; set; } = 0xA0;

    /// <summary>Glow radius in points (from a:glow @rad in EMU / 12700).</summary>
    public double RadiusPt { get; set; }
}

/// <summary>
/// Per-run text soft-edge descriptor (from <c>a:rPr/a:effectLst/a:softEdge</c>).
/// </summary>
public sealed class RunTextSoftEdge
{
    /// <summary>Soft-edge radius in points (from a:softEdge @rad in EMU / 12700).</summary>
    public double RadiusPt { get; set; }
}

/// <summary>DrawingML character capitalization.</summary>
public enum RunTextCaps
{
    None,
    Small,
    All,
}

/// <summary>
/// A single text run: a span of text with uniform character properties.
/// </summary>
public sealed class Run
{
    public string Text { get; set; } = string.Empty;

    /// <summary>Authored DrawingML language tag from <c>a:rPr/@lang</c>.</summary>
    public string? Language { get; set; }

    /// <summary>Authored DrawingML alternate language tag from <c>a:rPr/@altLang</c>.</summary>
    public string? AlternateLanguage { get; set; }

    /// <summary>Authored DrawingML Japanese-character layout flag from <c>a:rPr/@kumimoji</c>.</summary>
    public bool? Kumimoji { get; set; }

    /// <summary>Authored DrawingML smart-tag cleanup flag from <c>a:rPr/@smtClean</c>.</summary>
    public bool? SmartTagClean { get; set; }

    /// <summary>Authored DrawingML character-height normalization flag from <c>a:rPr/@normalizeH</c>.</summary>
    public bool? NormalizeHeight { get; set; }

    /// <summary>Authored DrawingML character spacing in hundredths of a point from <c>a:rPr/@spc</c>.</summary>
    public int? CharacterSpacingHundredthsPt { get; set; }

    /// <summary>Authored DrawingML kerning threshold in hundredths of a point from <c>a:rPr/@kern</c>.</summary>
    public int? KerningThresholdHundredthsPt { get; set; }

    /// <summary>Authored DrawingML underline token from <c>a:rPr/@u</c>; null preserves omission.</summary>
    public string? UnderlineStyleToken { get; set; }

    /// <summary>Authored DrawingML strike token from <c>a:rPr/@strike</c>; null preserves omission.</summary>
    public string? StrikeStyleToken { get; set; }

    /// <summary>Authored DrawingML dirty state from <c>a:rPr/@dirty</c>; null preserves omission.</summary>
    public bool? Dirty { get; set; }

    /// <summary>Authored DrawingML proofing suppression from <c>a:rPr/@noProof</c>; null preserves omission.</summary>
    public bool? NoProof { get; set; }

    /// <summary>Authored DrawingML spelling-error marker from <c>a:rPr/@err</c>; null preserves omission.</summary>
    public bool? Error { get; set; }

    /// <summary>
    /// Inline picture carried by a rich-text editing run. The run's text is the single
    /// object-replacement character (U+FFFC), so caret and selection offsets remain stable
    /// across WPF and Avalonia editors. Null means this is an ordinary text run.
    /// </summary>
    public ImagePart? InlineImage { get; set; }

    /// <summary>Authored inline-picture width in EMUs, when the source supplied one.</summary>
    public long? InlineImageWidthEmu { get; set; }

    /// <summary>Authored inline-picture height in EMUs, when the source supplied one.</summary>
    public long? InlineImageHeightEmu { get; set; }

    /// <summary>
    /// Inline embedded object carried by a rich-text editing run. The run's text is the
    /// single object-replacement character (U+FFFC), matching inline-picture caret behavior.
    /// Null means this is not an inline OLE object.
    /// </summary>
    public InlineOleObjectInfo? InlineOleObject { get; set; }

    /// <summary>
    /// Inline table carried by an object-replacement run. The run's text is U+FFFC, matching
    /// inline pictures and embedded objects so selection and caret offsets remain stable.
    /// </summary>
    public InlineTableInfo? InlineTable { get; set; }

    /// <summary>Font family name, or null to inherit from paragraph/layout/master.</summary>
    public string? FontFamily { get; set; }

    /// <summary>Font size in points, or null to inherit.</summary>
    public double? FontSizePt { get; set; }

    /// <summary>
    /// Raw DrawingML <c>a:rPr/@baseline</c> value in ST_Percentage units
    /// (one thousandth of a percent). Positive values raise the run and
    /// negative values lower it. Null means the attribute was absent.
    /// </summary>
    public int? BaselineOffset { get; set; }

    public bool Bold { get; set; }
    public bool Italic { get; set; }

    /// <summary>
    /// True when <see cref="Bold"/> was read from an explicit <c>a:rPr @b</c> attribute
    /// (including <c>b="0"</c>).  False means the attribute was absent — inherit from style chain.
    /// Set by the reader and by bold-toggle editing commands.
    /// </summary>
    public bool BoldSet { get; set; }

    /// <summary>
    /// True when <see cref="Italic"/> was read from an explicit <c>a:rPr @i</c> attribute
    /// (including <c>i="0"</c>).  False means the attribute was absent — inherit from style chain.
    /// Set by the reader and by italic-toggle editing commands.
    /// </summary>
    public bool ItalicSet { get; set; }

    public bool Underline { get; set; }
    public bool Strikethrough { get; set; }
    /// <summary>
    /// Explicit character direction. Null means inherit the paragraph direction or the
    /// first strong character, while true/false preserve an authored RTL/LTR run override.
    /// </summary>
    public bool? RightToLeft { get; set; }
    public RunTextCaps Caps { get; set; }

    /// <summary>Run color, or null to inherit.</summary>
    public ThemeAwareColor? Color { get; set; }

    /// <summary>Hyperlink on this run, or null.  Corresponds to <c>a:hlinkClick</c> inside <c>a:rPr</c>.</summary>
    public Hyperlink? Hyperlink { get; set; }

    /// <summary>
    /// When non-null this run is an <c>a:fld</c> field run. The <see cref="Text"/>
    /// field holds the cached text for rendering; the field type is here.
    /// </summary>
    public FieldRun? Field { get; set; }

    // ── WordArt / Text-effects (Wave 16A) ─────────────────────────────────────

    /// <summary>
    /// Glyph fill override — used for gradient and other complex text fills.
    /// When set, overrides <see cref="Color"/> for fill rendering.
    /// Null means use <see cref="Color"/> as a simple solid fill.
    /// Corresponds to <c>a:rPr/a:gradFill</c> (or <c>a:solidFill</c> mapped here
    /// for uniformity when richer rendering is needed).
    /// </summary>
    public ShapeFill? TextFill { get; set; }

    /// <summary>
    /// Outline drawn around each glyph.  Corresponds to <c>a:rPr/a:ln</c>.
    /// Null = no outline.
    /// </summary>
    public ShapeOutline? TextOutline { get; set; }

    /// <summary>
    /// Drop shadow behind glyphs.  Corresponds to <c>a:rPr/a:effectLst/a:outerShdw</c>.
    /// Null = no shadow.
    /// </summary>
    public RunTextShadow? TextShadow { get; set; }

    /// <summary>
    /// Mirrored reflection below/near glyphs. Corresponds to
    /// <c>a:rPr/a:effectLst/a:reflection</c>. Null = no reflection.
    /// </summary>
    public RunTextReflection? TextReflection { get; set; }

    /// <summary>
    /// Glow around glyphs. Corresponds to <c>a:rPr/a:effectLst/a:glow</c>.
    /// Null = no glow.
    /// </summary>
    public RunTextGlow? TextGlow { get; set; }

    /// <summary>
    /// Softened glyph edge. Corresponds to <c>a:rPr/a:effectLst/a:softEdge</c>.
    /// Null = no soft edge.
    /// </summary>
    public RunTextSoftEdge? TextSoftEdge { get; set; }

    // ── OMML Math (Theme 21) ───────────────────────────────────────────────────

    /// <summary>
    /// When non-null this run represents an OMML math equation embedded via
    /// <c>a14:m</c> or <c>mc:AlternateContent</c> in the paragraph.
    /// <see cref="Text"/> carries the flattened fallback plain text (m:t concatenated
    /// or mc:Fallback run text) for rendering.
    /// </summary>
    public MathRunInfo? Math { get; set; }
}

/// <summary>
/// A paragraph inside a <see cref="TextBody"/>. Contains one or more <see cref="Run"/> objects.
/// </summary>
public sealed class Paragraph
{
    /// <summary>Horizontal alignment. Null means inherit from layout/master defaults.</summary>
    public TextAlign? Align { get; set; }

    /// <summary>
    /// Explicit paragraph reading direction. True maps to <c>a:pPr rtl="1"</c>, false maps
    /// to <c>rtl="0"</c>, and null means the source omitted the attribute so direction is
    /// inherited from the text style chain. Keeping false distinct from null is required for
    /// truthful PPTX round-trips.
    /// </summary>
    public bool? RightToLeft { get; set; }

    /// <summary>Indent level (0 = normal body, 1–8 = bulleted sub-levels).</summary>
    public int Level { get; set; }

    public BulletKind BulletKind { get; set; } = BulletKind.None;

    /// <summary>
    /// True when the paragraph contains an explicit <c>a:buNone</c> element, meaning the
    /// paragraph actively suppresses any bullet inherited from lstStyle / master.
    /// False (the default) means no bullet element was present — the paragraph does NOT
    /// suppress inheritance; the compositor will re-inherit the style bullet normally.
    /// </summary>
    public bool BulletSuppressed { get; set; }

    /// <summary>The bullet character when <see cref="BulletKind"/> == Char (e.g. "•").</summary>
    public string? BulletChar { get; set; }

    /// <summary>
    /// Resolved image payload when <see cref="BulletKind"/> is <see cref="BulletKind.Image"/>.
    /// Populated from DrawingML <c>a:buBlip</c> relationships during PPTX import.
    /// </summary>
    public ImagePart? BulletImage { get; set; }

    // ── Wave 19A: extended bullet fields ──────────────────────────────────────

    /// <summary>
    /// Auto-number list format when <see cref="BulletKind"/> == Auto.
    /// Corresponds to <c>a:buAutoNum type=</c>.
    /// </summary>
    public AutoNumType AutoNumType { get; set; } = AutoNumType.ArabicPeriod;

    /// <summary>
    /// Start-at value for auto-numbered lists (<c>a:buAutoNum startAt=</c>).
    /// 1-based; 1 is the default.
    /// </summary>
    public int AutoNumStartAt { get; set; } = 1;

    /// <summary>
    /// True when the source explicitly authored the <c>startAt</c> attribute.
    /// This is distinct from <see cref="AutoNumStartAt"/> being 1: an explicit
    /// start at 1 restarts an active list, while an omitted value continues it.
    /// Rich-editor split continuations clear this flag on the new paragraph.
    /// </summary>
    public bool AutoNumStartAtSpecified { get; set; }

    /// <summary>
    /// Optional renderer-neutral level-text template retained from external rich text.
    /// Substitutions use <c>%1</c> through <c>%9</c> for the corresponding list levels;
    /// literal punctuation and text are preserved. When null, the standard
    /// <see cref="AutoNumType"/> formatter is authoritative.
    /// </summary>
    public string? AutoNumTextTemplate { get; set; }

    /// <summary>
    /// Left margin (indent from shape inset) in EMU from <c>a:pPr marL=</c>.
    /// Null means inherit from layout/master/style.
    /// Positive = bullet + text indented from the left edge.
    /// </summary>
    public long? MarginLeftEmu { get; set; }

    /// <summary>
    /// First-line/hanging indent in EMU from <c>a:pPr indent=</c>.
    /// Null means inherit. Typically negative (hanging: bullet is to the left of the text indent).
    /// </summary>
    public long? IndentEmu { get; set; }

    /// <summary>
    /// Bullet color override from <c>a:buClr/a:srgbClr</c> or theme color.
    /// Null = inherit the run's effective text color.
    /// </summary>
    public ThemeAwareColor? BulletColor { get; set; }

    /// <summary>
    /// True when <c>a:buClrTx</c> is present. This explicitly makes the bullet color follow
    /// the first non-empty run and blocks inherited bullet color overrides.
    /// </summary>
    public bool BulletColorFollowsText { get; set; }

    /// <summary>
    /// Bullet size as a percentage of the run font size, from <c>a:buSzPct val=</c>.
    /// Stored as 1000ths-of-a-percent per OOXML (e.g. 100000 = 100%).
    /// Null = 100% (same size as text).
    /// </summary>
    public int? BulletSizePct { get; set; }

    /// <summary>
    /// Absolute bullet size in points from <c>a:buSzPts val=</c>. The OOXML value is stored
    /// in hundredths of a point; this model stores points.
    /// </summary>
    public double? BulletSizePt { get; set; }

    /// <summary>
    /// True when <c>a:buSzTx</c> is present. This explicitly makes the bullet size follow
    /// the first non-empty run and blocks inherited bullet size overrides.
    /// </summary>
    public bool BulletSizeFollowsText { get; set; }

    /// <summary>
    /// Override font for the bullet glyph, from <c>a:buFont typeface=</c>.
    /// Null = same font as the first run in the paragraph.
    /// </summary>
    public string? BulletFontFamily { get; set; }

    /// <summary>
    /// True when <c>a:buFontTx</c> is present. This explicitly makes the bullet font follow
    /// the first non-empty run and blocks inherited bullet font overrides.
    /// </summary>
    public bool BulletFontFollowsText { get; set; }

    /// <summary>The text runs that make up this paragraph, in order.</summary>
    public List<Run> Runs { get; } = new();

    /// <summary>Spacing before this paragraph in points, or null to inherit.</summary>
    public double? SpaceBeforePt { get; set; }

    /// <summary>Spacing after this paragraph in points, or null to inherit.</summary>
    public double? SpaceAfterPt { get; set; }

    /// <summary>
    /// Explicit tab stops for this paragraph, in position order.
    /// Corresponds to <c>a:pPr/a:tabLst/a:tab</c>.
    /// Empty means use default tab spacing (inherited / PowerPoint default 914400 EMU = 1 inch).
    /// </summary>
    public List<TabStop> TabStops { get; } = new();
}

/// <summary>
/// The text body of a <see cref="SlideShape"/>: a list of <see cref="Paragraph"/> objects plus
/// optional body-level defaults (anchor, inset). Corresponds to <c>p:txBody</c> / <c>a:txBody</c>.
/// </summary>
public sealed class TextBody
{
    /// <summary>Paragraphs in order; may be empty for a shape with no text.</summary>
    public List<Paragraph> Paragraphs { get; } = new();

    /// <summary>
    /// Vertical text anchor within the bounding box (top/middle/bottom).
    /// Null means not explicitly set on this shape — inherit from layout/master.
    /// </summary>
    public VerticalAnchor? Anchor { get; set; }

    /// <summary>
    /// Default paragraph horizontal alignment from the body's <c>a:lstStyle/a:lvl1pPr algn</c>.
    /// Null means not set on this shape — inherit from layout/master.
    /// Stored here so the compositor can walk the inheritance chain without re-reading XML.
    /// </summary>
    public TextAlign? DefaultParaAlign { get; set; }

    /// <summary>
    /// Default paragraph reading direction from the body's <c>a:lstStyle/lvl1pPr rtl</c>.
    /// Null means the body did not author a direction and inheritance continues.
    /// </summary>
    public bool? DefaultParaRightToLeft { get; set; }

    /// <summary>Left inset (internal padding) in points. Null = use default (≈7pt).</summary>
    public double? InsetLeftPt { get; set; }
    /// <summary>Right inset in points. Null = use default.</summary>
    public double? InsetRightPt { get; set; }
    /// <summary>Top inset in points. Null = use default.</summary>
    public double? InsetTopPt { get; set; }
    /// <summary>Bottom inset in points. Null = use default.</summary>
    public double? InsetBottomPt { get; set; }

    /// <summary>True if text should wrap within the bounding box (default). False for no-wrap.</summary>
    public bool Wrap { get; set; } = true;

    /// <summary>
    /// Which autofit mode is set on this text body — <see cref="TextAutoFitKind.Normal"/>
    /// (<c>a:normAutofit</c>, shrink text to fit), <see cref="TextAutoFitKind.Shape"/>
    /// (<c>a:spAutoFit</c>, grow shape to fit text), or <see cref="TextAutoFitKind.None"/>
    /// (<c>a:noAutofit</c> or absent).
    /// </summary>
    public TextAutoFitKind AutoFitKind { get; set; }

    /// <summary>
    /// Back-compat convenience: true when <see cref="AutoFitKind"/> is <see cref="TextAutoFitKind.Normal"/>
    /// (PowerPoint "shrink text on overflow"). Setting this to true/false maps to Normal/None;
    /// use <see cref="AutoFitKind"/> directly to distinguish <see cref="TextAutoFitKind.Shape"/>
    /// (<c>a:spAutoFit</c>, "resize shape to fit text") from the shrink-text behavior.
    /// </summary>
    public bool AutoFit
    {
        get => AutoFitKind == TextAutoFitKind.Normal;
        set => AutoFitKind = value ? TextAutoFitKind.Normal : TextAutoFitKind.None;
    }

    // ── Wave 19A: normAutofit cached scaling ──────────────────────────────────

    /// <summary>
    /// Stored font-scale factor from <c>a:normAutofit fontScale=</c>, in 1000ths-of-a-percent.
    /// E.g. 62500 = 62.5%.  Zero / null means no normAutofit scaling was stored.
    /// Apply by multiplying every run's resolved font size by FontScalePPT / 100000.
    /// </summary>
    public int? FontScalePPT { get; set; }

    /// <summary>
    /// Stored line-spacing reduction from <c>a:normAutofit lnSpcReduction=</c>,
    /// in 1000ths-of-a-percent.  E.g. 20000 = 20% reduction applied to line spacing.
    /// Zero / null means no reduction.
    /// </summary>
    public int? LnSpcReductionPPT { get; set; }

    /// <summary>
    /// Full per-level list style from <c>a:lstStyle</c> on this text body.
    /// Null when not present or not yet populated. Used by layout placeholders to carry
    /// per-level defaults (alignment, font size, bullet) that the compositor inherits into slides.
    /// </summary>
    public TextStyleLevels? LstStyle { get; set; }

    /// <summary>
    /// Text orientation for this body.  Corresponds to <c>a:bodyPr vert=</c>.
    /// <see cref="TextVerticalType.Horizontal"/> is the default and is NOT written
    /// to XML (attribute absence = horizontal).
    /// </summary>
    public TextVerticalType VerticalType { get; set; } = TextVerticalType.Horizontal;

    /// <summary>
    /// WordArt warp preset name from <c>a:bodyPr/a:prstTxWarp @prst</c>, e.g.
    /// "textArchUp", "textArchDown", "textWave1", "textTriangle", "textCircle".
    /// Null means no warp (flat text).
    /// </summary>
    public string? WarpPreset { get; set; }

    /// <summary>
    /// Adjust guide values for the warp preset geometry, from
    /// <c>a:prstTxWarp/a:avLst/a:gd</c>.  Each entry is a (name, formula) pair,
    /// e.g. ("adj1", "val 30000").  Empty when no custom guides are present.
    /// Round-trips the avLst so non-default warp shapes are preserved.
    /// </summary>
    public List<(string Name, string Formula)> WarpAdjusts { get; } = new();

    /// <summary>Body-level WordArt 3-D material, bevel, extrusion, and lighting.</summary>
    public ShapeEffects? Text3dEffects { get; set; }

    // ── Wave 22B: text columns ─────────────────────────────────────────────────

    /// <summary>
    /// Number of text columns. 1 = single column (default).
    /// Corresponds to <c>a:bodyPr numCol=</c>.
    /// </summary>
    public int ColumnCount { get; set; } = 1;

    /// <summary>
    /// Spacing between columns in EMU. 0 = default (457200 EMU = 0.5 inch).
    /// Corresponds to <c>a:bodyPr spcCol=</c>.
    /// </summary>
    public long ColumnSpacingEmu { get; set; } = 0;
}

/// <summary>Vertical anchor (alignment) of a text body within its bounding box.</summary>
public enum VerticalAnchor
{
    Top = 0,
    Middle = 1,
    Bottom = 2,
    Distributed = 3
}
