namespace FreeW.Core.Model.Tests;

public sealed class CrossCoreResidualDeduplicationTests
{
    [Fact]
    public void CopyShellBase_CopiesDefaultsStylesAndAllPageGeometryWithoutAliasingColumnWidths()
    {
        var source = TextDocument.CreateEmpty();
        source.DoNotAutoCompressPictures = true;
        source.EmbedSystemFonts = true;
        source.SaveSubsetFonts = true;
        source.PageBordersDoNotSurroundHeader = true;
        source.PageBordersDoNotSurroundFooter = true;
        source.Page.WidthPt = 700;
        source.Page.HeightPt = 900;
        source.Page.MarginLeftPt = 40;
        source.Page.MarginRightPt = 41;
        source.Page.MarginTopPt = 42;
        source.Page.MarginBottomPt = 43;
        source.Page.Landscape = true;
        source.Page.ColumnCount = 3;
        source.Page.ColumnSpacingPt = 12;
        source.Page.ColumnsLineBetween = true;
        source.Page.ColumnWidthsPt = [100, 110, 120];
        source.Page.DifferentFirstPage = true;
        var target = TextDocument.CreateEmpty();

        DocumentModelCloner.CopyShellBase(source, target);

        target.DefaultRun.Should().Be(source.DefaultRun);
        target.DefaultParagraph.Should().Be(source.DefaultParagraph);
        target.Styles.Should().BeEquivalentTo(source.Styles);
        target.Page.Should().BeEquivalentTo(source.Page);
        target.Page.ColumnWidthsPt.Should().NotBeSameAs(source.Page.ColumnWidthsPt);
        target.DoNotAutoCompressPictures.Should().BeTrue();
        target.EmbedSystemFonts.Should().BeTrue();
        target.SaveSubsetFonts.Should().BeTrue();
        target.PageBordersDoNotSurroundHeader.Should().BeTrue();
        target.PageBordersDoNotSurroundFooter.Should().BeTrue();
    }

    [Fact]
    public void EnumerateBlockParagraphs_PreservesShallowOrderAndOptionalNestedTableExpansion()
    {
        var direct = new Paragraph { Runs = { new Run("direct") } };
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("cell"));
        var nested = Table.Create(1, 1);
        nested.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("nested"));
        table.Rows[0].Cells[0].NestedTables.Add(nested);
        Block[] blocks = [direct, table];

        TextDocumentStoryTraversal.EnumerateBlockParagraphs(blocks)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("direct", "cell");
        TextDocumentStoryTraversal.EnumerateBlockParagraphs(
                blocks,
                TextDocumentStoryTraversalOptions.IncludeNestedTables)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("direct", "cell", "nested");
    }

    [Fact]
    public void CompareCombineAndMerge_AdoptSharedOwners()
    {
        var combine = TestWorkspaceFileLocator.ReadAllText("freew", "FreeW.Core.Model", "DocumentCombine.cs");
        var compare = TestWorkspaceFileLocator.ReadAllText("freew", "FreeW.Core.Model", "DocumentCompare.cs");
        var merge = TestWorkspaceFileLocator.ReadAllText("freew", "FreeW.Core.Model", "DocumentMerge.cs");

        combine.Should().Contain("DocumentModelCloner.CopyShellBase(");
        compare.Should().Contain("DocumentModelCloner.CopyShellBase(");
        compare.Should().Contain("TextDocumentStoryTraversal.EnumerateBlockParagraphs(");
        merge.Should().Contain("TextDocumentStoryTraversal.EnumerateBlockParagraphs(");
        compare.Should().NotContain("private static IEnumerable<Paragraph> EnumerateParagraphs(IEnumerable<Block>");
        merge.Should().NotContain("private static IEnumerable<Paragraph> EnumerateParagraphs(IEnumerable<Block>");
    }
}
