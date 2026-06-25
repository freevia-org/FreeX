using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class WorksheetContextMenuPlannerTests
{
    [Fact]
    public void BuildCommands_DisablesTargetSpecificEntriesWhenCellHasNoMatchingMetadata()
    {
        var state = new WorksheetContextMenuState(
            HasThreadedComment: false,
            HasNote: false,
            HasHyperlink: false);

        var commands = FlattenActionCommands(WorksheetContextMenuPlanner.BuildCommands(state: state));

        commands.Single(command => command.Action == WorksheetContextMenuAction.EditComment).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.ResolveComment).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.DeleteComment).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.EditNote).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.DeleteNote).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.ShowHideNote).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.ClearHyperlinks).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.ClearFilter).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.ReapplyFilter).IsEnabled.Should().BeFalse();
        commands.Single(command => command.Action == WorksheetContextMenuAction.PickFromDropDown).IsEnabled.Should().BeFalse();
        commands.Select(command => command.Action).Should().NotContain(WorksheetContextMenuAction.PivotTableOptions);
    }

    [Fact]
    public void BuildCommands_EnablesTargetSpecificEntriesWhenCellHasMatchingMetadata()
    {
        var state = new WorksheetContextMenuState(
            HasThreadedComment: true,
            HasNote: true,
            HasHyperlink: true);

        var commands = FlattenActionCommands(WorksheetContextMenuPlanner.BuildCommands(state: state));

        commands.Single(command => command.Action == WorksheetContextMenuAction.EditComment).IsEnabled.Should().BeTrue();
        commands.Single(command => command.Action == WorksheetContextMenuAction.ResolveComment).IsEnabled.Should().BeTrue();
        commands.Single(command => command.Action == WorksheetContextMenuAction.DeleteComment).IsEnabled.Should().BeTrue();
        commands.Single(command => command.Action == WorksheetContextMenuAction.EditNote).IsEnabled.Should().BeTrue();
        commands.Single(command => command.Action == WorksheetContextMenuAction.DeleteNote).IsEnabled.Should().BeTrue();
        commands.Single(command => command.Action == WorksheetContextMenuAction.ShowHideNote).IsEnabled.Should().BeTrue();
        commands.Single(command => command.Header == "Clear Hyperlinks").IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void BuildCommands_KeepsThreadedCommentAndNoteStateIndependent()
    {
        var threadedOnlyCommands = FlattenActionCommands(WorksheetContextMenuPlanner.BuildCommands(
            state: new WorksheetContextMenuState(HasThreadedComment: true)));

        threadedOnlyCommands.Single(command => command.Action == WorksheetContextMenuAction.EditComment).IsEnabled.Should().BeTrue();
        threadedOnlyCommands.Single(command => command.Action == WorksheetContextMenuAction.ResolveComment).IsEnabled.Should().BeTrue();
        threadedOnlyCommands.Single(command => command.Action == WorksheetContextMenuAction.DeleteComment).IsEnabled.Should().BeTrue();
        threadedOnlyCommands.Single(command => command.Action == WorksheetContextMenuAction.EditNote).IsEnabled.Should().BeFalse();
        threadedOnlyCommands.Single(command => command.Action == WorksheetContextMenuAction.DeleteNote).IsEnabled.Should().BeFalse();
        threadedOnlyCommands.Single(command => command.Action == WorksheetContextMenuAction.ShowHideNote).IsEnabled.Should().BeFalse();

        var noteOnlyCommands = FlattenActionCommands(WorksheetContextMenuPlanner.BuildCommands(
            state: new WorksheetContextMenuState(HasNote: true)));

        noteOnlyCommands.Single(command => command.Action == WorksheetContextMenuAction.EditComment).IsEnabled.Should().BeFalse();
        noteOnlyCommands.Single(command => command.Action == WorksheetContextMenuAction.ResolveComment).IsEnabled.Should().BeFalse();
        noteOnlyCommands.Single(command => command.Action == WorksheetContextMenuAction.DeleteComment).IsEnabled.Should().BeFalse();
        noteOnlyCommands.Single(command => command.Action == WorksheetContextMenuAction.EditNote).IsEnabled.Should().BeTrue();
        noteOnlyCommands.Single(command => command.Action == WorksheetContextMenuAction.DeleteNote).IsEnabled.Should().BeTrue();
        noteOnlyCommands.Single(command => command.Action == WorksheetContextMenuAction.ShowHideNote).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void BuildCommands_EnablesFilterContextEntriesOnlyForFilterOrDropdownTargets()
    {
        var filterHeaderCommands = FlattenActionCommands(WorksheetContextMenuPlanner.BuildCommands(
            state: new WorksheetContextMenuState(
                HasAutoFilterHeaderTarget: true,
                HasDropdownTarget: true)));

        filterHeaderCommands.Single(command => command.Action == WorksheetContextMenuAction.ClearFilter).IsEnabled.Should().BeTrue();
        filterHeaderCommands.Single(command => command.Action == WorksheetContextMenuAction.ReapplyFilter).IsEnabled.Should().BeTrue();
        filterHeaderCommands.Single(command => command.Action == WorksheetContextMenuAction.PickFromDropDown).IsEnabled.Should().BeTrue();

        var validationDropdownCommands = FlattenActionCommands(WorksheetContextMenuPlanner.BuildCommands(
            state: new WorksheetContextMenuState(HasDropdownTarget: true)));

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

        var commands = FlattenActionCommands(WorksheetContextMenuPlanner.BuildCommands(state: state));

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
        var withoutLink = FlattenActionCommands(WorksheetContextMenuPlanner.BuildCommands(
            state: new WorksheetContextMenuState(HasHyperlink: false)));
        withoutLink.Select(command => command.Header).Should().Contain("Hyperlink...");
        withoutLink.Select(command => command.Header).Should().NotContain(["Open Hyperlink", "Edit Hyperlink...", "Remove Hyperlink"]);

        var withLink = FlattenActionCommands(WorksheetContextMenuPlanner.BuildCommands(
            state: new WorksheetContextMenuState(HasHyperlink: true)));

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
    public void BuildCommands_ShowsPivotTableOptionsOnlyForPivotTableCells()
    {
        var normalCellCommands = FlattenActionCommands(WorksheetContextMenuPlanner.BuildCommands(
            state: new WorksheetContextMenuState(HasPivotTableTarget: false)));

        normalCellCommands.Select(command => command.Header)
            .Should()
            .NotContain("PivotTable Options...");

        var pivotCellCommands = FlattenActionCommands(WorksheetContextMenuPlanner.BuildCommands(
            state: new WorksheetContextMenuState(HasPivotTableTarget: true)));

        pivotCellCommands.Select(command => command.Header).Should().ContainInOrder(
            "Hyperlink...",
            "PivotTable Options...",
            "Format Cells...");
        pivotCellCommands.Single(command => command.Header == "PivotTable Options...")
            .Should()
            .BeEquivalentTo(new WorksheetContextMenuCommand(
                "PivotTable Options...",
                WorksheetContextMenuAction.PivotTableOptions,
                AccessHeader: "PivotTable _Options..."));
    }
}
