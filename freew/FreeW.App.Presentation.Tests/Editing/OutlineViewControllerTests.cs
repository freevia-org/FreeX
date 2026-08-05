using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests;

public sealed class OutlineViewControllerTests
{
    [Fact]
    public void Refresh_and_display_options_own_rows_filter_and_selection_state()
    {
        var document = Sample();
        var controller = CreateController(document);

        controller.Refresh();
        controller.SelectBlock(4).Should().BeTrue();
        controller.Refresh();

        controller.SelectedBlockIndex.Should().Be(4);
        controller.CurrentOutlineLevel.Should().Be(2);

        controller.SetShowLevel(1);

        controller.ShowLevel.Should().Be(1);
        controller.VisibleRows.Select(row => row.Text)
            .Should().Equal("My Title", "Chapter One", "Chapter Two");
        controller.SelectedBlockIndex.Should().BeNull("the selected Heading 2 is no longer visible");

        controller.SetShowLevel(OutlineViewModel.ShowAllLevels);
        controller.SelectBlock(1).Should().BeTrue();
        controller.SetFirstLineOnly(true);

        controller.FirstLineOnly.Should().BeTrue();
        controller.VisibleRows.Single(row => row.BlockIndex == 1).Text.Should().Be("intro line one");
        controller.SelectedBlockIndex.Should().Be(1);
        controller.CurrentOutlineLevel.Should().Be(-1, "body text uses the Body Text level selection");
    }

    [Fact]
    public void Apply_and_outline_level_refresh_the_selected_row_through_injected_operations()
    {
        var document = Sample();
        var appliedBlockIndex = -1;
        var controller = CreateController(document);

        controller.Refresh();
        controller.Apply(_ => appliedBlockIndex = 99).Should().BeFalse();
        appliedBlockIndex.Should().Be(-1);

        controller.SelectBlock(4).Should().BeTrue();
        controller.Apply(blockIndex =>
        {
            appliedBlockIndex = blockIndex;
            ((Paragraph)document.Blocks[blockIndex]).StyleId = "Heading1";
        }).Should().BeTrue();

        appliedBlockIndex.Should().Be(4);
        controller.SelectedBlockIndex.Should().Be(4);
        controller.CurrentOutlineLevel.Should().Be(1);
        controller.VisibleRows.Single(row => row.BlockIndex == 4).Level.Should().Be(1);

        controller.SetOutlineLevel(-1).Should().BeTrue();

        ((Paragraph)document.Blocks[4]).StyleId.Should().Be("Normal");
        controller.SelectedBlockIndex.Should().Be(4);
        controller.CurrentOutlineLevel.Should().Be(-1);
        controller.VisibleRows.Single(row => row.BlockIndex == 4).IsHeading.Should().BeFalse();
    }

    [Fact]
    public void Move_reselects_the_heading_at_its_new_block_index()
    {
        var document = Sample();
        var controller = CreateController(document);

        controller.Refresh();
        controller.Move(moveUp: true).Should().BeFalse();
        controller.SelectBlock(6).Should().BeTrue();

        controller.Move(moveUp: true).Should().BeTrue();

        controller.SelectedBlockIndex.Should().Be(2);
        controller.VisibleRows.Where(row => row.IsHeading).Select(row => row.Text)
            .Should().Equal("My Title", "Chapter Two", "Chapter One", "Section A");
        controller.VisibleRows.Single(row => row.BlockIndex == 2).Text.Should().Be("Chapter Two");
    }

    [Fact]
    public void SelectBlock_rejects_hidden_or_unknown_rows_without_losing_the_current_selection()
    {
        var controller = CreateController(Sample());
        controller.Refresh();
        controller.SelectBlock(2).Should().BeTrue();

        controller.SelectBlock(99).Should().BeFalse();

        controller.SelectedBlockIndex.Should().Be(2);
        controller.ClearSelection();
        controller.SelectedBlockIndex.Should().BeNull();
        controller.CurrentOutlineLevel.Should().Be(-1);
    }

    private static OutlineViewController CreateController(TextDocument document) =>
        new(
            () => document,
            (blockIndex, level) => SetHeadingLevel(document, blockIndex, level),
            (blockIndex, moveUp) => MoveHeading(document, blockIndex, moveUp));

    private static void SetHeadingLevel(TextDocument document, int blockIndex, int level)
    {
        ((Paragraph)document.Blocks[blockIndex]).StyleId = level switch
        {
            < 0 => "Normal",
            0 => "Title",
            _ => $"Heading{Math.Min(level, OutlineTools.MaxHeadingLevel)}",
        };
    }

    private static int MoveHeading(TextDocument document, int blockIndex, bool moveUp)
    {
        var heading = document.Blocks[blockIndex];
        var reordered = OutlineTools.MoveSubtree(document.Blocks, blockIndex, moveUp);
        if (ReferenceEquals(reordered, document.Blocks))
            return blockIndex;

        document.Blocks.Clear();
        document.Blocks.AddRange(reordered);
        return document.Blocks.IndexOf(heading);
    }

    private static TextDocument Sample()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(Heading(0, "My Title"));
        document.Blocks.Add(new Paragraph("intro line one\nintro line two"));
        document.Blocks.Add(Heading(1, "Chapter One"));
        document.Blocks.Add(new Paragraph("one body"));
        document.Blocks.Add(Heading(2, "Section A"));
        document.Blocks.Add(new Paragraph("section body"));
        document.Blocks.Add(Heading(1, "Chapter Two"));
        return document;
    }

    private static Paragraph Heading(int level, string text) =>
        new(text) { StyleId = level == 0 ? "Title" : $"Heading{level}" };
}
