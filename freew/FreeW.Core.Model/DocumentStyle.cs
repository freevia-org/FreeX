namespace FreeW.Core.Model;

public enum StyleType { Paragraph, Character, Table, Numbering }

/// <summary>
/// A named style (Word's styles.xml). Carries optional run and paragraph formatting that a
/// paragraph/run resolves through, optionally chaining via <see cref="BasedOnStyleId"/>.
/// </summary>
public sealed class DocumentStyle
{
    public DocumentStyle()
    {
    }

    /// <summary>
    /// Copy constructor: every catalog rewrite (modify, rename, delete-promotion, undo snapshot) starts
    /// from a complete copy and overrides only what it changes through an object initializer, so a newly
    /// added property can never be silently dropped by one of those call sites.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public DocumentStyle(DocumentStyle source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Id = source.Id;
        Name = source.Name;
        Type = source.Type;
        BasedOnStyleId = source.BasedOnStyleId;
        IsDefault = source.IsDefault;
        NextStyleId = source.NextStyleId;
        LinkedStyleId = source.LinkedStyleId;
        QuickFormat = source.QuickFormat;
        OutlineLevel = source.OutlineLevel;
        Run = source.Run;
        Paragraph = source.Paragraph;
        TableBorders = source.TableBorders;
        PreservedTableStyleXml = source.PreservedTableStyleXml;
        PreservedNumbering = source.PreservedNumbering;
    }

    public required string Id { get; init; }
    public required string Name { get; init; }
    public StyleType Type { get; init; } = StyleType.Paragraph;
    public string? BasedOnStyleId { get; init; }

    /// <summary>
    /// Whether the source package marked this style as the default for its <see cref="Type"/>
    /// (<c>w:style/@w:default="1"</c>). A paragraph with no <c>w:pStyle</c> resolves through the default
    /// paragraph style whatever its id is — localized Word builds name it e.g. <c>a</c>, not
    /// <c>Normal</c>. Resolution is render-side (see <see cref="FindDefaultParagraphStyleId"/>); the id
    /// is never baked into paragraphs, so pStyle-less paragraphs round-trip pStyle-less.
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// The id of the document's default paragraph style (the first paragraph style flagged
    /// <see cref="IsDefault"/>), or null when the document declares none.
    /// </summary>
    public static string? FindDefaultParagraphStyleId(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (var style in document.Styles.Values)
        {
            if (style.IsDefault && style.Type == StyleType.Paragraph)
                return style.Id;
        }
        return null;
    }

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
    /// style when applied to a run selection. FreeW preserves the pairing during round-trip and resolves a
    /// paragraph style through its character link when named-style application targets selected text.
    /// Null when the style carries no link.
    /// </summary>
    public string? LinkedStyleId { get; init; }

    /// <summary>
    /// Word's "Add to the Styles gallery" flag (<c>w:style/w:qFormat</c>): whether a custom style is
    /// offered in the Home &gt; Styles quick gallery. A style that clears it stays in the catalog (and in
    /// Manage Styles) but is not shown as a gallery swatch. Styles authored in FreeW default to shown;
    /// the DOCX reader takes the value from the package.
    /// </summary>
    public bool QuickFormat { get; init; } = true;

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
