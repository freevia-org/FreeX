using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ExcelEditKeyPlannerTests
{
    [Theory]
    [InlineData(FormulaEditorKey.Enter, FormulaEditorModifiers.None, 11, 5)]
    [InlineData(FormulaEditorKey.Enter, FormulaEditorModifiers.Shift, 9, 5)]
    [InlineData(FormulaEditorKey.Tab, FormulaEditorModifiers.None, 10, 6)]
    [InlineData(FormulaEditorKey.Tab, FormulaEditorModifiers.Shift, 10, 4)]
    public void GetIntent_CommitsEntryAndMovesLikeExcel(FormulaEditorKey key, FormulaEditorModifiers modifiers, uint expectedRow, uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(key, modifiers, Current, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Theory]
    [InlineData(FormulaEditorEnterDirection.Right, FormulaEditorModifiers.None, 10, 6)]
    [InlineData(FormulaEditorEnterDirection.Right, FormulaEditorModifiers.Shift, 10, 4)]
    [InlineData(FormulaEditorEnterDirection.Up, FormulaEditorModifiers.None, 9, 5)]
    [InlineData(FormulaEditorEnterDirection.Left, FormulaEditorModifiers.None, 10, 4)]
    public void GetIntent_UsesConfiguredEnterDirection(
        FormulaEditorEnterDirection direction,
        FormulaEditorModifiers modifiers,
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
            FormulaEditorModifiers.None,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            moveSelectionAfterEnter: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(Current);
    }

    [Theory]
    [InlineData(FormulaEditorKey.Enter, FormulaEditorModifiers.Shift, 1, 1)]
    [InlineData(FormulaEditorKey.Tab, FormulaEditorModifiers.Shift, 1, 1)]
    [InlineData(FormulaEditorKey.Enter, FormulaEditorModifiers.None, CellAddress.MaxRow, CellAddress.MaxCol)]
    [InlineData(FormulaEditorKey.Tab, FormulaEditorModifiers.None, CellAddress.MaxRow, CellAddress.MaxCol)]
    public void GetIntent_CommitMovementClampsAtWorksheetEdges(
        FormulaEditorKey key,
        FormulaEditorModifiers modifiers,
        uint currentRow,
        uint currentCol)
    {
        var edgeCell = new CellAddress(SheetId, currentRow, currentCol);

        var intent = ExcelEditKeyPlanner.GetIntent(key, modifiers, edgeCell, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(edgeCell);
    }

    [Theory]
    [InlineData(FormulaEditorKey.Tab, FormulaEditorModifiers.Control, FormulaEditorKey.None)]
    [InlineData(FormulaEditorKey.Tab, FormulaEditorModifiers.Alt, FormulaEditorKey.None)]
    [InlineData(FormulaEditorKey.System, FormulaEditorModifiers.Alt, FormulaEditorKey.Tab)]
    [InlineData(FormulaEditorKey.Tab, FormulaEditorModifiers.Control | FormulaEditorModifiers.Shift, FormulaEditorKey.None)]
    [InlineData(FormulaEditorKey.Tab, FormulaEditorModifiers.Alt | FormulaEditorModifiers.Shift, FormulaEditorKey.None)]
    public void GetIntent_DoesNotTreatExtraModifiedTabAsCommitAndMove(
        FormulaEditorKey key,
        FormulaEditorModifiers modifiers,
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
