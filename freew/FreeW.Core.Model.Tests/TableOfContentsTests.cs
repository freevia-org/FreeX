namespace FreeW.Core.Model.Tests;

public class TableOfContentsTests
{
    [Fact]
    public void Build_EmptyDocument_YieldsOnlyTheHeadingParagraph()
    {
        var doc = new TextDocument();

        var toc = TableOfContents.Build(doc);

        toc.Should().ContainSingle();
        toc[0].PlainText.Should().Be(TableOfContents.HeadingText);
        toc[0].StyleId.Should().Be(TableOfContents.HeadingStyleId);
    }

    [Fact]
    public void Build_NoHeadings_YieldsOnlyTheHeadingParagraph()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));
        doc.Blocks.Add(new Paragraph("More body") { StyleId = "Normal" });

        var toc = TableOfContents.Build(doc);

        toc.Should().ContainSingle()
            .Which.StyleId.Should().Be(TableOfContents.HeadingStyleId);
    }

    [Fact]
    public void Build_TitleAndHeadings_YieldsHeadingThenEntriesInOrderWithLevelIndent()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("My Title") { StyleId = "Title" });        // level 0
        doc.Blocks.Add(new Paragraph("Intro body"));                            // excluded
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });  // level 1
        doc.Blocks.Add(new Paragraph("Section A") { StyleId = "Heading2" });    // level 2
        doc.Blocks.Add(new Paragraph("Detail") { StyleId = "Heading3" });       // level 3
        doc.Blocks.Add(new Paragraph("Chapter Two") { StyleId = "Heading1" });  // level 1

        var toc = TableOfContents.Build(doc);

        // Heading + one paragraph per outline entry, in document order.
        toc.Select(p => p.PlainText).Should().Equal(
            TableOfContents.HeadingText,
            "My Title",
            "Chapter One",
            "Section A",
            "Detail",
            "Chapter Two");

        // The heading uses the TOC heading style; entries use TOC{level} (clamped at MaxStyledLevel).
        toc.Select(p => p.StyleId).Should().Equal(
            TableOfContents.HeadingStyleId,
            "TOC0",
            "TOC1",
            "TOC2",
            "TOC3",
            "TOC1");

        // Left indent is level * IndentPerLevelPt for each entry (heading has none).
        toc.Select(p => p.Formatting.IndentLeftPt).Should().Equal(
            0,                                       // heading
            0 * TableOfContents.IndentPerLevelPt,    // Title (level 0)
            1 * TableOfContents.IndentPerLevelPt,    // Heading1
            2 * TableOfContents.IndentPerLevelPt,    // Heading2
            3 * TableOfContents.IndentPerLevelPt,    // Heading3
            1 * TableOfContents.IndentPerLevelPt);   // Heading1
    }

    [Fact]
    public void Build_DeepHeading_IndentsByTrueLevelButClampsStyleId()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Deep") { StyleId = "Heading6" });

        var toc = TableOfContents.Build(doc);

        var entry = toc[1];
        entry.PlainText.Should().Be("Deep");
        entry.Formatting.IndentLeftPt.Should().Be(6 * TableOfContents.IndentPerLevelPt);
        // The style id is clamped to the deepest registered level so it still resolves to a TOC style.
        entry.StyleId.Should().Be("TOC" + TableOfContents.MaxStyledLevel);
    }

    [Fact]
    public void Build_DoesNotMutateTheDocument()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });

        var before = doc.Blocks.Count;
        TableOfContents.Build(doc);

        doc.Blocks.Should().HaveCount(before);
    }

    [Fact]
    public void IsTocStyleId_RecognisesGeneratedStyles()
    {
        TableOfContents.IsTocStyleId(TableOfContents.HeadingStyleId).Should().BeTrue();
        TableOfContents.IsTocStyleId("TOC0").Should().BeTrue();
        TableOfContents.IsTocStyleId("TOC1").Should().BeTrue();
        TableOfContents.IsTocStyleId("TOC3").Should().BeTrue();

        TableOfContents.IsTocStyleId(null).Should().BeFalse();
        TableOfContents.IsTocStyleId("").Should().BeFalse();
        TableOfContents.IsTocStyleId("Normal").Should().BeFalse();
        TableOfContents.IsTocStyleId("Heading1").Should().BeFalse();
        TableOfContents.IsTocStyleId("TOC").Should().BeFalse();      // no level number
    }

    [Fact]
    public void IsTocParagraph_TrueOnlyForTocStyledParagraphs()
    {
        TableOfContents.IsTocParagraph(new Paragraph("x") { StyleId = "TOC1" }).Should().BeTrue();
        TableOfContents.IsTocParagraph(new Paragraph("x") { StyleId = "Heading1" }).Should().BeFalse();
        TableOfContents.IsTocParagraph(Table.Create(1, 1)).Should().BeFalse();
    }

    [Fact]
    public void EnsureStyles_RegistersTocStylesIdempotently()
    {
        var doc = TextDocument.CreateEmpty();

        TableOfContents.EnsureStyles(doc);
        TableOfContents.EnsureStyles(doc); // second call must not throw or duplicate

        doc.Styles.Should().ContainKey(TableOfContents.HeadingStyleId);
        doc.Styles.Should().ContainKey("TOC1");
        doc.Styles.Should().ContainKey("TOC2");
        doc.Styles.Should().ContainKey("TOC3");
    }

    [Fact]
    public void EnsureStyles_DoesNotOverwriteAnExistingStyle()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Styles[TableOfContents.HeadingStyleId] = new DocumentStyle
        {
            Id = TableOfContents.HeadingStyleId,
            Name = "Custom"
        };

        TableOfContents.EnsureStyles(doc);

        doc.Styles[TableOfContents.HeadingStyleId].Name.Should().Be("Custom");
    }
}
