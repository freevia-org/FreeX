namespace FreeW.Core.Model.Tests;

public sealed class CrossReferenceCommandTests
{
    [Fact]
    public void InsertCrossReference_RestoresHostRunsAndTargetBookmarksOnUndo()
    {
        var target = new Paragraph("Heading");
        target.BookmarkNames.AddRange(["chapter", "_Ref2"]);
        var host = new Paragraph("See ");
        var document = new TextDocument();
        document.Blocks.Add(target);
        document.Blocks.Add(host);
        var bus = new DocumentCommandBus(new TestContext(document));
        var field = Run.CrossReferenceFieldRun(
            new CrossReferenceField(CrossRefFieldKind.Ref, "_Ref3", CrossRefInsertAs.Text, true),
            "Heading");

        bus.Execute(new InsertCrossReferenceCommand(1, field, 0, "_Ref3"));
        target.BookmarkNames.Should().Equal("chapter", "_Ref2", "_Ref3");
        host.Runs.Should().Contain(field);

        bus.Undo().Should().BeTrue();
        target.BookmarkNames.Should().Equal("chapter", "_Ref2");
        host.PlainText.Should().Be("See ");

        bus.Redo().Should().BeTrue();
        target.BookmarkNames.Should().Equal("chapter", "_Ref2", "_Ref3");
        host.Runs.Should().Contain(field);
    }

    private sealed class TestContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
