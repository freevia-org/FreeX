using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ExcelEditKeyPlannerTests
{
    [Theory]
    [InlineData(Key.Up, 9, 5)]
    [InlineData(Key.Down, 11, 5)]
    [InlineData(Key.PageUp, 1, 5)]
    [InlineData(Key.PageDown, 19, 5)]
    public void GetIntent_AllowsFormulaBarNavigationKeys(Key key, uint expectedRow, uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(key, ModifierKeys.None, Current, pageSize: 9, allowFormulaBarNavigationKeys: true);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Theory]
    [InlineData(Key.PageUp, 0, 9)]
    [InlineData(Key.PageDown, 0, 11)]
    [InlineData(Key.PageUp, -5, 9)]
    [InlineData(Key.PageDown, -5, 11)]
    public void GetIntent_FormulaBarPageNavigationUsesMinimumSingleRowStep(
        Key key,
        int pageSize,
        uint expectedRow)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            ModifierKeys.None,
            Current,
            pageSize,
            allowFormulaBarNavigationKeys: true);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, Current.Col));
    }

    [Theory]
    [InlineData(Key.Up)]
    [InlineData(Key.Down)]
    [InlineData(Key.PageUp)]
    [InlineData(Key.PageDown)]
    public void GetIntent_LetsFormulaBarHandleShiftNavigationTextSelection(Key key)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            ModifierKeys.Shift,
            Current,
            pageSize: 9,
            allowFormulaBarNavigationKeys: true);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }
}
