using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ExcelEditKeyPlannerTests
{
    [Theory]
    [InlineData(Key.Enter, ModifierKeys.None, 11, 5)]
    [InlineData(Key.Enter, ModifierKeys.Shift, 9, 5)]
    [InlineData(Key.Tab, ModifierKeys.None, 10, 6)]
    [InlineData(Key.Tab, ModifierKeys.Shift, 10, 4)]
    public void GetIntent_CommitsEntryAndMovesLikeExcel(Key key, ModifierKeys modifiers, uint expectedRow, uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(key, modifiers, Current, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Theory]
    [InlineData(FreeXEnterDirection.Right, ModifierKeys.None, 10, 6)]
    [InlineData(FreeXEnterDirection.Right, ModifierKeys.Shift, 10, 4)]
    [InlineData(FreeXEnterDirection.Up, ModifierKeys.None, 9, 5)]
    [InlineData(FreeXEnterDirection.Left, ModifierKeys.None, 10, 4)]
    public void GetIntent_UsesConfiguredEnterDirection(
        FreeXEnterDirection direction,
        ModifierKeys modifiers,
        uint expectedRow,
        uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            Key.Enter,
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
            Key.Enter,
            ModifierKeys.None,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            moveSelectionAfterEnter: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(Current);
    }

    [Theory]
    [InlineData(Key.Enter, ModifierKeys.Shift, 1, 1)]
    [InlineData(Key.Tab, ModifierKeys.Shift, 1, 1)]
    [InlineData(Key.Enter, ModifierKeys.None, CellAddress.MaxRow, CellAddress.MaxCol)]
    [InlineData(Key.Tab, ModifierKeys.None, CellAddress.MaxRow, CellAddress.MaxCol)]
    public void GetIntent_CommitMovementClampsAtWorksheetEdges(
        Key key,
        ModifierKeys modifiers,
        uint currentRow,
        uint currentCol)
    {
        var edgeCell = new CellAddress(SheetId, currentRow, currentCol);

        var intent = ExcelEditKeyPlanner.GetIntent(key, modifiers, edgeCell, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(edgeCell);
    }

    [Theory]
    [InlineData(Key.Tab, ModifierKeys.Control, Key.None)]
    [InlineData(Key.Tab, ModifierKeys.Alt, Key.None)]
    [InlineData(Key.System, ModifierKeys.Alt, Key.Tab)]
    [InlineData(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift, Key.None)]
    [InlineData(Key.Tab, ModifierKeys.Alt | ModifierKeys.Shift, Key.None)]
    public void GetIntent_DoesNotTreatExtraModifiedTabAsCommitAndMove(
        Key key,
        ModifierKeys modifiers,
        Key systemKey)
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
