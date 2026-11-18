using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class SheetTabContextMenuPlannerExhaustiveStateTests
{
    public static TheoryData<string, SheetTabContextMenuState> AllCapabilityStates
    {
        get
        {
            var states = new TheoryData<string, SheetTabContextMenuState>();
            for (var bits = 0; bits < 1 << 8; bits++)
            {
                states.Add(
                    $"state-{Convert.ToString(bits, 2).PadLeft(8, '0')}",
                    new SheetTabContextMenuState(
                        CanDeleteSheet: IsSet(bits, 0),
                        CanHideSheet: IsSet(bits, 1),
                        CanUnhideSheet: IsSet(bits, 2),
                        CanSelectAllSheets: IsSet(bits, 3),
                        CanUngroupSheets: IsSet(bits, 4),
                        CanInsertSheet: IsSet(bits, 5),
                        CanRename: IsSet(bits, 6),
                        CanMoveOrCopy: IsSet(bits, 7)));
            }

            return states;
        }
    }

    [Theory]
    [MemberData(nameof(AllCapabilityStates))]
    public void BuildSheetTabCommands_PreservesMenuAndMatchesEveryCapabilityFlag(
        string caseId,
        SheetTabContextMenuState state)
    {
        var commands = SheetTabContextMenuPlanner.BuildSheetTabCommands(state);

        commands.Should().HaveCount(14, caseId);
        commands.Select((command, index) => (command, index))
            .Where(item => item.command.IsSeparator)
            .Select(item => item.index)
            .Should().Equal(4, 8, 11);

        foreach (var separator in commands.Where(command => command.IsSeparator))
        {
            separator.Action.Should().Be(SheetTabContextMenuAction.None, caseId);
            separator.ResourceKey.Should().BeEmpty(caseId);
            separator.KeyTip.Should().BeEmpty(caseId);
            separator.CommandName.Should().BeEmpty(caseId);
            separator.IsEnabled.Should().BeFalse(caseId);
        }

        var actionable = commands.Where(command => !command.IsSeparator).ToArray();
        actionable.Should().HaveCount(11, caseId);
        actionable.Select(command => command.Action).Should().OnlyHaveUniqueItems(caseId);
        actionable.Select(command => command.Action).Should().Equal(
            SheetTabContextMenuAction.InsertSheet,
            SheetTabContextMenuAction.DeleteSheet,
            SheetTabContextMenuAction.Rename,
            SheetTabContextMenuAction.MoveOrCopy,
            SheetTabContextMenuAction.ViewCode,
            SheetTabContextMenuAction.ProtectSheet,
            SheetTabContextMenuAction.TabColor,
            SheetTabContextMenuAction.Hide,
            SheetTabContextMenuAction.Unhide,
            SheetTabContextMenuAction.SelectAllSheets,
            SheetTabContextMenuAction.UngroupSheets);
        actionable.Should().OnlyContain(command =>
            !string.IsNullOrWhiteSpace(command.ResourceKey) &&
            !string.IsNullOrWhiteSpace(command.KeyTip) &&
            !string.IsNullOrWhiteSpace(command.CommandName), caseId);
        actionable.Select(command => command.KeyTip).Should().OnlyHaveUniqueItems(caseId);

        foreach (var command in actionable)
        {
            command.IsEnabled.Should().Be(ExpectedEnabled(command.Action, state),
                $"{caseId}: {command.Action} enablement should follow its owning capability");
        }
    }

    private static bool IsSet(int bits, int axis) => (bits & (1 << axis)) != 0;

    private static bool ExpectedEnabled(
        SheetTabContextMenuAction action,
        SheetTabContextMenuState state) =>
        action switch
        {
            SheetTabContextMenuAction.InsertSheet => state.CanInsertSheet,
            SheetTabContextMenuAction.DeleteSheet => state.CanDeleteSheet,
            SheetTabContextMenuAction.Rename => state.CanRename,
            SheetTabContextMenuAction.MoveOrCopy => state.CanMoveOrCopy,
            SheetTabContextMenuAction.ViewCode => false,
            SheetTabContextMenuAction.Hide => state.CanHideSheet,
            SheetTabContextMenuAction.Unhide => state.CanUnhideSheet,
            SheetTabContextMenuAction.SelectAllSheets => state.CanSelectAllSheets,
            SheetTabContextMenuAction.UngroupSheets => state.CanUngroupSheets,
            SheetTabContextMenuAction.ProtectSheet or SheetTabContextMenuAction.TabColor => true,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
}
