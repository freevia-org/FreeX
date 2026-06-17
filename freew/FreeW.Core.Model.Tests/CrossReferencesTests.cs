namespace FreeW.Core.Model.Tests;

public class CrossReferencesTests
{
    [Theory]
    [InlineData(CrossRefType.Heading)]
    [InlineData(CrossRefType.Bookmark)]
    [InlineData(CrossRefType.Caption)]
    [InlineData(CrossRefType.Footnote)]
    public void Targets_EmptyDocument_YieldsEmpty(CrossRefType type)
    {
        var doc = new TextDocument();

        CrossReferences.Targets(doc, type).Should().BeEmpty();
    }

    [Fact]
    public void Targets_Heading_EnumeratesOutlineHeadingsInOrder()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("My Title") { StyleId = "Title" });
        doc.Blocks.Add(new Paragraph("Intro body")); // excluded: not a heading
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Section A") { StyleId = "Heading2" });

        var targets = CrossReferences.Targets(doc, CrossRefType.Heading);

        targets.Should().Equal(
            new CrossRefTarget("My Title", null, 0),
            new CrossRefTarget("Chapter One", null, 2),
            new CrossRefTarget("Section A", null, 3));
    }

    [Fact]
    public void Targets_Heading_WithBookmark_CarriesAnchor()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1", BookmarkName = "ch1" });

        var targets = CrossReferences.Targets(doc, CrossRefType.Heading);

        targets.Should().ContainSingle()
            .Which.Should().Be(new CrossRefTarget("Chapter One", "ch1", 0));
    }

    [Fact]
    public void Targets_Bookmark_EnumeratesNamedParagraphsWithAnchorAsName()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("First") { BookmarkName = "alpha" });
        doc.Blocks.Add(new Paragraph("No bookmark"));
        doc.Blocks.Add(new Paragraph("Second") { BookmarkName = "beta" });

        var targets = CrossReferences.Targets(doc, CrossRefType.Bookmark);

        targets.Should().Equal(
            new CrossRefTarget("alpha", "alpha", 0),
            new CrossRefTarget("beta", "beta", 2));
    }

    [Fact]
    public void Targets_Bookmark_DeduplicatesRepeatedNames()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("First") { BookmarkName = "dup" });
        doc.Blocks.Add(new Paragraph("Second") { BookmarkName = "dup" });

        CrossReferences.Targets(doc, CrossRefType.Bookmark)
            .Should().ContainSingle()
            .Which.BlockIndex.Should().Be(0);
    }

    [Fact]
    public void Targets_Caption_EnumeratesCaptionParagraphsWithText()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Blocks.Add(Captions.BuildCaption(CaptionLabel.Figure, 1, "Diagram"));
        doc.Blocks.Add(Captions.BuildCaption(CaptionLabel.Table, 2, ""));

        var targets = CrossReferences.Targets(doc, CrossRefType.Caption);

        targets.Should().Equal(
            new CrossRefTarget("Figure 1: Diagram", null, 1),
            new CrossRefTarget("Table 2", null, 2));
    }

    [Fact]
    public void Targets_Footnote_EnumeratesByAscendingIdWithFootnoteNLabel()
    {
        var doc = new TextDocument();
        doc.Footnotes[2] = new Footnote(2, "second");
        doc.Footnotes[1] = new Footnote(1, "first");

        var targets = CrossReferences.Targets(doc, CrossRefType.Footnote);

        targets.Should().Equal(
            new CrossRefTarget("Footnote 1", null, null),
            new CrossRefTarget("Footnote 2", null, null));
    }

    [Fact]
    public void ReferenceText_ReturnsDisplayForEachTarget()
    {
        CrossReferences.ReferenceText(new CrossRefTarget("Chapter One", "ch1", 0))
            .Should().Be("Chapter One");
        CrossReferences.ReferenceText(new CrossRefTarget("alpha", "alpha", 1))
            .Should().Be("alpha");
        CrossReferences.ReferenceText(new CrossRefTarget("Figure 1: Diagram", null, 2))
            .Should().Be("Figure 1: Diagram");
        CrossReferences.ReferenceText(new CrossRefTarget("Footnote 3", null, null))
            .Should().Be("Footnote 3");
    }
}
