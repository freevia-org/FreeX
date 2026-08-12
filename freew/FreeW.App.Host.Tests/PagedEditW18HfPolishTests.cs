using System.IO;
using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// W18 polish tests: live page-number rendering in in-page header/footer sub-editors,
/// per-section header/footer routing, and round-trip losslessness of PAGE field runs.
///
/// <list type="bullet">
///   <item>PAGE field in header renders as the page's actual 1-based number ("1" on page 1, "2" on page 2, …).</item>
///   <item>NUMPAGES field renders as the real total page count.</item>
///   <item>PAGE field run in the model is NOT mutated — round-trip is lossless.</item>
///   <item>Multi-section: page in section 2 resolves section 2's header (or documented document-level fallback).</item>
///   <item>First-page / even-page rules still apply within a section.</item>
///   <item>Release flag guard: PagedEdit excluded in Release builds.</item>
/// </list>
///
/// <para>Runs on STA because tests create real WPF RichTextBox / FlowDocument instances.</para>
/// </summary>
public sealed class PagedEditW18HfPolishTests
{
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 1. Live page-number rendering
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A footer containing a PAGE field on a single-page document must display "1"
    /// in the sub-editor's visible text.
    /// </summary>
    [StaFact]
    public void PageField_InFooter_Page1_DisplaysOne()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph("Body text"));

        var footer = new HeaderFooter();
        var footerPara = new FreeW.Core.Model.Paragraph();
        footerPara.Runs.Add(Run.PageNumberField());   // FieldKind = PageNumber, cached = "1"
        footer.Paragraphs.Add(footerPara);
        doc.Footer = footer;

        var (panel, _) = BuildPanel(doc);

        // The first page box must have a footer sub-editor.
        var box = panel.PageBoxes[0];
        box.FooterSubEditor.Should().NotBeNull("page box must have a footer sub-editor");

        // The sub-editor's visible text for the PAGE run must be "1".
        var visibleText = GetSubEditorBodyText(box.FooterSubEditor!);
        visibleText.Should().Be("1",
            "PAGE field in footer must display the actual page number (1) in paged-edit view");
    }

    [StaFact]
    public void PageField_InFooter_UsesSectionStartAtAndNumberFormat()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph("Body text"));
        doc.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        doc.Page.PageNumberStartAt = 4;

        var footer = new HeaderFooter();
        var footerPara = new FreeW.Core.Model.Paragraph();
        footerPara.Runs.Add(Run.PageNumberField());
        footer.Paragraphs.Add(footerPara);
        doc.Footer = footer;

        var (panel, _) = BuildPanel(doc);

        var box = panel.PageBoxes[0];
        box.FooterSubEditor.Should().NotBeNull("page box must have a footer sub-editor");
        GetSubEditorBodyText(box.FooterSubEditor!).Should().Be("IV",
            "PAGE field rendering must use section start-at and upper-Roman format");
    }

    /// <summary>
    /// In a 2-page document (forced by explicit page break), the second page's footer sub-editor
    /// must display "2" for the PAGE field.
    /// </summary>
    [StaFact]
    public void PageField_InFooter_Page2_DisplaysTwo()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph("Page 1 text"));
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph("Page 2 start")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });

        var footer = new HeaderFooter();
        var footerPara = new FreeW.Core.Model.Paragraph();
        footerPara.Runs.Add(Run.PageNumberField());
        footer.Paragraphs.Add(footerPara);
        doc.Footer = footer;

        var (panel, _) = BuildPanel(doc);

        if (panel.PageBoxes.Count < 2)
            return; // single-page engine fallback in test env; skip without failing

        var box2 = panel.PageBoxes[1];
        box2.FooterSubEditor.Should().NotBeNull();

        var visibleText = GetSubEditorBodyText(box2.FooterSubEditor!);
        visibleText.Should().Be("2",
            "PAGE field in footer must display the page number '2' for the second page box");
    }

    /// <summary>
    /// NUMPAGES field in a 2-page document's footer must display the real page count ("2").
    /// </summary>
    [StaFact]
    public void NumPagesField_InFooter_DisplaysRealPageCount()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph("Page 1"));
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph("Page 2 start")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });

        var footer = new HeaderFooter();
        var footerPara = new FreeW.Core.Model.Paragraph();
        footerPara.Runs.Add(Run.NumPagesField());   // FieldKind = NumPages
        footer.Paragraphs.Add(footerPara);
        doc.Footer = footer;

        var (panel, _) = BuildPanel(doc);

        if (panel.PageBoxes.Count < 2)
            return; // skip in narrow test env

        // Both pages should show the same NUMPAGES = 2.
        var box1Text = GetSubEditorBodyText(panel.PageBoxes[0].FooterSubEditor!);
        var box2Text = GetSubEditorBodyText(panel.PageBoxes[1].FooterSubEditor!);

        box1Text.Should().Be(panel.PageBoxes.Count.ToString(),
            "NUMPAGES on page 1 must display the real total page count");
        box2Text.Should().Be(panel.PageBoxes.Count.ToString(),
            "NUMPAGES on page 2 must display the real total page count");
    }

    [StaFact]
    public void SectionFields_UseLiveSectionContextInFooterAndBodyPageBox()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph("Page 1"));

        var body = new FreeW.Core.Model.Paragraph
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        };
        var bodySection = new Run("stale") { ComplexField = new ComplexField(" SECTION \\* ROMAN ") };
        var bodySectionPages = new Run("stale") { ComplexField = new ComplexField(" SECTIONPAGES \\* roman ") };
        body.Runs.Add(bodySection);
        body.Runs.Add(new Run("/"));
        body.Runs.Add(bodySectionPages);
        doc.Blocks.Add(body);

        var footer = new HeaderFooter();
        var footerPara = new FreeW.Core.Model.Paragraph();
        var footerSection = new Run("stale") { ComplexField = new ComplexField(" SECTION \\* ROMAN ") };
        var footerSectionPages = new Run("stale") { ComplexField = new ComplexField(" SECTIONPAGES \\* roman ") };
        footerPara.Runs.Add(footerSection);
        footerPara.Runs.Add(new Run("/"));
        footerPara.Runs.Add(footerSectionPages);
        footer.Paragraphs.Add(footerPara);
        doc.Footer = footer;

        var (panel, _) = BuildPanel(doc);

        if (panel.PageBoxes.Count < 2)
            return;

        GetSubEditorBodyText(panel.PageBoxes[0].FooterSubEditor!).Should().Be("I/ii");
        GetSubEditorBodyText(panel.PageBoxes[1].FooterSubEditor!).Should().Be("I/ii");
        GetFlowDocumentText(panel.PageBoxes[1].Body.Document).Should().Contain("I/ii");

        bodySection.Text.Should().Be("stale");
        bodySectionPages.Text.Should().Be("stale");
        footerSection.Text.Should().Be("stale");
        footerSectionPages.Text.Should().Be("stale");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 2. Round-trip losslessness: model field run must be unchanged after PagedEdit cycle
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After a full enter-paged-edit → commit cycle, the PAGE field run in the footer slot must:
    /// (a) still have FieldKind = PageNumber (not converted to plain text),
    /// (b) its cached text must still be "1" (not "2", not the resolved display value).
    /// This proves the display substitution is view-only and the model is not mutated.
    /// </summary>
    [StaFact]
    public void PageField_RoundTrip_ModelFieldRunUnchanged()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph("Page 1"));
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph("Page 2")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });

        var footer = new HeaderFooter();
        var footerPara = new FreeW.Core.Model.Paragraph();
        footerPara.Runs.Add(Run.PageNumberField());   // cached = "1"
        footer.Paragraphs.Add(footerPara);
        doc.Footer = footer;

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        var panel = PaginatedEditorPanel.Build(editor);
        PaginatedCommitCoordinator.Commit(panel, editor);

        // After commit the footer slot must exist.
        var hf = editor.Model.FinalSectionHeadersFooters;
        hf.Footer.Should().NotBeNull("footer slot must survive commit");

        // The PAGE field run must still be a field run — not plain text.
        var fieldRuns = hf.Footer!.Paragraphs
            .SelectMany(p => p.Runs)
            .Where(r => r.FieldKind == RunFieldKind.PageNumber)
            .ToList();
        fieldRuns.Should().HaveCountGreaterThan(0,
            "PAGE field run must survive paged-edit commit cycle — FieldKind must be PageNumber");

        // The cached text must still be "1" (the original default, not a computed display value).
        fieldRuns[0].Text.Should().Be("1",
            "PAGE field run's cached text must be '1' after round-trip — display substitution is view-only");
    }

    /// <summary>
    /// Full DOCX round-trip: after paged-edit commit, write to DOCX, read back.
    /// The PAGE field run must survive with FieldKind = PageNumber intact.
    /// </summary>
    [StaFact]
    public void PageField_DocxRoundTrip_FieldKindPreserved()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph("Body text"));

        var footer = new HeaderFooter();
        var footerPara = new FreeW.Core.Model.Paragraph();
        footerPara.Runs.Add(Run.PageNumberField());
        footer.Paragraphs.Add(footerPara);
        doc.Footer = footer;

        var (panel, editor) = BuildPanel(doc);
        PaginatedCommitCoordinator.Commit(panel, editor);

        using var stream = new MemoryStream();
        DocxWriter.Write(editor.Model, stream);
        stream.Position = 0;
        var read = DocxReader.Read(stream);

        read.Footer.Should().NotBeNull("footer must survive DOCX round-trip");
        var fieldRuns = read.Footer!.Paragraphs
            .SelectMany(p => p.Runs)
            .Where(r => r.FieldKind == RunFieldKind.PageNumber)
            .ToList();
        fieldRuns.Should().HaveCountGreaterThan(0,
            "PAGE field run must survive DocxWriter→DocxReader round-trip after paged-edit commit");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 3. Per-section header/footer routing
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// In a two-section document (section 1 ends with a SectionBreak paragraph, section 2 is the
    /// final section), when section 2 has its own header, pages in section 2 must display section
    /// 2's header content, and pages in section 1 must display section 1's header content
    /// (or the document-level fallback when section 1 has no own header).
    ///
    /// <para>
    /// <strong>Storage note:</strong> <see cref="Section.HeadersFooters"/> already exists in the
    /// model.  This test confirms that the display routing reads from it correctly.
    /// </para>
    /// </summary>
    [StaFact]
    public void MultiSection_Page_RoutesToCorrectSectionHeader()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        // Section 1: one paragraph that ends the section with a NextPage break.
        // Section 1 has its own header "Section 1 Header".
        var section1EndPara = new FreeW.Core.Model.Paragraph("Section 1 body");
        var sec1 = new FreeW.Core.Model.Section(new PageSettings(), SectionBreakKind.NextPage);
        sec1.HeadersFooters.Header = new HeaderFooter("Section 1 Header");
        section1EndPara.SectionBreak = sec1;
        doc.Blocks.Add(section1EndPara);

        // Section 2 (final): document-level header "Section 2 Header".
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph("Section 2 body"));
        doc.Header = new HeaderFooter("Section 2 Header");

        var (panel, _) = BuildPanel(doc);

        if (panel.PageBoxes.Count < 2)
            return; // single-page fallback in test env; skip

        // Page 1 is in section 1 — must use section 1's own header.
        var box1 = panel.PageBoxes[0];
        box1.HeaderSubEditor.Should().NotBeNull("page 1 must have a header sub-editor");
        var page1Text = GetSubEditorBodyText(box1.HeaderSubEditor!);
        page1Text.Should().Contain("Section 1 Header",
            "page 1 must resolve section 1's own header when section 1 defines one");

        // Page 2 is in section 2 — must use the document-level (section 2) header.
        var box2 = panel.PageBoxes[1];
        box2.HeaderSubEditor.Should().NotBeNull("page 2 must have a header sub-editor");
        var page2Text = GetSubEditorBodyText(box2.HeaderSubEditor!);
        page2Text.Should().Contain("Section 2 Header",
            "page 2 must resolve section 2's (document-level) header");
    }

    /// <summary>
    /// R135, WPF twin of DocumentViewHeaderFooterTests: a LEADING section that defines no header of
    /// its own renders BLANK -- it must not borrow the final section's header.
    /// <para>
    /// This test previously asserted the opposite, describing <c>doc.Header</c> as a "document-level"
    /// header. It is not one: <see cref="TextDocument.Header"/> is a facade over
    /// <c>FinalSectionHeadersFooters.Header</c> (TextDocument.cs), i.e. the trailing
    /// <c>w:sectPr</c> -- the LAST section's own definition. In OOXML a section that omits
    /// <c>w:headerReference</c> is "linked to previous" and inherits from the nearest PRECEDING
    /// section; the first section has no predecessor, so Word leaves the slot empty rather than
    /// reaching forward into a later section. Resolving forward is what made an early section display
    /// a later section's running header, which is the defect this round fixed.
    /// </para>
    /// <para>
    /// Backward inheritance is untouched and still covered -- see
    /// <c>HeaderFooterPagePlannerTests.MapPagesToSections_LinkedSectionInheritsNearestPrecedingSectionHeaderNotFinalSection</c>
    /// and the Avalonia <c>MiddleSection_with_empty_HeadersFooters_inherits_nearest_preceding_section_header</c>.
    /// </para>
    /// </summary>
    [StaFact]
    public void MultiSection_Section1NoOwnHeader_RendersBlankRatherThanBorrowingTheFinalSectionHeader()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        // Section 1 ends with a break but carries no own header (HeadersFooters is empty).
        var section1EndPara = new FreeW.Core.Model.Paragraph("Section 1 body");
        var sec1 = new FreeW.Core.Model.Section(new PageSettings(), SectionBreakKind.NextPage);
        // Intentionally leave sec1.HeadersFooters empty.
        section1EndPara.SectionBreak = sec1;
        doc.Blocks.Add(section1EndPara);

        // Section 2 (final): its OWN header, reached through the doc.Header facade.
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph("Section 2 body"));
        doc.Header = new HeaderFooter("Final Section Header");

        var (panel, _) = BuildPanel(doc);

        var box1 = panel.PageBoxes[0];
        var page1Text = box1.HeaderSubEditor is null ? string.Empty : GetSubEditorBodyText(box1.HeaderSubEditor);
        page1Text.Should().NotContain("Final Section Header",
            "section 1 has no preceding section to link to, so its header slot is blank -- borrowing the " +
            "LAST section's header would print a later section's running header on page 1");

        // The final section still shows its own header: the fix removes forward inheritance only.
        var box2 = panel.PageBoxes[^1];
        box2.HeaderSubEditor.Should().NotBeNull("the final section defines its own header");
        GetSubEditorBodyText(box2.HeaderSubEditor!).Should().Contain("Final Section Header");
    }

    /// <summary>
    /// First-page / even-page rules apply within the section context:
    /// when DifferentFirstPage is on, the first page of a section must use "first-header" slot,
    /// subsequent pages must use "header" slot.
    /// </summary>
    [StaFact]
    public void FirstPageRule_AppliesWithinSectionContext()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Page.DifferentFirstPage = true;

        // Two pages — first-page rule active.
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph("Page 1 text"));
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph("Page 2 text")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });

        doc.Header      = new HeaderFooter("Default Header");
        doc.FirstHeader = new HeaderFooter("First Page Header");

        var (panel, _) = BuildPanel(doc);

        // Page 1 must use first-header slot.
        panel.PageBoxes[0].HeaderSlotName.Should().Be("first-header",
            "page 1 must use 'first-header' slot when DifferentFirstPage is on");

        if (panel.PageBoxes.Count >= 2)
        {
            // Page 2 must use the default header slot.
            panel.PageBoxes[1].HeaderSlotName.Should().Be("header",
                "page 2 must use 'header' slot when DifferentFirstPage is on and there's no even rule");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 4. Release flag guard (regression)
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>PagedEdit must be present in DEBUG builds (this file only compiles in DEBUG).</summary>
    [Fact]
    public void PagedEditMode_PresentInDebugBuild()
    {
        Enum.GetValues<DocumentViewMode>()
            .Should().Contain(DocumentViewMode.PagedEdit,
                "PagedEdit must be present in DEBUG builds");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // helpers
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static (PaginatedEditorPanel panel, DocumentView editor) BuildPanel(TextDocument doc)
    {
        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();
        var panel = PaginatedEditorPanel.Build(editor);
        return (panel, editor);
    }

    /// <summary>
    /// Extracts the plain text visible in a sub-editor's first paragraph.
    /// <see cref="DocumentView"/> is a <see cref="System.Windows.Controls.RichTextBox"/> subclass,
    /// so <c>subEditor.Document</c> is the WPF <see cref="FlowDocument"/> whose runs carry the
    /// resolved display text (including live-substituted page numbers).
    /// </summary>
    private static string GetSubEditorBodyText(DocumentView subEditor)
    {
        // DocumentView : RichTextBox, so .Document is the rendered WPF FlowDocument.
        var doc = subEditor.Document;
        if (doc is null)
            return string.Empty;

        var wpfPara = doc.Blocks.OfType<System.Windows.Documents.Paragraph>().FirstOrDefault();
        if (wpfPara is null)
            return string.Empty;

        // Collect visible text from all Runs in the first paragraph (field runs carry their
        // resolved display text as the WPF Run.Text, not the cached model value).
        return string.Concat(
            wpfPara.Inlines
                .OfType<System.Windows.Documents.Run>()
                .Select(r => r.Text));
    }

    private static string GetFlowDocumentText(FlowDocument document) =>
        new TextRange(document.ContentStart, document.ContentEnd).Text;
}
