namespace FreeP.Core.Model;

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
    public bool Underline { get; set; }
    public bool Strikethrough { get; set; }

    /// <summary>Run color, or null to inherit.</summary>
    public ThemeAwareColor? Color { get; set; }
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
}

/// <summary>Vertical anchor (alignment) of a text body within its bounding box.</summary>
public enum VerticalAnchor
{
    Top = 0,
    Middle = 1,
    Bottom = 2,
    Distributed = 3
}
