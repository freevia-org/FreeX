namespace FreeW.Core.Model.Tests;

public sealed class IndexEntryCommandTests
{
    [Fact]
    public void AddIndexEntry_IsUniqueAndUndoable()
    {
        var document = new TextDocument();
        document.IndexEntries.Add(new IndexEntry("Existing"));
        var bus = new DocumentCommandBus(new TestContext(document));

        bus.Execute(new AddIndexEntryCommand("New term"));
        document.IndexEntries.Select(entry => entry.Term).Should().Equal("Existing", "New term");
        bus.Undo().Should().BeTrue();
        document.IndexEntries.Select(entry => entry.Term).Should().Equal("Existing");
        bus.Redo().Should().BeTrue();
        document.IndexEntries.Select(entry => entry.Term).Should().Equal("Existing", "New term");

        bus.Execute(new AddIndexEntryCommand("existing"));
        document.IndexEntries.Select(entry => entry.Term).Should().Equal("Existing", "New term");
    }

    private sealed class TestContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
