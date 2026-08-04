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
        document.Properties.Category = "Before category";
        document.Properties.LastModifiedBy = "Word Owner";
        var view = new DocumentView();
        view.LoadDocument(document);

        view.ApplyDocumentProperties(new DocumentPropertiesDialogValues(
            "After",
            "Ada",
            "Parity",
            "metadata",
            null,
            "Reports",
            "Final",
            "en-GB",
            "4.2"));

        view.Document.Properties.Title.Should().Be("After");
        view.Document.Properties.Author.Should().Be("Ada");
        view.Document.Properties.Category.Should().Be("Reports");
        view.Document.Properties.ContentStatus.Should().Be("Final");
        view.Document.Properties.Language.Should().Be("en-GB");
        view.Document.Properties.Version.Should().Be("4.2");
        view.Document.Properties.LastModifiedBy.Should().Be("Word Owner");
        view.CanUndo.Should().BeTrue();

        view.Undo();
        view.Document.Properties.Title.Should().Be("Before");
        view.Document.Properties.Author.Should().BeNull();
        view.Document.Properties.Category.Should().Be("Before category");
        view.Document.Properties.ContentStatus.Should().BeNull();
        view.Document.Properties.LastModifiedBy.Should().Be("Word Owner");

        view.Redo();
        view.Document.Properties.Title.Should().Be("After");
        view.Document.Properties.Author.Should().Be("Ada");
        view.Document.Properties.Category.Should().Be("Reports");
        view.Document.Properties.ContentStatus.Should().Be("Final");
    }
}
