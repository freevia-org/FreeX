namespace FreeW.Core.Model;

/// <summary>
/// Pure, view-independent document operations for the Insert &gt; Pages group: building a simple cover
/// page, a horizontal rule paragraph, and a page-break paragraph. Each returns plain model blocks so
/// the operations are testable without WPF; the editor wires them to the undo/redo bus and re-renders.
/// </summary>
public static class DocumentOps
{
    /// <summary>The placeholder title used when the document has no <see cref="DocumentProperties.Title"/>.</summary>
    public const string DefaultCoverTitle = "Document Title";

    /// <summary>
    /// Builds the cover-page blocks for <paramref name="document"/>: a <c>Title</c>-styled paragraph
    /// (the document title, or <see cref="DefaultCoverTitle"/> when unset), a <c>Subtitle</c>-styled
    /// paragraph carrying the author (only when <see cref="DocumentProperties.Author"/> is set), and a
    /// trailing empty spacer paragraph. The blocks are returned in document order, ready to prepend at
    /// the start of the body. Pure: it does not mutate <paramref name="document"/>.
    /// </summary>
    public static IReadOnlyList<Block> BuildCoverPage(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var blocks = new List<Block>();

        var title = document.Properties.Title is { Length: > 0 } t ? t : DefaultCoverTitle;
        blocks.Add(new Paragraph(title)
        {
            StyleId = "Title",
            Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Center }
        });

        if (document.Properties.Author is { Length: > 0 } author)
        {
            blocks.Add(new Paragraph(author)
            {
                StyleId = "Subtitle",
                Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Center }
            });
        }

        // A blank spacer paragraph so the cover content is visually separated from the body that follows.
        blocks.Add(new Paragraph());
        return blocks;
    }

    /// <summary>
    /// Prepends a simple cover page (see <see cref="BuildCoverPage"/>) at the start of
    /// <paramref name="document"/>'s body, in document order. Mutates the document in place.
    /// </summary>
    public static void InsertCoverPage(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var blocks = BuildCoverPage(document);
        for (var i = 0; i < blocks.Count; i++)
            document.Blocks.Insert(i, blocks[i]);
    }

    /// <summary>
    /// Builds a horizontal-rule paragraph: an empty paragraph whose formatting carries a bottom-only
    /// border (see <see cref="ParagraphBorder.BottomOnly"/>), which renders and round-trips as a thin
    /// line under the paragraph.
    /// </summary>
    public static Paragraph CreateHorizontalRule(string colorHex = "#808080", double widthPt = 0.5) =>
        new()
        {
            Formatting = ParagraphFormatting.Default with
            {
                Border = new ParagraphBorder(colorHex, widthPt, BottomOnly: true)
            }
        };

    /// <summary>
    /// Builds a page-break paragraph: an empty paragraph whose formatting forces a page break before it
    /// (see <see cref="ParagraphFormatting.PageBreakBefore"/>), which Word honours when paginating.
    /// </summary>
    public static Paragraph CreatePageBreak() =>
        new()
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        };
}
