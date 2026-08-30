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
    ///
    /// <para>
    /// <paramref name="inheritedHeadersFooters"/> mirrors that same inheritance for headers/footers
    /// (see <see cref="ResolveInheritedHeadersFooters"/>): a brand-new <see cref="Section"/> otherwise
    /// gets an entirely empty <see cref="SectionHeadersFooters"/>, which, for the overwhelmingly common
    /// case (splitting the document's only section, which owns the header/footer via
    /// <see cref="TextDocument.FinalSectionHeadersFooters"/>), would make every page before the break
    /// render with no header/footer at all -- the resolver that decides what to draw on a page walks
    /// backward for a section that defines no slot of its own, and a newly created leading section has
    /// nothing earlier to walk back to. Passing the currently-effective header/footer set here (cloned,
    /// the same way <paramref name="inherited"/> clones the page settings) keeps the header/footer
    /// visibly unchanged on the pages before the break, exactly as Word does not blank a section's
    /// running header/footer merely because a break was inserted after it.
    /// </para>
    /// </summary>
    public static Paragraph CreateSectionBreak(
        SectionBreakKind breakKind,
        PageSettings? inherited = null,
        SectionHeadersFooters? inheritedHeadersFooters = null)
    {
        var section = new Section(inherited?.Clone() ?? new PageSettings(), breakKind);
        if (inheritedHeadersFooters is not null)
        {
            section.HeadersFooters = new SectionHeadersFooters
            {
                Header = DocumentModelCloner.CloneHeaderFooter(inheritedHeadersFooters.Header, RevisionClonePolicy.Preserve),
                Footer = DocumentModelCloner.CloneHeaderFooter(inheritedHeadersFooters.Footer, RevisionClonePolicy.Preserve),
                EvenHeader = DocumentModelCloner.CloneHeaderFooter(inheritedHeadersFooters.EvenHeader, RevisionClonePolicy.Preserve),
                EvenFooter = DocumentModelCloner.CloneHeaderFooter(inheritedHeadersFooters.EvenFooter, RevisionClonePolicy.Preserve),
                FirstHeader = DocumentModelCloner.CloneHeaderFooter(inheritedHeadersFooters.FirstHeader, RevisionClonePolicy.Preserve),
                FirstFooter = DocumentModelCloner.CloneHeaderFooter(inheritedHeadersFooters.FirstFooter, RevisionClonePolicy.Preserve)
            };
        }

        return new Paragraph { SectionBreak = section };
    }

    /// <summary>
    /// Resolves the effective (through per-slot "link to previous") header/footer set for
    /// <paramref name="sectionIndex"/> in <paramref name="document"/> -- the value a section-break
    /// insertion should copy into the new leading section it creates (see
    /// <see cref="CreateSectionBreak"/>), so splitting a section does not blank the header/footer that
    /// was showing there. Each of the six slots (default/even/first header and footer) is resolved
    /// independently: when the target section does not define a slot itself, this walks backward
    /// through earlier sections for the nearest one that does, exactly like the presentation-layer page
    /// planner that decides what to actually draw (duplicated here, in Core.Model, which cannot
    /// reference that layer). A negative <paramref name="sectionIndex"/> matches
    /// <see cref="PageSettingsSectionResolver"/>'s convention and resolves against the document's final
    /// section.
    /// </summary>
    public static SectionHeadersFooters ResolveInheritedHeadersFooters(TextDocument document, int sectionIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        var sections = document.Sections;
        var clamped = sectionIndex < 0
            ? sections.Count - 1
            : Math.Clamp(sectionIndex, 0, sections.Count - 1);

        HeaderFooter? ResolveSlot(Func<SectionHeadersFooters, HeaderFooter?> selector)
        {
            for (var i = clamped; i >= 0; i--)
            {
                var value = selector(sections[i].HeadersFooters);
                if (value is not null)
                    return value;
            }

            return null;
        }

        return new SectionHeadersFooters
        {
            Header = ResolveSlot(hf => hf.Header),
            Footer = ResolveSlot(hf => hf.Footer),
            EvenHeader = ResolveSlot(hf => hf.EvenHeader),
            EvenFooter = ResolveSlot(hf => hf.EvenFooter),
            FirstHeader = ResolveSlot(hf => hf.FirstHeader),
            FirstFooter = ResolveSlot(hf => hf.FirstFooter)
        };
    }

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
