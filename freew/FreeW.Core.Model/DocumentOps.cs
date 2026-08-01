using Free.Shared.Opc;

namespace FreeW.Core.Model;

/// <summary>
/// Pure, view-independent document operations for the Insert &gt; Pages group: building a simple cover
/// page, a blank page, a horizontal rule paragraph, and a page-break paragraph. Each returns plain model blocks so
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
    /// Builds the paragraphs needed to insert a whole blank page at the caret. The first paragraph starts
    /// the blank page; the second starts the following page, so existing downstream content is pushed after
    /// the inserted blank page.
    /// </summary>
    public static IReadOnlyList<Block> BuildBlankPage() =>
    [
        CreatePageBreak(),
        CreatePageBreak()
    ];

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

    /// <summary>
    /// Creates a paragraph that ends a section with the given <paramref name="breakKind"/>. Setting
    /// <see cref="Paragraph.SectionBreak"/> on a paragraph makes it the last paragraph of a section,
    /// carrying that section's page setup. A new <see cref="PageSettings"/> is cloned from
    /// <paramref name="inherited"/> (when supplied) so the new section inherits the current layout;
    /// when <paramref name="inherited"/> is null a fresh default <see cref="PageSettings"/> is used.
    /// </summary>
    public static Paragraph CreateSectionBreak(SectionBreakKind breakKind, PageSettings? inherited = null) =>
        new()
        {
            SectionBreak = new Section(inherited?.Clone() ?? new PageSettings(), breakKind)
        };

    /// <summary>
    /// Creates a column-break paragraph. Word represents a column break as a <c>w:br w:type="column"</c>
    /// run inside a paragraph. The break-only paragraph keeps that source semantic distinct from a page
    /// break so multi-column layout advances to the next column and DOCX save emits the authored token.
    /// </summary>
    public static Paragraph CreateColumnBreak() =>
        new() { Runs = { Run.ColumnBreak() } };

    /// <summary>
    /// Builds the cover-page blocks for <paramref name="document"/> using the given <paramref name="preset"/>:
    /// <see cref="CoverPagePreset.Default"/> uses the existing centred layout; <see cref="CoverPagePreset.Banded"/>
    /// uses a left-aligned dark-blue-banded title; <see cref="CoverPagePreset.Motion"/> uses right-aligned
    /// title with an italic date paragraph. All presets return blocks ready to prepend at the start of the body.
    /// Pure: does not mutate <paramref name="document"/>.
    /// </summary>
    public static IReadOnlyList<Block> BuildCoverPage(TextDocument document, CoverPagePreset preset)
    {
        ArgumentNullException.ThrowIfNull(document);
        return preset switch
        {
            CoverPagePreset.Banded => BuildBandedCoverPage(document),
            CoverPagePreset.Motion => BuildMotionCoverPage(document),
            _ => BuildCoverPage(document)
        };
    }

    private static IReadOnlyList<Block> BuildBandedCoverPage(TextDocument document)
    {
        var blocks = new List<Block>();
        var title = document.Properties.Title is { Length: > 0 } t ? t : DefaultCoverTitle;

        blocks.Add(new Paragraph(title)
        {
            StyleId = "Title",
            Formatting = ParagraphFormatting.Default with
            {
                Alignment = TextAlignment.Left,
                ShadingColorHex = "#1F3864",
                SpaceBeforePt = 24
            }
        });

        if (document.Properties.Author is { Length: > 0 } author)
        {
            blocks.Add(new Paragraph(author)
            {
                StyleId = "Subtitle",
                Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Left }
            });
        }

        blocks.Add(new Paragraph());
        return blocks;
    }

    private static IReadOnlyList<Block> BuildMotionCoverPage(TextDocument document)
    {
        var blocks = new List<Block>();
        var title = document.Properties.Title is { Length: > 0 } t ? t : DefaultCoverTitle;

        blocks.Add(new Paragraph(title)
        {
            StyleId = "Title",
            Formatting = ParagraphFormatting.Default with
            {
                Alignment = TextAlignment.Right,
                SpaceBeforePt = 72
            }
        });

        // A date paragraph: italic text showing today's date, right-aligned.
        var dateText = DateTime.Now.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var datePara = new Paragraph();
        datePara.Runs.Add(new Run(dateText)
        {
            Formatting = new RunFormatting { Italic = true }
        });
        datePara.Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Right };
        blocks.Add(datePara);

        blocks.Add(new Paragraph());
        return blocks;
    }
}

/// <summary>
/// The three built-in cover-page presets available via the Insert &gt; Pages &gt; Cover Page gallery.
/// <see cref="Default"/> is the existing centred Title + optional Subtitle layout; <see cref="Banded"/>
/// adds a dark-blue accent band behind the title (left-aligned); <see cref="Motion"/> right-aligns the
/// title with a date line below it.
/// </summary>
public enum CoverPagePreset
{
    Default,
    Banded,
    Motion
}
