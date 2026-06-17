namespace FreeW.Core.Model;

/// <summary>Paragraph horizontal alignment.</summary>
public enum TextAlignment { Left, Center, Right, Justify }

/// <summary>List decoration for a paragraph.</summary>
/// <remarks>
/// <see cref="MultiLevel"/> is an outline (legal) numbering whose level text accumulates the
/// ancestors' counters, e.g. <c>1</c>, <c>1.1</c>, <c>1.1.1</c>. It persists as a third
/// numbering definition in word/numbering.xml; see <c>DocxWriter.BuildNumbering</c>.
/// </remarks>
public enum ListKind { None, Bullet, Number, MultiLevel }

/// <summary>Horizontal alignment of text at a paragraph tab stop (maps to OOXML w:tab/@w:val).</summary>
public enum TabStopAlignment { Left, Center, Right, Decimal }

/// <summary>
/// Immutable paragraph tab stop (pPr/w:tabs/w:tab). Round-trips to docx as a single
/// <c>w:tab</c> with <c>w:pos</c> in dxa (twentieths of a point) and <c>w:val</c> giving the
/// alignment.
/// </summary>
/// <param name="PositionPt">Tab-stop position from the left margin, in points.</param>
/// <param name="Alignment">How text aligns at the stop.</param>
public sealed record TabStop(double PositionPt, TabStopAlignment Alignment = TabStopAlignment.Left);

/// <summary>
/// Vertical alignment of a run's glyphs relative to the baseline (rPr/w:vertAlign). Maps to docx
/// <c>w:vertAlign w:val="superscript|subscript"</c>; <see cref="Baseline"/> writes nothing.
/// </summary>
public enum VerticalAlign { Baseline, Superscript, Subscript }

/// <summary>
/// Immutable paragraph box border (pPr/w:pBdr). By default all four edges are drawn with the given
/// colour and width; when <paramref name="BottomOnly"/> is set only the bottom edge is drawn, which
/// models a horizontal rule under the paragraph. Round-trips to docx as the <c>w:pBdr</c> edges (each
/// <c>w:val="single"</c>) — all four for a box, or just <c>w:bottom</c> for a bottom-only rule —
/// mirroring how table borders map to <c>w:tblBorders</c>.
/// </summary>
/// <param name="ColorHex">Border colour as an RRGGBB hex (e.g. <c>"#000000"</c>).</param>
/// <param name="WidthPt">Border width in points (docx stores this as eighths of a point in <c>w:sz</c>).</param>
/// <param name="BottomOnly">
/// When true, only the bottom edge is drawn (a horizontal rule). Defaults to false so existing callers
/// keep the full box and their docx round-trip is unchanged.
/// </param>
public sealed record ParagraphBorder(string ColorHex = "#000000", double WidthPt = 0.5, bool BottomOnly = false);

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
    /// When true, a page break is forced before this paragraph (pPr/w:pageBreakBefore). Defaults to
    /// false so existing paragraphs are unaffected. Round-trips to docx as <c>w:pageBreakBefore</c>,
    /// which Word honours when paginating; FreeW's editor renders it as a visual separator above the
    /// paragraph.
    /// </summary>
    public bool PageBreakBefore { get; init; }

    /// <summary>
    /// Paragraph shading (background fill) as an RRGGBB hex (e.g. <c>"#FFFF00"</c>). Null means no
    /// shading. Round-trips to docx as paragraph shading (<c>pPr/w:shd w:fill</c>), mirroring run
    /// <see cref="RunFormatting.HighlightColorHex"/>.
    /// </summary>
    public string? ShadingColorHex { get; init; }

    /// <summary>
    /// Paragraph tab stops (pPr/w:tabs), in document order. Never null; defaults to an empty list so
    /// paragraphs without explicit stops are unaffected. Round-trips to docx as one <c>w:tab</c> per
    /// stop, mirroring how <c>w:ind</c>/<c>w:spacing</c> are written/read.
    /// </summary>
    public IReadOnlyList<TabStop> TabStops { get; init; } = [];

    public static readonly ParagraphFormatting Default = new();
}
