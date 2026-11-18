using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WorksheetContextMenuPlannerExhaustiveStateTests
{
    public static TheoryData<string, WorksheetContextMenuState> AllWorksheetStates
    {
        get
        {
            var states = new TheoryData<string, WorksheetContextMenuState>();
            for (var bits = 0; bits < 1 << 8; bits++)
            {
                states.Add(
                    $"state-{Convert.ToString(bits, 2).PadLeft(8, '0')}",
                    new WorksheetContextMenuState(
                        HasThreadedComment: IsSet(bits, 0),
                        IsThreadedCommentResolved: IsSet(bits, 1),
                        HasNote: IsSet(bits, 2),
                        HasHyperlink: IsSet(bits, 3),
                        HasAutoFilterHeaderTarget: IsSet(bits, 4),
                        HasDropdownTarget: IsSet(bits, 5),
                        HasPivotTableTarget: IsSet(bits, 6),
                        NoteIsShown: IsSet(bits, 7)));
            }

            return states;
        }
    }

    [Theory]
    [MemberData(nameof(AllWorksheetStates))]
    public void BuildCommands_EmitsStructurallyActionablePlanForEveryWorksheetState(
        string caseId,
        WorksheetContextMenuState state)
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(state: state);

        commands.Should().NotBeEmpty($"{caseId} must produce a worksheet menu");
        AssertValidNodes(commands, caseId);

        var flattened = Flatten(commands).Where(command => !command.IsSeparator).ToArray();
        var actions = flattened.Where(command => command.Action != WorksheetContextMenuAction.None).ToArray();

        var expectedCommentAction = state.IsThreadedCommentResolved
            ? WorksheetContextMenuAction.UnresolveComment
            : WorksheetContextMenuAction.ResolveComment;
        actions.Should().ContainSingle(command => command.Action == expectedCommentAction,
            $"{caseId} should expose exactly the resolved-state comment action");
        actions.Should().NotContain(command => command.Action == (state.IsThreadedCommentResolved
            ? WorksheetContextMenuAction.ResolveComment
            : WorksheetContextMenuAction.UnresolveComment),
            $"{caseId} should not expose the opposite resolved-state comment action");
        actions.Single(command => command.Action == expectedCommentAction).IsEnabled
            .Should().Be(state.HasThreadedComment, $"{caseId} comment availability follows HasThreadedComment");

        var noteToggle = actions.Single(command => command.Action == WorksheetContextMenuAction.ShowHideNote);
        noteToggle.Header.Should().Be(state.NoteIsShown ? "Hide Note" : "Show Note", caseId);
        noteToggle.AccessHeader.Should().NotBeNullOrWhiteSpace($"{caseId} note toggle access text");
        noteToggle.IsEnabled.Should().Be(state.HasNote, $"{caseId} note toggle availability follows HasNote");

        actions.Single(command => command.Action == WorksheetContextMenuAction.ClearFilter).IsEnabled
            .Should().Be(state.HasAutoFilterHeaderTarget, $"{caseId} clear-filter availability follows its target");
        actions.Single(command => command.Action == WorksheetContextMenuAction.ReapplyFilter).IsEnabled
            .Should().Be(state.HasAutoFilterHeaderTarget, $"{caseId} reapply-filter availability follows its target");
        actions.Where(command => command.Action is WorksheetContextMenuAction.EditComment
                or WorksheetContextMenuAction.DeleteComment)
            .Should().OnlyContain(command => command.IsEnabled == state.HasThreadedComment, caseId);
        actions.Where(command => command.Action is WorksheetContextMenuAction.EditNote
                or WorksheetContextMenuAction.DeleteNote)
            .Should().OnlyContain(command => command.IsEnabled == state.HasNote, caseId);

        actions.Where(command => command.Action is WorksheetContextMenuAction.OpenHyperlink
                or WorksheetContextMenuAction.RemoveHyperlinks)
            .Should().HaveCount(state.HasHyperlink ? 2 : 0, caseId);
        actions.Where(command => command.Action == WorksheetContextMenuAction.PivotTableOptions)
            .Should().HaveCount(state.HasPivotTableTarget ? 1 : 0, caseId);
    }

    private static bool IsSet(int bits, int axis) => (bits & (1 << axis)) != 0;

    private static IEnumerable<WorksheetContextMenuCommand> Flatten(
        IEnumerable<WorksheetContextMenuCommand> commands)
    {
        foreach (var command in commands)
        {
            yield return command;
            foreach (var child in Flatten(command.Children))
                yield return child;
        }
    }

    private static void AssertValidNodes(
        IEnumerable<WorksheetContextMenuCommand> commands,
        string caseId)
    {
        foreach (var command in commands)
        {
            if (command.IsSeparator)
            {
                command.Action.Should().Be(WorksheetContextMenuAction.None, caseId);
                command.Header.Should().BeEmpty(caseId);
                command.AccessHeader.Should().BeEmpty(caseId);
                command.Children.Should().BeEmpty(caseId);
                command.IsEnabled.Should().BeFalse(caseId);
                continue;
            }

            command.Header.Should().NotBeNullOrWhiteSpace(caseId);
            command.AccessHeader.Should().NotBeNullOrWhiteSpace(caseId);
            if (command.Children.Count > 0)
            {
                command.Action.Should().Be(WorksheetContextMenuAction.None,
                    $"{caseId}: submenu '{command.Header}' is a container, not an action");
                AssertValidNodes(command.Children, caseId);
            }
            else
            {
                command.Action.Should().NotBe(WorksheetContextMenuAction.None,
                    $"{caseId}: leaf '{command.Header}' must be actionable");
            }
        }
    }
}
