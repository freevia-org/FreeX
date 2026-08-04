using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentPropertiesUndoTests
{
    [Fact]
    public void ApplyDocumentProperties_is_one_undoable_operation()
    {
        var document = TextDocument.CreateEmpty();
        document.Properties.Title = "Before";
        document.Properties.Category = "Keep";
        var view = new DocumentView();
        view.LoadDocument(document);

        view.ApplyDocumentProperties(new DocumentPropertiesDialogValues(
            "After",
            "Ada",
            "Parity",
            "metadata",
            null));

        view.Document.Properties.Title.Should().Be("After");
        view.Document.Properties.Author.Should().Be("Ada");
        view.Document.Properties.Category.Should().Be("Keep");
        view.CanUndo.Should().BeTrue();

        view.Undo();
        view.Document.Properties.Title.Should().Be("Before");
        view.Document.Properties.Author.Should().BeNull();
        view.Document.Properties.Category.Should().Be("Keep");

        view.Redo();
        view.Document.Properties.Title.Should().Be("After");
        view.Document.Properties.Author.Should().Be("Ada");
        view.Document.Properties.Category.Should().Be("Keep");
    }
}
