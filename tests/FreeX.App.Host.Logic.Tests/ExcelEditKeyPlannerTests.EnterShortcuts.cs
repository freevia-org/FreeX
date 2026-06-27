using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class ExcelEditKeyPlannerTests
{
    [Fact]
    public void GetIntent_MapsAltEnterToLineBreakInsertion()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(FormulaEditorKey.Enter, FormulaEditorModifiers.Alt, Current, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.InsertLineBreak);
        intent.Target.Should().BeNull();
    }

    [Fact]
    public void GetIntent_MapsSystemAltEnterToLineBreakInsertion()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            FormulaEditorKey.System,
            FormulaEditorModifiers.Alt,
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
        var intent = ExcelEditKeyPlanner.GetIntent(FormulaEditorKey.Enter, FormulaEditorModifiers.Control, Current, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitSelection);
        intent.Target.Should().BeNull();
    }

    [Fact]
    public void GetIntent_MapsSystemCtrlEnterToCommitSelection()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            FormulaEditorKey.System,
            FormulaEditorModifiers.Control,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            systemKey: FormulaEditorKey.Enter);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitSelection);
        intent.Target.Should().BeNull();
    }

    [Theory]
    [InlineData(FormulaEditorModifiers.Control | FormulaEditorModifiers.Shift)]
    [InlineData(FormulaEditorModifiers.Control | FormulaEditorModifiers.Alt)]
    [InlineData(FormulaEditorModifiers.Control | FormulaEditorModifiers.Shift | FormulaEditorModifiers.Alt)]
    public void GetIntent_DoesNotTreatExtraModifiedEnterAsCommitSelection(FormulaEditorModifiers modifiers)
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
    [InlineData(FormulaEditorModifiers.Shift | FormulaEditorModifiers.Alt)]
    [InlineData(FormulaEditorModifiers.Control | FormulaEditorModifiers.Alt)]
    [InlineData(FormulaEditorModifiers.Control | FormulaEditorModifiers.Shift | FormulaEditorModifiers.Alt)]
    public void GetIntent_DoesNotTreatExtraModifiedEnterAsLineBreakInsertion(FormulaEditorModifiers modifiers)
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
            FormulaEditorModifiers.Alt | FormulaEditorModifiers.Shift,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }
}
