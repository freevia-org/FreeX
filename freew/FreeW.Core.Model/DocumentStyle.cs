namespace FreeW.Core.Model;

public enum StyleType { Paragraph, Character, Table, Numbering }

/// <summary>
/// A named style (Word's styles.xml). Carries optional run and paragraph formatting that a
/// paragraph/run resolves through, optionally chaining via <see cref="BasedOnStyleId"/>.
/// </summary>
public sealed class DocumentStyle
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public StyleType Type { get; init; } = StyleType.Paragraph;
    public string? BasedOnStyleId { get; init; }

    /// <summary>
    /// The style applied to the paragraph created when the user presses Enter at the end of a paragraph
    /// carrying this style — Word's "Style for following paragraph" (<c>w:next</c> in styles.xml). For
    /// example the built-in Heading styles set this to <c>Normal</c> so body text follows a heading. Null
    /// when the style does not specify a follow-on (Word then keeps the same style). Only meaningful for
    /// paragraph styles; ignored for character styles. A value that does not name an existing style is
    /// dropped on read/create to avoid a dangling reference.
    /// </summary>
    public string? NextStyleId { get; init; }

    /// <summary>
    /// The paired style id (Word's <c>w:style/w:link</c>) — e.g. a paragraph style's linked character
    /// style, or a character style's linked paragraph style. Word uses this to offer a single named style
    /// (like "Heading 1") that behaves as a paragraph style when applied to a paragraph and a character
    /// style when applied to a run selection. FreeW does not resolve through the link; it is captured and
    /// re-emitted only so the pairing survives a round-trip instead of silently unlinking the built-in
    /// style pairs. Null when the style carries no link.
    /// </summary>
    public string? LinkedStyleId { get; init; }

    /// <summary>
    /// The Word outline level carried by <c>w:pPr/w:outlineLvl</c> for a paragraph style.
    /// Heading styles use levels 0 through 8; null preserves an ordinary, non-outline style.
    /// </summary>
    public int? OutlineLevel { get; init; }
    public RunFormatting Run { get; set; } = RunFormatting.Default;
    public ParagraphFormatting Paragraph { get; set; } = ParagraphFormatting.Default;

    /// <summary>
    /// Whether this (table) style defines visible cell borders in its <c>w:tblPr/w:tblBorders</c> — e.g. the
    /// built-in <c>TableGrid</c> style Word applies to a default bordered table. A table that references such
    /// a style via <c>w:tblStyle</c> but sets no explicit <c>tblBorders</c> of its own still draws borders;
    /// the reader ORs this into the table's resolved <see cref="TableFormatting.Borders"/>. False for styles
    /// with no table borders (the common case), so non-table styles are unaffected.
    /// </summary>
    public bool TableBorders { get; init; }

    /// <summary>
    /// Exact imported <c>w:style w:type="table"</c> payload. FreeW does not yet model every conditional
    /// table-style band, so retaining the source element prevents first-row, banding, and edge formatting
    /// from disappearing during an unrelated document edit. Null for FreeW-authored styles.
    /// </summary>
    public string? PreservedTableStyleXml { get; init; }

    /// <summary>
    /// The original <c>w:pPr/w:numPr</c> (numId + ilvl) this style's definition carried on read that FreeW
    /// does not model as one of its own lists. Captured so the writer can re-emit the style's numbering
    /// pointing at the preserved <see cref="PreservedParts.OriginalNumbering"/> definition (after the same
    /// disjoint-id remap used for paragraph-level preserved numbering, keeping it clear of FreeW's own fixed
    /// list ids). Null for an authored-from-scratch style (or a FreeW-modelled list), so such a style emits no
    /// numbering and round-trips unchanged.
    /// </summary>
    public PreservedNumbering? PreservedNumbering { get; set; }
}
