using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class ExcelEditKeyPlannerTests
{
    [Fact]
    public void GetIntent_MapsAltEnterToLineBreakInsertion()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(Key.Enter, ModifierKeys.Alt, Current, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.InsertLineBreak);
        intent.Target.Should().BeNull();
    }

    [Fact]
    public void GetIntent_MapsSystemAltEnterToLineBreakInsertion()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            Key.System,
            ModifierKeys.Alt,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            systemKey: Key.Enter);

        intent.Action.Should().Be(ExcelEditKeyAction.InsertLineBreak);
        intent.Target.Should().BeNull();
    }

    [Fact]
    public void GetIntent_MapsCtrlEnterToCommitSelection()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(Key.Enter, ModifierKeys.Control, Current, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitSelection);
        intent.Target.Should().BeNull();
    }

    [Fact]
    public void GetIntent_MapsSystemCtrlEnterToCommitSelection()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            Key.System,
            ModifierKeys.Control,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            systemKey: Key.Enter);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitSelection);
        intent.Target.Should().BeNull();
    }

    [Theory]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)]
    public void GetIntent_DoesNotTreatExtraModifiedEnterAsCommitSelection(ModifierKeys modifiers)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            Key.Enter,
            modifiers,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }

    [Theory]
    [InlineData(ModifierKeys.Shift | ModifierKeys.Alt)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)]
    public void GetIntent_DoesNotTreatExtraModifiedEnterAsLineBreakInsertion(ModifierKeys modifiers)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            Key.System,
            modifiers,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            systemKey: Key.Enter);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }

    [Fact]
    public void GetIntent_DoesNotTreatDirectAltShiftEnterAsLineBreakInsertion()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            Key.Enter,
            ModifierKeys.Alt | ModifierKeys.Shift,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }
}
