using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class MultilevelListMutationCoordinatorTests
{
    [Fact]
    public void ApplyDefinition_ValidatesSelectionAndUndoesFormattingStylesAndNumberFormatsTogether()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Top")
        {
            Formatting = ParagraphFormatting.Default with { ListLevel = 0 }
        });
        document.Blocks.Add(Table.Create(1, 1));
        document.Blocks.Add(new Paragraph("Deep")
        {
            Formatting = ParagraphFormatting.Default with { ListLevel = 7 }
        });
        var originalFormats = document.MultiLevelList.NumberFormats.ToArray();
        var bus = new DocumentCommandBus(new Context(document));
        var formats = MultiLevelListFormat.DecimalNumberFormats.ToArray();
        formats[0] = ListNumberFormat.UpperRoman;
        formats[1] = ListNumberFormat.LowerLetter;
        var definition = new MultilevelListDefinition(
            Levels: 3,
            Level0StartAt: 4,
            Level1StartAt: 7,
            formats,
            LinkToHeadingStyles: true);

        var count = MultilevelListMutationCoordinator.ApplyDefinition(
            document,
            bus,
            new[] { -1, 0, 1, 2, 2, 99 },
            definition);

        count.Should().Be(2);
        var top = (Paragraph)document.Blocks[0];
        var deep = (Paragraph)document.Blocks[2];
        top.Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        top.Formatting.ListLevel.Should().Be(0);
        top.Formatting.ListStartOverride.Should().Be(4);
        top.StyleId.Should().Be("Heading1");
        deep.Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        deep.Formatting.ListLevel.Should().Be(2);
        deep.StyleId.Should().Be("Heading3");
        document.MultiLevelList.NumberFormats.Take(2)
            .Should().Equal(ListNumberFormat.UpperRoman, ListNumberFormat.LowerLetter);

        bus.Undo().Should().BeTrue();
        top.Formatting.ListKind.Should().Be(ListKind.None);
        top.Formatting.ListLevel.Should().Be(0);
        top.StyleId.Should().BeNull();
        deep.Formatting.ListKind.Should().Be(ListKind.None);
        deep.Formatting.ListLevel.Should().Be(7);
        deep.StyleId.Should().BeNull();
        document.MultiLevelList.NumberFormats.Should().Equal(originalFormats);
        bus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ApplyDefinition_WithNoParagraphTargets_DoesNotChangeCatalogOrHistory()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(Table.Create(1, 1));
        var bus = new DocumentCommandBus(new Context(document));
        var formats = MultiLevelListFormat.DecimalNumberFormats.ToArray();
        formats[0] = ListNumberFormat.UpperRoman;

        var count = MultilevelListMutationCoordinator.ApplyDefinition(
            document,
            bus,
            new[] { -1, 0, 1 },
            new MultilevelListDefinition(3, 2, 3, formats));

        count.Should().Be(0);
        document.MultiLevelList.NumberFormats.Should().Equal(MultiLevelListFormat.DecimalNumberFormats);
        bus.CanUndo.Should().BeFalse();
        bus.IsUndoGroupOpen.Should().BeFalse();
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }
}
