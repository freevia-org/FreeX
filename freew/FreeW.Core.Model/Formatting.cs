namespace FreeW.Core.Model;

/// <summary>Paragraph horizontal alignment.</summary>
public enum TextAlignment { Left, Center, Right, Justify }

/// <summary>List decoration for a paragraph.</summary>
public enum ListKind { None, Bullet, Number }

/// <summary>
/// Vertical alignment of a run's glyphs relative to the baseline (rPr/w:vertAlign). Maps to docx
/// <c>w:vertAlign w:val="superscript|subscript"</c>; <see cref="Baseline"/> writes nothing.
/// </summary>
public enum VerticalAlign { Baseline, Superscript, Subscript }

/// <summary>
/// Immutable paragraph box border (pPr/w:pBdr). When present, all four edges are drawn with the
/// given colour and width. Round-trips to docx as <c>w:top</c>/<c>w:bottom</c>/<c>w:left</c>/<c>w:right</c>
/// (each <c>w:val="single"</c>), mirroring how table borders map to <c>w:tblBorders</c>.
/// </summary>
/// <param name="ColorHex">Border colour as an RRGGBB hex (e.g. <c>"#000000"</c>).</param>
/// <param name="WidthPt">Border width in points (docx stores this as eighths of a point in <c>w:sz</c>).</param>
public sealed record ParagraphBorder(string ColorHex = "#000000", double WidthPt = 0.5);

/// <summary>
/// Immutable character formatting for a run. Null members inherit from the paragraph style /
/// document default, mirroring how Word resolves run properties (rPr).
/// </summary>
public sealed record RunFormatting
{
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }
    public string? FontFamily { get; init; }
    public double? FontSizePt { get; init; }
    public string? ColorHex { get; init; }

    /// <summary>
    /// Highlight (text background) colour as an RRGGBB hex (e.g. <c>"#FFFF00"</c>). Null means no
    /// highlight. Round-trips to docx as run shading (<c>w:shd w:fill</c>), mirroring <see cref="ColorHex"/>.
    /// </summary>
    public string? HighlightColorHex { get; init; }

    /// <summary>
    /// Superscript/subscript baseline offset (rPr/w:vertAlign). Defaults to
    /// <see cref="VerticalAlign.Baseline"/> (no offset).
    /// </summary>
    public VerticalAlign VerticalAlign { get; init; } = VerticalAlign.Baseline;

    /// <summary>
    /// Renders lowercase letters as small capitals (rPr/w:smallCaps toggle). Mirrors how
    /// <see cref="Bold"/> models a docx toggle element.
    /// </summary>
    public bool SmallCaps { get; init; }

    /// <summary>
    /// Renders all letters as capitals (rPr/w:caps toggle). Mirrors how <see cref="Bold"/> models a
    /// docx toggle element. When both this and <see cref="SmallCaps"/> are set, Word treats caps as
    /// winning; we preserve both flags so the round-trip is lossless.
    /// </summary>
    public bool AllCaps { get; init; }

    public static readonly RunFormatting Default = new();
}

/// <summary>
/// Immutable paragraph formatting (pPr): alignment, spacing, indents, list. Points throughout,
/// matching the docx unit model once divided/multiplied by the OOXML twentieths.
/// </summary>
public sealed record ParagraphFormatting
{
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;
    public double SpaceBeforePt { get; init; }
    public double SpaceAfterPt { get; init; } = 8;
    public double LineSpacing { get; init; } = 1.15;
    public double IndentLeftPt { get; init; }
    public double IndentRightPt { get; init; }
    public double FirstLineIndentPt { get; init; }
    public ListKind ListKind { get; init; } = ListKind.None;
    public int ListLevel { get; init; }

    /// <summary>
    /// Box border around the paragraph (pPr/w:pBdr), or null for no border. Mirrors how table
    /// borders are modelled; round-trips to docx as the four <c>w:pBdr</c> edges.
    /// </summary>
    public ParagraphBorder? Border { get; init; }

    /// <summary>
    /// Paragraph shading (background fill) as an RRGGBB hex (e.g. <c>"#FFFF00"</c>). Null means no
    /// shading. Round-trips to docx as paragraph shading (<c>pPr/w:shd w:fill</c>), mirroring run
    /// <see cref="RunFormatting.HighlightColorHex"/>.
    /// </summary>
    public string? ShadingColorHex { get; init; }

    public static readonly ParagraphFormatting Default = new();
}
