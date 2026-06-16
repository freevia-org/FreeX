namespace FreeW.Core.Model.Tests;

public class DocumentOutlineTests
{
    [Fact]
    public void Of_EmptyDocument_YieldsEmptyOutline()
    {
        var doc = new TextDocument();

        DocumentOutline.Of(doc).Should().BeEmpty();
    }

    [Fact]
    public void Of_NoHeadingParagraphs_YieldsEmptyOutline()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Just body text"));
        doc.Blocks.Add(new Paragraph("More body text") { StyleId = "Normal" });
        doc.Blocks.Add(new Paragraph("A quote") { StyleId = "Quote" });

        DocumentOutline.Of(doc).Should().BeEmpty();
    }

    [Fact]
    public void Of_TitleAndHeadings_YieldsEntriesInOrderWithLevelsAndText()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("My Title") { StyleId = "Title" });
        doc.Blocks.Add(new Paragraph("Intro body")); // excluded: no style
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Section A") { StyleId = "Heading2" });
        doc.Blocks.Add(new Paragraph("Detail") { StyleId = "Heading3" });
        doc.Blocks.Add(new Paragraph("More body") { StyleId = "Normal" }); // excluded
        doc.Blocks.Add(new Paragraph("Chapter Two") { StyleId = "Heading1" });

        var outline = DocumentOutline.Of(doc);

        outline.Should().Equal(
            new OutlineEntry(0, 0, "My Title", "Title"),
            new OutlineEntry(2, 1, "Chapter One", "Heading1"),
            new OutlineEntry(3, 2, "Section A", "Heading2"),
            new OutlineEntry(4, 3, "Detail", "Heading3"),
            new OutlineEntry(6, 1, "Chapter Two", "Heading1"));
    }

    [Fact]
    public void Of_BlockIndexMatchesDocumentOrderAcrossTables()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Heading before table") { StyleId = "Heading1" }); // index 0
        doc.Blocks.Add(Table.Create(2, 2));                                              // index 1
        doc.Blocks.Add(new Paragraph("Heading after table") { StyleId = "Heading2" });   // index 2

        var outline = DocumentOutline.Of(doc);

        outline.Should().Equal(
            new OutlineEntry(0, 1, "Heading before table", "Heading1"),
            new OutlineEntry(2, 2, "Heading after table", "Heading2"));
    }

    [Fact]
    public void Of_DeepHeadingNumber_DerivesLevelFromStyleId()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Deep") { StyleId = "Heading6" });

        var outline = DocumentOutline.Of(doc);

        outline.Should().ContainSingle()
            .Which.Level.Should().Be(6);
    }

    [Fact]
    public void Of_SubtitleIsNotAHeading()
    {
        // Subtitle is a built-in style but not part of the heading outline.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("A subtitle") { StyleId = "Subtitle" });

        DocumentOutline.Of(doc).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, false, 0)]
    [InlineData("", false, 0)]
    [InlineData("Normal", false, 0)]
    [InlineData("Quote", false, 0)]
    [InlineData("Subtitle", false, 0)]
    [InlineData("Heading", false, 0)]      // no number
    [InlineData("Heading0", false, 0)]     // zero is not a heading level
    [InlineData("Heading-1", false, 0)]    // negative is rejected
    [InlineData("Title", true, 0)]
    [InlineData("Heading1", true, 1)]
    [InlineData("Heading2", true, 2)]
    [InlineData("Heading3", true, 3)]
    [InlineData("Heading10", true, 10)]
    public void TryGetLevel_ClassifiesStyleIds(string? styleId, bool expectedIsHeading, int expectedLevel)
    {
        DocumentOutline.TryGetLevel(styleId, out var level).Should().Be(expectedIsHeading);
        if (expectedIsHeading)
            level.Should().Be(expectedLevel);
    }
}
