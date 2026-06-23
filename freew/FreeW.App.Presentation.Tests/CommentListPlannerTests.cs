using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class CommentListPlannerTests
{
    [Fact]
    public void Build_ReturnsCommentThreadsInDocumentOrderIncludingTables()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var body = new Paragraph();
        body.Runs.Add(new Run("Body") { CommentId = 2 });
        body.Runs.Add(Run.CommentReference(2));
        doc.Blocks.Add(body);

        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        var tableParagraph = new Paragraph();
        tableParagraph.Runs.Add(new Run("Table") { CommentId = 7 });
        tableParagraph.Runs.Add(Run.CommentReference(7));
        cell.Paragraphs.Add(tableParagraph);
        row.Cells.Add(cell);
        table.Rows.Add(row);
        doc.Blocks.Add(table);

        doc.Comments[7] = new Comment(7, "Table note", "Taylor", "T") { Resolved = true };
        doc.Comments[2] = new Comment(2, "Body note", "Alex", "A");
        doc.Comments[2].AddReply(3, "Reply", "Casey", "C");

        var items = CommentListPlanner.Build(doc);

        items.Select(item => item.Id).Should().Equal(2, 7);
        items[0].Author.Should().Be("Alex");
        items[0].Text.Should().Be("Body note");
        items[0].ReplyCount.Should().Be(1);
        items[0].Resolved.Should().BeFalse();
        items[1].Author.Should().Be("Taylor");
        items[1].BlockIndex.Should().Be(1);
        items[1].Resolved.Should().BeTrue();
    }
}
