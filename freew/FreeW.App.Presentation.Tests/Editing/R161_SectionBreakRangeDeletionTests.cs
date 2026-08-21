using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

/// <summary>
/// freew-section-breaks F1: R143 fixed the COLLAPSED-CARET case (Backspace/Delete exactly at a
/// section-break paragraph boundary), via <see cref="DocumentEditingSession.TryMergeBodyParagraphWithPrevious"/>
/// / <see cref="DocumentEditingSession.TryMergeBodyParagraphWithNext"/>. But the generic multi-block RANGE
/// path -- <see cref="DocumentEditingSession.TryDeleteBodyText"/> (any non-collapsed Delete/Backspace
/// selection) and <see cref="DocumentEditingSession.TryReplaceBodyText"/> (typing over a selection) -- still
/// called the strict <c>CanRestructure(DocumentTextRange)</c>, which rejected the whole span the instant ANY
/// paragraph inside it (not just at a boundary) owned a <see cref="Paragraph.SectionBreak"/>. Declining sends
/// both hosts to native RichTextBox/Avalonia editing, which does not apply Word's
/// earlier-discarded/later-survives section-merge rule at all. These tests exercise the exact production
/// entry points named by the finding and assert both that the merge now succeeds and that the surviving
/// section is the correct one (mirroring R143's own assertions, generalized to a range that spans the break
/// rather than sitting exactly on it).
/// </summary>
public sealed class R161_SectionBreakRangeDeletionTests
{
    [Fact]
    public void TryDeleteBodyText_RangeSpanningASectionBreakParagraph_MergesAndDiscardsTheEarlierSection()
    {
        // Two sections: paragraph 0 ("one") ends section A (Legal landscape); paragraph 1 ("two") has no
        // break, so it belongs to the document's final section (Letter portrait). This is the finding's
        // own repro shape (EVIDENCE section of freew-section-breaks F1).
        var first = new Paragraph("one")
        {
            SectionBreak = new Section(new PageSettings { WidthPt = 1008, HeightPt = 612, Landscape = true }),
        };
        var second = new Paragraph("two");
        var session = SessionWith(first, second);
        session.Document.Sections.Should().HaveCount(2);

        // A selection from partway through paragraph 0 to partway through paragraph 1 -- spanning the
        // section-break paragraph itself, not just a caret boundary.
        var range = Range(0, 1, 1, 2);

        session.TryDeleteBodyText(range, out var result)
            .Should().BeTrue("a range that spans a section-break paragraph must now be reachable by the model-aware editing path");

        session.Document.Blocks.Should().ContainSingle();
        ParagraphAt(session, 0).PlainText.Should().Be("oo");
        result.Caret.Should().Be(new DocumentTextPosition(0, 1));

        // Section A's own properties (the earlier, discarded mark) must be gone; the merged paragraph
        // falls under the FOLLOWING section (here, the document's final/default section).
        ParagraphAt(session, 0).SectionBreak.Should().BeNull();
        session.Document.Sections.Should().HaveCount(1);
        session.Document.Sections[0].Page.WidthPt.Should().Be(612);
        session.Document.Sections[0].Page.Landscape.Should().BeFalse();

        session.Commands.Undo().Should().BeTrue();
        session.Document.Blocks.Should().HaveCount(2);
        session.Document.Sections.Should().HaveCount(2);
        session.Document.Sections[0].Page.WidthPt.Should().Be(1008);
        ParagraphAt(session, 0).PlainText.Should().Be("one");
        ParagraphAt(session, 1).PlainText.Should().Be("two");
    }

    [Fact]
    public void TryReplaceBodyText_TypingOverASelectionSpanningASectionBreakParagraph_MergesAndDiscardsTheEarlierSection()
    {
        var first = new Paragraph("one")
        {
            SectionBreak = new Section(new PageSettings { WidthPt = 1008, HeightPt = 612, Landscape = true }),
        };
        var second = new Paragraph("two");
        var session = SessionWith(first, second);

        session.TryReplaceBodyText(Range(0, 1, 1, 2), "X", formatting: null, out var result)
            .Should().BeTrue("typing over a selection that spans a section-break paragraph must now be reachable by the model-aware editing path");

        session.Document.Blocks.Should().ContainSingle();
        ParagraphAt(session, 0).PlainText.Should().Be("oXo");
        result.Caret.Should().Be(new DocumentTextPosition(0, 2));
        ParagraphAt(session, 0).SectionBreak.Should().BeNull();
        session.Document.Sections.Should().HaveCount(1);
        session.Document.Sections[0].Page.WidthPt.Should().Be(612);

        session.Commands.Undo().Should().BeTrue();
        session.Document.Sections.Should().HaveCount(2);
    }

    /// <summary>
    /// Three sections, both intervening breaks inside the deleted span: deleting from paragraph 0 through
    /// paragraph 2 (leaving paragraph 2, with no break of its own, as the survivor) must discard BOTH
    /// section A's and section B's own properties and fall under the document's final section -- not just
    /// the immediately-following one. This is the range generalization of R143's own "adopts the
    /// immediately following section, not the final one" test; here the survivor legitimately IS the final
    /// section because the endpoint paragraph carries no break at all.
    /// </summary>
    [Fact]
    public void TryDeleteBodyText_RangeSpanningTwoSectionBreaks_KeepsOnlyTheSurvivorParagraphsSection()
    {
        var first = new Paragraph("aaa") { SectionBreak = new Section(new PageSettings { WidthPt = 1008 }) };
        var second = new Paragraph("bbb") { SectionBreak = new Section(new PageSettings { WidthPt = 500 }) };
        var third = new Paragraph("ccc");
        var session = SessionWith(first, second, third);
        session.Document.Sections.Should().HaveCount(3);

        session.TryDeleteBodyText(Range(0, 1, 2, 2), out _).Should().BeTrue();

        session.Document.Blocks.Should().ContainSingle();
        ParagraphAt(session, 0).PlainText.Should().Be("ac");
        ParagraphAt(session, 0).SectionBreak.Should().BeNull();
        session.Document.Sections.Should().HaveCount(1);
    }

    /// <summary>
    /// Sibling/no-regression: when the span's END paragraph itself still owns the surviving section break
    /// (the range does not reach past it), that break must be carried onto the merged paragraph UNCHANGED,
    /// not dropped -- proving the fix is a pass-through of the real survivor, not just "clear every break".
    /// </summary>
    [Fact]
    public void TryDeleteBodyText_RangeEndingOnASectionBreakParagraph_KeepsThatParagraphsOwnSection()
    {
        var first = new Paragraph("one");
        var second = new Paragraph("two") { SectionBreak = new Section(new PageSettings { WidthPt = 700 }) };
        var third = new Paragraph("three");
        var session = SessionWith(first, second, third);
        session.Document.Sections.Should().HaveCount(2);

        session.TryDeleteBodyText(Range(0, 1, 1, 2), out _).Should().BeTrue();

        session.Document.Blocks.Should().HaveCount(2);
        ParagraphAt(session, 0).PlainText.Should().Be("oo");
        ParagraphAt(session, 0).SectionBreak.Should().NotBeNull();
        ParagraphAt(session, 0).SectionBreak!.Page.WidthPt.Should().Be(700);
        session.Document.Sections.Should().HaveCount(2);
        ParagraphAt(session, 1).PlainText.Should().Be("three");
    }

    /// <summary>
    /// Sibling/no-regression: the fix only relaxes the SectionBreak clause of the span check. A paragraph
    /// that is unmergeable for an unrelated structural reason (here, an active bookmark) must still refuse
    /// the whole range exactly as before, proving the relaxation did not widen past section breaks.
    /// </summary>
    [Fact]
    public void TryDeleteBodyText_RangeCoveringABookmarkedParagraph_StillRefusesTheMerge()
    {
        var first = new Paragraph("one") { BookmarkName = "Target" };
        var second = new Paragraph("two");
        var session = SessionWith(first, second);

        session.TryDeleteBodyText(Range(0, 1, 1, 2), out _)
            .Should().BeFalse("a bookmarked paragraph must still block restructuring merges");

        session.Document.Blocks.Should().HaveCount(2);
        ParagraphAt(session, 0).PlainText.Should().Be("one");
        ParagraphAt(session, 1).PlainText.Should().Be("two");
    }

    /// <summary>
    /// Sibling/no-regression: an ordinary cross-paragraph range with no section break anywhere in it still
    /// behaves exactly as before the fix (merged paragraph carries no section break either side).
    /// </summary>
    [Fact]
    public void TryDeleteBodyText_RangeWithNoSectionBreakInvolved_MergesNormally()
    {
        var session = SessionWith(new Paragraph("alpha"), new Paragraph("beta"));

        session.TryDeleteBodyText(Range(0, 2, 1, 2), out _).Should().BeTrue();

        ParagraphAt(session, 0).PlainText.Should().Be("alta");
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
