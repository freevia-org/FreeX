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
/// Leader fill drawn across the empty space a tab character jumps over (maps to OOXML
/// w:tab/@w:leader). <see cref="None"/> leaves the gap blank.
/// </summary>
public enum TabLeader { None, Dots, Dashes, Underline }

/// <summary>
/// Immutable paragraph tab stop (pPr/w:tabs/w:tab). Round-trips to docx as a single
/// <c>w:tab</c> with <c>w:pos</c> in dxa (twentieths of a point), <c>w:val</c> giving the
/// alignment, and an optional <c>w:leader</c> for the fill drawn across the tab gap.
/// </summary>
/// <param name="PositionPt">Tab-stop position from the left margin, in points.</param>
/// <param name="Alignment">How text aligns at the stop.</param>
/// <param name="Leader">Fill drawn across the tab gap; <see cref="TabLeader.None"/> leaves it blank.</param>
public sealed record TabStop(
    double PositionPt,
    TabStopAlignment Alignment = TabStopAlignment.Left,
    TabLeader Leader = TabLeader.None);

/// <summary>
/// Vertical alignment of a run's glyphs relative to the baseline (rPr/w:vertAlign). Maps to docx
/// <c>w:vertAlign w:val="superscript|subscript"</c>; <see cref="Baseline"/> writes nothing.
/// </summary>
public enum VerticalAlign { Baseline, Superscript, Subscript }

/// <summary>
/// OpenType ligature mode for a run (rPr/w14:ligatures, the Office 2010 extension). <see cref="None"/>
/// is the default and writes nothing (existing runs round-trip unchanged); the remaining values map to the
/// <c>w14:val</c> tokens <c>none</c>, <c>standard</c>, <c>contextual</c>, <c>standardContextual</c>,
/// <c>historical</c>, <c>discretional</c>, <c>standardHistorical</c>, <c>contextualHistorical</c>,
/// <c>standardContextualHistorical</c>, <c>contextualDiscretional</c>, <c>standardDiscretional</c>,
/// <c>standardContextualDiscretional</c>, <c>historicalDiscretional</c>, <c>standardHistoricalDiscretional</c>,
/// <c>contextualHistoricalDiscretional</c>, <c>all</c>. <see cref="NoneExplicit"/> is the explicit
/// <c>w14:val="none"</c> (ligatures deliberately turned off), distinct from <see cref="None"/> which emits
/// no element at all.
/// </summary>
public enum LigatureMode
{
    /// <summary>No w14:ligatures element is emitted (inherit / default). Existing runs map here.</summary>
    None,
    /// <summary>Explicit <c>w14:val="none"</c> — ligatures turned off.</summary>
    NoneExplicit,
    Standard,
    Contextual,
    StandardContextual,
    Historical,
    Discretional,
    StandardHistorical,
    ContextualHistorical,
    StandardContextualHistorical,
    ContextualDiscretional,
    StandardDiscretional,
    StandardContextualDiscretional,
    HistoricalDiscretional,
    StandardHistoricalDiscretional,
    ContextualHistoricalDiscretional,
    All
}

/// <summary>
/// OpenType number-form style for a run (rPr/w14:numForm). <see cref="Default"/> emits nothing (existing
/// runs round-trip unchanged); <see cref="Lining"/> / <see cref="OldStyle"/> map to the <c>w14:val</c>
/// tokens <c>lining</c> / <c>oldStyle</c> (<see cref="Default"/> would be <c>default</c>).
/// </summary>
public enum NumberForm { Default, Lining, OldStyle }

/// <summary>
/// OpenType number-spacing style for a run (rPr/w14:numSpacing). <see cref="Default"/> emits nothing
/// (existing runs round-trip unchanged); <see cref="Proportional"/> / <see cref="Tabular"/> map to the
/// <c>w14:val</c> tokens <c>proportional</c> / <c>tabular</c> (<see cref="Default"/> would be <c>default</c>).
/// </summary>
public enum NumberSpacing { Default, Proportional, Tabular }

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

    /// <summary>
    /// Advanced character spacing (expand/condense), in points (rPr/w:spacing). Positive expands the
    /// inter-character spacing, negative condenses it. Defaults to <c>0</c> (no <c>w:spacing</c> emitted),
    /// so existing runs round-trip byte-unchanged. Stored in points to match FreeW's unit model; the writer
    /// converts to twentieths of a point (dxa) for <c>w:spacing/@w:val</c>.
    /// </summary>
    public double CharacterSpacingPt { get; init; }

    /// <summary>
    /// Kerning threshold: the minimum font size (in points) at which kerning is applied (rPr/w:kern). Null
    /// (the default) emits no <c>w:kern</c>, so existing runs are unaffected. A value of <c>0</c> would
    /// disable kerning; we model "unset" as null and only emit the element when a positive threshold is set.
    /// The writer converts the threshold to half-points for <c>w:kern/@w:val</c>.
    /// </summary>
    public double? KerningMinSizePt { get; init; }

    /// <summary>
    /// Raised/lowered baseline position, in points (rPr/w:position) — distinct from
    /// <see cref="VerticalAlign"/> super/subscript, which also shrinks the glyph. Positive raises the text,
    /// negative lowers it. Defaults to <c>0</c> (no <c>w:position</c> emitted) so existing runs round-trip
    /// unchanged. The writer converts to half-points for <c>w:position/@w:val</c>.
    /// </summary>
    public double PositionPt { get; init; }

    /// <summary>
    /// OpenType ligature mode (rPr/w14:ligatures). Defaults to <see cref="LigatureMode.None"/>, which emits
    /// no element so existing runs are unaffected. Any other value emits a <c>w14:ligatures</c> in the w14
    /// extension region of the run properties.
    /// </summary>
    public LigatureMode Ligatures { get; init; } = LigatureMode.None;

    /// <summary>
    /// OpenType stylistic set id (rPr/w14:stylisticSets/w14:styleSet/@w14:id), or null for none. Defaults
    /// to null so no <c>w14:stylisticSets</c> is emitted and existing runs are unaffected. When set, a single
    /// stylistic set is applied (e.g. <c>1</c> selects "Stylistic Set 1"). Modelled as a single optional id
    /// (the common case); the reader recovers the first declared set when several are present.
    /// </summary>
    public int? StylisticSet { get; init; }

    /// <summary>
    /// OpenType number form (rPr/w14:numForm). Defaults to <see cref="NumberForm.Default"/>, which emits no
    /// element so existing runs are unaffected; <see cref="NumberForm.Lining"/>/<see cref="NumberForm.OldStyle"/>
    /// emit a <c>w14:numForm</c> in the w14 extension region.
    /// </summary>
    public NumberForm NumberForm { get; init; } = NumberForm.Default;

    /// <summary>
    /// OpenType number spacing (rPr/w14:numSpacing). Defaults to <see cref="NumberSpacing.Default"/>, which
    /// emits no element so existing runs are unaffected; <see cref="NumberSpacing.Proportional"/>/
    /// <see cref="NumberSpacing.Tabular"/> emit a <c>w14:numSpacing</c> in the w14 extension region.
    /// </summary>
    public NumberSpacing NumberSpacing { get; init; } = NumberSpacing.Default;

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
    /// When true, this paragraph is kept on the same page as the one that follows it
    /// (pPr/w:keepNext). Defaults to false so existing paragraphs are unaffected. Round-trips to docx
    /// as the <c>w:keepNext</c> toggle, mirroring <see cref="PageBreakBefore"/>; the editor maps it to
    /// WPF <c>Paragraph.KeepWithNext</c>.
    /// </summary>
    public bool KeepWithNext { get; init; }

    /// <summary>
    /// When true, all lines of this paragraph are kept together on a single page rather than split
    /// across a page boundary (pPr/w:keepLines). Defaults to false so existing paragraphs are
    /// unaffected. Round-trips to docx as the <c>w:keepLines</c> toggle, mirroring
    /// <see cref="PageBreakBefore"/>; the editor maps it to WPF <c>Paragraph.KeepTogether</c>.
    /// </summary>
    public bool KeepLinesTogether { get; init; }

    /// <summary>
    /// When true, widow/orphan control is enabled for this paragraph (pPr/w:widowControl), preventing a
    /// single first/last line from being stranded alone on a page. Round-trips to docx as the
    /// <c>w:widowControl</c> toggle, mirroring <see cref="PageBreakBefore"/>.
    /// <para>
    /// Defaults to <c>false</c>. Note: real Word enables widow control by default; FreeW intentionally
    /// keeps it off by default so that existing documents/round-trips are unchanged (a paragraph with no
    /// explicit <c>w:widowControl</c> reads back as false here, not Word's implicit on). The WPF
    /// FlowDocument has no widow-control property, so this flag is carried through the model/docx only
    /// (preserved across an editor edit/commit cycle via the paragraph's Tag).
    /// </para>
    /// </summary>
    public bool WidowControl { get; init; }

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
