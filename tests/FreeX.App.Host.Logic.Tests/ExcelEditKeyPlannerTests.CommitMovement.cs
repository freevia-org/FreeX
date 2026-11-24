using Free.Shared.Shell;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ExcelEditKeyPlannerTests
{
    [Theory]
    [InlineData(FormulaEditorKey.Enter, FreeKeyModifiers.None, 11, 5)]
    [InlineData(FormulaEditorKey.Enter, FreeKeyModifiers.Shift, 9, 5)]
    [InlineData(FormulaEditorKey.Tab, FreeKeyModifiers.None, 10, 6)]
    [InlineData(FormulaEditorKey.Tab, FreeKeyModifiers.Shift, 10, 4)]
    public void GetIntent_CommitsEntryAndMovesLikeExcel(FormulaEditorKey key, FreeKeyModifiers modifiers, uint expectedRow, uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(key, modifiers, Current, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Theory]
    [InlineData(FormulaEditorEnterDirection.Right, FreeKeyModifiers.None, 10, 6)]
    [InlineData(FormulaEditorEnterDirection.Right, FreeKeyModifiers.Shift, 10, 4)]
    [InlineData(FormulaEditorEnterDirection.Up, FreeKeyModifiers.None, 9, 5)]
    [InlineData(FormulaEditorEnterDirection.Left, FreeKeyModifiers.None, 10, 4)]
    public void GetIntent_UsesConfiguredEnterDirection(
        FormulaEditorEnterDirection direction,
        FreeKeyModifiers modifiers,
        uint expectedRow,
        uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            FormulaEditorKey.Enter,
            modifiers,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            enterDirection: direction);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Fact]
    public void GetIntent_CanCommitEnterWithoutMovingSelection()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            FormulaEditorKey.Enter,
            FreeKeyModifiers.None,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            moveSelectionAfterEnter: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(Current);
    }

    [Theory]
    [InlineData(FormulaEditorKey.Enter, FreeKeyModifiers.Shift, 1, 1)]
    [InlineData(FormulaEditorKey.Tab, FreeKeyModifiers.Shift, 1, 1)]
    [InlineData(FormulaEditorKey.Enter, FreeKeyModifiers.None, CellAddress.MaxRow, CellAddress.MaxCol)]
    [InlineData(FormulaEditorKey.Tab, FreeKeyModifiers.None, CellAddress.MaxRow, CellAddress.MaxCol)]
    public void GetIntent_CommitMovementClampsAtWorksheetEdges(
        FormulaEditorKey key,
        FreeKeyModifiers modifiers,
        uint currentRow,
        uint currentCol)
    {
        var edgeCell = new CellAddress(SheetId, currentRow, currentCol);

        var intent = ExcelEditKeyPlanner.GetIntent(key, modifiers, edgeCell, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(edgeCell);
    }

    [Theory]
    [InlineData(FormulaEditorKey.Tab, FreeKeyModifiers.Control, FormulaEditorKey.None)]
    [InlineData(FormulaEditorKey.Tab, FreeKeyModifiers.Alt, FormulaEditorKey.None)]
    [InlineData(FormulaEditorKey.System, FreeKeyModifiers.Alt, FormulaEditorKey.Tab)]
    [InlineData(FormulaEditorKey.Tab, FreeKeyModifiers.Control | FreeKeyModifiers.Shift, FormulaEditorKey.None)]
    [InlineData(FormulaEditorKey.Tab, FreeKeyModifiers.Alt | FreeKeyModifiers.Shift, FormulaEditorKey.None)]
    public void GetIntent_DoesNotTreatExtraModifiedTabAsCommitAndMove(
        FormulaEditorKey key,
        FreeKeyModifiers modifiers,
        FormulaEditorKey systemKey)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            modifiers,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            systemKey: systemKey);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }
}
