using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;
using ModelSection = FreeW.Core.Model.Section;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round 132 fix: a document whose sections do not share one page geometry (e.g. a landscape
/// section followed by a taller portrait final section) must paginate each section against its
/// OWN <see cref="PageSettings"/>, not the document's final-section geometry applied uniformly.
///
/// <see cref="PaginationEngine.ComputeBlockPageAssignment"/> used to paginate the whole document as
/// one flow sized to <c>editor.Model.Page</c> (the final section). When an earlier section's real
/// content area is smaller than the final section's, that earlier section's blocks were packed
/// against the wrong (larger) page box, so content that actually needed 2 physical pages was
/// assigned to a single page box — and the overflow was silently dropped from Print / Print Preview
/// (the fixed-size <see cref="PageBox"/> body never gets a second page to hold the tail).
///
/// <para>Runs on STA because tests create real WPF DocumentView / PaginatedEditorPanel instances.</para>
/// </summary>
public sealed class PagedEditMixedGeometryPaginationTests
{
    // Section 1: landscape Letter with large margins, so its true content area is tiny (~112pt
    // tall — enough for only a handful of lines). Section 2 (final): portrait Letter with normal
    // margins, so its content area (648pt tall) is nearly 6x taller. Ten short default-font
    // paragraphs comfortably fit in ONE portrait-sized page but do NOT fit in one true landscape
    // page — reproducing the exact "packed against the taller final-section page box" scenario
    // from the bug report.
    private const int Section1ParagraphCount = 10;

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 1. Engine-level proof: section 1's blocks must be split across 2 local pages, not 1.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void MixedGeometry_LandscapeSectionThenTallerPortraitFinal_ComputeBlockPageAssignment_SplitsSectionOne()
    {
        var doc = BuildMixedGeometryDocument(out var markerIndex, out var lastSection1Index);

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        var assignment = PaginationEngine.ComputeBlockPageAssignment(editor);

        assignment.Should().HaveCount(doc.Blocks.Count);

        // Section 1's own (landscape, tiny content area) geometry cannot fit all 10 paragraphs plus
        // the marker on one page — the assignment must show at least 2 distinct page indices among
        // section 1's blocks, and the marker's page must be > 0.
        var section1Pages = assignment.Take(markerIndex + 1).Distinct().ToList();
        section1Pages.Count.Should().BeGreaterThan(1,
            "section 1's content overflows its OWN (small landscape) content area and must span " +
            "more than one physical page — sizing it against the taller final section's geometry " +
            "would wrongly pack it all onto page 0");

        // The final section's paragraph must start on a page AFTER all of section 1's pages, i.e.
        // the running page offset correctly carried section 1's true (2-page) page count forward.
        var finalSectionBlockIndex = doc.Blocks.Count - 1;
        assignment[finalSectionBlockIndex].Should().BeGreaterThan(assignment[lastSection1Index],
            "the final (portrait) section must start after section 1's true page count, not right " +
            "after a single (wrongly-sized) section-1 page");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 2. End-to-end proof: the last paragraph of section 1 is not dropped from the paged/print view.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void MixedGeometry_LandscapeSectionThenTallerPortraitFinal_LastParagraphOfSectionOneAppears()
    {
        var doc = BuildMixedGeometryDocument(out _, out _);

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        // This is exactly the panel PrintLayout.BuildPaginator / SectionAwareDocumentPaginator.Build
        // use for Print Preview and Print when section geometry differs (PrintPreviewWindow.cs /
        // MainWindow.PrintDocument -> PrintLayout.BuildPaginator -> SectionAwareDocumentPaginator.Build
        // -> PaginatedEditorPanel.Build(sourceEditor, includeParityBlankPages: true)).
        var panel = PaginatedEditorPanel.Build(editor, includeParityBlankPages: true);

        // Section 1 needed more than one physical page, so the whole paged view must contain more
        // than the naive "1 landscape page + 1 portrait page" the bug produced.
        panel.PageBoxes.Count.Should().BeGreaterThan(2,
            "section 1's overflow must produce its own extra page(s) instead of being dropped");

        var allBodyText = string.Join(
            "\n",
            panel.PageBoxes.Select(box =>
                new TextRange(box.Body.Document.ContentStart, box.Body.Document.ContentEnd).Text));

        allBodyText.Should().Contain("Section1 Line 10",
            "the last paragraph of section 1 must still be rendered somewhere in the paged view, " +
            "not silently clipped off the bottom of an undersized page box");

        allBodyText.Should().Contain("Section2 Final Paragraph",
            "the final section's own content must still render");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 3. Sibling / no-regression: a two-section document that shares ONE geometry (the common case)
    //    must keep assigning blocks strictly in document order with no spurious extra pages —
    //    proving BuildPageSegments' new per-section split doesn't over-correct same-geometry docs.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void SameGeometryTwoSections_BlockAssignmentStaysSequentialAndMinimal()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        doc.Blocks.Add(new Paragraph("Section1 only paragraph"));

        var sharedPage = new PageSettings
        {
            WidthPt = 612,
            HeightPt = 792,
            MarginLeftPt = 72,
            MarginRightPt = 72,
            MarginTopPt = 72,
            MarginBottomPt = 72,
        };
        var marker = new Paragraph("[ break ]")
        {
            SectionBreak = new ModelSection(sharedPage, SectionBreakKind.NextPage)
        };
        doc.Blocks.Add(marker);

        doc.Page.WidthPt = 612;
        doc.Page.HeightPt = 792;
        doc.Page.MarginLeftPt = 72;
        doc.Page.MarginRightPt = 72;
        doc.Page.MarginTopPt = 72;
        doc.Page.MarginBottomPt = 72;
        doc.Blocks.Add(new Paragraph("Section2 only paragraph"));

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        var assignment = PaginationEngine.ComputeBlockPageAssignment(editor);

        assignment.Should().HaveCount(3);
        // Non-decreasing, and only ONE forced break (the NextPage marker) — no extra pages appear
        // just because the document happens to have two sections.
        assignment[0].Should().Be(0, "section 1's single short paragraph fits on page 0");
        assignment[1].Should().Be(0, "the marker paragraph itself ends section 1 on page 0");
        assignment[2].Should().Be(1,
            "the NextPage section break forces exactly one new page for section 2 — same-geometry " +
            "sections must not fragment into extra pages beyond what an explicit break requires");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // helpers
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Section 1: landscape Letter, huge top/bottom margins (true content area ~112pt tall).
    /// Section 2 (final, doc.Page): portrait Letter, normal margins (content area ~648pt tall).
    /// Section 1 carries <see cref="Section1ParagraphCount"/> short default-font paragraphs, which
    /// comfortably fit within section 2's much taller content area but overflow section 1's own
    /// tiny one — reproducing the bug's "packed against the taller final-section page box" scenario.
    /// </summary>
    private static TextDocument BuildMixedGeometryDocument(out int markerIndex, out int lastSection1Index)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        for (var i = 1; i <= Section1ParagraphCount; i++)
            doc.Blocks.Add(new Paragraph($"Section1 Line {i:00}"));
        lastSection1Index = doc.Blocks.Count - 1;

        var landscapeTinyPage = new PageSettings
        {
            WidthPt = 792,        // 11in landscape width
            HeightPt = 612,       // 8.5in landscape height
            Landscape = true,
            MarginLeftPt = 72,
            MarginRightPt = 72,
            MarginTopPt = 250,    // huge margins -> content height = 612 - 500 = 112pt
            MarginBottomPt = 250,
        };
        var marker = new Paragraph("[ section break marker ]")
        {
            SectionBreak = new ModelSection(landscapeTinyPage, SectionBreakKind.NextPage)
        };
        doc.Blocks.Add(marker);
        markerIndex = doc.Blocks.Count - 1;

        // Final section: portrait Letter, normal margins -> content height = 792 - 144 = 648pt.
        doc.Page.WidthPt = 612;
        doc.Page.HeightPt = 792;
        doc.Page.Landscape = false;
        doc.Page.MarginLeftPt = 72;
        doc.Page.MarginRightPt = 72;
        doc.Page.MarginTopPt = 72;
        doc.Page.MarginBottomPt = 72;
        doc.Blocks.Add(new Paragraph("Section2 Final Paragraph"));

        return doc;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 4. Regression guard: per-segment pagination must reserve the same footnote body-region height
    //    PrintLayout.BuildPaginatedDocument does, or the segment's usable body area is too TALL and
    //    over-fills the page relative to what Print/Print Preview/PDF actually render.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fourteen short body paragraphs are sized to comfortably fill (but not overflow) one page's
    /// content area when NO footnote reserve is applied — establishing the baseline that they fit on
    /// page 0. Adding a footnote reference (with a sizeable footnote body, which lives in
    /// <see cref="TextDocument.Footnotes"/> and never adds text to the body paragraph itself) does not
    /// change the body paragraphs' own flowed height, so the ONLY way the same content can spill onto
    /// a second page is if the segment's usable body height was shrunk by a footnote-region reserve —
    /// exactly what <c>PrintLayout.BuildPaginatedDocument</c> does for the whole-document flow via
    /// <c>ApplyFootnoteBodyReserve</c>. Before the fix, <c>ComputeSegmentPageAssignment</c> built its
    /// per-segment <see cref="FlowDocument.PagePadding"/> purely from <c>PageLayout.MarginsDip</c> and
    /// ignored that reserve, so this same content stayed wrongly packed onto page 0 in both cases.
    /// </summary>
    [StaFact]
    public void FootnoteBodyReserve_ShrinksSegmentBodyHeight_PushesOverflowToNextPage()
    {
        TextDocument BuildDoc(bool withFootnote)
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            doc.Page.WidthPt = 612;
            doc.Page.HeightPt = 400;
            doc.Page.MarginLeftPt = 36;
            doc.Page.MarginRightPt = 36;
            doc.Page.MarginTopPt = 36;
            doc.Page.MarginBottomPt = 36;

            for (var i = 1; i <= 14; i++)
                doc.Blocks.Add(new Paragraph($"Body line {i:00}"));

            if (withFootnote)
            {
                doc.Footnotes[1] = new Footnote(
                    1,
                    string.Join(" ", Enumerable.Repeat("footnote text word", 40)));
                var lastPara = (Paragraph)doc.Blocks[^1];
                lastPara.Runs.Add(Run.FootnoteReference(1));
            }

            return doc;
        }

        var baselineEditor = new DocumentView();
        baselineEditor.LoadModel(BuildDoc(withFootnote: false));
        baselineEditor.CommitToModel();
        var baselineAssignment = PaginationEngine.ComputeBlockPageAssignment(baselineEditor);

        baselineAssignment.Should().OnlyContain(page => page == 0,
            "with no footnotes, the fourteen short body paragraphs fit within the page's plain " +
            "(margins-only) content area — this establishes that any overflow below is caused by the " +
            "footnote reserve, not by the paragraphs themselves being too tall");

        var footnoteEditor = new DocumentView();
        footnoteEditor.LoadModel(BuildDoc(withFootnote: true));
        footnoteEditor.CommitToModel();
        var footnoteAssignment = PaginationEngine.ComputeBlockPageAssignment(footnoteEditor);

        footnoteAssignment.Should().Contain(page => page > 0,
            "the footnote's estimated rendered region must reserve bottom space on this segment's " +
            "page, exactly as PrintLayout.BuildPaginatedDocument's ApplyFootnoteBodyReserve does for " +
            "the whole-document flow — shrinking the usable body area so the same fourteen paragraphs " +
            "no longer all fit on page 0. If this segment's PagePadding ignores the footnote reserve " +
            "(the round-132 regression), the assignment stays identical to the no-footnote baseline " +
            "and this assertion fails.");
    }
}
