namespace FreeW.Core.Model;

public enum StyleType { Paragraph, Character }

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
    /// The original <c>w:pPr/w:numPr</c> (numId + ilvl) this style's definition carried on read that FreeW
    /// does not model as one of its own lists. Captured so the writer can re-emit the style's numbering
    /// pointing at the preserved <see cref="PreservedParts.OriginalNumbering"/> definition (after the same
    /// disjoint-id remap used for paragraph-level preserved numbering, keeping it clear of FreeW's own fixed
    /// list ids). Null for an authored-from-scratch style (or a FreeW-modelled list), so such a style emits no
    /// numbering and round-trips unchanged.
    /// </summary>
    public PreservedNumbering? PreservedNumbering { get; set; }
}
