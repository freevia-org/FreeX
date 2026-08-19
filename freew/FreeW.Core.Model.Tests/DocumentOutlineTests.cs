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
        doc.Blocks.Add(new Paragraph("Deep") { StyleId = "Heading9" });

        var outline = DocumentOutline.Of(doc);

        outline.Should().ContainSingle()
            .Which.Level.Should().Be(9);
    }

    [Fact]
    public void Of_OverDeepHeadingNumber_ClampsToWordOutlineDepth()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Too deep") { StyleId = "Heading10" });

        var outline = DocumentOutline.Of(doc);

        outline.Should().ContainSingle()
            .Which.Should().Be(new OutlineEntry(0, DocumentOutline.MaxOutlineLevel, "Too deep", "Heading10"));
    }

    [Fact]
    public void Of_SubtitleIsNotAHeading()
    {
        // Subtitle is a built-in style but not part of the heading outline.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("A subtitle") { StyleId = "Subtitle" });

        DocumentOutline.Of(doc).Should().BeEmpty();
    }

    [Fact]
    public void Of_HeadingStyledInsideTableCell_IsOmitted()
    {
        // Sibling no-regression case: DocumentOutline.Of is also the navigation pane's and outline
        // view's block-index source for promote/demote/move/collapse, which only make sense against a
        // top-level Paragraph block. It must keep excluding table-cell headings entirely (not just
        // fail to find them) so those features are unaffected by OfIncludingTableCells below.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Intro") { StyleId = "Heading1" });
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("Heading Inside Table"));
        table.Rows[0].Cells[0].Paragraphs[0].StyleId = "Heading1";
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph("Conclusion") { StyleId = "Heading1" });

        var outline = DocumentOutline.Of(doc);

        outline.Should().Equal(
            new OutlineEntry(0, 1, "Intro", "Heading1"),
            new OutlineEntry(2, 1, "Conclusion", "Heading1"));
    }

    [Fact]
    public void OfIncludingTableCells_HeadingStyledInsideTableCell_IsIncluded()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Intro") { StyleId = "Heading1" });
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("Heading Inside Table"));
        table.Rows[0].Cells[0].Paragraphs[0].StyleId = "Heading1";
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph("Conclusion") { StyleId = "Heading1" });

        var outline = DocumentOutline.OfIncludingTableCells(doc);

        outline.Select(entry => entry.Text).Should().Equal("Intro", "Heading Inside Table", "Conclusion");
        // The table-cell heading is anchored to the table's own top-level block index (1), same as the
        // convention DocumentBodyParagraphs already uses for the INDEX feature.
        outline[1].Should().Be(new OutlineEntry(1, 1, "Heading Inside Table", "Heading1"));
    }

    [Fact]
    public void OfIncludingTableCells_NoTables_MatchesOf()
    {
        // Sibling no-regression case: for a document with no tables, the table-aware walk must produce
        // exactly what Of produces (same entries, same block indexes).
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("My Title") { StyleId = "Title" });
        doc.Blocks.Add(new Paragraph("Intro body"));
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });

        DocumentOutline.OfIncludingTableCells(doc).Should().Equal(DocumentOutline.Of(doc));
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
    [InlineData("Heading9", true, 9)]
    [InlineData("Heading10", true, 9)]
    public void TryGetLevel_ClassifiesStyleIds(string? styleId, bool expectedIsHeading, int expectedLevel)
    {
        DocumentOutline.TryGetLevel(styleId, out var level).Should().Be(expectedIsHeading);
        if (expectedIsHeading)
            level.Should().Be(expectedLevel);
    }
}
