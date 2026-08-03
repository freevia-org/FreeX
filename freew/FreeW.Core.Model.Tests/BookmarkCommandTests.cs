namespace FreeW.Core.Model.Tests;

public sealed class BookmarkCommandTests
{
    [Fact]
    public void SetParagraphBookmarkName_PreservesSiblingNamesAcrossUndoAndRedo()
    {
        var paragraph = new Paragraph("Target");
        paragraph.BookmarkNames.AddRange(["old", "sibling"]);
        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        var bus = new DocumentCommandBus(new TestContext(document));

        bus.Execute(new SetParagraphBookmarkNameCommand(0, "replacement"));
        paragraph.BookmarkNames.Should().Equal("replacement", "sibling");

        bus.Undo().Should().BeTrue();
        paragraph.BookmarkNames.Should().Equal("old", "sibling");

        bus.Redo().Should().BeTrue();
        paragraph.BookmarkNames.Should().Equal("replacement", "sibling");
    }

    [Fact]
    public void RemoveBookmark_PreservesEveryParagraphNameListAcrossUndoAndRedo()
    {
        var first = new Paragraph("First");
        first.BookmarkNames.AddRange(["target", "first-control"]);
        var second = new Paragraph("Second");
        second.BookmarkNames.AddRange(["second-control", "target"]);
        var document = new TextDocument();
        document.Blocks.Add(first);
        document.Blocks.Add(second);
        var bus = new DocumentCommandBus(new TestContext(document));

        bus.Execute(new RemoveBookmarkCommand("target"));
        first.BookmarkNames.Should().Equal("first-control");
        second.BookmarkNames.Should().Equal("second-control");

        bus.Undo().Should().BeTrue();
        first.BookmarkNames.Should().Equal("target", "first-control");
        second.BookmarkNames.Should().Equal("second-control", "target");

        bus.Redo().Should().BeTrue();
        first.BookmarkNames.Should().Equal("first-control");
        second.BookmarkNames.Should().Equal("second-control");
    }

    private sealed class TestContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
