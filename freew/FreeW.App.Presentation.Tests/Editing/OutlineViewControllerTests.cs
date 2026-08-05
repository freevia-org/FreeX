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
    public void Outline_level_refreshes_the_selected_row_through_the_injected_editor_operation()
    {
        var document = Sample();
        var controller = CreateController(document);

        controller.Refresh();
        controller.SetOutlineLevel(1).Should().BeFalse();
        controller.SelectBlock(4).Should().BeTrue();

        controller.SetOutlineLevel(-1).Should().BeTrue();

        ((Paragraph)document.Blocks[4]).StyleId.Should().Be("Normal");
        controller.SelectedBlockIndex.Should().Be(4);
        controller.CurrentOutlineLevel.Should().Be(-1);
        controller.VisibleRows.Single(row => row.BlockIndex == 4).IsHeading.Should().BeFalse();
    }

    [Fact]
    public void Execute_routes_every_command_and_reselects_moved_headings()
    {
        var document = Sample();
        var invocations = new List<(OutlineCommand Command, int BlockIndex)>();
        var moves = new List<(int BlockIndex, bool MoveUp)>();
        var controller = new OutlineViewController(new OutlineViewOperations(
            getDocument: () => document,
            setHeadingLevel: (_, _) => { },
            moveHeading: (blockIndex, moveUp) =>
            {
                moves.Add((blockIndex, moveUp));
                return moveUp ? 2 : 6;
            },
            promoteToHeading1: blockIndex => invocations.Add((OutlineCommand.PromoteToHeading1, blockIndex)),
            promote: blockIndex => invocations.Add((OutlineCommand.Promote, blockIndex)),
            demote: blockIndex => invocations.Add((OutlineCommand.Demote, blockIndex)),
            expand: blockIndex => invocations.Add((OutlineCommand.Expand, blockIndex)),
            collapse: blockIndex => invocations.Add((OutlineCommand.Collapse, blockIndex)),
            isHeadingCollapsed: _ => false));

        controller.Refresh();
        controller.Execute(OutlineCommand.Promote).Should().BeFalse();
        foreach (var command in new[]
                 {
                     OutlineCommand.PromoteToHeading1,
                     OutlineCommand.Promote,
                     OutlineCommand.Demote,
                     OutlineCommand.Expand,
                     OutlineCommand.Collapse,
                 })
        {
            controller.SelectBlock(4).Should().BeTrue();
            controller.Execute(command).Should().BeTrue();
        }

        controller.SelectBlock(4).Should().BeTrue();
        controller.Execute(OutlineCommand.MoveUp).Should().BeTrue();
        controller.SelectedBlockIndex.Should().Be(2);
        controller.SelectBlock(4).Should().BeTrue();
        controller.Execute(OutlineCommand.MoveDown).Should().BeTrue();
        controller.SelectedBlockIndex.Should().Be(6);

        invocations.Should().Equal(
            (OutlineCommand.PromoteToHeading1, 4),
            (OutlineCommand.Promote, 4),
            (OutlineCommand.Demote, 4),
            (OutlineCommand.Expand, 4),
            (OutlineCommand.Collapse, 4));
        moves.Should().Equal((4, true), (4, false));
    }

    [Fact]
    public void Execute_move_reorders_the_document_and_tracks_the_new_block_index()
    {
        var document = Sample();
        var controller = CreateController(document);

        controller.Refresh();
        controller.SelectBlock(6).Should().BeTrue();

        controller.Execute(OutlineCommand.MoveUp).Should().BeTrue();

        controller.SelectedBlockIndex.Should().Be(2);
        controller.VisibleRows.Where(row => row.IsHeading).Select(row => row.Text)
            .Should().Equal("My Title", "Chapter Two", "Chapter One", "Section A");
        controller.VisibleRows.Single(row => row.BlockIndex == 2).Text.Should().Be("Chapter Two");
    }

    [Fact]
    public void SelectBlock_navigates_only_for_visible_rows_when_requested()
    {
        var document = Sample();
        var navigated = new List<int>();
        var controller = CreateController(document, navigated.Add);
        controller.Refresh();

        controller.SelectBlock(2).Should().BeTrue();
        navigated.Should().BeEmpty();
        controller.SelectBlock(4, navigate: true).Should().BeTrue();
        controller.SelectBlock(99, navigate: true).Should().BeFalse();

        navigated.Should().Equal(4);
        controller.SelectedBlockIndex.Should().Be(4);
        controller.ClearSelection();
        controller.SelectedBlockIndex.Should().BeNull();
        controller.CurrentOutlineLevel.Should().Be(-1);
    }

    [Fact]
    public void Refresh_and_collapse_commands_project_native_collapse_state_once()
    {
        var document = Sample();
        var collapsed = new HashSet<int>();
        var controller = new OutlineViewController(new OutlineViewOperations(
            getDocument: () => document,
            setHeadingLevel: (_, _) => { },
            moveHeading: (blockIndex, _) => blockIndex,
            promoteToHeading1: _ => { },
            promote: _ => { },
            demote: _ => { },
            expand: blockIndex => collapsed.Remove(blockIndex),
            collapse: blockIndex => collapsed.Add(blockIndex),
            isHeadingCollapsed: collapsed.Contains));

        controller.Refresh();
        controller.SelectBlock(2).Should().BeTrue();
        controller.Execute(OutlineCommand.Collapse).Should().BeTrue();
        controller.ProjectedRows.Single(row => row.Row.BlockIndex == 2).IsCollapsed.Should().BeTrue();

        controller.Execute(OutlineCommand.Expand).Should().BeTrue();
        controller.ProjectedRows.Single(row => row.Row.BlockIndex == 2).IsCollapsed.Should().BeFalse();
    }

    [Fact]
    public void Planner_owns_option_catalogs_command_order_and_grouping()
    {
        OutlineViewPlanner.ShowLevelOptions.Select(option => (option.Label, option.Level)).Should().Equal(
            ("All Levels", OutlineViewModel.ShowAllLevels),
            ("Level 1", 1),
            ("Level 2", 2),
            ("Level 3", 3),
            ("Level 4", 4),
            ("Level 5", 5),
            ("Level 6", 6),
            ("Level 7", 7),
            ("Level 8", 8),
            ("Level 9", 9));
        OutlineViewPlanner.OutlineLevelOptions.First().Should().Be(new OutlineLevelOption("Body Text", -1));
        OutlineViewPlanner.OutlineLevelOptions[1].Should().Be(new OutlineLevelOption("Title", 0));
        OutlineViewPlanner.OutlineLevelOptions.Last().Should()
            .Be(new OutlineLevelOption($"Level {OutlineTools.MaxHeadingLevel}", OutlineTools.MaxHeadingLevel));
        OutlineViewPlanner.OutlineLevelOptionIndex(2).Should().Be(3);
        OutlineViewPlanner.OutlineLevelOptionIndex(99).Should().Be(0);

        OutlineViewPlanner.CommandPlans.Select(plan => (plan.Command, plan.Label, plan.StartsGroup))
            .Should().Equal(
                (OutlineCommand.PromoteToHeading1, "Promote to Heading 1", false),
                (OutlineCommand.Promote, "Promote", false),
                (OutlineCommand.Demote, "Demote", false),
                (OutlineCommand.MoveUp, "Move Up", true),
                (OutlineCommand.MoveDown, "Move Down", false),
                (OutlineCommand.Expand, "Expand", true),
                (OutlineCommand.Collapse, "Collapse", false));
    }

    [Fact]
    public void Planner_formats_indent_markers_and_empty_heading_fallback()
    {
        var markers = new OutlineRowMarkers("open ", "closed ", "body ");

        OutlineViewPlanner.FormatRow(
                new OutlineProjectedRow(new OutlineRow(3, 2, string.Empty, IsHeading: true), IsCollapsed: true),
                markers)
            .Should().Be("        closed (untitled heading)");
        OutlineViewPlanner.FormatRow(
                new OutlineProjectedRow(new OutlineRow(4, 1, "copy", IsHeading: false), IsCollapsed: false),
                markers)
            .Should().Be("    body copy");
    }

    private static OutlineViewController CreateController(
        TextDocument document,
        Action<int>? navigateToBlock = null) =>
        new(new OutlineViewOperations(
            getDocument: () => document,
            setHeadingLevel: (blockIndex, level) => SetHeadingLevel(document, blockIndex, level),
            moveHeading: (blockIndex, moveUp) => MoveHeading(document, blockIndex, moveUp),
            promoteToHeading1: blockIndex => SetHeadingLevel(document, blockIndex, 1),
            promote: blockIndex => ShiftHeadingLevel(document, blockIndex, -1),
            demote: blockIndex => ShiftHeadingLevel(document, blockIndex, 1),
            expand: _ => { },
            collapse: _ => { },
            isHeadingCollapsed: _ => false,
            navigateToBlock: navigateToBlock));

    private static void SetHeadingLevel(TextDocument document, int blockIndex, int level)
    {
        ((Paragraph)document.Blocks[blockIndex]).StyleId = level switch
        {
            < 0 => "Normal",
            0 => "Title",
            _ => $"Heading{Math.Min(level, OutlineTools.MaxHeadingLevel)}",
        };
    }

    private static void ShiftHeadingLevel(TextDocument document, int blockIndex, int delta)
    {
        var paragraph = (Paragraph)document.Blocks[blockIndex];
        var level = DocumentOutline.TryGetLevel(paragraph.StyleId, out var currentLevel)
            ? currentLevel
            : OutlineTools.MaxHeadingLevel;
        SetHeadingLevel(document, blockIndex, Math.Clamp(level + delta, 1, OutlineTools.MaxHeadingLevel));
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
