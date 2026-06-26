namespace FreeP.Core.Model;

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
/// A field run inside a paragraph — corresponds to <c>a:fld</c> in OOXML.
/// Examples: type="slidenum", type="datetime1", type="footer".
/// The <see cref="CachedText"/> is the value baked in by PowerPoint on save (used as
/// the deterministic default for rendering without a live date/slide-number source).
/// </summary>
public sealed class FieldRun
{
    /// <summary>Field type string from a:fld type= attribute, e.g. "slidenum", "datetime1", "datetime14".</summary>
    public string FieldType { get; set; } = string.Empty;

    /// <summary>Cached text baked by PowerPoint (the value rendered if no live resolver is available).</summary>
    public string CachedText { get; set; } = string.Empty;

    /// <summary>Font/formatting properties (same as Run). May be null → inherit.</summary>
    public string? FontFamily { get; set; }
    public double? FontSizePt { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }

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
/// A single text run: a span of text with uniform character properties.
/// </summary>
public sealed class Run
{
    public string Text { get; set; } = string.Empty;

    /// <summary>Font family name, or null to inherit from paragraph/layout/master.</summary>
    public string? FontFamily { get; set; }

    /// <summary>Font size in points, or null to inherit.</summary>
    public double? FontSizePt { get; set; }

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
}

/// <summary>
/// A paragraph inside a <see cref="TextBody"/>. Contains one or more <see cref="Run"/> objects.
/// </summary>
public sealed class Paragraph
{
    /// <summary>Horizontal alignment. Null means inherit from layout/master defaults.</summary>
    public TextAlign? Align { get; set; }

    /// <summary>Indent level (0 = normal body, 1–8 = bulleted sub-levels).</summary>
    public int Level { get; set; }

    public BulletKind BulletKind { get; set; } = BulletKind.None;

    /// <summary>The bullet character when <see cref="BulletKind"/> == Char (e.g. "•").</summary>
    public string? BulletChar { get; set; }

    /// <summary>The text runs that make up this paragraph, in order.</summary>
    public List<Run> Runs { get; } = new();

    /// <summary>Spacing before this paragraph in points, or null to inherit.</summary>
    public double? SpaceBeforePt { get; set; }

    /// <summary>Spacing after this paragraph in points, or null to inherit.</summary>
    public double? SpaceAfterPt { get; set; }
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

    /// <summary>True if the shape auto-fits (resizes) to its text content.</summary>
    public bool AutoFit { get; set; }

    /// <summary>
    /// Full per-level list style from <c>a:lstStyle</c> on this text body.
    /// Null when not present or not yet populated. Used by layout placeholders to carry
    /// per-level defaults (alignment, font size, bullet) that the compositor inherits into slides.
    /// </summary>
    public TextStyleLevels? LstStyle { get; set; }

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
}

/// <summary>Vertical anchor (alignment) of a text body within its bounding box.</summary>
public enum VerticalAnchor
{
    Top = 0,
    Middle = 1,
    Bottom = 2,
    Distributed = 3
}
