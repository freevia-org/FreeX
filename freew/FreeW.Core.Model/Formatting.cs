namespace FreeW.Core.Model;

/// <summary>Paragraph horizontal alignment.</summary>
public enum TextAlignment { Left, Center, Right, Justify }

/// <summary>
/// How a paragraph's line spacing is interpreted (pPr/w:spacing/@w:lineRule).
/// <see cref="Multiple"/> (the default, OOXML <c>auto</c>) treats the spacing value as a multiple of the
/// line's natural height (1 = single, 1.5, 2 = double). <see cref="Exact"/> (<c>exact</c>) forces an
/// absolute line height in points regardless of font size, and <see cref="AtLeast"/> (<c>atLeast</c>) uses
/// the value as a minimum, growing for taller content.
/// </summary>
public enum LineSpacingRule { Multiple, AtLeast, Exact }

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
/// <param name="IsClear">Whether this operation removes an inherited stop at the same position.</param>
public sealed record TabStop(
    double PositionPt,
    TabStopAlignment Alignment = TabStopAlignment.Left,
    TabLeader Leader = TabLeader.None,
    bool IsClear = false);

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
/// Border line style (the <c>w:val</c> token shared by <c>w:pBdr</c>/<c>w:pgBorders</c>/<c>w:tblBorders</c>
/// edges). <see cref="Single"/> is the default thin solid rule; the others map to Word's common
/// Borders-and-Shading line styles. Modelled as an enum so the value round-trips losslessly through the
/// edge's <c>w:val</c> attribute (see <see cref="BorderLineStyles"/> for the token mapping).
/// </summary>
public enum BorderLineStyle { Single, Dotted, Dashed, Double, Thick, Wave }

/// <summary>
/// Maps <see cref="BorderLineStyle"/> values to/from the OOXML <c>w:val</c> tokens used on border edges.
/// Centralised here so the writer, reader and any edge (paragraph/page/table) agree on one mapping.
/// </summary>
public static class BorderLineStyles
{
    /// <summary>The <c>w:val</c> token for a line style (e.g. <see cref="BorderLineStyle.Dotted"/> → "dotted").</summary>
    public static string ToToken(BorderLineStyle style) => style switch
    {
        BorderLineStyle.Dotted => "dotted",
        BorderLineStyle.Dashed => "dashed",
        BorderLineStyle.Double => "double",
        BorderLineStyle.Thick => "thick",
        BorderLineStyle.Wave => "wave",
        _ => "single",
    };

    /// <summary>Parses a <c>w:val</c> token back into a line style; unknown/solid tokens fall back to single.</summary>
    public static BorderLineStyle FromToken(string? token) => token switch
    {
        "dotted" => BorderLineStyle.Dotted,
        "dashed" => BorderLineStyle.Dashed,
        "double" => BorderLineStyle.Double,
        "thick" => BorderLineStyle.Thick,
        "wave" => BorderLineStyle.Wave,
        _ => BorderLineStyle.Single,
    };
}

/// <summary>
/// The fill pattern of paragraph/cell shading (the <c>w:shd/@w:val</c> token). <see cref="Clear"/> is a
/// solid fill of the <c>w:fill</c> colour (Word's default for "Shading"); the percentage values are the
/// classic dithered patterns. Modelled as an enum so the pattern round-trips through <c>w:shd/@w:val</c>
/// while the colour stays in <see cref="ParagraphFormatting.ShadingColorHex"/>.
/// </summary>
public enum ShadingPattern { Clear, Solid, Pct10, Pct25, Pct50 }

/// <summary>Maps <see cref="ShadingPattern"/> values to/from the OOXML <c>w:shd/@w:val</c> tokens.</summary>
public static class ShadingPatterns
{
    /// <summary>The <c>w:val</c> token for a shading pattern (e.g. <see cref="ShadingPattern.Pct25"/> → "pct25").</summary>
    public static string ToToken(ShadingPattern pattern) => pattern switch
    {
        ShadingPattern.Solid => "solid",
        ShadingPattern.Pct10 => "pct10",
        ShadingPattern.Pct25 => "pct25",
        ShadingPattern.Pct50 => "pct50",
        _ => "clear",
    };

    /// <summary>Parses a <c>w:shd/@w:val</c> token back into a pattern; unknown tokens fall back to clear.</summary>
    public static ShadingPattern FromToken(string? token) => token switch
    {
        "solid" => ShadingPattern.Solid,
        "pct10" => ShadingPattern.Pct10,
        "pct25" => ShadingPattern.Pct25,
        "pct50" => ShadingPattern.Pct50,
        _ => ShadingPattern.Clear,
    };
}

/// <summary>
/// Immutable paragraph box border (pPr/w:pBdr). By default all four edges are drawn with the given
/// colour, width and <see cref="LineStyle"/>; the per-edge flags (<see cref="Top"/>/<see cref="Left"/>/
/// <see cref="Bottom"/>/<see cref="Right"/>) select which edges are drawn (all four = a box). When
/// <paramref name="BottomOnly"/> is set only the bottom edge is drawn, which models a horizontal rule under
/// the paragraph. Round-trips to docx as the <c>w:pBdr</c> edges, mirroring how table borders map to
/// <c>w:tblBorders</c>.
/// </summary>
/// <param name="ColorHex">Border colour as an RRGGBB hex (e.g. <c>"#000000"</c>).</param>
/// <param name="WidthPt">Border width in points (docx stores this as eighths of a point in <c>w:sz</c>).</param>
/// <param name="BottomOnly">
/// When true, only the bottom edge is drawn (a horizontal rule). Defaults to false so existing callers
/// keep the full box and their docx round-trip is unchanged. Equivalent to clearing the top/left/right
/// per-edge flags; kept as a distinct flag so the existing horizontal-rule round-trip stays lossless.
/// </param>
public sealed record ParagraphBorder(string ColorHex = "#000000", double WidthPt = 0.5, bool BottomOnly = false)
{
    /// <summary>The line style of every drawn edge (w:val). Defaults to <see cref="BorderLineStyle.Single"/>.</summary>
    public BorderLineStyle LineStyle { get; init; } = BorderLineStyle.Single;

    /// <summary>Whether the top edge is drawn. Defaults to true (a full box) so existing callers are unaffected.</summary>
    public bool Top { get; init; } = true;

    /// <summary>Whether the left edge is drawn. Defaults to true (a full box).</summary>
    public bool Left { get; init; } = true;

    /// <summary>Whether the bottom edge is drawn. Defaults to true (a full box).</summary>
    public bool Bottom { get; init; } = true;

    /// <summary>Whether the right edge is drawn. Defaults to true (a full box).</summary>
    public bool Right { get; init; } = true;
}

/// <summary>
/// Authored Word theme linkage for a run color. <see cref="ValueToken"/> is the cached <c>w:val</c>
/// fallback Word writes beside <c>w:themeColor</c>; tint and shade retain their hexadecimal byte tokens.
/// </summary>
public sealed record WordThemeColor(
    string ThemeToken,
    string ValueToken,
    string? TintHex = null,
    string? ShadeHex = null);

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

    /// <summary>
    /// Whether the run uses Word's double strikethrough decoration (<c>w:rPr/w:dstrike</c>).
    /// The single and double flags are retained independently; double strike wins only at paint time.
    /// </summary>
    public bool DoubleStrikethrough { get; init; }

    /// <summary>
    /// Whether the run is hidden text (<c>w:rPr/w:vanish</c>). Defaults to false. As with the other
    /// non-nullable run toggles, style and document-default inheritance resolves this property with
    /// logical OR; a direct false value therefore means "not set here", not an explicit inheritance reset.
    /// </summary>
    public bool Hidden { get; init; }

    /// <summary>
    /// Whether the run is hidden when the document is displayed as a web page
    /// (<c>w:rPr/w:webHidden</c>). Defaults to false. Style and document-default inheritance uses the
    /// same logical-OR semantics as the other non-nullable run toggles.
    /// </summary>
    public bool WebHidden { get; init; }

    /// <summary>
    /// Whether spelling and grammar proofing is disabled for the run (<c>w:rPr/w:noProof</c>).
    /// Defaults to false. Style and document-default inheritance uses the same logical-OR semantics
    /// as the other non-nullable run toggles.
    /// </summary>
    public bool NoProof { get; init; }

    public string? FontFamily { get; init; }
    public double? FontSizePt { get; init; }
    public string? ColorHex { get; init; }

    /// <summary>
    /// Optional Word theme source for <see cref="ColorHex"/> (<c>w:color/@w:themeColor</c>, tint, and
    /// shade). The cached <see cref="ColorHex"/> remains the renderer fallback. Writers retain this link
    /// only while that fallback is unchanged, so a later fixed-color edit cannot be overridden by stale
    /// theme metadata.
    /// </summary>
    public WordThemeColor? ThemeColor { get; init; }

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
    /// Right-to-left run direction (rPr/w:rtl). When true the run's characters lay out right-to-left
    /// (Arabic/Hebrew). Defaults to false so LTR runs are unaffected and round-trip byte-unchanged.
    /// Mirrors how <see cref="Bold"/> models a docx toggle element.
    /// </summary>
    public bool Rtl { get; init; }

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

    /// <summary>
    /// Character border: a box drawn around the run's glyphs (rPr/w:rBdr). Null means no border.
    /// Reuses <see cref="ParagraphBorder"/> for colour, width and line style; the per-edge flags and
    /// <see cref="ParagraphBorder.BottomOnly"/> are honoured by the writer so an asymmetric character
    /// border round-trips. Round-trips to docx as the four <c>w:rBdr</c> edges in the same encoding as
    /// <c>w:pBdr</c>, so existing paragraphs are unaffected.
    /// </summary>
    public ParagraphBorder? CharacterBorder { get; init; }

    /// <summary>
    /// Character shading (background fill with optional pattern) as an RRGGBB hex (e.g. <c>"#FFFF00"</c>).
    /// Null means no shading. Round-trips to docx as run shading (<c>w:rPr/w:shd</c>) using the
    /// <see cref="CharacterShadingPattern"/> for <c>w:val</c> and this value for <c>w:fill</c>. When set,
    /// overrides <see cref="HighlightColorHex"/> in the DOCX writer (both share the <c>w:shd</c> slot;
    /// this field takes precedence so patterns are preserved).
    /// </summary>
    public string? CharacterShadingHex { get; init; }

    /// <summary>
    /// The fill pattern of <see cref="CharacterShadingHex"/> (rPr/w:shd/@w:val). Defaults to
    /// <see cref="ShadingPattern.Clear"/> — a solid fill — which preserves existing highlight behaviour.
    /// Only meaningful when <see cref="CharacterShadingHex"/> is set.
    /// </summary>
    public ShadingPattern CharacterShadingPattern { get; init; } = ShadingPattern.Clear;

    /// <summary>
    /// BCP-47 proofing language tag for this run (rPr/w:lang), e.g. <c>"en-US"</c>, <c>"fr-FR"</c>.
    /// Null means no explicit language (inherits from the paragraph/document default). Round-trips to
    /// docx as <c>w:lang w:val</c>; also sets the WPF run's <c>xml:lang</c> so the built-in spell checker
    /// uses the correct dictionary when one is available.
    /// </summary>
    public string? LanguageTag { get; init; }

    public static readonly RunFormatting Default = new();
}

/// <summary>
/// Immutable paragraph formatting (pPr): alignment, spacing, indents, list. Points throughout,
/// matching the docx unit model once divided/multiplied by the OOXML twentieths.
/// </summary>
public sealed record ParagraphFormatting
{
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;

    /// <summary>
    /// Right-to-left paragraph direction (pPr/w:bidi). When true the paragraph lays out right-to-left
    /// (Arabic/Hebrew) and its default alignment is the right edge. Defaults to false so LTR paragraphs are
    /// unaffected and round-trip byte-unchanged. Maps to WPF <c>FlowDirection.RightToLeft</c> in the editor.
    /// </summary>
    public bool Rtl { get; init; }

    public double SpaceBeforePt { get; init; }
    public double SpaceAfterPt { get; init; } = 8;

    /// <summary>
    /// Whether Word's <c>w:beforeAutospacing</c> token controls the before-spacing axis. The renderer keeps
    /// <see cref="SpaceBeforePt"/>'s measured automatic-spacing approximation, while this flag preserves the
    /// source semantic so a save writes the automatic token instead of a conflicting numeric value.
    /// </summary>
    public bool BeforeAutoSpacing { get; init; }

    /// <summary><inheritdoc cref="BeforeAutoSpacing"/></summary>
    public bool AfterAutoSpacing { get; init; }

    /// <summary>
    /// Word's <c>w:contextualSpacing</c> state: enabled suppresses the before/after gap between adjacent
    /// paragraphs of the same effective style, disabled preserves an explicit <c>w:val="0"</c>, and null
    /// leaves the token absent so the surrounding style/document default remains authoritative.
    /// </summary>
    public bool? ContextualSpacing { get; init; }

    /// <summary>
    /// Whether <see cref="SpaceBeforePt"/> / <see cref="SpaceAfterPt"/> were set *explicitly* on this
    /// paragraph or style (a direct <c>w:spacing/@w:before</c>/<c>@w:after</c> or an autospacing toggle), as
    /// opposed to an inherited document-default/built-in value. Lets the render-time cascade inherit the
    /// paragraph's style spacing when the paragraph sets none, instead of FreeW's 0/8pt default. Render-only
    /// (mirrors <see cref="LineSpacingIsSet"/>); the writer emits from the value fields, so round-trip is
    /// unaffected.
    /// </summary>
    public bool SpaceBeforeIsSet { get; init; }

    /// <summary><inheritdoc cref="SpaceBeforeIsSet"/></summary>
    public bool SpaceAfterIsSet { get; init; }

    /// <summary>
    /// Line spacing as a multiple of the natural line height (pPr/w:spacing/@w:line with
    /// <c>lineRule="auto"</c>): 1 = single, 1.15 (the default), 1.5, 2 = double. Used only when
    /// <see cref="LineRule"/> is <see cref="LineSpacingRule.Multiple"/>; for the exact/at-least rules the
    /// absolute height in <see cref="LineHeightPt"/> applies instead.
    /// </summary>
    public double LineSpacing { get; init; } = 1.15;

    /// <summary>
    /// How <see cref="LineSpacing"/> / <see cref="LineHeightPt"/> is interpreted (w:lineRule). Defaults to
    /// <see cref="LineSpacingRule.Multiple"/> so existing paragraphs are unaffected.
    /// </summary>
    public LineSpacingRule LineRule { get; init; } = LineSpacingRule.Multiple;

    /// <summary>
    /// Absolute line height in points, used when <see cref="LineRule"/> is
    /// <see cref="LineSpacingRule.Exact"/> or <see cref="LineSpacingRule.AtLeast"/> (w:line with
    /// <c>lineRule="exact"/"atLeast"</c>, the value in twentieths of a point). Zero (the default) when the
    /// multiple rule applies.
    /// </summary>
    public double LineHeightPt { get; init; }

    /// <summary>
    /// Whether the line spacing (<see cref="LineSpacing"/>/<see cref="LineRule"/>/<see cref="LineHeightPt"/>)
    /// was set *explicitly* on this paragraph or style (a direct <c>w:spacing/@w:line</c>), as opposed to
    /// carrying the inherited document-default/built-in value. Lets the render-time cascade tell an explicit
    /// setting from an inherited one so a paragraph with no direct line spacing inherits its style's value
    /// (Word's cascade: direct ?? style ?? docDefault) rather than the masked default. Render-only; the writer
    /// continues to emit from the value fields, so this does not affect docx round-trip.
    /// </summary>
    public bool LineSpacingIsSet { get; init; }

    public double IndentLeftPt { get; init; }
    public double IndentRightPt { get; init; }
    public double FirstLineIndentPt { get; init; }
    public ListKind ListKind { get; init; } = ListKind.None;
    public int ListLevel { get; init; }

    /// <summary>
    /// When set, this paragraph restarts its list counter at the given value (1-based, Word's "Restart
    /// at 1" is value 1). Null means "continue" — the counter runs on from the previous list item.
    /// Maps to <c>w:lvlOverride/w:startOverride</c> on a dedicated <c>w:num</c> in numbering.xml; only
    /// meaningful when <see cref="ListKind"/> is <see cref="ListKind.Number"/> or
    /// <see cref="ListKind.MultiLevel"/>; silently ignored for bullets.
    /// <para>
    /// Round-trips end-to-end: the writer emits a per-restart <c>w:num</c> clone pointing at the same
    /// <c>w:abstractNum</c> with <c>w:lvlOverride/@startOverride</c>; the reader detects such a num and
    /// maps it back, restoring this property on the paragraph.
    /// </para>
    /// </summary>
    public int? ListStartOverride { get; init; }

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
    /// A paragraph without an explicit token uses Word's default-on behavior when rendered. The model keeps
    /// the serialized value separately through <see cref="WidowControlIsSet"/>, so an explicit off token
    /// remains distinguishable from an omitted token during package round-trip.
    /// </para>
    /// </summary>
    public bool WidowControl { get; init; }

    /// <summary>
    /// True when the source paragraph explicitly carries <c>w:widowControl</c>, including an explicit off
    /// value. This preserves Word's omitted/default-on distinction without changing the public boolean's
    /// default value for newly created model paragraphs.
    /// </summary>
    public bool WidowControlIsSet { get; init; }

    /// <summary>
    /// When true, automatic hyphenation is suppressed for this paragraph (pPr/w:suppressAutoHyphens), even
    /// when the document has <see cref="PageSettings.AutoHyphenation"/> on. Defaults to false so existing
    /// paragraphs are unaffected; round-trips to docx as the <c>w:suppressAutoHyphens</c> toggle, mirroring
    /// <see cref="WidowControl"/>. The live editor honours it by skipping soft-hyphen insertion for the
    /// paragraph's runs.
    /// </summary>
    public bool SuppressAutoHyphens { get; init; }

    /// <summary>
    /// When true, Word omits line numbers alongside this paragraph
    /// (<c>pPr/w:suppressLineNumbers</c>) while retaining the paragraph's place in the line-number
    /// sequence. <see cref="SuppressLineNumbersIsSet"/> preserves an explicit off token so direct
    /// paragraph formatting can override a style-level suppression.
    /// </summary>
    public bool SuppressLineNumbers { get; init; }

    /// <summary>
    /// True when the source paragraph explicitly carries <c>w:suppressLineNumbers</c>, including an
    /// explicit off value. This distinguishes inherited/absent formatting from an authored override.
    /// </summary>
    public bool SuppressLineNumbersIsSet { get; init; }

    /// <summary>
    /// Paragraph shading (background fill) as an RRGGBB hex (e.g. <c>"#FFFF00"</c>). Null means no
    /// shading. Round-trips to docx as paragraph shading (<c>pPr/w:shd w:fill</c>), mirroring run
    /// <see cref="RunFormatting.HighlightColorHex"/>.
    /// </summary>
    public string? ShadingColorHex { get; init; }

    /// <summary>
    /// The fill pattern of <see cref="ShadingColorHex"/> (pPr/w:shd/@w:val). Defaults to
    /// <see cref="ShadingPattern.Clear"/> — a solid fill of the colour — which is what the existing
    /// writer emitted, so paragraphs round-trip byte-unchanged. Only meaningful when
    /// <see cref="ShadingColorHex"/> is set.
    /// </summary>
    public ShadingPattern ShadingPattern { get; init; } = ShadingPattern.Clear;

    /// <summary>
    /// Paragraph tab stops (pPr/w:tabs), in document order. Never null; defaults to an empty list so
    /// paragraphs without explicit stops are unaffected. Round-trips to docx as one <c>w:tab</c> per
    /// stop, mirroring how <c>w:ind</c>/<c>w:spacing</c> are written/read.
    /// </summary>
    public IReadOnlyList<TabStop> TabStops { get; init; } = [];

    public static readonly ParagraphFormatting Default = new();
}
