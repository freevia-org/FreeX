using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ExcelEditKeyPlannerTests
{
    [Theory]
    [InlineData(Key.Up, 9, 5)]
    [InlineData(Key.Down, 11, 5)]
    [InlineData(Key.Left, 10, 4)]
    [InlineData(Key.Right, 10, 6)]
    [InlineData(Key.PageUp, 1, 5)]
    [InlineData(Key.PageDown, 19, 5)]
    public void GetIntent_SelectsFormulaReferenceRangeInsteadOfCommittingWhenFormulaRangeEntryIsActive(
        Key key,
        uint expectedRow,
        uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            ModifierKeys.None,
            Current,
            pageSize: 9,
            allowFormulaBarNavigationKeys: false,
            formulaRangeEntryActive: true);

        intent.Action.Should().Be(ExcelEditKeyAction.SelectFormulaReference);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Theory]
    [InlineData(Key.PageUp, 0, 9)]
    [InlineData(Key.PageDown, 0, 11)]
    [InlineData(Key.PageUp, -5, 9)]
    [InlineData(Key.PageDown, -5, 11)]
    public void GetIntent_FormulaReferencePageNavigationUsesMinimumSingleRowStep(
        Key key,
        int pageSize,
        uint expectedRow)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            ModifierKeys.None,
            Current,
            pageSize,
            allowFormulaBarNavigationKeys: false,
            formulaRangeEntryActive: true);

        intent.Action.Should().Be(ExcelEditKeyAction.SelectFormulaReference);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, Current.Col));
    }

    [Theory]
    [InlineData(Key.Up, 1, 1)]
    [InlineData(Key.Left, 1, 1)]
    [InlineData(Key.PageUp, 1, 1)]
    [InlineData(Key.Down, CellAddress.MaxRow, CellAddress.MaxCol)]
    [InlineData(Key.Right, CellAddress.MaxRow, CellAddress.MaxCol)]
    [InlineData(Key.PageDown, CellAddress.MaxRow, CellAddress.MaxCol)]
    public void GetIntent_FormulaReferenceMovementClampsAtWorksheetEdges(
        Key key,
        uint currentRow,
        uint currentCol)
    {
        var edgeCell = new CellAddress(SheetId, currentRow, currentCol);

        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            ModifierKeys.None,
            edgeCell,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            formulaRangeEntryActive: true);

        intent.Action.Should().Be(ExcelEditKeyAction.SelectFormulaReference);
        intent.Target.Should().Be(edgeCell);
    }

    [Theory]
    [InlineData(Key.Enter, ModifierKeys.None, 11, 5)]
    [InlineData(Key.Tab, ModifierKeys.None, 10, 6)]
    public void GetIntent_StillCommitsEnterAndTabWhenFormulaRangeEntryIsActive(
        Key key,
        ModifierKeys modifiers,
        uint expectedRow,
        uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            modifiers,
            Current,
            pageSize: 9,
            allowFormulaBarNavigationKeys: false,
            formulaRangeEntryActive: true);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }
}
