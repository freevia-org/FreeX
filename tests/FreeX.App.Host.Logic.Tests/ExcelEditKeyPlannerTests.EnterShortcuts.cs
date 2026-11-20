using Free.Shared.Shell;
using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class ExcelEditKeyPlannerTests
{
    [Fact]
    public void GetIntent_MapsAltEnterToLineBreakInsertion()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(FormulaEditorKey.Enter, FreeKeyModifiers.Alt, Current, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.InsertLineBreak);
        intent.Target.Should().BeNull();
    }

    [Fact]
    public void GetIntent_MapsSystemAltEnterToLineBreakInsertion()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            FormulaEditorKey.System,
            FreeKeyModifiers.Alt,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            systemKey: FormulaEditorKey.Enter);

        intent.Action.Should().Be(ExcelEditKeyAction.InsertLineBreak);
        intent.Target.Should().BeNull();
    }

    [Fact]
    public void GetIntent_MapsCtrlEnterToCommitSelection()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(FormulaEditorKey.Enter, FreeKeyModifiers.Control, Current, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitSelection);
        intent.Target.Should().BeNull();
    }

    [Fact]
    public void GetIntent_MapsSystemCtrlEnterToCommitSelection()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            FormulaEditorKey.System,
            FreeKeyModifiers.Control,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            systemKey: FormulaEditorKey.Enter);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitSelection);
        intent.Target.Should().BeNull();
    }

    [Theory]
    [InlineData(FreeKeyModifiers.Control | FreeKeyModifiers.Shift)]
    [InlineData(FreeKeyModifiers.Control | FreeKeyModifiers.Alt)]
    [InlineData(FreeKeyModifiers.Control | FreeKeyModifiers.Shift | FreeKeyModifiers.Alt)]
    public void GetIntent_DoesNotTreatExtraModifiedEnterAsCommitSelection(FreeKeyModifiers modifiers)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            FormulaEditorKey.Enter,
            modifiers,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }

    [Theory]
    [InlineData(FreeKeyModifiers.Shift | FreeKeyModifiers.Alt)]
    [InlineData(FreeKeyModifiers.Control | FreeKeyModifiers.Alt)]
    [InlineData(FreeKeyModifiers.Control | FreeKeyModifiers.Shift | FreeKeyModifiers.Alt)]
    public void GetIntent_DoesNotTreatExtraModifiedEnterAsLineBreakInsertion(FreeKeyModifiers modifiers)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            FormulaEditorKey.System,
            modifiers,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            systemKey: FormulaEditorKey.Enter);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }

    [Fact]
    public void GetIntent_DoesNotTreatDirectAltShiftEnterAsLineBreakInsertion()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            FormulaEditorKey.Enter,
            FreeKeyModifiers.Alt | FreeKeyModifiers.Shift,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }
}
