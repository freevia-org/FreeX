using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ExcelEditKeyPlannerTests
{
    [Fact]
    public void GetIntent_DoesNotCommitInlineEditorOnPlainArrowKeys()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(FormulaEditorKey.Left, FormulaEditorModifiers.None, Current, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }

    [Theory]
    [InlineData(FormulaEditorKey.Up, 9, 5)]
    [InlineData(FormulaEditorKey.Down, 11, 5)]
    [InlineData(FormulaEditorKey.Left, 10, 4)]
    [InlineData(FormulaEditorKey.Right, 10, 6)]
    public void GetIntent_CommitsEmptyInlineEditorAndMovesOnPlainArrowKeys(FormulaEditorKey key, uint expectedRow, uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            FormulaEditorModifiers.None,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            inlineEditorCommitsOnArrow: true);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Theory]
    [InlineData(FormulaEditorKey.Up, 9, 5)]
    [InlineData(FormulaEditorKey.Down, 11, 5)]
    [InlineData(FormulaEditorKey.Left, 10, 4)]
    [InlineData(FormulaEditorKey.Right, 10, 6)]
    public void GetIntent_CommitsNonFormulaInlineEditorAndMovesOnPlainArrowKeys(FormulaEditorKey key, uint expectedRow, uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            FormulaEditorModifiers.None,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            inlineEditorCommitsOnArrow: true);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Fact]
    public void GetIntent_LetsNonEmptyInlineEditorHandlePlainArrowKeys()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            FormulaEditorKey.Right,
            FormulaEditorModifiers.None,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            inlineEditorCommitsOnArrow: false);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }

    [Fact]
    public void GetIntent_LetsInlineEditorHandleShiftArrowTextSelection()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            FormulaEditorKey.Right,
            FormulaEditorModifiers.Shift,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            inlineEditorCommitsOnArrow: true);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }
}
