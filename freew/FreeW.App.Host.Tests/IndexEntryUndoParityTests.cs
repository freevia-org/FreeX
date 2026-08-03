using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class IndexEntryUndoParityTests
{
    [StaFact]
    public void MarkIndexEntry_IsUndoableAndIgnoresCaseInsensitiveDuplicate()
    {
        var document = TextDocument.CreateEmpty();
        var editor = new DocumentView();
        editor.LoadModel(document);

        editor.MarkIndexEntry("Alpha");
        editor.Model.IndexEntries.Select(entry => entry.Term).Should().Equal("Alpha");
        editor.Undo();
        editor.Model.IndexEntries.Should().BeEmpty();
        editor.Redo();
        editor.Model.IndexEntries.Select(entry => entry.Term).Should().Equal("Alpha");

        editor.MarkIndexEntry("alpha");
        editor.Model.IndexEntries.Select(entry => entry.Term).Should().Equal("Alpha");
    }
}
