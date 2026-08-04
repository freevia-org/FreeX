using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class DocumentPropertiesUndoTests
{
    [StaFact]
    public void ApplyDocumentProperties_is_one_undoable_operation()
    {
        var document = TextDocument.CreateEmpty();
        document.Properties.Title = "Before";
        document.Properties.Category = "Keep";
        var view = new DocumentView();
        view.LoadModel(document);

        view.ApplyDocumentProperties(new DocumentPropertiesDialogValues(
            "After",
            "Ada",
            "Parity",
            "metadata",
            null));

        view.Model.Properties.Title.Should().Be("After");
        view.Model.Properties.Author.Should().Be("Ada");
        view.Model.Properties.Category.Should().Be("Keep");
        view.CanUndo.Should().BeTrue();

        view.Undo();
        view.Model.Properties.Title.Should().Be("Before");
        view.Model.Properties.Author.Should().BeNull();
        view.Model.Properties.Category.Should().Be("Keep");

        view.Redo();
        view.Model.Properties.Title.Should().Be("After");
        view.Model.Properties.Author.Should().Be("Ada");
        view.Model.Properties.Category.Should().Be("Keep");
    }
}
