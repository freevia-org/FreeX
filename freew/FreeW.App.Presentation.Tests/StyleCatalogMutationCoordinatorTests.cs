using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class StyleCatalogMutationCoordinatorTests
{
    [Fact]
    public void CreateAndApply_ValidatesTargetsAndUndoesCatalogAndParagraphChangesTogether()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Add(new Paragraph("Second"));
        document.Blocks.Add(Table.Create(1, 1));
        var bus = new DocumentCommandBus(new Context(document));

        var created = StyleCatalogMutationCoordinator.CreateAndApply(
            document,
            bus,
            new[] { -1, 0, 1, 2, 99 },
            "Body Note",
            "Normal",
            RunFormatting.Default with { Italic = true },
            ParagraphFormatting.Default,
            "Normal");

        document.Styles.Should().ContainKey(created.Id);
        ((Paragraph)document.Blocks[0]).StyleId.Should().Be(created.Id);
        ((Paragraph)document.Blocks[1]).StyleId.Should().Be(created.Id);
        document.Blocks[2].Should().BeOfType<Table>();

        bus.Undo().Should().BeTrue();
        document.Styles.Should().NotContainKey(created.Id);
        ((Paragraph)document.Blocks[0]).StyleId.Should().BeNull();
        ((Paragraph)document.Blocks[1]).StyleId.Should().BeNull();
        bus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void CreateAndApply_WhenCreationFails_ClosesUndoGroupWithoutMutation()
    {
        var document = TextDocument.CreateEmpty();
        var originalStyleIds = document.Styles.Keys.ToArray();
        var bus = new DocumentCommandBus(new Context(document));

        var action = () => StyleCatalogMutationCoordinator.CreateAndApply(
            document,
            bus,
            new[] { 0 },
            "  ",
            null,
            RunFormatting.Default,
            ParagraphFormatting.Default);

        action.Should().Throw<ArgumentException>();
        bus.IsUndoGroupOpen.Should().BeFalse();
        bus.CanUndo.Should().BeFalse();
        document.Styles.Keys.Should().Equal(originalStyleIds);
        ((Paragraph)document.Blocks[0]).StyleId.Should().BeNull();
    }

    [Fact]
    public void Modify_UpdatesCatalogAndUndoRestoresOriginalStyle()
    {
        var document = TextDocument.CreateEmpty();
        var bus = new DocumentCommandBus(new Context(document));
        var original = document.Styles["Normal"];
        var run = original.Run with { Bold = true };
        var paragraph = original.Paragraph with { SpaceAfterPt = 18 };

        var updated = StyleCatalogMutationCoordinator.Modify(
            document,
            bus,
            "Normal",
            run,
            paragraph,
            basedOnId: null,
            nextStyleId: null);

        updated.Should().NotBeNull();
        document.Styles["Normal"].Run.Bold.Should().BeTrue();
        document.Styles["Normal"].Paragraph.SpaceAfterPt.Should().Be(18);
        bus.Undo().Should().BeTrue();
        document.Styles["Normal"].Should().BeEquivalentTo(original);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Missing")]
    public void Modify_RejectsUnknownOrBlankStyleWithoutHistory(string styleId)
    {
        var document = TextDocument.CreateEmpty();
        var bus = new DocumentCommandBus(new Context(document));

        var updated = StyleCatalogMutationCoordinator.Modify(
            document,
            bus,
            styleId,
            RunFormatting.Default,
            ParagraphFormatting.Default,
            null,
            null);

        updated.Should().BeNull();
        bus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Delete_RejectsBuiltInAndUndoesCustomStyleDeletion()
    {
        var document = TextDocument.CreateEmpty();
        var custom = StyleManager.CreateStyle(
            document,
            "Custom",
            "Normal",
            RunFormatting.Default,
            ParagraphFormatting.Default);
        var bus = new DocumentCommandBus(new Context(document));

        StyleCatalogMutationCoordinator.Delete(document, bus, "Normal").Should().BeFalse();
        StyleCatalogMutationCoordinator.Delete(document, bus, custom.Id).Should().BeTrue();
        document.Styles.Should().NotContainKey(custom.Id);

        bus.Undo().Should().BeTrue();
        document.Styles.Should().ContainKey(custom.Id);
        bus.CanUndo.Should().BeFalse();
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }
}
