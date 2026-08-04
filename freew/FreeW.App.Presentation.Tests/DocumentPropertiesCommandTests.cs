using FluentAssertions;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentPropertiesCommandTests
{
    [Fact]
    public void Apply_undo_and_redo_edit_all_dialog_properties_but_preserve_read_only_details()
    {
        var document = TextDocument.CreateEmpty();
        document.Properties.Title = "Before";
        document.Properties.Author = "Original author";
        document.Properties.Category = "Before category";
        document.Properties.LastModifiedBy = "Word Owner";
        document.Properties.Created = new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);
        var bus = new DocumentCommandBus(new CommandContext(document));
        var values = DocumentPropertiesDialogValues.FromInput(
            "  After  ",
            "Ada",
            "Parity",
            " freew metadata ",
            "   ",
            " Reports ",
            " Final ",
            " en-GB ",
            " 4.2 ");

        bus.Execute(new ApplyDocumentPropertiesCommand(values));

        document.Properties.Title.Should().Be("After");
        document.Properties.Author.Should().Be("Ada");
        document.Properties.Subject.Should().Be("Parity");
        document.Properties.Keywords.Should().Be("freew metadata");
        document.Properties.Comments.Should().BeNull();
        document.Properties.Category.Should().Be("Reports");
        document.Properties.ContentStatus.Should().Be("Final");
        document.Properties.Language.Should().Be("en-GB");
        document.Properties.Version.Should().Be("4.2");
        document.Properties.LastModifiedBy.Should().Be("Word Owner");
        document.Properties.Created.Should().Be(new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero));

        bus.Undo().Should().BeTrue();
        document.Properties.Title.Should().Be("Before");
        document.Properties.Author.Should().Be("Original author");
        document.Properties.Subject.Should().BeNull();
        document.Properties.Keywords.Should().BeNull();
        document.Properties.Category.Should().Be("Before category");
        document.Properties.ContentStatus.Should().BeNull();
        document.Properties.Language.Should().BeNull();
        document.Properties.Version.Should().BeNull();
        document.Properties.LastModifiedBy.Should().Be("Word Owner");

        bus.Redo().Should().BeTrue();
        document.Properties.Title.Should().Be("After");
        document.Properties.Author.Should().Be("Ada");
        document.Properties.Category.Should().Be("Reports");
        document.Properties.ContentStatus.Should().Be("Final");
        document.Properties.Language.Should().Be("en-GB");
        document.Properties.Version.Should().Be("4.2");
        document.Properties.LastModifiedBy.Should().Be("Word Owner");
    }

    private sealed class CommandContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }
}
