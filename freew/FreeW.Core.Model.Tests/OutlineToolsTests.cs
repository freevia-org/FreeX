namespace FreeW.Core.Model.Tests;

public class OutlineToolsTests
{
    // --- Promote: raises a heading one rank toward the top (Title) ---

    [Theory]
    [InlineData("Heading6", "Heading5")]
    [InlineData("Heading4", "Heading3")]
    [InlineData("Heading3", "Heading2")]
    [InlineData("Heading2", "Heading1")]
    [InlineData("Heading1", "Title")] // Heading1 promotes to the title
    [InlineData("Title", "Title")]    // Title is already the top — stays put
    public void Promote_RaisesHeadingOneRank(string styleId, string expected)
    {
        OutlineTools.Promote(styleId).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Normal")]
    [InlineData("Subtitle")] // a built-in style, but not part of the heading outline
    [InlineData("Quote")]
    public void Promote_NonHeading_ReturnsStyleUnchanged(string? styleId)
    {
        OutlineTools.Promote(styleId).Should().Be(styleId);
    }

    // --- Demote: lowers a heading one rank toward the bottom, capped at Heading6 ---

    [Theory]
    [InlineData("Title", "Heading1")] // Title demotes to Heading1
    [InlineData("Heading1", "Heading2")]
    [InlineData("Heading2", "Heading3")]
    [InlineData("Heading3", "Heading4")]
    [InlineData("Heading4", "Heading5")]
    [InlineData("Heading5", "Heading6")]
    [InlineData("Heading6", "Heading6")] // capped at the deepest level
    [InlineData("Heading10", "Heading6")] // already past the cap — clamps down to the cap
    public void Demote_LowersHeadingOneRankCapped(string styleId, string expected)
    {
        OutlineTools.Demote(styleId).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Normal")]
    [InlineData("Subtitle")]
    public void Demote_NonHeading_BecomesHeading1(string? styleId)
    {
        OutlineTools.Demote(styleId).Should().Be("Heading1");
    }

    [Fact]
    public void MaxHeadingLevel_IsHeading6()
    {
        OutlineTools.MaxHeadingLevel.Should().Be(6);
    }

    // --- Round-trip / inverse-ish behaviour across the registered heading range ---

    [Theory]
    [InlineData("Heading1")]
    [InlineData("Heading2")]
    [InlineData("Heading3")]
    [InlineData("Heading4")]
    public void PromoteThenDemote_RoundTripsInteriorHeadings(string styleId)
    {
        // Promote then demote returns to the same level for headings that are not at an outline edge.
        OutlineTools.Demote(OutlineTools.Promote(styleId)).Should().Be(styleId);
    }

    [Fact]
    public void Demote_OnDocumentParagraph_SetsHeadingStyle()
    {
        // Demonstrates the helper used against a real model paragraph's StyleId.
        var doc = new TextDocument();
        var paragraph = new Paragraph("A plain body line");
        doc.Blocks.Add(paragraph);

        paragraph.StyleId = OutlineTools.Demote(paragraph.StyleId);

        paragraph.StyleId.Should().Be("Heading1");
        DocumentOutline.TryGetLevel(paragraph.StyleId, out var level).Should().BeTrue();
        level.Should().Be(1);
    }

    [Fact]
    public void Promote_OnHeadingParagraph_RaisesStyle()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph("Section") { StyleId = "Heading2" };
        doc.Blocks.Add(paragraph);

        paragraph.StyleId = OutlineTools.Promote(paragraph.StyleId);

        paragraph.StyleId.Should().Be("Heading1");
    }

    // --- SubtreeRange: the heading plus its descendants (down to the next same-or-higher heading) ---

    private static Paragraph H(int level, string text) =>
        new(text) { StyleId = level == 0 ? "Title" : "Heading" + level };

    private static Paragraph Body(string text) => new(text);

    // [H1 "A", body a1, H2 "A.1", body a11, H1 "B", body b1]
    private static List<Block> SampleDoc() => new()
    {
        H(1, "A"), Body("a1"), H(2, "A.1"), Body("a11"), H(1, "B"), Body("b1"),
    };

    [Fact]
    public void SubtreeRange_CoversHeadingAndDescendants()
    {
        var blocks = SampleDoc();

        // "A" (index 0) owns through its H2 subtree, stopping at "B" (index 4).
        OutlineTools.SubtreeRange(blocks, 0).Should().Be((0, 4));
        // "A.1" (index 2) owns only its own body, stopping at the next H1 "B".
        OutlineTools.SubtreeRange(blocks, 2).Should().Be((2, 4));
        // "B" (index 4) owns to the end of the document.
        OutlineTools.SubtreeRange(blocks, 4).Should().Be((4, 6));
    }

    [Fact]
    public void SubtreeRange_NonHeading_IsEmpty()
    {
        var blocks = SampleDoc();
        OutlineTools.SubtreeRange(blocks, 1).Should().Be((1, 1)); // a body paragraph: empty span
    }

    // --- MoveSubtree: relocate a heading subtree by one sibling position (pure; returns a new list) ---

    private static IEnumerable<string> Texts(IReadOnlyList<Block> blocks) =>
        blocks.OfType<Paragraph>().Select(p => p.PlainText);

    [Fact]
    public void MoveSubtree_Down_SwapsWithFollowingSiblingSubtree()
    {
        var blocks = SampleDoc();

        // Move "A" (and its A.1 subtree) down past sibling "B".
        var moved = OutlineTools.MoveSubtree(blocks, 0, moveUp: false);

        Texts(moved).Should().Equal("B", "b1", "A", "a1", "A.1", "a11");
        // Original block instances are preserved (a permutation, nothing recreated).
        moved.Should().HaveCount(blocks.Count);
        moved.Should().OnlyContain(b => blocks.Contains(b));
    }

    [Fact]
    public void MoveSubtree_Up_IsTheInverseOfDown()
    {
        var blocks = SampleDoc();

        var down = OutlineTools.MoveSubtree(blocks, 0, moveUp: false);
        // "A" now starts at index 2 in the moved list; moving it back up restores the original order.
        var back = OutlineTools.MoveSubtree(down, 2, moveUp: true);

        Texts(back).Should().Equal("A", "a1", "A.1", "a11", "B", "b1");
    }

    [Fact]
    public void MoveSubtree_Up_AtFirstSibling_IsNoOp()
    {
        var blocks = SampleDoc();

        var moved = OutlineTools.MoveSubtree(blocks, 0, moveUp: true);

        moved.Should().BeSameAs(blocks); // nothing above to move past
    }

    [Fact]
    public void MoveSubtree_Down_AtLastSibling_IsNoOp()
    {
        var blocks = SampleDoc();

        var moved = OutlineTools.MoveSubtree(blocks, 4, moveUp: false);

        moved.Should().BeSameAs(blocks); // nothing below to move past
    }

    [Fact]
    public void MoveSubtree_Down_NestedLastChild_DoesNotLeaveParentSection()
    {
        var blocks = SampleDoc();

        var moved = OutlineTools.MoveSubtree(blocks, 2, moveUp: false);

        moved.Should().BeSameAs(blocks);
        Texts(moved).Should().Equal("A", "a1", "A.1", "a11", "B", "b1");
    }

    [Fact]
    public void MoveSubtree_Up_NestedFirstChild_DoesNotLeaveParentSection()
    {
        var blocks = SampleDoc();

        var moved = OutlineTools.MoveSubtree(blocks, 2, moveUp: true);

        moved.Should().BeSameAs(blocks);
        Texts(moved).Should().Equal("A", "a1", "A.1", "a11", "B", "b1");
    }

    [Fact]
    public void MoveSubtree_NonHeadingIndex_IsNoOp()
    {
        var blocks = SampleDoc();

        OutlineTools.MoveSubtree(blocks, 1, moveUp: false).Should().BeSameAs(blocks);
    }
}
