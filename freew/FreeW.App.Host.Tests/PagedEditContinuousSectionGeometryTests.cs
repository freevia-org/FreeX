using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;
using ModelSection = FreeW.Core.Model.Section;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round 143 fix: content on either side of a <see cref="SectionBreakKind.Continuous"/> section break
/// must be measured against its OWN section's <see cref="PageSettings"/>, not a neighbouring section's.
///
/// <see cref="PaginationEngine"/>'s internal <c>BuildPageSegments</c> used to start a new measurement
/// segment only at page-type (<see cref="SectionBreakKind.NextPage"/>/EvenPage/OddPage) breaks, folding
/// everything between two page-type breaks -- including content straddling a Continuous break into a
/// differently-configured section -- into ONE segment sized by whichever section's page settings
/// happened to close it. A Continuous break that changes page width, margins, or column count (an
/// entirely ordinary Word layout -- e.g. a landscape "figure" section dropped into an otherwise
/// portrait document via Insert &gt; Breaks &gt; Continuous) therefore had its content measured against
/// the WRONG page box: content that only fits a small/narrow section's true content area got packed
/// as if it had the neighbouring (larger) section's room, silently overflowing/clipping when actually
/// rendered against its own real page box.
///
/// <para>Runs on STA because tests create real WPF DocumentView / PaginatedEditorPanel instances.</para>
/// </summary>
public sealed class PagedEditContinuousSectionGeometryTests
{
    private const int Section1ParagraphCount = 10;

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 1. Engine-level proof: section 1's blocks must be split across its OWN (tiny) page's worth of
    //    physical pages, not packed onto one page sized by the final section's much taller box.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void ContinuousBreak_LandscapeSectionThenTallerPortraitFinal_ComputeBlockPageAssignment_SplitsSectionOne()
    {
        var doc = BuildContinuousMixedGeometryDocument(out var markerIndex, out var lastSection1Index);

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        var assignment = PaginationEngine.ComputeBlockPageAssignment(editor);

        assignment.Should().HaveCount(doc.Blocks.Count);

        // Section 1's own (landscape, tiny content area) geometry cannot fit all 10 paragraphs plus
        // the marker on one page -- the assignment must show at least 2 distinct page indices among
        // section 1's blocks. Before the fix, the WHOLE document (section 1 + section 2, joined only
        // by a Continuous break) was one segment sized by the final (portrait, roomy) section, so all
        // of section 1's short paragraphs wrongly fit on page 0.
        var section1Pages = assignment.Take(markerIndex + 1).Distinct().ToList();
        section1Pages.Count.Should().BeGreaterThan(1,
            "section 1's content overflows its OWN (small landscape) content area and must span " +
            "more than one physical page even though the break into section 2 is Continuous -- sizing " +
            "it against the final section's taller geometry would wrongly pack it all onto page 0");

        var finalSectionBlockIndex = doc.Blocks.Count - 1;
        assignment[finalSectionBlockIndex].Should().BeGreaterThan(assignment[lastSection1Index],
            "the final (portrait) section must start after section 1's true page count");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 2. End-to-end proof: the last paragraph of section 1 is not dropped from the paged/print view.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void ContinuousBreak_LandscapeSectionThenTallerPortraitFinal_LastParagraphOfSectionOneAppears()
    {
        var doc = BuildContinuousMixedGeometryDocument(out _, out _);

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        var panel = PaginatedEditorPanel.Build(editor, includeParityBlankPages: true);

        panel.PageBoxes.Count.Should().BeGreaterThan(1,
            "section 1's overflow must produce its own extra page(s) instead of being silently " +
            "measured as fitting on a single, wrongly-sized page");

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
    // 3. Sibling / no-regression: a NextPage break still forces exactly one physical page, unaffected
    //    by the Continuous-break geometry fix (guards against the fix over-firing on page-type breaks).
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void NextPageBreak_StillForcesExactlyOnePhysicalPage()
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
        assignment[0].Should().Be(0, "section 1's single short paragraph fits on page 0");
        assignment[1].Should().Be(0, "the marker paragraph itself ends section 1 on page 0");
        assignment[2].Should().Be(1,
            "the NextPage section break forces exactly one new page for section 2 -- same-geometry " +
            "sections must not fragment into extra pages beyond what an explicit break requires");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 4. Sibling / no-regression: a Continuous break between two SAME-geometry sections must NOT
    //    fragment pagination into a spurious extra page -- proving the fix's merge-back-when-identical
    //    step keeps the common "Continuous break used only to vary headers/footers" case unaffected.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void ContinuousBreak_SameGeometryTwoSections_BlockAssignmentStaysSequentialAndMinimal()
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
            SectionBreak = new ModelSection(sharedPage, SectionBreakKind.Continuous)
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
        assignment.Should().OnlyContain(page => page == 0,
            "a Continuous break between two sections with IDENTICAL page geometry must not introduce " +
            "any extra page -- the fix must merge same-geometry Continuous-adjacent ranges back " +
            "together rather than always paginating every section as its own isolated segment");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 5. Three-section combo: Continuous(different geometry) followed by NextPage, matching the
    //    guidance to verify both a Continuous break AND that a page-type break still works together.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void ContinuousThenNextPage_ThreeSections_EachMeasuredWithOwnGeometryAndOffsetsCorrectly()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        // Section 1: landscape, tiny content area (~112pt tall) -- forces its 10 short paragraphs to
        // overflow onto more than one physical page.
        for (var i = 1; i <= Section1ParagraphCount; i++)
            doc.Blocks.Add(new Paragraph($"Section1 Line {i:00}"));
        var lastSection1Index = doc.Blocks.Count - 1;

        var landscapeTinyPage = new PageSettings
        {
            WidthPt = 792,
            HeightPt = 612,
            Landscape = true,
            MarginLeftPt = 72,
            MarginRightPt = 72,
            MarginTopPt = 250,
            MarginBottomPt = 250,
        };
        doc.Blocks.Add(new Paragraph("[ continuous break marker ]")
        {
            SectionBreak = new ModelSection(landscapeTinyPage, SectionBreakKind.Continuous)
        });

        // Section 2: portrait, roomy content area, one short paragraph, ends with a NextPage break.
        var portraitRoomyPage = new PageSettings
        {
            WidthPt = 612,
            HeightPt = 792,
            MarginLeftPt = 72,
            MarginRightPt = 72,
            MarginTopPt = 72,
            MarginBottomPt = 72,
        };
        doc.Blocks.Add(new Paragraph("Section2 paragraph"));
        var lastSection2Index = doc.Blocks.Count - 1;
        doc.Blocks.Add(new Paragraph("[ next-page break marker ]")
        {
            SectionBreak = new ModelSection(portraitRoomyPage, SectionBreakKind.NextPage)
        });

        // Section 3 (final): same roomy portrait geometry, one short paragraph.
        doc.Page.WidthPt = 612;
        doc.Page.HeightPt = 792;
        doc.Page.MarginLeftPt = 72;
        doc.Page.MarginRightPt = 72;
        doc.Page.MarginTopPt = 72;
        doc.Page.MarginBottomPt = 72;
        doc.Blocks.Add(new Paragraph("Section3 Final Paragraph"));
        var finalBlockIndex = doc.Blocks.Count - 1;

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        var assignment = PaginationEngine.ComputeBlockPageAssignment(editor);

        assignment.Should().HaveCount(doc.Blocks.Count);

        // Section 1 (tiny landscape) must still overflow to more than one physical page, exactly as
        // it does with only a Continuous break present -- proving a later NextPage break in the same
        // document does not stop section 1 from being measured against its OWN geometry.
        var section1Pages = assignment.Take(lastSection1Index + 1).Distinct().ToList();
        section1Pages.Count.Should().BeGreaterThan(1,
            "section 1's own tiny landscape content area still overflows even with a later NextPage " +
            "break present in the document");

        // Section 2 (roomy portrait) is short and fits on a single page of its own -- it must not
        // inherit section 1's tiny geometry.
        var section2Page = assignment[lastSection2Index];

        // The NextPage break must still force section 3 to start strictly after section 2's page.
        assignment[finalBlockIndex].Should().BeGreaterThan(section2Page,
            "the NextPage break between section 2 and section 3 must still force a fresh physical page");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // helpers
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Same geometry shape as <c>PagedEditMixedGeometryPaginationTests.BuildMixedGeometryDocument</c>
    /// but joined by a <see cref="SectionBreakKind.Continuous"/> break instead of NextPage, so section
    /// 1 does not get an explicit forced page break -- any extra page it needs must come purely from
    /// its own (tiny) content area being correctly applied during measurement.
    /// </summary>
    private static TextDocument BuildContinuousMixedGeometryDocument(out int markerIndex, out int lastSection1Index)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        for (var i = 1; i <= Section1ParagraphCount; i++)
            doc.Blocks.Add(new Paragraph($"Section1 Line {i:00}"));
        lastSection1Index = doc.Blocks.Count - 1;

        var landscapeTinyPage = new PageSettings
        {
            WidthPt = 792,
            HeightPt = 612,
            Landscape = true,
            MarginLeftPt = 72,
            MarginRightPt = 72,
            MarginTopPt = 250,
            MarginBottomPt = 250,
        };
        var marker = new Paragraph("[ section break marker ]")
        {
            SectionBreak = new ModelSection(landscapeTinyPage, SectionBreakKind.Continuous)
        };
        doc.Blocks.Add(marker);
        markerIndex = doc.Blocks.Count - 1;

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
}
