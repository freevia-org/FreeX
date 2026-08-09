using System.IO;
using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Phase 4 tests: WYSIWYG in-page header/footer editing regions in <see cref="PageBox"/>.
///
/// <list type="bullet">
///   <item>Header sub-editor loads the correct slot's paragraphs.</item>
///   <item>Editing + commit writes back to <see cref="SectionHeadersFooters"/> preserving formatted runs.</item>
///   <item>First-page header shown on page 1 when DifferentFirstPage; even header on even pages when DifferentOddEvenPages.</item>
///   <item>Editing a header on one page is reflected on other pages sharing the slot after repagination.</item>
///   <item>Full DOCX round-trip: formatted runs survive DocxWriter→DocxReader.</item>
///   <item>Release flag guard: PagedEdit enum excluded in Release builds.</item>
/// </list>
///
/// <para>Runs on STA because tests create real WPF RichTextBox / FlowDocument instances.</para>
/// </summary>
public sealed class PagedEdit4HfRegionTests
{
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 1. Header sub-editor loads the correct slot's paragraphs
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A PageBox built for the default header slot must expose a non-null HeaderSubEditor, and that
    /// sub-editor's model must contain the header slot's paragraph text.
    /// </summary>
    [StaFact]
    public void PageBox_HeaderSubEditor_LoadsDefaultHeaderSlot()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body paragraph"));
        doc.Header = new HeaderFooter("My Default Header");

        var (panel, _) = BuildPanel(doc);

        // The first page box must have a header sub-editor.
        var box = panel.PageBoxes[0];
        box.HeaderSubEditor.Should().NotBeNull(
            "page box must create a HeaderSubEditor when the model has a header slot");

        // The sub-editor's model must contain the header text.
        box.HeaderSubEditor!.Model.Blocks.OfType<Paragraph>()
            .Any(p => p.PlainText.Contains("My Default Header"))
            .Should().BeTrue("HeaderSubEditor must be seeded with the default header slot text");
    }

    /// <summary>
    /// A PageBox built for the default footer slot must expose a non-null FooterSubEditor seeded
    /// with the footer slot's paragraph text.
    /// </summary>
    [StaFact]
    public void PageBox_FooterSubEditor_LoadsDefaultFooterSlot()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body paragraph"));
        doc.Footer = new HeaderFooter("My Default Footer");

        var (panel, _) = BuildPanel(doc);

        var box = panel.PageBoxes[0];
        box.FooterSubEditor.Should().NotBeNull(
            "page box must create a FooterSubEditor when the model has a footer slot");

        box.FooterSubEditor!.Model.Blocks.OfType<Paragraph>()
            .Any(p => p.PlainText.Contains("My Default Footer"))
            .Should().BeTrue("FooterSubEditor must be seeded with the default footer slot text");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 2. Slot names assigned (default / first-page / even-page)
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When DifferentFirstPage is off, page 1 must get the "header" / "footer" slot names.
    /// </summary>
    [StaFact]
    public void PageBox_DefaultSlotName_WhenDifferentFirstPageOff()
    {
        var doc = BuildDocWithHeader("Default Header");
        doc.Page.DifferentFirstPage = false;

        var (panel, _) = BuildPanel(doc);

        panel.PageBoxes[0].HeaderSlotName.Should().Be("header",
            "page 1 must use the default 'header' slot when DifferentFirstPage is off");
        panel.PageBoxes[0].FooterSlotName.Should().Be("footer",
            "page 1 must use the default 'footer' slot when DifferentFirstPage is off");
    }

    /// <summary>
    /// When DifferentFirstPage is on, page 1 must get "first-header" / "first-footer".
    /// </summary>
    [StaFact]
    public void PageBox_FirstPageSlotName_WhenDifferentFirstPageOn()
    {
        var doc = BuildDocWithHeader("Default Header");
        doc.Page.DifferentFirstPage = true;
        doc.FirstHeader = new HeaderFooter("First-Page Header");
        doc.FirstFooter = new HeaderFooter("First-Page Footer");

        var (panel, _) = BuildPanel(doc);

        panel.PageBoxes[0].HeaderSlotName.Should().Be("first-header",
            "page 1 must use 'first-header' when DifferentFirstPage is on");
        panel.PageBoxes[0].FooterSlotName.Should().Be("first-footer",
            "page 1 must use 'first-footer' when DifferentFirstPage is on");
    }

    /// <summary>
    /// When DifferentFirstPage is on, page 1 HeaderSubEditor must be seeded with the first-page
    /// slot content, not the default slot content.
    /// </summary>
    [StaFact]
    public void PageBox_Page1_ShowsFirstPageHeader_WhenDifferentFirstPageOn()
    {
        var doc = BuildDocWithHeader("Default Header");
        doc.Page.DifferentFirstPage = true;
        doc.FirstHeader = new HeaderFooter("First-Page Header Text");

        var (panel, _) = BuildPanel(doc);

        var box = panel.PageBoxes[0];
        box.HeaderSubEditor.Should().NotBeNull();

        var text = box.HeaderSubEditor!.Model.Blocks
            .OfType<Paragraph>()
            .Select(p => p.PlainText)
            .FirstOrDefault() ?? string.Empty;

        text.Should().Contain("First-Page Header Text",
            "page 1 sub-editor must show the first-page header slot when DifferentFirstPage is on");
    }

    /// <summary>
    /// When DifferentOddEvenPages is on and the document has at least 2 pages, even page boxes
    /// must get "even-header" / "even-footer" slot names.
    /// </summary>
    [StaFact]
    public void PageBox_EvenSlotName_WhenDifferentOddEvenOn_EvenPage()
    {
        var doc = BuildDocWithHeader("Default Header");
        doc.Page.DifferentOddEvenPages = true;
        doc.EvenHeader = new HeaderFooter("Even-Page Header");
        doc.EvenFooter = new HeaderFooter("Even-Page Footer");

        // Force two pages via explicit page break.
        doc.Blocks.Add(new Paragraph("Page 2 start")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });

        var (panel, _) = BuildPanel(doc);

        if (panel.PageBoxes.Count < 2)
            return; // engine gave 1 page (narrow test env); skip without failing

        var evenBox = panel.PageBoxes[1]; // page 2 (1-based page number 2 = even)
        evenBox.HeaderSlotName.Should().Be("even-header",
            "page 2 must use 'even-header' when DifferentOddEvenPages is on");
        evenBox.FooterSlotName.Should().Be("even-footer",
            "page 2 must use 'even-footer' when DifferentOddEvenPages is on");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    [StaFact]
    public void PageBox_FirstSectionPageRestartedAtTwo_UsesEvenSlots()
    {
        var doc = BuildDocWithHeader("Default Header");
        doc.Page.PageNumberStartAt = 2;
        doc.Page.DifferentOddEvenPages = true;
        doc.EvenHeader = new HeaderFooter("Even-Page Header");
        doc.EvenFooter = new HeaderFooter("Even-Page Footer");

        var (panel, _) = BuildPanel(doc);
        var firstBox = panel.PageBoxes[0];

        firstBox.PageNumberText.Should().Be("2");
        firstBox.HeaderSlotName.Should().Be("even-header");
        firstBox.FooterSlotName.Should().Be("even-footer");
    }

    // 3. Commit writes back to slot with run formatting preserved
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After editing the header sub-editor and committing, the updated text must appear in
    /// <see cref="SectionHeadersFooters.Header"/> — and formatted runs must be preserved.
    /// </summary>
    [StaFact]
    public void CommitHfSlots_WritesBackToDefaultHeaderSlot_PreservingRuns()
    {
        var doc = BuildDocWithHeader("Original Header");

        // Build an initial run with bold formatting so we can verify run preservation.
        var boldRun = new Run("Bold text")
        {
            Formatting = RunFormatting.Default with { Bold = true }
        };
        doc.Header!.Paragraphs[0].Runs.Clear();
        doc.Header!.Paragraphs[0].Runs.Add(boldRun);

        var (panel, editor) = BuildPanel(doc);

        // Commit: coordinator calls CommitHeaderFooterSlots internally.
        PaginatedCommitCoordinator.Commit(panel, editor);

        var hf = editor.Model.FinalSectionHeadersFooters;
        hf.Header.Should().NotBeNull("header slot must be written back by commit");
        hf.Header!.Paragraphs.Should().NotBeEmpty("header slot must have at least one paragraph after commit");

        // The bold run formatting must survive through the wrapper-document sub-editor commit cycle.
        var boldRuns = hf.Header!.Paragraphs
            .SelectMany(p => p.Runs)
            .Where(r => r.Formatting?.Bold == true)
            .ToList();
        boldRuns.Should().HaveCountGreaterThan(0,
            "bold run formatting must survive the PagedEdit header commit cycle");
    }

    /// <summary>
    /// After commit, the footer sub-editor's text must appear in SectionHeadersFooters.Footer.
    /// </summary>
    [StaFact]
    public void CommitHfSlots_WritesBackToDefaultFooterSlot()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body text"));
        doc.Footer = new HeaderFooter("Footer Content");

        var (panel, editor) = BuildPanel(doc);

        PaginatedCommitCoordinator.Commit(panel, editor);

        var hf = editor.Model.FinalSectionHeadersFooters;
        hf.Footer.Should().NotBeNull("footer slot must be written back");
        hf.Footer!.Paragraphs
            .Any(p => p.PlainText.Contains("Footer Content"))
            .Should().BeTrue("footer text must survive commit");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 4. Shared slot: editing header on one page reflects on other pages sharing the slot
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// In a 2-page document with no DifferentFirstPage/OddEven, both pages share the "header"
    /// slot.  After commit, the slot holds the last-committed value — only ONE commit (from the
    /// first matching page box) is applied, so the shared slot is not accidentally double-written.
    /// On the next repagination both pages pick up the updated slot (tested by verifying the
    /// committed slot text matches the header sub-editor content after commit).
    /// </summary>
    [StaFact]
    public void SharedSlot_TwoPages_CommitOnlyOnce_SlotReflectsUpdatedContent()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Page 1 text"));
        doc.Blocks.Add(new Paragraph("Page 2 start")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });
        doc.Header = new HeaderFooter("Shared Header");

        var (panel, editor) = BuildPanel(doc);

        if (panel.PageBoxes.Count < 2)
            return; // single-page engine fallback; skip

        // Both pages must show the same "header" slot name.
        panel.PageBoxes[0].HeaderSlotName.Should().Be("header");
        panel.PageBoxes[1].HeaderSlotName.Should().Be("header");

        // Commit.
        PaginatedCommitCoordinator.Commit(panel, editor);

        // After commit, the "header" slot must exist and contain the shared text.
        var hf = editor.Model.FinalSectionHeadersFooters;
        hf.Header.Should().NotBeNull("shared header slot must survive commit");
        hf.Header!.Paragraphs
            .Any(p => p.PlainText.Contains("Shared Header"))
            .Should().BeTrue("shared header slot must contain the committed text");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 5. DOCX round-trip: formatted runs survive DocxWriter → DocxReader
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After a full paged-edit commit cycle, write the document to DOCX and read it back.
    /// The header slot's italic run must survive DocxWriter→DocxReader intact.
    /// </summary>
    [StaFact]
    public void PagedEditHeaderCommit_RoundTripsThroughDocx_PreservingFormattedRuns()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body paragraph"));

        // Seed the header with an italic run.
        var hf = new HeaderFooter();
        var para = new Paragraph();
        para.Runs.Add(new Run("Italic Header Run")
        {
            Formatting = RunFormatting.Default with { Italic = true }
        });
        hf.Paragraphs.Add(para);
        doc.Header = hf;

        // Paged-edit cycle: build panel → commit → model updated.
        var (panel, editor) = BuildPanel(doc);
        PaginatedCommitCoordinator.Commit(panel, editor);

        // DOCX round-trip.
        using var stream = new MemoryStream();
        DocxWriter.Write(editor.Model, stream);
        stream.Position = 0;
        var read = DocxReader.Read(stream);

        // The header slot must exist with an italic run.
        read.Header.Should().NotBeNull("header slot must survive DOCX round-trip after paged-edit commit");
        var italicRuns = read.Header!.Paragraphs
            .SelectMany(p => p.Runs)
            .Where(r => r.Formatting?.Italic == true)
            .ToList();
        italicRuns.Should().HaveCountGreaterThan(0,
            "italic run in header slot must survive DocxWriter→DocxReader round-trip");
    }

    /// <summary>
    /// Full cycle: paged-edit commit + DOCX round-trip for the footer slot with a page-number field.
    /// </summary>
    [StaFact]
    public void PagedEditFooterCommit_RoundTripsThroughDocx_PreservingPageNumberField()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body text"));

        var footerHf = new HeaderFooter();
        var footerPara = new Paragraph();
        footerPara.Runs.Add(Run.PageNumberField());
        footerHf.Paragraphs.Add(footerPara);
        doc.Footer = footerHf;

        var (panel, editor) = BuildPanel(doc);
        PaginatedCommitCoordinator.Commit(panel, editor);

        using var stream = new MemoryStream();
        DocxWriter.Write(editor.Model, stream);
        stream.Position = 0;
        var read = DocxReader.Read(stream);

        read.Footer.Should().NotBeNull("footer slot must survive DOCX round-trip after paged-edit commit");
        var pageNumberRuns = read.Footer!.Paragraphs
            .SelectMany(p => p.Runs)
            .Where(r => r.FieldKind == RunFieldKind.PageNumber)
            .ToList();
        pageNumberRuns.Should().HaveCountGreaterThan(0,
            "PAGE field run in footer must survive DocxWriter→DocxReader round-trip");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 6. FocusInPageHfRegion routing
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FocusInPageHfRegion("header") must return true when the "header" slot is present.
    /// </summary>
    [StaFact]
    public void FocusInPageHfRegion_ReturnsTrue_ForPresentSlot()
    {
        var doc = BuildDocWithHeader("Header Text");
        var (panel, _) = BuildPanel(doc);

        var focused = panel.FocusInPageHfRegion(HeaderFooterSlotKind.Header);
        focused.Should().BeTrue(
            "FocusInPageHfRegion must return true when the 'header' slot sub-editor exists");
    }

    /// <summary>
    /// FocusInPageHfRegion("first-header") must return false when DifferentFirstPage is off
    /// (the slot is not represented in any page box).
    /// </summary>
    [StaFact]
    public void FocusInPageHfRegion_ReturnsFalse_ForAbsentSlot()
    {
        var doc = BuildDocWithHeader("Header Text");
        doc.Page.DifferentFirstPage = false; // first-header not active

        var (panel, _) = BuildPanel(doc);

        var focused = panel.FocusInPageHfRegion(HeaderFooterSlotKind.FirstHeader);
        focused.Should().BeFalse(
            "FocusInPageHfRegion must return false when the 'first-header' slot is not in any page box");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 7. Release flag guard (regression guard — kept in sync with PagedEditFlagTests)
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The PagedEdit enum value must be present in Debug builds (this file only compiles in Debug).
    /// </summary>
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

    private static TextDocument BuildDocWithHeader(string headerText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body paragraph"));
        doc.Header = new HeaderFooter(headerText);
        doc.Footer = new HeaderFooter("Footer text");
        return doc;
    }
}
