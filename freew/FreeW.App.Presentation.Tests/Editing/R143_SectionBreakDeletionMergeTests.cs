using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

/// <summary>
/// R143 (freew-section-3): before this fix, <see cref="DocumentEditingSession.CanRestructure"/> refused
/// any paragraph-merge that touched a <see cref="Paragraph.SectionBreak"/>-owning paragraph, so pressing
/// Backspace at the start of (or Delete at the end of) a section-break paragraph in the WPF host silently
/// declined the model-aware merge and fell through to native RichTextBox editing, which leaves the
/// section-break marker (and its distinct <see cref="PageSettings"/>) completely untouched -- the user has
/// no way to actually delete a section break. Word's rule when a section-break paragraph mark IS deleted:
/// the two sections merge into one, adopting the FOLLOWING section's page setup (the preceding section's
/// own properties are discarded). These tests exercise the exact production entry point
/// (<see cref="DocumentBodyEditingCoordinator.TryApplyDeletion"/>, called from
/// FreeW.App.Host.Editing.DocumentView.TryApplyBodyBackspace/TryApplyBodyDeleteForward at
/// freew/FreeW.App.Host/Editing/DocumentView.cs:14259-14261) and assert both that the merge now succeeds
/// and that the surviving section is the correct one.
/// </summary>
public sealed class R143_SectionBreakDeletionMergeTests
{
    [Fact]
    public void Backspace_AtStartOfParagraphAfterSectionBreak_DeletesTheBreakAndAdoptsFollowingSection()
    {
        // Two sections: paragraph 0 ends section A (Legal landscape, 1008x612); paragraph 1 has no break,
        // so it belongs to the document's final section (Letter portrait, the default 612x792).
        var first = new Paragraph("one")
        {
            SectionBreak = new Section(new PageSettings { WidthPt = 1008, HeightPt = 612, Landscape = true }),
        };
        var second = new Paragraph("two");
        var session = SessionWith(first, second);
        session.Document.Sections.Should().HaveCount(2);

        // Caret at the start of paragraph 1 ("two"), Backspace: this is the exact gesture the finding
        // describes, and the exact call DocumentBodyEditingCoordinator.TryApplyDeletion makes on behalf of
        // FreeW.App.Host.Editing.DocumentView.TryApplyBodyBackspace.
        session.Body.TryApplyDeletion(
                Range(1, 0, 1, 0),
                DocumentBodyDeleteDirection.Backward,
                trackChanges: false,
                mergeForwardBoundary: false,
                out var result)
            .Should().BeTrue("the section break must now be reachable by the model-aware editing path");

        result.Transition.Should().Be(DocumentBodyEditorTransition.MergeWithPreviousParagraph);
        session.Document.Blocks.Should().HaveCount(1);
        ParagraphAt(session, 0).PlainText.Should().Be("onetwo");

        // Section A's own properties (the earlier/deleted mark) must be gone, and the merged content must
        // fall under the FOLLOWING section (here, the document's final/default section) -- not a leftover
        // of the deleted section.
        ParagraphAt(session, 0).SectionBreak.Should().BeNull();
        session.Document.Sections.Should().HaveCount(1);
        session.Document.Sections[0].Page.WidthPt.Should().Be(612);
        session.Document.Sections[0].Page.Landscape.Should().BeFalse();

        // Undo must restore both paragraphs AND section A's own page settings exactly.
        session.Commands.Undo().Should().BeTrue();
        session.Document.Blocks.Should().HaveCount(2);
        session.Document.Sections.Should().HaveCount(2);
        session.Document.Sections[0].Page.WidthPt.Should().Be(1008);
        session.Document.Sections[0].Page.Landscape.Should().BeTrue();
        ParagraphAt(session, 0).PlainText.Should().Be("one");
        ParagraphAt(session, 1).PlainText.Should().Be("two");
    }

    [Fact]
    public void DeleteForward_AtEndOfSectionBreakParagraph_DeletesTheBreakAndAdoptsFollowingSection()
    {
        var first = new Paragraph("one")
        {
            SectionBreak = new Section(new PageSettings { WidthPt = 1008, HeightPt = 612, Landscape = true }),
        };
        var second = new Paragraph("two");
        var session = SessionWith(first, second);

        // Caret at the end of paragraph 0 (the section-break owner), Delete: the forward-direction twin of
        // the Backspace gesture above, matching FreeW.App.Host.Editing.DocumentView.TryApplyBodyDeleteForward.
        session.Body.TryApplyDeletion(
                Range(0, 3, 0, 3),
                DocumentBodyDeleteDirection.Forward,
                trackChanges: false,
                mergeForwardBoundary: true,
                out var result)
            .Should().BeTrue("the section break must now be reachable by the model-aware editing path");

        result.Transition.Should().Be(DocumentBodyEditorTransition.MergeWithNextParagraph);
        session.Document.Blocks.Should().HaveCount(1);
        ParagraphAt(session, 0).PlainText.Should().Be("onetwo");
        ParagraphAt(session, 0).SectionBreak.Should().BeNull();
        session.Document.Sections.Should().HaveCount(1);
        session.Document.Sections[0].Page.WidthPt.Should().Be(612);
    }

    /// <summary>
    /// Three sections: deleting the break between the FIRST and SECOND must fold section A into section B
    /// -- adopting section B's page width (500), not section A's (1008) and not the final section's (612).
    /// This is the case that actually distinguishes "adopt the following section" from "always fall back to
    /// the document default", and pins the direction (earlier paragraph's mark is what gets deleted).
    /// </summary>
    [Fact]
    public void Backspace_BetweenTwoNonFinalSections_AdoptsTheImmediatelyFollowingSectionNotTheFinalOne()
    {
        var first = new Paragraph("a")
        {
            SectionBreak = new Section(new PageSettings { WidthPt = 1008 }),
        };
        var second = new Paragraph("b")
        {
            SectionBreak = new Section(new PageSettings { WidthPt = 500 }),
        };
        var third = new Paragraph("c");
        var session = SessionWith(first, second, third);
        session.Document.Sections.Should().HaveCount(3);

        session.Body.TryApplyDeletion(
                Range(1, 0, 1, 0),
                DocumentBodyDeleteDirection.Backward,
                trackChanges: false,
                mergeForwardBoundary: false,
                out var result)
            .Should().BeTrue();

        result.Transition.Should().Be(DocumentBodyEditorTransition.MergeWithPreviousParagraph);
        session.Document.Blocks.Should().HaveCount(2);
        ParagraphAt(session, 0).PlainText.Should().Be("ab");
        // The merged paragraph must carry section B's own break (width 500), not be null and not be 1008.
        ParagraphAt(session, 0).SectionBreak.Should().NotBeNull();
        ParagraphAt(session, 0).SectionBreak!.Page.WidthPt.Should().Be(500);
        session.Document.Sections.Should().HaveCount(2);
        session.Document.Sections[0].Page.WidthPt.Should().Be(500);
    }

    /// <summary>
    /// Sibling/neighbour-behavior test: the fix only relaxes the SectionBreak clause of CanRestructure. A
    /// paragraph that is unmergeable for an unrelated structural reason (here, an active bookmark) must
    /// still refuse the merge exactly as before, proving the relaxation did not widen past section breaks.
    /// </summary>
    [Fact]
    public void Backspace_AtStartOfParagraphAfterBookmarkedParagraph_StillRefusesTheMerge()
    {
        var first = new Paragraph("one") { BookmarkName = "Target" };
        var second = new Paragraph("two");
        var session = SessionWith(first, second);

        session.Body.TryApplyDeletion(
                Range(1, 0, 1, 0),
                DocumentBodyDeleteDirection.Backward,
                trackChanges: false,
                mergeForwardBoundary: false,
                out _)
            .Should().BeFalse("a bookmarked paragraph must still block restructuring merges");

        session.Document.Blocks.Should().HaveCount(2);
        ParagraphAt(session, 0).PlainText.Should().Be("one");
        ParagraphAt(session, 1).PlainText.Should().Be("two");
    }

    /// <summary>
    /// Sibling/neighbour-behavior test: an ordinary paragraph-boundary merge with no section break anywhere
    /// still behaves exactly as before the fix (result carries no SectionBreak either side).
    /// </summary>
    [Fact]
    public void Backspace_BetweenOrdinaryParagraphs_MergesNormallyWithNoSectionBreakInvolved()
    {
        var session = SessionWith(new Paragraph("alpha"), new Paragraph("beta"));

        session.Body.TryApplyDeletion(
                Range(1, 0, 1, 0),
                DocumentBodyDeleteDirection.Backward,
                trackChanges: false,
                mergeForwardBoundary: false,
                out var result)
            .Should().BeTrue();

        result.Transition.Should().Be(DocumentBodyEditorTransition.MergeWithPreviousParagraph);
        ParagraphAt(session, 0).PlainText.Should().Be("alphabeta");
        ParagraphAt(session, 0).SectionBreak.Should().BeNull();
    }

    private static DocumentTextRange Range(
        int anchorBlock,
        int anchorOffset,
        int activeBlock,
        int activeOffset) => new(
        new DocumentTextPosition(anchorBlock, anchorOffset),
        new DocumentTextPosition(activeBlock, activeOffset));

    private static DocumentEditingSession SessionWith(params Paragraph[] paragraphs)
    {
        var document = new TextDocument();
        document.Blocks.AddRange(paragraphs);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        return session;
    }

    private static Paragraph ParagraphAt(DocumentEditingSession session, int index) =>
        (Paragraph)session.Document.Blocks[index];
}
