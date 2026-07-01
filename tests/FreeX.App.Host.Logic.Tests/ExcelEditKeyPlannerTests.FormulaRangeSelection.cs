using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ExcelEditKeyPlannerTests
{
    [Theory]
    [InlineData(FormulaEditorKey.Up, 9, 5)]
    [InlineData(FormulaEditorKey.Down, 11, 5)]
    [InlineData(FormulaEditorKey.Left, 10, 4)]
    [InlineData(FormulaEditorKey.Right, 10, 6)]
    [InlineData(FormulaEditorKey.PageUp, 1, 5)]
    [InlineData(FormulaEditorKey.PageDown, 19, 5)]
    public void GetIntent_SelectsFormulaReferenceRangeInsteadOfCommittingWhenFormulaRangeEntryIsActive(
        FormulaEditorKey key,
        uint expectedRow,
        uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            FormulaEditorModifiers.None,
            Current,
            pageSize: 9,
            allowFormulaBarNavigationKeys: false,
            formulaRangeEntryActive: true);

        intent.Action.Should().Be(ExcelEditKeyAction.SelectFormulaReference);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Theory]
    [InlineData(FormulaEditorKey.PageUp, 0, 9)]
    [InlineData(FormulaEditorKey.PageDown, 0, 11)]
    [InlineData(FormulaEditorKey.PageUp, -5, 9)]
    [InlineData(FormulaEditorKey.PageDown, -5, 11)]
    public void GetIntent_FormulaReferencePageNavigationUsesMinimumSingleRowStep(
        FormulaEditorKey key,
        int pageSize,
        uint expectedRow)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            FormulaEditorModifiers.None,
            Current,
            pageSize,
            allowFormulaBarNavigationKeys: false,
            formulaRangeEntryActive: true);

        intent.Action.Should().Be(ExcelEditKeyAction.SelectFormulaReference);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, Current.Col));
    }

    [Theory]
    [InlineData(FormulaEditorKey.Up, 1, 1)]
    [InlineData(FormulaEditorKey.Left, 1, 1)]
    [InlineData(FormulaEditorKey.PageUp, 1, 1)]
    [InlineData(FormulaEditorKey.Down, CellAddress.MaxRow, CellAddress.MaxCol)]
    [InlineData(FormulaEditorKey.Right, CellAddress.MaxRow, CellAddress.MaxCol)]
    [InlineData(FormulaEditorKey.PageDown, CellAddress.MaxRow, CellAddress.MaxCol)]
    public void GetIntent_FormulaReferenceMovementClampsAtWorksheetEdges(
        FormulaEditorKey key,
        uint currentRow,
        uint currentCol)
    {
        var edgeCell = new CellAddress(SheetId, currentRow, currentCol);

        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            FormulaEditorModifiers.None,
            edgeCell,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            formulaRangeEntryActive: true);

        intent.Action.Should().Be(ExcelEditKeyAction.SelectFormulaReference);
        intent.Target.Should().Be(edgeCell);
    }

    [Theory]
    [InlineData(FormulaEditorKey.Enter, FormulaEditorModifiers.None, 11, 5)]
    [InlineData(FormulaEditorKey.Tab, FormulaEditorModifiers.None, 10, 6)]
    public void GetIntent_StillCommitsEnterAndTabWhenFormulaRangeEntryIsActive(
        FormulaEditorKey key,
        FormulaEditorModifiers modifiers,
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
