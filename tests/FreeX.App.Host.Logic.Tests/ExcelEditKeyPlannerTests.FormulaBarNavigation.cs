using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ExcelEditKeyPlannerTests
{
    [Theory]
    [InlineData(FormulaEditorKey.Up, 9, 5)]
    [InlineData(FormulaEditorKey.Down, 11, 5)]
    [InlineData(FormulaEditorKey.PageUp, 1, 5)]
    [InlineData(FormulaEditorKey.PageDown, 19, 5)]
    public void GetIntent_AllowsFormulaBarNavigationKeys(FormulaEditorKey key, uint expectedRow, uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(key, FormulaEditorModifiers.None, Current, pageSize: 9, allowFormulaBarNavigationKeys: true);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Theory]
    [InlineData(FormulaEditorKey.PageUp, 0, 9)]
    [InlineData(FormulaEditorKey.PageDown, 0, 11)]
    [InlineData(FormulaEditorKey.PageUp, -5, 9)]
    [InlineData(FormulaEditorKey.PageDown, -5, 11)]
    public void GetIntent_FormulaBarPageNavigationUsesMinimumSingleRowStep(
        FormulaEditorKey key,
        int pageSize,
        uint expectedRow)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            FormulaEditorModifiers.None,
            Current,
            pageSize,
            allowFormulaBarNavigationKeys: true);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, Current.Col));
    }

    [Theory]
    [InlineData(FormulaEditorKey.Up)]
    [InlineData(FormulaEditorKey.Down)]
    [InlineData(FormulaEditorKey.PageUp)]
    [InlineData(FormulaEditorKey.PageDown)]
    public void GetIntent_LetsFormulaBarHandleShiftNavigationTextSelection(FormulaEditorKey key)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            FormulaEditorModifiers.Shift,
            Current,
            pageSize: 9,
            allowFormulaBarNavigationKeys: true);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }
}
