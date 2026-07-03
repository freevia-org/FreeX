namespace FreeW.Core.Model.Tests;

/// <summary>
/// Coverage for the pure Outline-view row computation (<see cref="OutlineViewModel.Build"/>): the indented
/// heading/body structure shown by View &gt; Outline, the "Show Level" filter, and "Show First Line Only".
/// </summary>
public class OutlineViewModelTests
{
    private static Paragraph H(int level, string text) =>
        new(text) { StyleId = level == 0 ? "Title" : "Heading" + level };

    // [Title, body, H1, body, H2, body, H1]
    private static TextDocument Sample()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(H(0, "My Title"));
        doc.Blocks.Add(new Paragraph("intro body"));
        doc.Blocks.Add(H(1, "Chapter One"));
        doc.Blocks.Add(new Paragraph("one body"));
        doc.Blocks.Add(H(2, "Section A"));
        doc.Blocks.Add(new Paragraph("section body"));
        doc.Blocks.Add(H(1, "Chapter Two"));
        return doc;
    }

    [Fact]
    public void Build_EmptyDocument_YieldsNoRows()
    {
        OutlineViewModel.Build(new TextDocument()).Should().BeEmpty();
    }

    [Fact]
    public void Build_AllLevels_ShowsEveryBlockAtItsOutlineDepth()
    {
        var rows = OutlineViewModel.Build(Sample());

        rows.Should().Equal(
            new OutlineRow(0, 0, "My Title", true),
            new OutlineRow(1, 0, "intro body", false),    // body before/under the title sits at level 0
            new OutlineRow(2, 1, "Chapter One", true),
            new OutlineRow(3, 1, "one body", false),      // body under Heading 1 is indented to level 1
            new OutlineRow(4, 2, "Section A", true),
            new OutlineRow(5, 2, "section body", false),  // body under Heading 2 is indented to level 2
            new OutlineRow(6, 1, "Chapter Two", true));
    }

    [Fact]
    public void Build_ShowLevel1_KeepsOnlyTitleAndHeading1_HidesDeeperHeadingsAndBody()
    {
        var rows = OutlineViewModel.Build(Sample(), showLevel: 1);

        rows.Select(r => r.Text).Should().Equal("My Title", "Chapter One", "Chapter Two");
        rows.Should().OnlyContain(r => r.IsHeading, "Show Level hides body text");
    }

    [Fact]
    public void Build_ShowLevel2_KeepsHeadingsThroughLevel2_StillHidesBody()
    {
        var rows = OutlineViewModel.Build(Sample(), showLevel: 2);

        rows.Select(r => r.Text).Should().Equal("My Title", "Chapter One", "Section A", "Chapter Two");
    }

    [Fact]
    public void Build_FirstLineOnly_TrimsEachRowToItsFirstLine()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(H(1, "Heading"));
        doc.Blocks.Add(new Paragraph("first line\nsecond line\nthird line"));

        var rows = OutlineViewModel.Build(doc, firstLineOnly: true);

        rows.Select(r => r.Text).Should().Equal("Heading", "first line");
    }

    [Fact]
    public void Build_Table_AppearsAsBodyRowUnderItsHeading()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(H(1, "Data"));
        var table = new Table();
        var row = new TableRow();
        row.Cells.Add(new TableCell("cell text"));
        table.Rows.Add(row);
        doc.Blocks.Add(table);

        var rows = OutlineViewModel.Build(doc);

        rows.Should().HaveCount(2);
        rows[1].IsHeading.Should().BeFalse();
        rows[1].Level.Should().Be(1, "the table is indented under its owning heading");
        rows[1].Text.Should().Contain("cell text");
    }

    [Fact]
    public void Build_OverDeepHeading_UsesWordOutlineDepthCap()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Deep custom heading") { StyleId = "Heading10" });
        doc.Blocks.Add(new Paragraph("body under deep heading"));

        var rows = OutlineViewModel.Build(doc);

        rows.Should().Equal(
            new OutlineRow(0, DocumentOutline.MaxOutlineLevel, "Deep custom heading", true),
            new OutlineRow(1, DocumentOutline.MaxOutlineLevel, "body under deep heading", false));
    }
}
