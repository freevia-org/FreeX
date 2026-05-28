using FluentAssertions;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class WorksheetContextMenuPlannerTests
{
    [Fact]
    public void UiTestCatalog_WorksheetContextMenuCommandCountMatchesPlanner()
    {
        var catalog = File.ReadAllText(WorkspaceFileLocator.Find("docs", "UI_TEST_CATALOG.md"));
        var commandCount = WorksheetContextMenuPlanner.BuildCommands()
            .Count(command => !command.IsSeparator);

        catalog.Should().Contain(
            $"| Worksheet context menu commands | {commandCount} | From `WorksheetContextMenuPlanner.BuildCommands()`. |");
        catalog.Should().Contain($"Worksheet context menu has {commandCount} planner commands");
        catalog.Should().Contain($"| Worksheet context menu | {commandCount} planner commands via right-click, Shift+F10, Menu key. |");
        catalog.Should().Contain($"| UI-CAT-CONTEXT-001 | Worksheet context menu | {commandCount} worksheet context-menu planner commands. |");
        catalog.Should().NotContain("47 planner commands");
        catalog.Should().NotContain("47 worksheet context-menu planner commands");
    }

    [Fact]
    public void BuildCommands_IncludesCommonExcelWorksheetContextActions()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands();

        commands.Select(command => command.Header).Should().ContainInOrder(
            "Cut",
            "Copy",
            "Paste",
            "Paste Special...",
            "Insert Copied Cells...",
            "Insert...",
            "Insert Row Above",
            "Delete...",
            "Delete Row(s)",
            "Sort A to Z",
            "Custom Sort...",
            "Filter...",
            "Clear Filter",
            "Reapply Filter",
            "Pick From Drop-down List...",
            "Quick Analysis",
            "Define Name...",
            "Create Table...",
            "Format as Table...",
            "Text to Columns...",
            "Remove Duplicates...",
            "Data Validation...",
            "Hide Rows",
            "Unhide Rows",
            "Row Height...",
            "AutoFit Row Height",
            "Hide Columns",
            "Unhide Columns",
            "Column Width...",
            "AutoFit Column Width",
            "New Comment",
            "Edit Comment...",
            "Resolve Comment",
            "Delete Comment",
            "New Note",
            "Edit Note...",
            "Delete Note",
            "Show Notes",
            "Hyperlink...",
            "Format Cells...",
            "Clear All",
            "Clear Formats",
            "Clear Comments",
            "Clear Hyperlinks",
            "Clear Contents");

        commands.Single(command => command.Header == "Clear Filter")
            .Action.Should().Be(WorksheetContextMenuAction.ClearFilter);
        commands.Single(command => command.Header == "Custom Sort...")
            .Action.Should().Be(WorksheetContextMenuAction.CustomSort);
        commands.Single(command => command.Header == "Reapply Filter")
            .Action.Should().Be(WorksheetContextMenuAction.ReapplyFilter);
        commands.Single(command => command.Header == "Pick From Drop-down List...")
            .Action.Should().Be(WorksheetContextMenuAction.PickFromDropDown);
        commands.Single(command => command.Header == "Quick Analysis")
            .Action.Should().Be(WorksheetContextMenuAction.QuickAnalysis);
        commands.Single(command => command.Header == "Insert Copied Cells...")
            .Action.Should().Be(WorksheetContextMenuAction.InsertCopiedCells);
        commands.Single(command => command.Header == "Define Name...")
            .Action.Should().Be(WorksheetContextMenuAction.DefineName);
        commands.Single(command => command.Header == "Create Table...")
            .Action.Should().Be(WorksheetContextMenuAction.CreateTable);
        commands.Single(command => command.Header == "Format as Table...")
            .Action.Should().Be(WorksheetContextMenuAction.FormatAsTable);
        commands.Single(command => command.Header == "Text to Columns...")
            .Action.Should().Be(WorksheetContextMenuAction.TextToColumns);
        commands.Single(command => command.Header == "Remove Duplicates...")
            .Action.Should().Be(WorksheetContextMenuAction.RemoveDuplicates);
        commands.Single(command => command.Header == "Data Validation...")
            .Action.Should().Be(WorksheetContextMenuAction.DataValidation);
        commands.Single(command => command.Header == "Row Height...")
            .Action.Should().Be(WorksheetContextMenuAction.RowHeight);
        commands.Single(command => command.Header == "AutoFit Row Height")
            .Action.Should().Be(WorksheetContextMenuAction.AutoFitRowHeight);
        commands.Single(command => command.Header == "Column Width...")
            .Action.Should().Be(WorksheetContextMenuAction.ColumnWidth);
        commands.Single(command => command.Header == "AutoFit Column Width")
            .Action.Should().Be(WorksheetContextMenuAction.AutoFitColumnWidth);
        commands.Single(command => command.Header == "Clear All")
            .Action.Should().Be(WorksheetContextMenuAction.ClearAll);
        commands.Single(command => command.Header == "Clear Comments")
            .Action.Should().Be(WorksheetContextMenuAction.ClearComments);
        commands.Single(command => command.Header == "New Comment")
            .Action.Should().Be(WorksheetContextMenuAction.NewComment);
        commands.Single(command => command.Header == "Edit Comment...")
            .Action.Should().Be(WorksheetContextMenuAction.EditComment);
        commands.Single(command => command.Header == "Resolve Comment")
            .Action.Should().Be(WorksheetContextMenuAction.ResolveComment);
        commands.Single(command => command.Header == "Delete Comment")
            .Action.Should().Be(WorksheetContextMenuAction.DeleteComment);
        commands.Single(command => command.Header == "Edit Note...")
            .Action.Should().Be(WorksheetContextMenuAction.EditNote);
        commands.Single(command => command.Header == "Show Notes")
            .Action.Should().Be(WorksheetContextMenuAction.ShowNotes);
    }

    [Fact]
    public void BuildCommands_ExposesInsertDeleteGroupInExcelLikeOrder()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands()
            .Where(command => !command.IsSeparator)
            .ToList();

        commands.Select(command => command.Header).Should().ContainInOrder(
            "Insert...",
            "Insert Row Above",
            "Insert Row Below",
            "Insert Column Left",
            "Insert Column Right",
            "Delete...",
            "Delete Row(s)",
            "Delete Column(s)");

        commands.Single(command => command.Header == "Insert...")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Insert...",
                WorksheetContextMenuAction.InsertCells,
                AccessHeader: "_Insert..."));
        commands.Single(command => command.Header == "Insert Row Above")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Insert Row Above",
                WorksheetContextMenuAction.InsertRowAbove,
                AccessHeader: "Insert Row _Above"));
        commands.Single(command => command.Header == "Insert Row Below")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Insert Row Below",
                WorksheetContextMenuAction.InsertRowBelow,
                AccessHeader: "Insert Row _Below"));
        commands.Single(command => command.Header == "Insert Column Left")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Insert Column Left",
                WorksheetContextMenuAction.InsertColumnLeft,
                AccessHeader: "Insert Column _Left"));
        commands.Single(command => command.Header == "Insert Column Right")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Insert Column Right",
                WorksheetContextMenuAction.InsertColumnRight,
                AccessHeader: "Insert Column _Right"));
        commands.Single(command => command.Header == "Delete...")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Delete...",
                WorksheetContextMenuAction.DeleteCells,
                AccessHeader: "_Delete..."));
        commands.Single(command => command.Header == "Delete Row(s)")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Delete Row(s)",
                WorksheetContextMenuAction.DeleteRows,
                AccessHeader: "Delete _Row(s)"));
        commands.Single(command => command.Header == "Delete Column(s)")
            .Should().BeEquivalentTo(new WorksheetContextMenuCommand(
                "Delete Column(s)",
                WorksheetContextMenuAction.DeleteColumns,
                AccessHeader: "Delete _Column(s)"));
    }

    [Theory]
    [InlineData("Cut", "Cu_t")]
    [InlineData("Copy", "_Copy")]
    [InlineData("Paste", "_Paste")]
    [InlineData("Paste Special...", "Paste _Special...")]
    [InlineData("Insert Copied Cells...", "Insert Copied _Cells...")]
    [InlineData("Insert...", "_Insert...")]
    [InlineData("Insert Row Above", "Insert Row _Above")]
    [InlineData("Insert Row Below", "Insert Row _Below")]
    [InlineData("Insert Column Left", "Insert Column _Left")]
    [InlineData("Insert Column Right", "Insert Column _Right")]
    [InlineData("Delete...", "_Delete...")]
    [InlineData("Delete Row(s)", "Delete _Row(s)")]
    [InlineData("Delete Column(s)", "Delete _Column(s)")]
    [InlineData("Quick Analysis", "_Quick Analysis")]
    [InlineData("Hide Rows", "_Hide Rows")]
    [InlineData("Unhide Rows", "Unhide Ro_ws")]
    [InlineData("Row Height...", "Row _Height...")]
    [InlineData("AutoFit Row Height", "AutoFit Row He_ight")]
    [InlineData("Hide Columns", "Hide Col_umns")]
    [InlineData("Unhide Columns", "Unhide Co_lumns")]
    [InlineData("Column Width...", "Column _Width...")]
    [InlineData("AutoFit Column Width", "AutoFit Column Wi_dth")]
    [InlineData("Edit Comment...", "_Edit Comment...")]
    [InlineData("Resolve Comment", "Resol_ve Comment")]
    [InlineData("Delete Comment", "Delete _Comment")]
    [InlineData("Format Cells...", "_Format Cells...")]
    [InlineData("Clear Contents", "Clear C_ontents")]
    public void BuildCommands_ProvidesKeyboardAccessHeaders(string header, string expectedAccessHeader)
    {
        var command = WorksheetContextMenuPlanner.BuildCommands()
            .Single(command => command.Header == header);

        command.AccessHeader.Should().Be(expectedAccessHeader);
    }

    [Fact]
    public void BuildCommands_DisablesTargetSpecificEntriesWhenCellHasNoMatchingMetadata()
    {
        var state = new WorksheetContextMenuState(
            HasThreadedComment: false,
            HasNote: false,
            HasHyperlink: false);

        var commands = WorksheetContextMenuPlanner.BuildCommands(state: state);

        commands.Single(command => command.Action == WorksheetContextMenuAction.EditComment).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.ResolveComment).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.DeleteComment).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.EditNote).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.DeleteNote).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.ShowNotes).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.ClearHyperlinks).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.ClearFilter).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.ReapplyFilter).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.PickFromDropDown).IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void BuildCommands_EnablesTargetSpecificEntriesWhenCellHasMatchingMetadata()
    {
        var state = new WorksheetContextMenuState(
            HasThreadedComment: true,
            HasNote: true,
            HasHyperlink: true);

        var commands = WorksheetContextMenuPlanner.BuildCommands(state: state);

        commands.Single(command => command.Action == WorksheetContextMenuAction.EditComment).IsEnabled.Should().BeTrue();
        commands.Single(command => command.Action == WorksheetContextMenuAction.ResolveComment).IsEnabled.Should().BeTrue();
        commands.Single(command => command.Action == WorksheetContextMenuAction.DeleteComment).IsEnabled.Should().BeTrue();
        commands.Single(command => command.Action == WorksheetContextMenuAction.EditNote).IsEnabled.Should().BeTrue();
        commands.Single(command => command.Action == WorksheetContextMenuAction.DeleteNote).IsEnabled.Should().BeTrue();
        commands.Single(command => command.Action == WorksheetContextMenuAction.ShowNotes).IsEnabled.Should().BeTrue();
        commands.Single(command => command.Header == "Clear Hyperlinks").IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void BuildCommands_EnablesFilterContextEntriesOnlyForFilterOrDropdownTargets()
    {
        var filterHeaderCommands = WorksheetContextMenuPlanner.BuildCommands(
            state: new WorksheetContextMenuState(
                HasAutoFilterHeaderTarget: true,
                HasDropdownTarget: true));

        filterHeaderCommands.Single(command => command.Action == WorksheetContextMenuAction.ClearFilter).IsEnabled.Should().BeTrue();
        filterHeaderCommands.Single(command => command.Action == WorksheetContextMenuAction.ReapplyFilter).IsEnabled.Should().BeTrue();
        filterHeaderCommands.Single(command => command.Action == WorksheetContextMenuAction.PickFromDropDown).IsEnabled.Should().BeTrue();

        var validationDropdownCommands = WorksheetContextMenuPlanner.BuildCommands(
            state: new WorksheetContextMenuState(HasDropdownTarget: true));

        validationDropdownCommands.Single(command => command.Action == WorksheetContextMenuAction.ClearFilter).IsEnabled.Should().BeFalse();
        validationDropdownCommands.Single(command => command.Action == WorksheetContextMenuAction.ReapplyFilter).IsEnabled.Should().BeFalse();
        validationDropdownCommands.Single(command => command.Action == WorksheetContextMenuAction.PickFromDropDown).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void BuildCommands_ShowsUnresolveCommentForResolvedThreadedComment()
    {
        var state = new WorksheetContextMenuState(
            HasThreadedComment: true,
            IsThreadedCommentResolved: true);

        var commands = WorksheetContextMenuPlanner.BuildCommands(state: state);

        commands.Select(command => command.Header).Should().ContainInOrder(
            "Edit Comment...",
            "Unresolve Comment",
            "Delete Comment");
        commands.Single(command => command.Header == "Unresolve Comment").Should().BeEquivalentTo(
            new WorksheetContextMenuCommand(
                "Unresolve Comment",
                WorksheetContextMenuAction.UnresolveComment,
                AccessHeader: "Un_resolve Comment"));
        commands.Select(command => command.Header).Should().NotContain("Resolve Comment");
    }

    [Fact]
    public void BuildCommands_UsesExcelLikeHyperlinkStateCommands()
    {
        var withoutLink = WorksheetContextMenuPlanner.BuildCommands(
            state: new WorksheetContextMenuState(HasHyperlink: false));
        withoutLink.Select(command => command.Header).Should().Contain("Hyperlink...");
        withoutLink.Select(command => command.Header).Should().NotContain(["Open Hyperlink", "Edit Hyperlink...", "Remove Hyperlink"]);

        var withLink = WorksheetContextMenuPlanner.BuildCommands(
            state: new WorksheetContextMenuState(HasHyperlink: true));

        withLink.Select(command => command.Header).Should().ContainInOrder(
            "Open Hyperlink",
            "Edit Hyperlink...",
            "Remove Hyperlink",
            "Format Cells...");
        withLink.Select(command => command.Header).Should().NotContain("Hyperlink...");
        withLink.Single(command => command.Header == "Open Hyperlink")
            .Action.Should().Be(WorksheetContextMenuAction.OpenHyperlink);
        withLink.Single(command => command.Header == "Edit Hyperlink...")
            .Action.Should().Be(WorksheetContextMenuAction.Hyperlink);
        withLink.Single(command => command.Header == "Remove Hyperlink")
            .Action.Should().Be(WorksheetContextMenuAction.RemoveHyperlinks);
    }

    [Fact]
    public void BuildCommands_ForPictureTargetIncludesExcelObjectCommands()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.Picture);

        commands.Select(command => command.Header).Should().ContainInOrder(
            "Format Picture...",
            "Crop...",
            "Reset Crop",
            "Edit Alt Text...",
            "Selection Pane...");
        commands.Single(command => command.Header == "Format Picture...")
            .Action.Should().Be(WorksheetContextMenuAction.FormatPicture);
        commands.Single(command => command.Header == "Edit Alt Text...")
            .Action.Should().Be(WorksheetContextMenuAction.EditAltText);
    }

    [Theory]
    [InlineData(WorksheetContextMenuTargetKind.Shape, "Format Shape...", true)]
    [InlineData(WorksheetContextMenuTargetKind.TextBox, "Format Text Box...", false)]
    public void BuildCommands_ForDrawingObjectTargetsIncludesExcelObjectCommands(
        WorksheetContextMenuTargetKind targetKind,
        string formatHeader,
        bool includesReorder)
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(targetKind);

        commands.Select(command => command.Header).Should().ContainInOrder(
            formatHeader,
            "Size and Properties...",
            "Rotate...",
            "Shape Fill...",
            "Shape Outline...",
            "Edit Alt Text...",
            "Selection Pane...");
        if (includesReorder)
        {
            commands.Select(command => command.Header).Should().ContainInOrder(
                "Bring Forward",
                "Send Backward");
        }

        commands.Single(command => command.Header == formatHeader)
            .Action.Should().Be(WorksheetContextMenuAction.FormatDrawingObject);
        commands.Single(command => command.Header == formatHeader)
            .AccessHeader.Should().Be($"_Format {formatHeader["Format ".Length..]}");
    }

    [Fact]
    public void BuildCommands_ForWholeRowSelectionIncludesOnlyRowLayoutCommands()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.RowSelection);

        commands.Select(command => command.Header).Should().ContainInOrder(
            "Cut",
            "Copy",
            "Paste",
            "Insert Row Above",
            "Delete Row(s)",
            "Row Height...",
            "AutoFit Row Height",
            "Hide Rows",
            "Unhide Rows",
            "Group",
            "Ungroup",
            "Format Cells...",
            "Clear Contents");
        commands.Single(command => command.Header == "Group").Should().BeEquivalentTo(
            new WorksheetContextMenuCommand(
                "Group",
                WorksheetContextMenuAction.Group,
                AccessHeader: "_Group"));
        commands.Single(command => command.Header == "Ungroup").Should().BeEquivalentTo(
            new WorksheetContextMenuCommand(
                "Ungroup",
                WorksheetContextMenuAction.Ungroup,
                AccessHeader: "_Ungroup"));
        commands.Single(command => command.Header == "Format Cells...").Should().BeEquivalentTo(
            new WorksheetContextMenuCommand(
                "Format Cells...",
                WorksheetContextMenuAction.FormatCells,
                AccessHeader: "_Format Cells..."));
        commands.Select(command => command.Header).Should().NotContain([
            "Insert Column Left",
            "Delete Column(s)",
            "Column Width...",
            "AutoFit Column Width",
            "Hide Columns",
            "Unhide Columns"
        ]);
    }

    [Fact]
    public void BuildCommands_ForWholeColumnSelectionIncludesOnlyColumnLayoutCommands()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.ColumnSelection);

        commands.Select(command => command.Header).Should().ContainInOrder(
            "Cut",
            "Copy",
            "Paste",
            "Insert Column Left",
            "Delete Column(s)",
            "Column Width...",
            "AutoFit Column Width",
            "Hide Columns",
            "Unhide Columns",
            "Group",
            "Ungroup",
            "Format Cells...",
            "Clear Contents");
        commands.Single(command => command.Header == "Group").Should().BeEquivalentTo(
            new WorksheetContextMenuCommand(
                "Group",
                WorksheetContextMenuAction.Group,
                AccessHeader: "_Group"));
        commands.Single(command => command.Header == "Ungroup").Should().BeEquivalentTo(
            new WorksheetContextMenuCommand(
                "Ungroup",
                WorksheetContextMenuAction.Ungroup,
                AccessHeader: "_Ungroup"));
        commands.Single(command => command.Header == "Format Cells...").Should().BeEquivalentTo(
            new WorksheetContextMenuCommand(
                "Format Cells...",
                WorksheetContextMenuAction.FormatCells,
                AccessHeader: "_Format Cells..."));
        commands.Select(command => command.Header).Should().NotContain([
            "Insert Row Above",
            "Delete Row(s)",
            "Row Height...",
            "AutoFit Row Height",
            "Hide Rows",
            "Unhide Rows"
        ]);
    }

    [Theory]
    [MemberData(nameof(RowColumnSizingVisibilityCases))]
    public void BuildCommands_RowAndColumnTargetsExposeSizingVisibilityMetadata(
        WorksheetContextMenuTargetKind targetKind,
        WorksheetContextMenuCommand[] expectedCommands)
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(targetKind)
            .Where(command => !command.IsSeparator)
            .ToList();

        commands.Select(command => command.Header).Should().ContainInOrder(
            expectedCommands.Select(command => command.Header));
        foreach (var expectedCommand in expectedCommands)
        {
            commands.Single(command => command.Header == expectedCommand.Header)
                .Should()
                .BeEquivalentTo(expectedCommand);
        }
    }

    [Theory]
    [MemberData(nameof(TargetSpecificCommandEnvelopeCases))]
    public void BuildCommands_TargetSpecificMenusExposeOnlyExpectedCommandFamilies(
        WorksheetContextMenuTargetKind targetKind,
        string[] expectedHeaders,
        string[] absentHeaders)
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(targetKind);

        commands.Select(command => command.Header)
            .Where(header => header.Length > 0)
            .Should()
            .Contain(expectedHeaders)
            .And.NotContain(absentHeaders);
    }

    public static TheoryData<WorksheetContextMenuTargetKind, string[], string[]> TargetSpecificCommandEnvelopeCases => new()
    {
        {
            WorksheetContextMenuTargetKind.Worksheet,
            [
                "Insert...",
                "Custom Sort...",
                "Quick Analysis",
                "Data Validation...",
                "New Comment",
                "Format Cells..."
            ],
            [
                "Format Picture...",
                "Format Shape...",
                "Format Text Box...",
                "Group",
                "Ungroup"
            ]
        },
        {
            WorksheetContextMenuTargetKind.RowSelection,
            [
                "Insert Row Above",
                "Delete Row(s)",
                "Row Height...",
                "AutoFit Row Height",
                "Group",
                "Ungroup"
            ],
            [
                "Insert...",
                "Data Validation...",
                "Column Width...",
                "Format Picture..."
            ]
        },
        {
            WorksheetContextMenuTargetKind.ColumnSelection,
            [
                "Insert Column Left",
                "Delete Column(s)",
                "Column Width...",
                "AutoFit Column Width",
                "Group",
                "Ungroup"
            ],
            [
                "Insert...",
                "Data Validation...",
                "Row Height...",
                "Format Picture..."
            ]
        },
        {
            WorksheetContextMenuTargetKind.Picture,
            [
                "Format Picture...",
                "Crop...",
                "Reset Crop",
                "Edit Alt Text...",
                "Selection Pane..."
            ],
            [
                "Insert...",
                "Format Cells...",
                "Format Shape...",
                "Group"
            ]
        },
        {
            WorksheetContextMenuTargetKind.Shape,
            [
                "Format Shape...",
                "Size and Properties...",
                "Rotate...",
                "Bring Forward",
                "Send Backward"
            ],
            [
                "Insert...",
                "Format Cells...",
                "Format Picture...",
                "Format Text Box..."
            ]
        },
        {
            WorksheetContextMenuTargetKind.TextBox,
            [
                "Format Text Box...",
                "Size and Properties...",
                "Rotate...",
                "Shape Fill...",
                "Shape Outline..."
            ],
            [
                "Insert...",
                "Format Cells...",
                "Format Picture...",
                "Bring Forward",
                "Send Backward"
            ]
        }
    };

    public static TheoryData<WorksheetContextMenuTargetKind, WorksheetContextMenuCommand[]> RowColumnSizingVisibilityCases => new()
    {
        {
            WorksheetContextMenuTargetKind.Worksheet,
            [
                new("Hide Rows", WorksheetContextMenuAction.HideRows, AccessHeader: "_Hide Rows"),
                new("Unhide Rows", WorksheetContextMenuAction.UnhideRows, AccessHeader: "Unhide Ro_ws"),
                new("Row Height...", WorksheetContextMenuAction.RowHeight, AccessHeader: "Row _Height..."),
                new("AutoFit Row Height", WorksheetContextMenuAction.AutoFitRowHeight, AccessHeader: "AutoFit Row He_ight"),
                new("Hide Columns", WorksheetContextMenuAction.HideColumns, AccessHeader: "Hide Col_umns"),
                new("Unhide Columns", WorksheetContextMenuAction.UnhideColumns, AccessHeader: "Unhide Co_lumns"),
                new("Column Width...", WorksheetContextMenuAction.ColumnWidth, AccessHeader: "Column _Width..."),
                new("AutoFit Column Width", WorksheetContextMenuAction.AutoFitColumnWidth, AccessHeader: "AutoFit Column Wi_dth")
            ]
        },
        {
            WorksheetContextMenuTargetKind.RowSelection,
            [
                new("Row Height...", WorksheetContextMenuAction.RowHeight, AccessHeader: "Row _Height..."),
                new("AutoFit Row Height", WorksheetContextMenuAction.AutoFitRowHeight, AccessHeader: "AutoFit Row He_ight"),
                new("Hide Rows", WorksheetContextMenuAction.HideRows, AccessHeader: "_Hide Rows"),
                new("Unhide Rows", WorksheetContextMenuAction.UnhideRows, AccessHeader: "Unhide Ro_ws")
            ]
        },
        {
            WorksheetContextMenuTargetKind.ColumnSelection,
            [
                new("Column Width...", WorksheetContextMenuAction.ColumnWidth, AccessHeader: "Column _Width..."),
                new("AutoFit Column Width", WorksheetContextMenuAction.AutoFitColumnWidth, AccessHeader: "AutoFit Column Wi_dth"),
                new("Hide Columns", WorksheetContextMenuAction.HideColumns, AccessHeader: "Hide Col_umns"),
                new("Unhide Columns", WorksheetContextMenuAction.UnhideColumns, AccessHeader: "Unhide Co_lumns")
            ]
        }
    };
}
